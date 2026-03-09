using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Koukei.Audio;

public sealed class OutOfProcessAudioPlaybackService : IAudioPlaybackService, IAsyncDisposable
{
    private const string HostExecutableName = "Koukei.Audio.Host.exe";
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HostExitTimeout = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<AudioPlaybackIpcMessage>> _pendingRequests = new();
    private readonly object _lifetimeLock = new();
    private readonly object _metadataLock = new();
    private readonly object _stateLock = new();
    private readonly IAudioMetadataService _audioMetadataService;
    private Process? _hostProcess;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _connectionCancellation;
    private CancellationTokenSource? _metadataCancellation;
    private CancellationTokenSource? _recoveryCancellation;
    private Task? _readerTask;
    private AudioPlaybackState _lastState = AudioPlaybackState.Empty;
    private AudioPlaybackRequest? _activeRequest;
    private long _nextRequestId;
    private long _lastStateTimestamp;
    private long _metadataGeneration;
    private long _playbackSessionVersion;
    private int _connectionGeneration;
    private int _expectedDisconnectGeneration = -1;
    private int _automaticRecoveryAttempts;
    private bool _isDisposed;

    public OutOfProcessAudioPlaybackService(IAudioMetadataService audioMetadataService)
    {
        _audioMetadataService = audioMetadataService
            ?? throw new ArgumentNullException(nameof(audioMetadataService));
    }

    public event EventHandler<AudioPlaybackStateChangedEventArgs>? StateChanged;

    public event EventHandler<AudioMediaChangedEventArgs>? MediaChanged;

    public event EventHandler<AudioPlaybackEndedEventArgs>? PlaybackEnded;

    public async Task PlayAsync(
        AudioPlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);
        var sessionVersion = BeginPlaybackSession();
        var metadataGeneration = StartMetadataLoad(request);
        try
        {
            await SendVoidRequestAsync(
                AudioPlaybackIpcProtocol.PlayOperation,
                request,
                cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (sessionVersion == _playbackSessionVersion)
                {
                    _activeRequest = request;
                    _automaticRecoveryAttempts = 0;
                }
            }
        }
        catch
        {
            EndPlaybackSession(sessionVersion);
            CancelMetadataLoad(metadataGeneration);
            throw;
        }
    }

    public Task<AudioPlaybackState> GetPlaybackStateAsync(CancellationToken cancellationToken = default) =>
        SendRequestAsync<AudioPlaybackState>(
            AudioPlaybackIpcProtocol.GetStateOperation,
            payload: null,
            cancellationToken);

    public Task SetPausedAsync(bool isPaused, CancellationToken cancellationToken = default) =>
        SendVoidRequestAsync(
            AudioPlaybackIpcProtocol.SetPausedOperation,
            new AudioPlaybackBooleanValue(isPaused),
            cancellationToken);

    public Task SeekAbsoluteAsync(double seconds, CancellationToken cancellationToken = default) =>
        SendVoidRequestAsync(
            AudioPlaybackIpcProtocol.SeekAbsoluteOperation,
            new AudioPlaybackDoubleValue(seconds),
            cancellationToken);

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SendVoidRequestAsync(
            AudioPlaybackIpcProtocol.SetVolumeOperation,
            new AudioPlaybackDoubleValue(volume),
            cancellationToken);

    public Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default) =>
        SendVoidRequestAsync(
            AudioPlaybackIpcProtocol.SetMutedOperation,
            new AudioPlaybackBooleanValue(isMuted),
            cancellationToken);

    public Task SetSpeedAsync(double speed, CancellationToken cancellationToken = default) =>
        SendVoidRequestAsync(
            AudioPlaybackIpcProtocol.SetSpeedOperation,
            new AudioPlaybackDoubleValue(speed),
            cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await SendVoidRequestAsync(
            AudioPlaybackIpcProtocol.StopOperation,
            payload: null,
            cancellationToken).ConfigureAwait(false);
        BeginPlaybackSession();
        CancelMetadataLoad();
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        BeginPlaybackSession();
        CancelMetadataLoad();
        var isConnected = false;
        var generation = 0;
        lock (_lifetimeLock)
        {
            isConnected = _pipe is { IsConnected: true };
            generation = _connectionGeneration;
        }

        var released = !isConnected;
        try
        {
            if (isConnected)
            {
                await SendVoidRequestAsync(
                    AudioPlaybackIpcProtocol.CloseOperation,
                    payload: null,
                    cancellationToken).ConfigureAwait(false);
                released = true;
            }
        }
        finally
        {
            if (!released)
            {
                lock (_lifetimeLock)
                {
                    if (generation == _connectionGeneration)
                    {
                        _expectedDisconnectGeneration = generation;
                    }
                }

                await ShutdownConnectionAsync(generation, waitForGracefulExit: false)
                    .ConfigureAwait(false);
            }

            PublishState(AudioPlaybackState.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        BeginPlaybackSession();
        CancelMetadataLoad();
        int generation;
        var isConnected = false;
        lock (_lifetimeLock)
        {
            generation = _connectionGeneration;
            isConnected = _pipe is { IsConnected: true };
            if (isConnected)
            {
                _expectedDisconnectGeneration = generation;
            }
        }

        if (isConnected)
        {
            try
            {
                await SendVoidRequestAsync(
                    AudioPlaybackIpcProtocol.ShutdownOperation,
                    payload: null,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Disposal still tears down a host that cannot acknowledge shutdown.
            }
        }

        _isDisposed = true;
        await ShutdownConnectionAsync(generation, waitForGracefulExit: true).ConfigureAwait(false);
        _recoveryCancellation?.Dispose();
        _recoveryCancellation = null;
        _connectionGate.Dispose();
        _writeGate.Dispose();
    }

    private async Task SendVoidRequestAsync(
        string operation,
        object? payload,
        CancellationToken cancellationToken)
    {
        _ = await SendRequestCoreAsync(operation, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendRequestAsync<T>(
        string operation,
        object? payload,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestCoreAsync(operation, payload, cancellationToken)
            .ConfigureAwait(false);
        return AudioPlaybackIpcProtocol.DeserializePayload<T>(response.Payload);
    }

    private async Task<AudioPlaybackIpcMessage> SendRequestCoreAsync(
        string operation,
        object? payload,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var generation = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<AudioPlaybackIpcMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(requestId, completion))
        {
            throw new AudioPlaybackHostException("Could not register the audio host request.");
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var pending))
            {
                pending.TrySetCanceled(cancellationToken);
                _ = SendCancellationSafelyAsync(requestId, generation);
            }
        });

        try
        {
            var message = AudioPlaybackIpcProtocol.Serialize(
                AudioPlaybackIpcProtocol.RequestKind,
                requestId,
                operation,
                payload);
            await WriteMessageAsync(message, generation, cancellationToken).ConfigureAwait(false);
            var response = await completion.Task.ConfigureAwait(false);
            if (!response.Success)
            {
                throw new AudioPlaybackHostException(
                    string.IsNullOrWhiteSpace(response.Error)
                        ? $"Audio host operation '{operation}' failed."
                        : response.Error);
            }

            return response;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw new AudioPlaybackHostException("The audio host connection was lost.", ex);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task<int> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        lock (_lifetimeLock)
        {
            if (_pipe is { IsConnected: true })
            {
                return _connectionGeneration;
            }
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            lock (_lifetimeLock)
            {
                if (_pipe is { IsConnected: true })
                {
                    return _connectionGeneration;
                }
            }

            var hostPath = ResolveHostExecutablePath();
            var pipeName = $"koukei-audio-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var processStartInfo = new ProcessStartInfo(hostPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
            };
            processStartInfo.ArgumentList.Add("--pipe");
            processStartInfo.ArgumentList.Add(pipeName);
            processStartInfo.ArgumentList.Add("--parent-pid");
            processStartInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var process = Process.Start(processStartInfo)
                ?? throw new AudioPlaybackHostException("Could not start the audio host process.");
            var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCancellation.CancelAfter(ConnectionTimeout);
                await pipe.ConnectAsync(timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                pipe.Dispose();
                _ = TerminateAndDisposeHostAsync(process);
                throw new AudioPlaybackHostException("Could not connect to the audio host process.", ex);
            }

            var reader = new StreamReader(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 16 * 1024,
                leaveOpen: true);
            var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024,
            leaveOpen: true)
            {
                AutoFlush = false
            };
            var connectionCancellation = new CancellationTokenSource();
            int generation;
            lock (_lifetimeLock)
            {
                generation = unchecked(++_connectionGeneration);
                _expectedDisconnectGeneration = -1;
                _hostProcess = process;
                _pipe = pipe;
                _reader = reader;
                _writer = writer;
                _connectionCancellation = connectionCancellation;
                _readerTask = ReadMessagesAsync(reader, generation, connectionCancellation.Token);
            }

            return generation;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task ReadMessagesAsync(
        StreamReader reader,
        int generation,
        CancellationToken cancellationToken)
    {
        Exception? disconnectException = null;
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
                    throw new InvalidDataException(
                        $"Unsupported audio host protocol version {message.Version}.");
                }

                switch (message.Kind)
                {
                    case AudioPlaybackIpcProtocol.ResponseKind:
                        if (_pendingRequests.TryRemove(message.Id, out var completion))
                        {
                            completion.TrySetResult(message);
                        }
                        break;
                    case AudioPlaybackIpcProtocol.EventKind:
                        DispatchEvent(message);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            disconnectException = ex;
        }
        finally
        {
            HandleConnectionLost(generation, disconnectException);
        }
    }

    private void DispatchEvent(AudioPlaybackIpcMessage message)
    {
        switch (message.Name)
        {
            case AudioPlaybackIpcProtocol.StateChangedEvent:
                PublishState(AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackState>(message.Payload));
                break;
            case AudioPlaybackIpcProtocol.PlaybackEndedEvent:
                RaiseEventSafely(
                    PlaybackEnded,
                    new AudioPlaybackEndedEventArgs(
                        AudioPlaybackIpcProtocol.DeserializePayload<AudioPlaybackRequest>(message.Payload)));
                break;
        }
    }

    private void PublishState(AudioPlaybackState state)
    {
        lock (_stateLock)
        {
            _lastState = state;
            _lastStateTimestamp = Stopwatch.GetTimestamp();
        }

        RaiseEventSafely(StateChanged, new AudioPlaybackStateChangedEventArgs(state));
    }

    private long BeginPlaybackSession()
    {
        CancellationTokenSource? previousRecovery;
        long version;
        lock (_stateLock)
        {
            version = ++_playbackSessionVersion;
            _activeRequest = null;
            _automaticRecoveryAttempts = 0;
            previousRecovery = _recoveryCancellation;
            _recoveryCancellation = new CancellationTokenSource();
        }

        previousRecovery?.Cancel();
        previousRecovery?.Dispose();
        return version;
    }

    private void EndPlaybackSession(long sessionVersion)
    {
        CancellationTokenSource? recoveryCancellation = null;
        lock (_stateLock)
        {
            if (sessionVersion != _playbackSessionVersion)
            {
                return;
            }

            _activeRequest = null;
            recoveryCancellation = _recoveryCancellation;
            _recoveryCancellation = null;
        }

        recoveryCancellation?.Cancel();
        recoveryCancellation?.Dispose();
    }

    private long StartMetadataLoad(AudioPlaybackRequest request)
    {
        CancellationTokenSource? previousCancellation;
        CancellationTokenSource cancellation;
        long generation;
        var fallbackMetadata = new AudioMediaMetadata(
            request.FilePath,
            FirstNonEmpty(request.Title, Path.GetFileNameWithoutExtension(request.FilePath)),
            Artist: null,
            Album: null,
            AlbumArt: null);
        lock (_metadataLock)
        {
            generation = ++_metadataGeneration;
            previousCancellation = _metadataCancellation;
            cancellation = new CancellationTokenSource();
            _metadataCancellation = cancellation;
            PublishMetadata(fallbackMetadata);
        }

        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        _ = LoadAndPublishMetadataAsync(request, generation, cancellation);
        return generation;
    }

    private async Task LoadAndPublishMetadataAsync(
        AudioPlaybackRequest request,
        long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            var metadata = await _audioMetadataService
                .GetMetadataAsync(request.FilePath, cancellation.Token)
                .ConfigureAwait(false);
            var mappedMetadata = new AudioMediaMetadata(
                metadata.FilePath,
                FirstNonEmpty(
                    request.Title,
                    metadata.Title,
                    Path.GetFileNameWithoutExtension(request.FilePath)),
                metadata.Artist,
                metadata.Album,
                metadata.AlbumArt,
                metadata.Lyrics);
            lock (_metadataLock)
            {
                if (generation != _metadataGeneration ||
                    !ReferenceEquals(_metadataCancellation, cancellation) ||
                    cancellation.IsCancellationRequested)
                {
                    return;
                }

                // Serialize metadata publication with cancellation so an old
                // artwork event cannot overtake Close or the next Play request.
                PublishMetadata(mappedMetadata);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // The fallback title is already visible; metadata is best-effort UI data.
        }
        finally
        {
            lock (_metadataLock)
            {
                if (ReferenceEquals(_metadataCancellation, cancellation))
                {
                    _metadataCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void CancelMetadataLoad()
    {
        CancellationTokenSource? cancellation;
        lock (_metadataLock)
        {
            ++_metadataGeneration;
            cancellation = _metadataCancellation;
            _metadataCancellation = null;
        }

        cancellation?.Cancel();
    }

    private void CancelMetadataLoad(long generation)
    {
        CancellationTokenSource? cancellation = null;
        lock (_metadataLock)
        {
            if (generation != _metadataGeneration)
            {
                return;
            }

            ++_metadataGeneration;
            cancellation = _metadataCancellation;
            _metadataCancellation = null;
        }

        cancellation?.Cancel();
    }

    private void PublishMetadata(AudioMediaMetadata metadata) =>
        RaiseEventSafely(MediaChanged, new AudioMediaChangedEventArgs(metadata));

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private void HandleConnectionLost(int generation, Exception? exception)
    {
        bool expected;
        Process? process;
        lock (_lifetimeLock)
        {
            if (generation != _connectionGeneration)
            {
                return;
            }

            expected = _expectedDisconnectGeneration == generation || _isDisposed;
            process = _hostProcess;
            _reader?.Dispose();
            _writer?.Dispose();
            _pipe?.Dispose();
            _connectionCancellation?.Dispose();
            _connectionCancellation = null;
            _reader = null;
            _writer = null;
            _pipe = null;
            _hostProcess = null;
            _readerTask = null;
        }

        var hostTerminationTask = process is not null
            ? TerminateAndDisposeHostAsync(process)
            : Task.CompletedTask;

        var hostException = exception as AudioPlaybackHostException
            ?? new AudioPlaybackHostException(
                exception?.Message ?? "The audio host process exited unexpectedly.",
                exception ?? new EndOfStreamException());
        foreach (var pending in _pendingRequests.ToArray())
        {
            if (_pendingRequests.TryRemove(pending.Key, out var completion))
            {
                completion.TrySetException(hostException);
            }
        }

        if (expected)
        {
            return;
        }

        AudioPlaybackState previousState;
        AudioPlaybackRequest? recoveryRequest = null;
        CancellationToken recoveryToken = default;
        long playbackSessionVersion = 0;
        lock (_stateLock)
        {
            previousState = ProjectPlaybackState(_lastState, _lastStateTimestamp);
            if (_activeRequest is not null &&
                previousState.Status is (
                    AudioPlaybackStatus.Loading or
                    AudioPlaybackStatus.Playing or
                    AudioPlaybackStatus.Paused) &&
                _automaticRecoveryAttempts == 0 &&
                _recoveryCancellation is { IsCancellationRequested: false } recoveryCancellation)
            {
                _automaticRecoveryAttempts++;
                recoveryRequest = _activeRequest;
                recoveryToken = recoveryCancellation.Token;
                playbackSessionVersion = _playbackSessionVersion;
            }
        }

        if (recoveryRequest is not null)
        {
            PublishState(previousState with
            {
                Status = AudioPlaybackStatus.Loading,
                ErrorMessage = null
            });
            _ = RecoverPlaybackAsync(
                recoveryRequest,
                previousState,
                playbackSessionVersion,
                recoveryToken,
                hostTerminationTask);
            return;
        }

        _ = hostTerminationTask;

        if (previousState.Status != AudioPlaybackStatus.None)
        {
            PublishState(previousState with
            {
                Status = AudioPlaybackStatus.Failed,
                ErrorMessage = hostException.Message
            });
        }
    }

    private static AudioPlaybackState ProjectPlaybackState(
        AudioPlaybackState state,
        long stateTimestamp)
    {
        if (state.Status != AudioPlaybackStatus.Playing || stateTimestamp == 0)
        {
            return state;
        }

        var projectedPosition = state.Position +
            Stopwatch.GetElapsedTime(stateTimestamp).TotalSeconds *
            Math.Clamp(state.Speed, 0.25, 4);
        if (state.Duration > 0)
        {
            projectedPosition = Math.Clamp(projectedPosition, 0, state.Duration);
        }

        return state with { Position = Math.Max(0, projectedPosition) };
    }

    private async Task RecoverPlaybackAsync(
        AudioPlaybackRequest request,
        AudioPlaybackState previousState,
        long playbackSessionVersion,
        CancellationToken cancellationToken,
        Task previousHostTermination)
    {
        try
        {
            await previousHostTermination.WaitAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            EnsurePlaybackSessionCurrent(request, playbackSessionVersion, cancellationToken);
            await SendVoidRequestAsync(
                AudioPlaybackIpcProtocol.PlayOperation,
                request,
                cancellationToken).ConfigureAwait(false);
            await SendVoidRequestAsync(
                AudioPlaybackIpcProtocol.SetPausedOperation,
                new AudioPlaybackBooleanValue(true),
                cancellationToken).ConfigureAwait(false);
            await SendVoidRequestAsync(
                AudioPlaybackIpcProtocol.SetVolumeOperation,
                new AudioPlaybackDoubleValue(previousState.Volume),
                cancellationToken).ConfigureAwait(false);
            await SendVoidRequestAsync(
                AudioPlaybackIpcProtocol.SetMutedOperation,
                new AudioPlaybackBooleanValue(previousState.IsMuted),
                cancellationToken).ConfigureAwait(false);
            await SendVoidRequestAsync(
                AudioPlaybackIpcProtocol.SetSpeedOperation,
                new AudioPlaybackDoubleValue(previousState.Speed),
                cancellationToken).ConfigureAwait(false);
            if (previousState.Position > 0)
            {
                await SendVoidRequestAsync(
                    AudioPlaybackIpcProtocol.SeekAbsoluteOperation,
                    new AudioPlaybackDoubleValue(previousState.Position),
                    cancellationToken).ConfigureAwait(false);
            }

            EnsurePlaybackSessionCurrent(request, playbackSessionVersion, cancellationToken);
            await SendVoidRequestAsync(
                AudioPlaybackIpcProtocol.SetPausedOperation,
                new AudioPlaybackBooleanValue(
                    previousState.Status == AudioPlaybackStatus.Paused),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var shouldPublishFailure = false;
            lock (_stateLock)
            {
                if (playbackSessionVersion == _playbackSessionVersion &&
                    ReferenceEquals(_activeRequest, request))
                {
                    _activeRequest = null;
                    shouldPublishFailure = true;
                }
            }

            if (shouldPublishFailure)
            {
                PublishState(previousState with
                {
                    Status = AudioPlaybackStatus.Failed,
                    ErrorMessage = $"The audio host could not recover: {ex.GetBaseException().Message}"
                });
            }
        }
    }

    private void EnsurePlaybackSessionCurrent(
        AudioPlaybackRequest request,
        long playbackSessionVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (playbackSessionVersion != _playbackSessionVersion ||
                !ReferenceEquals(_activeRequest, request))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    private async Task WriteMessageAsync(
        string message,
        int generation,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StreamWriter writer;
            lock (_lifetimeLock)
            {
                if (generation != _connectionGeneration ||
                    _writer is null ||
                    _pipe is not { IsConnected: true })
                {
                    throw new AudioPlaybackHostException("The audio host connection is not available.");
                }

                writer = _writer;
            }

            await writer.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task SendCancellationSafelyAsync(long requestId, int generation)
    {
        try
        {
            var message = AudioPlaybackIpcProtocol.Serialize(
                AudioPlaybackIpcProtocol.CancelKind,
                requestId);
            await WriteMessageAsync(message, generation, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The original request already observes cancellation; a disconnected host needs no cancel message.
        }
    }

    private async Task ShutdownConnectionAsync(int generation, bool waitForGracefulExit)
    {
        Task? readerTask;
        CancellationTokenSource? connectionCancellation;
        lock (_lifetimeLock)
        {
            if (generation != _connectionGeneration)
            {
                return;
            }

            readerTask = _readerTask;
            connectionCancellation = _connectionCancellation;
        }

        if (readerTask is not null && readerTask.Id != Task.CurrentId)
        {
            if (waitForGracefulExit)
            {
                var gracefulDelay = Task.Delay(HostExitTimeout);
                var completedTask = await Task.WhenAny(readerTask, gracefulDelay).ConfigureAwait(false);
                if (ReferenceEquals(completedTask, readerTask))
                {
                    await readerTask.ConfigureAwait(false);
                    return;
                }
            }

            connectionCancellation?.Cancel();
            var cancellationDelay = Task.Delay(HostExitTimeout);
            var stoppedTask = await Task.WhenAny(readerTask, cancellationDelay).ConfigureAwait(false);
            if (ReferenceEquals(stoppedTask, readerTask))
            {
                await readerTask.ConfigureAwait(false);
                return;
            }
        }
        else
        {
            connectionCancellation?.Cancel();
        }

        HandleConnectionLost(generation, exception: null);
    }

    private static string ResolveHostExecutablePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("KOUKEI_AUDIO_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var fullOverridePath = Path.GetFullPath(overridePath);
            if (File.Exists(fullOverridePath))
            {
                return fullOverridePath;
            }

            throw new FileNotFoundException("The configured audio host executable was not found.", fullOverridePath);
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "AudioHost", HostExecutableName),
            Path.Combine(AppContext.BaseDirectory, HostExecutableName)
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"{HostExecutableName} was not found beside the application. Rebuild Koukei.UI to deploy the audio host.",
            candidates[0]);
    }

    private static async Task TerminateAndDisposeHostAsync(Process process)
    {
        using (process)
        {
            try
            {
                var exitTask = process.WaitForExitAsync();
                var exitDelay = Task.Delay(HostExitTimeout);
                if (!ReferenceEquals(
                        await Task.WhenAny(exitTask, exitDelay).ConfigureAwait(false),
                        exitTask))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between the timeout and the kill request.
                    }

                    _ = await Task.WhenAny(exitTask, Task.Delay(HostExitTimeout)).ConfigureAwait(false);
                }
            }
            catch
            {
                // Process cleanup must not surface through the playback event loop.
            }
        }
    }

    private void RaiseEventSafely<TEventArgs>(
        EventHandler<TEventArgs>? eventHandler,
        TEventArgs eventArgs)
        where TEventArgs : EventArgs
    {
        if (eventHandler is null)
        {
            return;
        }

        foreach (EventHandler<TEventArgs> handler in eventHandler.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
                // A UI subscriber must not terminate the host connection reader.
            }
        }
    }
}
