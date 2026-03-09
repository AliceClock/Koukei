using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

namespace Koukei.Audio.Host;

internal sealed class AudioPlaybackHostServer : IAsyncDisposable
{
    private const int CommandQueueCapacity = 64;
    private const int OutboundQueueCapacity = 128;

    private readonly record struct PendingCommand(
        AudioPlaybackIpcMessage Message,
        CancellationTokenSource Cancellation);

    private readonly record struct OutboundMessage(
        string? SerializedMessage,
        bool IsStateSignal,
        TaskCompletionSource? Completion);

    private readonly string _pipeName;
    private readonly int _parentProcessId;
    private readonly IAudioPlaybackService _playbackService;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<PendingCommand> _commands = Channel.CreateBounded<PendingCommand>(
        new BoundedChannelOptions(CommandQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private readonly Channel<OutboundMessage> _outbound = Channel.CreateBounded<OutboundMessage>(
        new BoundedChannelOptions(OutboundQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _requestCancellations = new();
    private readonly TaskCompletionSource _shutdownResponseSent = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _pendingStateLock = new();
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private AudioPlaybackState? _pendingState;
    private bool _stateSignalQueued;
    private bool _isDisposed;

    public AudioPlaybackHostServer(
        string pipeName,
        int parentProcessId,
        IAudioPlaybackService playbackService)
    {
        _pipeName = pipeName;
        _parentProcessId = parentProcessId;
        _playbackService = playbackService;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        var token = linkedCancellation.Token;
        _pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: 64 * 1024,
            outBufferSize: 64 * 1024);

        var parentExitTask = WaitForParentExitAsync(_parentProcessId, token);
        await _pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
        _reader = new StreamReader(
            _pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024,
            leaveOpen: true);
        _writer = new StreamWriter(
            _pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 16 * 1024,
            leaveOpen: true)
        {
            AutoFlush = false
        };

        _playbackService.StateChanged += PlaybackService_StateChanged;
        _playbackService.PlaybackEnded += PlaybackService_PlaybackEnded;

        var writerTask = ProcessOutboundMessagesAsync(token);
        var readTask = ReadRequestsAsync(token);
        var commandTask = ProcessCommandsAsync(token);
        await Task.WhenAny(readTask, commandTask, writerTask, parentExitTask).ConfigureAwait(false);
        if (_shutdownResponseSent.Task.IsCompleted)
        {
            await IgnoreCancellationAsync(readTask).ConfigureAwait(false);
            await IgnoreCancellationAsync(commandTask).ConfigureAwait(false);
        }
        _shutdown.Cancel();
        _commands.Writer.TryComplete();
        _outbound.Writer.TryComplete();

        await IgnoreCancellationAsync(readTask).ConfigureAwait(false);
        await IgnoreCancellationAsync(commandTask).ConfigureAwait(false);
        await IgnoreCancellationAsync(writerTask).ConfigureAwait(false);
        await IgnoreCancellationAsync(parentExitTask).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _shutdown.Cancel();
        _commands.Writer.TryComplete();
        _outbound.Writer.TryComplete();
        foreach (var request in _requestCancellations)
        {
            request.Value.Cancel();
        }

        _playbackService.StateChanged -= PlaybackService_StateChanged;
        _playbackService.PlaybackEnded -= PlaybackService_PlaybackEnded;
        try
        {
            await _playbackService.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        _reader?.Dispose();
        _writer?.Dispose();
        _pipe?.Dispose();
        _shutdown.Dispose();
    }

    private async Task ReadRequestsAsync(CancellationToken cancellationToken)
    {
        var reader = _reader ?? throw new InvalidOperationException("The audio host pipe is not connected.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var message = AudioPlaybackIpcProtocol.Deserialize(line);
                if (message.Version != AudioPlaybackIpcProtocol.Version)
                {
                    await SendResponseAsync(
                        message.Id,
                        success: false,
                        payload: null,
                        $"Unsupported audio IPC protocol version {message.Version}.",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (message.Kind == AudioPlaybackIpcProtocol.CancelKind)
                {
                    if (_requestCancellations.TryGetValue(message.Id, out var requestCancellation))
                    {
                        requestCancellation.Cancel();
                    }
                    continue;
                }

                if (message.Kind != AudioPlaybackIpcProtocol.RequestKind)
                {
                    continue;
                }

                var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (!_requestCancellations.TryAdd(message.Id, commandCancellation))
                {
                    commandCancellation.Dispose();
                    await SendResponseAsync(
                        message.Id,
                        success: false,
                        payload: null,
                        "The audio host received a duplicate request identifier.",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _commands.Writer.WriteAsync(
                    new PendingCommand(message, commandCancellation),
                    cancellationToken).ConfigureAwait(false);
                if (string.Equals(
                        message.Name,
                        AudioPlaybackIpcProtocol.ShutdownOperation,
                        StringComparison.Ordinal))
                {
                    await _shutdownResponseSent.Task.ConfigureAwait(false);
                    break;
                }
            }
        }
        finally
        {
            _commands.Writer.TryComplete();
        }
    }

    private async Task ProcessCommandsAsync(CancellationToken cancellationToken)
    {
        PendingCommand? deferredCommand = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            PendingCommand command;
            if (deferredCommand is { } deferred)
            {
                command = deferred;
                deferredCommand = null;
            }
            else
            {
                if (!await _commands.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) ||
                    !_commands.Reader.TryRead(out command))
                {
                    break;
                }
            }

            if (IsCoalescableControl(command.Message.Name))
            {
                while (_commands.Reader.TryRead(out var candidate))
                {
                    if (string.Equals(
                            candidate.Message.Name,
                            command.Message.Name,
                            StringComparison.Ordinal))
                    {
                        await CompleteSupersededCommandAsync(command).ConfigureAwait(false);
                        command = candidate;
                        continue;
                    }

                    deferredCommand = candidate;
                    break;
                }
            }

            var shouldShutdown = string.Equals(
                command.Message.Name,
                AudioPlaybackIpcProtocol.ShutdownOperation,
                StringComparison.Ordinal);
            try
            {
                var payload = await ExecuteCommandAsync(
                    command.Message,
                    command.Cancellation.Token).ConfigureAwait(false);
                await SendResponseAsync(
                    command.Message.Id,
                    success: true,
                    payload,
                    error: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (command.Cancellation.IsCancellationRequested)
            {
                await SendResponseSafelyAsync(
                    command.Message.Id,
                    success: false,
                    payload: null,
                    "The audio operation was canceled.").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await SendResponseSafelyAsync(
                    command.Message.Id,
                    success: false,
                    payload: null,
                    ex.GetBaseException().Message).ConfigureAwait(false);
            }
            finally
            {
                if (_requestCancellations.TryRemove(command.Message.Id, out var requestCancellation))
                {
                    requestCancellation.Dispose();
                }
            }

            if (shouldShutdown)
            {
                _shutdownResponseSent.TrySetResult();
                break;
            }
        }
    }

    private async Task CompleteSupersededCommandAsync(PendingCommand command)
    {
        try
        {
            await SendResponseSafelyAsync(
                command.Message.Id,
                success: !command.Cancellation.IsCancellationRequested,
                payload: null,
                error: command.Cancellation.IsCancellationRequested
                    ? "The audio operation was canceled."
                    : null).ConfigureAwait(false);
        }
        finally
        {
            if (_requestCancellations.TryRemove(command.Message.Id, out var cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private static bool IsCoalescableControl(string? operation) =>
        operation is
            AudioPlaybackIpcProtocol.SeekAbsoluteOperation or
            AudioPlaybackIpcProtocol.SetVolumeOperation or
            AudioPlaybackIpcProtocol.SetSpeedOperation;

    private async Task<object?> ExecuteCommandAsync(
        AudioPlaybackIpcMessage message,
        CancellationToken cancellationToken)
    {
        switch (message.Name)
        {
            case AudioPlaybackIpcProtocol.PlayOperation:
                await _playbackService.PlayAsync(
                    AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackRequest>(message.Payload),
                    cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.GetStateOperation:
                return await _playbackService.GetPlaybackStateAsync(cancellationToken).ConfigureAwait(false);
            case AudioPlaybackIpcProtocol.SetPausedOperation:
                await _playbackService.SetPausedAsync(
                    AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackBooleanValue>(message.Payload).Value,
                    cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.SeekAbsoluteOperation:
                await _playbackService.SeekAbsoluteAsync(
                    AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackDoubleValue>(message.Payload).Value,
                    cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.SetVolumeOperation:
                await _playbackService.SetVolumeAsync(
                    AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackDoubleValue>(message.Payload).Value,
                    cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.SetMutedOperation:
                await _playbackService.SetMutedAsync(
                    AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackBooleanValue>(message.Payload).Value,
                    cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.SetSpeedOperation:
                await _playbackService.SetSpeedAsync(
                    AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackDoubleValue>(message.Payload).Value,
                    cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.StopOperation:
                await _playbackService.StopAsync(cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.CloseOperation:
                await _playbackService.CloseAsync(cancellationToken).ConfigureAwait(false);
                return null;
            case AudioPlaybackIpcProtocol.ShutdownOperation:
                await _playbackService.CloseAsync(cancellationToken).ConfigureAwait(false);
                return null;
            default:
                throw new InvalidOperationException($"Unknown audio host operation '{message.Name}'.");
        }
    }

    private void PlaybackService_StateChanged(object? sender, AudioPlaybackStateChangedEventArgs args) =>
        QueueLatestState(args.State);

    private void PlaybackService_PlaybackEnded(object? sender, AudioPlaybackEndedEventArgs args) =>
        QueueDurableEvent(AudioPlaybackIpcProtocol.PlaybackEndedEvent, args.Request);

    private void QueueLatestState(AudioPlaybackState state)
    {
        var shouldQueueSignal = false;
        lock (_pendingStateLock)
        {
            _pendingState = state;
            if (!_stateSignalQueued)
            {
                _stateSignalQueued = true;
                shouldQueueSignal = true;
            }
        }

        if (!shouldQueueSignal)
        {
            return;
        }

        var signal = new OutboundMessage(
            SerializedMessage: null,
            IsStateSignal: true,
            Completion: null);
        if (!_outbound.Writer.TryWrite(signal))
        {
            _ = QueueStateSignalAsync(signal);
        }
    }

    private async Task QueueStateSignalAsync(OutboundMessage signal)
    {
        try
        {
            await _outbound.Writer.WriteAsync(signal, _shutdown.Token).ConfigureAwait(false);
        }
        catch
        {
            _shutdown.Cancel();
        }
    }

    private void QueueDurableEvent(string eventName, object payload)
    {
        var message = AudioPlaybackIpcProtocol.Serialize(
            AudioPlaybackIpcProtocol.EventKind,
            name: eventName,
            payload: payload);
        var outbound = new OutboundMessage(
            message,
            IsStateSignal: false,
            Completion: null);
        if (!_outbound.Writer.TryWrite(outbound))
        {
            _ = QueueDurableMessageAsync(outbound);
        }
    }

    private async Task QueueDurableMessageAsync(OutboundMessage message)
    {
        try
        {
            await _outbound.Writer.WriteAsync(message, _shutdown.Token).ConfigureAwait(false);
        }
        catch
        {
            _shutdown.Cancel();
        }
    }

    private async Task SendResponseAsync(
        long requestId,
        bool success,
        object? payload,
        string? error,
        CancellationToken cancellationToken)
    {
        var message = AudioPlaybackIpcProtocol.Serialize(
            AudioPlaybackIpcProtocol.ResponseKind,
            requestId,
            payload: payload,
            success: success,
            error: error);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _outbound.Writer.WriteAsync(
            new OutboundMessage(message, IsStateSignal: false, completion),
            cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendResponseSafelyAsync(
        long requestId,
        bool success,
        object? payload,
        string? error)
    {
        try
        {
            await SendResponseAsync(
                requestId,
                success,
                payload,
                error,
                _shutdown.Token).ConfigureAwait(false);
        }
        catch
        {
            _shutdown.Cancel();
        }
    }

    private async Task ProcessOutboundMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var writer = _writer
                ?? throw new EndOfStreamException("The audio IPC client disconnected.");
            await foreach (var outbound in _outbound.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                string? message = outbound.SerializedMessage;
                if (outbound.IsStateSignal)
                {
                    AudioPlaybackState? state;
                    lock (_pendingStateLock)
                    {
                        state = _pendingState;
                        _pendingState = null;
                        _stateSignalQueued = false;
                    }

                    if (state is null)
                    {
                        outbound.Completion?.TrySetResult();
                        continue;
                    }

                    message = AudioPlaybackIpcProtocol.Serialize(
                        AudioPlaybackIpcProtocol.EventKind,
                        name: AudioPlaybackIpcProtocol.StateChangedEvent,
                        payload: state);
                }

                if (message is null)
                {
                    outbound.Completion?.TrySetResult();
                    continue;
                }

                try
                {
                    await writer.WriteLineAsync(message.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    outbound.Completion?.TrySetResult();
                }
                catch (Exception ex)
                {
                    outbound.Completion?.TrySetException(ex);
                    throw;
                }
            }
        }
        catch
        {
            while (_outbound.Reader.TryRead(out var pending))
            {
                pending.Completion?.TrySetException(
                    new EndOfStreamException("The audio IPC client disconnected."));
            }

            throw;
        }
    }

    private static async Task WaitForParentExitAsync(
        int parentProcessId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            while (!cancellationToken.IsCancellationRequested)
            {
                parent.Refresh();
                if (parent.HasExited)
                {
                    return;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }
        }
        catch (ArgumentException)
        {
            // Parent already exited before the monitor was established.
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
