using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using Koukei.Ffmpeg;

namespace Koukei.Audio;

public sealed class SoundFlowAudioPlaybackService : IAudioPlaybackService, IAsyncDisposable
{
    private const uint PlaybackPeriodMilliseconds = 40;
    private const uint PlaybackBufferPeriods = 3;

    private static readonly TimeSpan StateUpdateInterval = TimeSpan.FromMilliseconds(500);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IAudioMetadataService _audioMetadataService;
    private readonly object _stateLock = new();
    private AudioEngine? _engine;
    private AudioPlaybackDevice? _device;
    private RecoverableSoundPlayer? _soundPlayer;
    private CancellationTokenSource? _stateLoopCancellation;
    private Task? _stateLoopTask;
    private AudioPlaybackState _state = AudioPlaybackState.Empty;
    private AudioMediaMetadata? _metadata;
    private AudioFileMetadata? _fileMetadata;
    private AudioPlaybackRequest? _currentRequest;
    private long _stateLoopGeneration;
    private bool _hasRaisedPlaybackEnded;
    private bool _isDisposed;

    public SoundFlowAudioPlaybackService()
        : this(new FfmpegAudioMetadataService(
            new FfmpegMediaProbe(),
            includeAttachedPictures: false))
    {
    }

    public SoundFlowAudioPlaybackService(IAudioMetadataService audioMetadataService)
    {
        _audioMetadataService = audioMetadataService ?? throw new ArgumentNullException(nameof(audioMetadataService));
    }

    public event EventHandler<AudioPlaybackStateChangedEventArgs>? StateChanged;

    public event EventHandler<AudioMediaChangedEventArgs>? MediaChanged;

    public event EventHandler<AudioPlaybackEndedEventArgs>? PlaybackEnded;

    public async Task PlayAsync(AudioPlaybackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            StopCurrentPlaybackCore();
            if (!File.Exists(request.FilePath))
            {
                throw new FileNotFoundException("The audio file does not exist.", request.FilePath);
            }

            _currentRequest = request;
            lock (_stateLock)
            {
                _hasRaisedPlaybackEnded = false;
            }
            _metadata = await ReadInitialMetadataAsync(request, cancellationToken).ConfigureAwait(false);
            PublishMetadata(_metadata);
            PublishState(AudioPlaybackState.Empty with { Status = AudioPlaybackStatus.Loading });

            PlayWithSoundFlowCore(request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PublishState(AudioPlaybackState.Empty with
            {
                Status = AudioPlaybackStatus.Failed,
                ErrorMessage = ex.Message
            });
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AudioPlaybackState> GetPlaybackStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            return Task.FromResult(_state);
        }
    }

    public async Task SetPausedAsync(bool isPaused, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_soundPlayer is null)
            {
                return;
            }

            if (isPaused)
            {
                _soundPlayer.Pause();
            }
            else
            {
                _soundPlayer.Play();
            }

            PublishSoundFlowState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SeekAbsoluteAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(seconds))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var player = _soundPlayer;
            if (player is not null &&
                await Task.Run(() => player.SeekSafely(seconds), cancellationToken).ConfigureAwait(false))
            {
                PublishSoundFlowState();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        volume = Math.Clamp(volume, 0, 100);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_soundPlayer is not null)
            {
                _soundPlayer.Volume = (float)(volume / 100d);
                PublishSoundFlowState();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_soundPlayer is not null)
            {
                _soundPlayer.Mute = isMuted;
                PublishSoundFlowState();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        speed = Math.Clamp(speed, 0.25, 4);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_soundPlayer is not null)
            {
                _soundPlayer.PlaybackSpeed = (float)speed;
                PublishSoundFlowState();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopStateLoop();
            if (_soundPlayer is not null)
            {
                _soundPlayer.StopSafely();
                PublishSoundFlowState();
            }
            else
            {
                PublishState(AudioPlaybackState.Empty with { Status = AudioPlaybackStatus.Stopped });
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isDisposed)
            {
                return;
            }

            StopCurrentPlaybackCore();
            _device?.Stop();
            _device?.Dispose();
            _device = null;
            _engine?.Dispose();
            _engine = null;
            _metadata = null;
            _fileMetadata = null;
            _currentRequest = null;
            PublishState(AudioPlaybackState.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await CloseAsync().ConfigureAwait(false);
        _isDisposed = true;
        _gate.Dispose();
    }

    private void PlayWithSoundFlowCore(AudioPlaybackRequest request)
    {
        EnsureSoundFlowInitialized();
        var stream = new FileStream(
            request.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        StreamDataProvider? provider = null;
        RecoverableSoundPlayer? player = null;
        try
        {
            var playbackFormat = GetPlaybackFormat();
            stream.Position = 0;
            provider = new StreamDataProvider(_engine!, playbackFormat, stream);
            playbackFormat = playbackFormat with
            {
                Channels = provider.FormatInfo?.ChannelCount > 0
                    ? provider.FormatInfo.ChannelCount
                    : playbackFormat.Channels,
                Layout = AudioFormat.GetLayoutFromChannels(
                    provider.FormatInfo?.ChannelCount > 0
                        ? provider.FormatInfo.ChannelCount
                        : playbackFormat.Channels),
                SampleRate = provider.SampleRate > 0
                    ? provider.SampleRate
                    : playbackFormat.SampleRate
            };
            EnsurePlaybackDevice(playbackFormat);
            player = new RecoverableSoundPlayer(_engine!, playbackFormat, provider)
            {
                Volume = 1f
            };
            player.SetTimeStretchQuality(WsolaPerformancePreset.Balanced);
            player.PlaybackEnded += SoundPlayer_PlaybackEnded;
            player.PlaybackFailed += SoundPlayer_PlaybackFailed;
            _device!.MasterMixer.AddComponent(player);

            _soundPlayer = player;
            var tags = provider.FormatInfo?.Tags;
            var currentMetadata = _metadata;
            var title = FirstNonEmpty(
                request.Title,
                tags?.Title,
                currentMetadata?.Title,
                Path.GetFileNameWithoutExtension(request.FilePath));
            _metadata = new AudioMediaMetadata(
                request.FilePath,
                title,
                NullIfEmpty(tags?.Artist) ?? currentMetadata?.Artist,
                NullIfEmpty(tags?.Album) ?? currentMetadata?.Album,
                tags?.AlbumArt is { Length: > 0 } albumArt
                    ? albumArt
                    : currentMetadata?.AlbumArt,
                currentMetadata?.Lyrics);
            PublishMetadata(_metadata);

            player.Play();
            StartStateLoop();
            PublishSoundFlowState();

            provider = null;
            player = null;
        }
        finally
        {
            player?.DisposeSafely();
            provider?.Dispose();
            if (provider is null && player is null && _soundPlayer is null)
            {
                stream.Dispose();
            }
        }
    }

    private void EnsureSoundFlowInitialized()
    {
        if (_engine is not null)
        {
            return;
        }

        var engine = new MiniAudioEngine([
            MiniAudioBackend.Wasapi,
            MiniAudioBackend.DirectSound,
            MiniAudioBackend.WinMm
        ]);
        try
        {
            engine.RegisterCodecFactory(new FFmpegCodecFactory());
            _engine = engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private void EnsurePlaybackDevice(AudioFormat format)
    {
        if (_device is not null && _device.Format == format)
        {
            if (!_device.IsRunning)
            {
                _device.Start();
            }
            return;
        }

        _device?.Stop();
        _device?.Dispose();
        _device = null;

        var config = new MiniAudioDeviceConfig
        {
            // WinUI page construction and image realization can briefly create
            // substantial CPU/GC pressure. A media player can trade a little
            // control latency for enough buffered audio to ride out that work
            // without an audible underrun.
            PeriodSizeInFrames = (uint)Math.Max(
                512L,
                (long)format.SampleRate * PlaybackPeriodMilliseconds / 1000),
            Periods = PlaybackBufferPeriods,
            Playback = new DeviceSubConfig
            {
                ShareMode = ShareMode.Shared
            }
        };
        _device = _engine!.InitializePlaybackDevice(null, format, config);
        _device.Start();
    }

    private void StopCurrentPlaybackCore()
    {
        StopStateLoop();
        DisposeSoundPlayerCore();
    }

    private void DisposeSoundPlayerCore()
    {
        var player = _soundPlayer;
        _soundPlayer = null;
        if (player is null)
        {
            return;
        }

        player.PlaybackEnded -= SoundPlayer_PlaybackEnded;
        player.PlaybackFailed -= SoundPlayer_PlaybackFailed;
        try
        {
            player.StopSafely();
        }
        catch
        {
        }

        try
        {
            _device?.MasterMixer.RemoveComponent(player);
        }
        catch
        {
        }

        try
        {
            player.DisposeSafely();
        }
        catch
        {
        }
    }

    private void StartStateLoop()
    {
        StopStateLoop();
        var cancellation = new CancellationTokenSource();
        long generation;
        lock (_stateLock)
        {
            generation = ++_stateLoopGeneration;
        }

        _stateLoopCancellation = cancellation;
        _stateLoopTask = Task.Run(() => RunStateLoopAsync(cancellation.Token, generation));
    }

    private void StopStateLoop()
    {
        lock (_stateLock)
        {
            ++_stateLoopGeneration;
        }

        var cancellation = _stateLoopCancellation;
        var stateLoopTask = _stateLoopTask;
        _stateLoopCancellation = null;
        _stateLoopTask = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (stateLoopTask is null)
        {
            cancellation.Dispose();
            return;
        }

        // Never synchronously join this task while holding the playback gate: a
        // state subscriber may itself be waiting for a gated playback operation.
        _ = ObserveStateLoopCompletionAsync(stateLoopTask, cancellation);
    }

    private static async Task ObserveStateLoopCompletionAsync(
        Task stateLoopTask,
        CancellationTokenSource cancellation)
    {
        try
        {
            await stateLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The loop is stopped; playback cleanup can continue safely.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task RunStateLoopAsync(
        CancellationToken cancellationToken,
        long generation)
    {
        try
        {
            using var timer = new PeriodicTimer(StateUpdateInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                PublishSoundFlowState(generation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void PublishSoundFlowState()
    {
        if (!TryCreateSoundFlowState(out var state))
        {
            return;
        }

        PublishState(state);
    }

    private void PublishSoundFlowState(long generation)
    {
        if (!TryCreateSoundFlowState(out var state))
        {
            return;
        }

        lock (_stateLock)
        {
            if (generation != _stateLoopGeneration)
            {
                return;
            }

            _state = state;
            // Keep the generation check and subscriber notification ordered with
            // StopStateLoop so a stale Playing event cannot follow Stopped/Loading.
            RaiseEventSafely(StateChanged, new AudioPlaybackStateChangedEventArgs(state));
        }
    }

    private bool TryCreateSoundFlowState(out AudioPlaybackState state)
    {
        var player = _soundPlayer;
        if (player is null || !player.TryGetPlaybackSnapshot(out var snapshot))
        {
            state = AudioPlaybackState.Empty;
            return false;
        }

        var status = snapshot.State switch
        {
            PlaybackState.Playing => AudioPlaybackStatus.Playing,
            PlaybackState.Paused => AudioPlaybackStatus.Paused,
            PlaybackState.Stopped => AudioPlaybackStatus.Stopped,
            _ => AudioPlaybackStatus.None
        };
        state = new AudioPlaybackState(
            status,
            snapshot.Position,
            snapshot.Duration,
            snapshot.Volume,
            snapshot.IsMuted,
            snapshot.Speed,
            snapshot.IsSeekable,
            AudioPlaybackBackend.SoundFlow,
            ErrorMessage: null);
        return true;
    }

    private void SoundPlayer_PlaybackEnded(object? sender, EventArgs args)
    {
        if (sender is RecoverableSoundPlayer player)
        {
            _ = ReleaseEndedSoundPlayerAsync(player);
        }
    }

    private async Task ReleaseEndedSoundPlayerAsync(RecoverableSoundPlayer endedPlayer)
    {
        // Leave SoundFlow's playback callback before disposing the component that
        // raised it; an uncontended semaphore wait can otherwise complete inline.
        await Task.Yield();

        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_isDisposed || !ReferenceEquals(_soundPlayer, endedPlayer))
            {
                return;
            }

            StopStateLoop();
            AudioPlaybackState current;
            lock (_stateLock)
            {
                current = _state;
            }

            DisposeSoundPlayerCore();
            PublishState(current with
            {
                Status = AudioPlaybackStatus.Stopped,
                Position = current.Duration
            });
            RaisePlaybackEndedOnce();
        }
        catch
        {
            // End-of-playback cleanup is best-effort and must not fault the audio callback.
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SoundPlayer_PlaybackFailed(object? sender, SoundFlowPlaybackFailedEventArgs args)
    {
        if (sender is RecoverableSoundPlayer player)
        {
            _ = RecoverFromSoundFlowFailureAsync(player, args.Exception);
        }
    }

    private async Task RecoverFromSoundFlowFailureAsync(
        RecoverableSoundPlayer failedPlayer,
        Exception soundFlowException)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_isDisposed || !ReferenceEquals(_soundPlayer, failedPlayer))
            {
                return;
            }

            AudioPlaybackState stateBeforeFailure;
            lock (_stateLock)
            {
                stateBeforeFailure = _state;
            }

            StopStateLoop();
            DisposeSoundPlayerCore();
            PublishState(stateBeforeFailure with
            {
                Status = AudioPlaybackStatus.Failed,
                Backend = AudioPlaybackBackend.SoundFlow,
                ErrorMessage = soundFlowException.GetBaseException().Message
            });
        }
        catch
        {
            // Recovery is fire-and-forget and must not create an unobserved task exception.
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PublishState(AudioPlaybackState state)
    {
        lock (_stateLock)
        {
            _state = state;
        }
        RaiseEventSafely(StateChanged, new AudioPlaybackStateChangedEventArgs(state));
    }

    private void PublishMetadata(AudioMediaMetadata metadata)
    {
        RaiseEventSafely(MediaChanged, new AudioMediaChangedEventArgs(metadata));
    }

    private void RaisePlaybackEndedOnce()
    {
        AudioPlaybackRequest? request;
        lock (_stateLock)
        {
            if (_hasRaisedPlaybackEnded)
            {
                return;
            }
            _hasRaisedPlaybackEnded = true;
            request = _currentRequest;
        }

        if (request is not null)
        {
            RaiseEventSafely(PlaybackEnded, new AudioPlaybackEndedEventArgs(request));
        }
    }

    private void RaiseEventSafely(EventHandler? eventHandler, EventArgs eventArgs)
    {
        if (eventHandler is null)
        {
            return;
        }

        foreach (EventHandler handler in eventHandler.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
                // Playback cleanup and native loops must survive subscriber failures.
            }
        }
    }

    private void RaiseEventSafely<TEventArgs>(EventHandler<TEventArgs>? eventHandler, TEventArgs eventArgs)
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
                // Playback cleanup and native loops must survive subscriber failures.
            }
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<AudioMediaMetadata> ReadInitialMetadataAsync(
        AudioPlaybackRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await _audioMetadataService
                .GetMetadataAsync(request.FilePath, cancellationToken)
                .ConfigureAwait(false);
            _fileMetadata = metadata;
            return new AudioMediaMetadata(
                request.FilePath,
                FirstNonEmpty(request.Title, metadata.Title, Path.GetFileNameWithoutExtension(request.FilePath)),
                metadata.Artist,
                metadata.Album,
                metadata.AlbumArt,
                metadata.Lyrics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _fileMetadata = null;
        }

        return new AudioMediaMetadata(
            request.FilePath,
            FirstNonEmpty(request.Title, Path.GetFileNameWithoutExtension(request.FilePath)),
            Artist: null,
            Album: null,
            AlbumArt: null);
    }

    private AudioFormat GetPlaybackFormat()
    {
        var channelCount = _fileMetadata?.ChannelCount;
        var sampleRate = _fileMetadata?.SampleRate;
        if (channelCount is > 0 && sampleRate is > 0)
        {
            return new AudioFormat
            {
                Format = SampleFormat.F32,
                Channels = channelCount.Value,
                Layout = AudioFormat.GetLayoutFromChannels(channelCount.Value),
                SampleRate = sampleRate.Value
            };
        }

        return AudioFormat.DvdHq;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
