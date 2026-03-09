using Koukei.Mpv;
using Koukei.Mpv.Interop;
using System.Globalization;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace Koukei.Video;

public sealed class MpvVideoPlaybackService : IVideoPlaybackService, IAsyncDisposable
{
    private const ulong FullscreenObservationId = 1;
    private const ulong AlwaysOnTopObservationId = 2;
    private const ulong PauseObservationId = 3;
    private const ulong TimePositionObservationId = 4;
    private const ulong DurationObservationId = 5;
    private const ulong VolumeObservationId = 6;
    private const ulong MuteObservationId = 7;
    private const ulong SpeedObservationId = 8;
    private const ulong SeekableObservationId = 9;
    private const ulong PlaylistPositionObservationId = 10;
    private const ulong PlaylistCountObservationId = 11;
    private const ulong EofReachedObservationId = 12;
    private const string DisplaySwapChainPropertyName = "display-swapchain";
    private const string D3D11CompositionSizeOptionName = "d3d11-composition-size";

    private enum MpvPlaybackMode
    {
        None,
        Standalone,
        Window,
        D3D11Composition
    }

    private static readonly string[] BundledScriptLoadOrder =
    {
        "thumbfast.lua",
        "open-file.lua",
        "pip_lite.lua",
        "pause_indicator_lite.lua"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<MpvError>> _pendingCommands = new();
    private readonly object _chapterLock = new();
    private readonly object _playbackStateLock = new();
    private readonly object _scriptLoadLock = new();
    private readonly List<string> _bundledScriptPaths = new();
    private CancellationTokenSource? _eventLoopCancellation;
    private MpvHandle _eventLoopHandle;
    private Task? _eventLoopTask;
    private MpvHandle _handle;
    private IReadOnlyList<VideoChapterInfo> _chapters = Array.Empty<VideoChapterInfo>();
    private IntPtr _windowHandle;
    private bool _areBundledScriptsLoaded;
    private bool _isInitialVideoSizePending;
    private bool _hasRaisedPlaybackEndedForCurrentFile;
    private int _compositionPixelHeight;
    private int _compositionPixelWidth;
    private int _closeRequestCount;
    private string? _currentFilePath;
    private long _displaySwapChain;
    private bool _isDisposed;
    private bool _isInitialized;
    private VideoPlaybackState _playbackState = VideoPlaybackState.Empty;
    private MpvPlaybackMode _playbackMode;
    private volatile bool _isShutdown;
    private long _nextAsyncCommandId;

    public event EventHandler<VideoWindowStateChangedEventArgs>? WindowStateChanged;

    public event EventHandler<VideoSwapChainChangedEventArgs>? SwapChainChanged;

    public event EventHandler<VideoPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    public event EventHandler<VideoSizeChangedEventArgs>? VideoSizeChanged;

    public event EventHandler<VideoChaptersChangedEventArgs>? ChaptersChanged;

    public event EventHandler<VideoPlaybackEndedEventArgs>? PlaybackEnded;

    public event EventHandler? PlaybackClosed;

    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return ExecuteAsync(() =>
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The media file does not exist.", filePath);
            }

            EnsureInitialized(windowHandle: IntPtr.Zero);
            _currentFilePath = filePath;
            _hasRaisedPlaybackEndedForCurrentFile = false;
            ResetPlaybackStateForNewFile();
            ThrowIfError("mpv loadfile failed", MpvCommandInvoker.Invoke(_handle, "loadfile", filePath, "replace"));
        }, cancellationToken);
    }

    public Task PlayAsync(string filePath, IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required for embedded playback.", nameof(windowHandle));
        }

        return ExecuteAsync(() =>
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The media file does not exist.", filePath);
            }

            EnsureInitialized(windowHandle);
            _currentFilePath = filePath;
            _hasRaisedPlaybackEndedForCurrentFile = false;
            ResetPlaybackStateForNewFile();
            ThrowIfError("mpv loadfile failed", MpvCommandInvoker.Invoke(_handle, "loadfile", filePath, "replace"));
        }, cancellationToken);
    }

    public Task PlayWithD3D11CompositionAsync(
        string filePath,
        int pixelWidth,
        int pixelHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ValidateCompositionSize(pixelWidth, pixelHeight);

        return ExecuteAsync(() =>
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The media file does not exist.", filePath);
            }

            EnsureD3D11CompositionInitialized(pixelWidth, pixelHeight);
            _currentFilePath = filePath;
            _hasRaisedPlaybackEndedForCurrentFile = false;
            ResetPlaybackStateForNewFile();
            ThrowIfError("mpv loadfile failed", MpvCommandInvoker.Invoke(_handle, "loadfile", filePath, "replace"));
            TryNotifyDisplaySwapChainChanged(_handle);
        }, cancellationToken);
    }

    public Task PlayPlaylistAsync(
        IReadOnlyList<string> filePaths,
        int startIndex,
        IntPtr windowHandle,
        CancellationToken cancellationToken = default)
    {
        ValidatePlaylistArguments(filePaths, startIndex);
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required for embedded playback.", nameof(windowHandle));
        }

        return ExecuteAsync(() =>
        {
            var normalizedPaths = ValidatePlaylistFiles(filePaths);
            EnsureInitialized(windowHandle);
            LoadPlaylistCore(normalizedPaths, startIndex);
        }, cancellationToken);
    }

    public Task PlayPlaylistWithD3D11CompositionAsync(
        IReadOnlyList<string> filePaths,
        int startIndex,
        int pixelWidth,
        int pixelHeight,
        CancellationToken cancellationToken = default)
    {
        ValidatePlaylistArguments(filePaths, startIndex);
        ValidateCompositionSize(pixelWidth, pixelHeight);

        return ExecuteAsync(() =>
        {
            var normalizedPaths = ValidatePlaylistFiles(filePaths);
            EnsureD3D11CompositionInitialized(pixelWidth, pixelHeight);
            LoadPlaylistCore(normalizedPaths, startIndex);
            TryNotifyDisplaySwapChainChanged(_handle);
        }, cancellationToken);
    }

    public Task SetD3D11CompositionSizeAsync(
        int pixelWidth,
        int pixelHeight,
        CancellationToken cancellationToken = default)
    {
        ValidateCompositionSize(pixelWidth, pixelHeight);

        return ExecuteAsync(() =>
        {
            if (!_isInitialized ||
                _handle.Handle == IntPtr.Zero ||
                _isShutdown ||
                _playbackMode != MpvPlaybackMode.D3D11Composition)
            {
                return;
            }

            SetD3D11CompositionSize(pixelWidth, pixelHeight);
            TryNotifyDisplaySwapChainChanged(_handle);
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError("mpv stop failed", MpvCommandInvoker.Invoke(_handle, "stop"));
        }, cancellationToken);
    }

    public Task<VideoPlaybackState> GetPlaybackStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_playbackStateLock)
        {
            return Task.FromResult(_playbackState);
        }
    }

    public Task<IReadOnlyList<VideoChapterInfo>> GetChaptersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_chapterLock)
        {
            return Task.FromResult<IReadOnlyList<VideoChapterInfo>>(_chapters.ToArray());
        }
    }

    public Task SetPausedAsync(bool isPaused, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                "mpv set pause failed",
                MpvNative.MpvSetPropertyString(_handle, "pause", isPaused ? "yes" : "no"));
        }, cancellationToken);
    }

    public Task SeekRelativeAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "The seek offset must be finite.");
        }

        return SeekAsync(seconds, "relative+exact", cancellationToken);
    }

    public Task SeekAbsoluteAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "The seek position must be finite.");
        }

        return SeekAsync(Math.Max(0, seconds), "absolute+exact", cancellationToken);
    }

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        return SetDoublePropertyAsync("volume", Math.Clamp(volume, 0, 100), "mpv set volume failed", cancellationToken);
    }

    public Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default)
    {
        return SetFlagPropertyAsync("mute", isMuted, cancellationToken);
    }

    public Task SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        return SetDoublePropertyAsync("speed", Math.Clamp(speed, 0.25, 4), "mpv set speed failed", cancellationToken);
    }

    public Task PlaylistPreviousAsync(CancellationToken cancellationToken = default)
    {
        if (!CanMovePlaylist(previous: true))
        {
            return Task.CompletedTask;
        }

        return InvokeCommandAsync("mpv playlist previous failed", cancellationToken, "playlist-prev", "weak");
    }

    public Task PlaylistNextAsync(CancellationToken cancellationToken = default)
    {
        if (!CanMovePlaylist(previous: false))
        {
            return Task.CompletedTask;
        }

        return InvokeCommandAsync("mpv playlist next failed", cancellationToken, "playlist-next", "weak");
    }

    public async Task AppendToPlaylistAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The media file does not exist.", filePath);
        }

        ulong commandId = 0;
        TaskCompletionSource<MpvError> completion = null!;
        var commandRegistered = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isInitialized ||
                _handle.Handle == IntPtr.Zero ||
                _isShutdown ||
                _playbackMode == MpvPlaybackMode.None)
            {
                throw new InvalidOperationException(
                    "Cannot add a video because there is no active video playback session.");
            }

            // Append to the current handle exactly as it is. In D3D11 composition mode
            // _windowHandle is intentionally zero; passing it to EnsureInitialized would
            // misclassify the session as Standalone and destroy the playing video.
            commandId = unchecked((ulong)Interlocked.Increment(ref _nextAsyncCommandId));
            completion = new TaskCompletionSource<MpvError>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingCommands.TryAdd(commandId, completion))
            {
                throw new InvalidOperationException("Could not register the mpv playlist command.");
            }
            commandRegistered = true;

            // Close/Dispose fail pending commands before waiting for _gate. Check
            // again after registration so a close request cannot slip between the
            // dictionary scan and this command becoming visible.
            if (Volatile.Read(ref _closeRequestCount) > 0)
            {
                throw new MpvException("The mpv player is closing.");
            }

            ThrowIfError(
                "mpv append playlist item failed",
                MpvCommandInvoker.InvokeAsynchronous(_handle, commandId, "loadfile", filePath, "append"));

            // Once libmpv accepts an asynchronous command it owns the outcome. Waiting
            // for its reply while retaining the service gate keeps later playlist
            // mutations ordered and prevents a caller-side timeout/cancellation from
            // reporting failure after libmpv has actually appended the item.
            var result = await completion.Task.ConfigureAwait(false);
            ThrowIfError("mpv append playlist item failed", result);
            RefreshPlaylistStateFromNative();
        }
        catch
        {
            if (commandRegistered)
            {
                _pendingCommands.TryRemove(commandId, out _);
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task PlayPlaylistItemAsync(int index, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                "mpv select playlist item failed",
                MpvCommandInvoker.Invoke(
                    _handle,
                    "playlist-play-index",
                    index.ToString(CultureInfo.InvariantCulture)));
            RefreshPlaylistStateFromNative();
        }, cancellationToken);
    }

    public Task MovePlaylistItemAsync(
        int index,
        int targetIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);

        return ExecuteAsync(() =>
        {
            if (!_isInitialized ||
                _handle.Handle == IntPtr.Zero ||
                _isShutdown ||
                index == targetIndex)
            {
                return;
            }

            // Swap adjacent entries until the source reaches its requested index.
            // This avoids playlist-move's asymmetric destination-index behavior
            // when a source entry is moved toward the end of the playlist.
            var direction = targetIndex > index ? 1 : -1;
            for (var currentIndex = index;
                 currentIndex != targetIndex;
                 currentIndex += direction)
            {
                var adjacentIndex = currentIndex + direction;
                var nativeSourceIndex = Math.Max(currentIndex, adjacentIndex);
                var nativeTargetIndex = Math.Min(currentIndex, adjacentIndex);
                ThrowIfError(
                    "mpv move playlist item failed",
                    MpvCommandInvoker.Invoke(
                        _handle,
                        "playlist-move",
                        nativeSourceIndex.ToString(CultureInfo.InvariantCulture),
                        nativeTargetIndex.ToString(CultureInfo.InvariantCulture)));
            }

            RefreshPlaylistStateFromNative();
        }, cancellationToken);
    }

    public Task RemovePlaylistItemAsync(int index, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                "mpv remove playlist item failed",
                MpvCommandInvoker.Invoke(
                    _handle,
                    "playlist-remove",
                    index.ToString(CultureInfo.InvariantCulture)));
            RefreshPlaylistStateFromNative();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<VideoTrackInfo>> GetTracksAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync<IReadOnlyList<VideoTrackInfo>>(ReadTracksCore, cancellationToken);
    }

    public Task SelectAudioTrackAsync(long trackId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trackId);
        return SetTrackPropertyAsync("aid", trackId.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    public Task SelectSubtitleTrackAsync(long? trackId, CancellationToken cancellationToken = default)
    {
        if (trackId is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trackId));
        }

        var value = trackId?.ToString(CultureInfo.InvariantCulture) ?? "no";
        return SetTrackPropertyAsync("sid", value, cancellationToken);
    }

    public Task AddAudioTrackAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return AddExternalTrackAsync("audio-add", filePath, cancellationToken);
    }

    public Task AddSubtitleTrackAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return AddExternalTrackAsync("sub-add", filePath, cancellationToken);
    }

    public Task CycleAudioTrackAsync(CancellationToken cancellationToken = default)
    {
        return InvokeCommandAsync("mpv cycle audio track failed", cancellationToken, "cycle", "audio");
    }

    public Task CycleSubtitleTrackAsync(CancellationToken cancellationToken = default)
    {
        return InvokeCommandAsync("mpv cycle subtitle track failed", cancellationToken, "cycle", "sub");
    }

    public Task ScreenshotAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            var screenshotPath = CreateScreenshotPath();
            ThrowIfError(
                "mpv screenshot failed",
                MpvCommandInvoker.Invoke(_handle, "screenshot-to-file", screenshotPath, "video"));
        }, cancellationToken);
    }

    public Task ToggleStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return InvokeCommandAsync(
            "mpv statistics overlay failed",
            cancellationToken,
            "script-binding",
            "stats/display-stats-toggle");
    }

    public Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken = default)
    {
        return SetFlagPropertyAsync("fullscreen", isFullscreen, cancellationToken);
    }

    public Task SetAlwaysOnTopAsync(bool isAlwaysOnTop, CancellationToken cancellationToken = default)
    {
        return SetFlagPropertyAsync("ontop", isAlwaysOnTop, cancellationToken);
    }

    public Task SendKeyPressAsync(string keyName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return SendInputKeyAsync("keypress", keyName, cancellationToken);
    }

    public Task SendKeyDownAsync(string keyName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return SendInputKeyAsync("keydown", keyName, cancellationToken);
    }

    public Task SendKeyUpAsync(string keyName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return SendInputKeyAsync("keyup", keyName, cancellationToken);
    }

    public Task SendMouseMoveAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                "mpv mouse move failed",
                MpvCommandInvoker.Invoke(
                    _handle,
                    "mouse",
                    x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture)));
        }, cancellationToken);
    }

    public Task SendMouseButtonAsync(
        int x,
        int y,
        string keyName,
        bool isPressed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                "mpv mouse position failed",
                MpvCommandInvoker.Invoke(
                    _handle,
                    "mouse",
                    x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture)));

            ThrowIfError(
                "mpv mouse button failed",
                MpvCommandInvoker.Invoke(_handle, isPressed ? "keydown" : "keyup", keyName));
        }, cancellationToken);
    }

    public Task SendMouseKeyPressAsync(int x, int y, string keyName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                "mpv mouse position failed",
                MpvCommandInvoker.Invoke(
                    _handle,
                    "mouse",
                    x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture)));

            ThrowIfError("mpv mouse keypress failed", MpvCommandInvoker.Invoke(_handle, "keypress", keyName));
        }, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _closeRequestCount);
        CancelPendingCommands();

        var gateEntered = false;
        try
        {
            // Once shutdown begins, complete native cleanup even if the caller later
            // cancels. CancelPendingCommands above releases an append waiting on reply.
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            gateEntered = true;
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            await Task.Run(() => DestroyCore(terminate: true), CancellationToken.None).ConfigureAwait(false);
        }
        catch (DllNotFoundException ex)
        {
            throw CreateMissingMpvException(ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new MpvException("The loaded libmpv library is incompatible with Koukei.", ex);
        }
        finally
        {
            if (gateEntered)
            {
                _gate.Release();
            }

            Interlocked.Decrement(ref _closeRequestCount);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        Interlocked.Increment(ref _closeRequestCount);
        CancelPendingCommands();

        var gateEntered = false;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            gateEntered = true;
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            DestroyCore(terminate: true);
        }
        finally
        {
            if (gateEntered)
            {
                _gate.Release();
            }

            Interlocked.Decrement(ref _closeRequestCount);
        }
    }

    private async Task ExecuteAsync(Action action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (Volatile.Read(ref _closeRequestCount) > 0)
            {
                throw new InvalidOperationException("The mpv player is closing.");
            }
            await Task.Run(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DllNotFoundException ex)
        {
            throw CreateMissingMpvException(ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new MpvException("The loaded libmpv library is incompatible with Koukei.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (Volatile.Read(ref _closeRequestCount) > 0)
            {
                throw new InvalidOperationException("The mpv player is closing.");
            }
            return await Task.Run(action, cancellationToken).ConfigureAwait(false);
        }
        catch (DllNotFoundException ex)
        {
            throw CreateMissingMpvException(ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new MpvException("The loaded libmpv library is incompatible with Koukei.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureInitialized(IntPtr windowHandle)
    {
        var playbackMode = windowHandle == IntPtr.Zero
            ? MpvPlaybackMode.Standalone
            : MpvPlaybackMode.Window;

        if (_isInitialized &&
            !_isShutdown &&
            _handle.Handle != IntPtr.Zero &&
            _windowHandle == windowHandle &&
            _playbackMode == playbackMode)
        {
            return;
        }

        DestroyCore(terminate: false);
        MpvNativeLibraryResolver.EnsureRegistered();

        _handle = MpvNative.MpvCreate();
        if (_handle.Handle == IntPtr.Zero)
        {
            throw new MpvException("mpv_create returned a null handle.");
        }

        _windowHandle = windowHandle;
        _playbackMode = playbackMode;

        if (windowHandle != IntPtr.Zero)
        {
            SetOptionInt64("wid", windowHandle.ToInt64());
        }

        SetOption("idle", "yes");
        SetOption("keep-open", "yes");
        SetOption("input-default-bindings", "yes");
        SetOption("input-vo-keyboard", "yes");
        _ = TrySetOption("border", "no");
        _ = TrySetOption("title-bar", "no");
        ConfigureApplicationOwnedUi();
        ConfigureBundledScripts();
        SetOption("terminal", "no");
        SetOption("msg-level", "all=warn");
        SetOption("demuxer-max-bytes", "64MiB");
        SetOption("demuxer-max-back-bytes", "16MiB");

        ThrowIfError("mpv initialize failed", MpvNative.MpvInitialize(_handle));

        _isShutdown = false;
        _isInitialized = true;
        ObserveWindowStateProperties();
        StartEventLoop();
        TryLoadBundledScripts(_handle);
    }

    private void LoadPlaylistCore(IReadOnlyList<string> filePaths, int startIndex)
    {
        var playlistPath = CreateTemporaryPlaylist(filePaths);
        try
        {
            _currentFilePath = filePaths[startIndex];
            _hasRaisedPlaybackEndedForCurrentFile = false;
            ResetPlaybackStateForNewFile();
            ThrowIfError(
                "mpv load playlist failed",
                MpvCommandInvoker.Invoke(_handle, "loadlist", playlistPath, "replace"));
            ThrowIfError(
                "mpv select initial playlist item failed",
                MpvCommandInvoker.Invoke(
                    _handle,
                    "playlist-play-index",
                    startIndex.ToString(CultureInfo.InvariantCulture)));
            RefreshPlaylistStateFromNative();

            if (!TryGetPropertyInt64(_handle, "playlist-count", out var playlistCount) ||
                playlistCount != filePaths.Count)
            {
                throw new MpvException(
                    $"mpv loaded {playlistCount} of {filePaths.Count} playlist items.");
            }
        }
        finally
        {
            try
            {
                File.Delete(playlistPath);
            }
            catch
            {
            }
        }
    }

    private static void ValidatePlaylistArguments(IReadOnlyList<string> filePaths, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (filePaths.Count == 0)
        {
            throw new ArgumentException("The playlist must contain at least one file.", nameof(filePaths));
        }

        if (startIndex < 0 || startIndex >= filePaths.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }
    }

    private static IReadOnlyList<string> ValidatePlaylistFiles(IReadOnlyList<string> filePaths)
    {
        var normalizedPaths = new string[filePaths.Count];
        for (var index = 0; index < filePaths.Count; index++)
        {
            var filePath = filePaths[index];
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Playlist file paths cannot be empty.", nameof(filePaths));
            }

            var normalizedPath = Path.GetFullPath(filePath);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException("A playlist media file does not exist.", normalizedPath);
            }

            normalizedPaths[index] = normalizedPath;
        }

        return normalizedPaths;
    }

    private static string CreateTemporaryPlaylist(IReadOnlyList<string> filePaths)
    {
        var playlistPath = Path.Combine(
            Path.GetTempPath(),
            $"Koukei-playback-{Guid.NewGuid():N}.m3u8");
        try
        {
            var lines = new string[filePaths.Count + 1];
            lines[0] = "#EXTM3U";
            for (var index = 0; index < filePaths.Count; index++)
            {
                lines[index + 1] = new Uri(filePaths[index], UriKind.Absolute).AbsoluteUri;
            }

            File.WriteAllLines(
                playlistPath,
                lines,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return playlistPath;
        }
        catch
        {
            try
            {
                File.Delete(playlistPath);
            }
            catch
            {
            }

            throw;
        }
    }

    private void EnsureD3D11CompositionInitialized(int pixelWidth, int pixelHeight)
    {
        if (_isInitialized &&
            !_isShutdown &&
            _handle.Handle != IntPtr.Zero &&
            _playbackMode == MpvPlaybackMode.D3D11Composition)
        {
            SetD3D11CompositionSize(pixelWidth, pixelHeight);
            return;
        }

        DestroyCore(terminate: false);
        MpvNativeLibraryResolver.EnsureRegistered();

        _handle = MpvNative.MpvCreate();
        if (_handle.Handle == IntPtr.Zero)
        {
            throw new MpvException("mpv_create returned a null handle.");
        }

        _windowHandle = IntPtr.Zero;
        _playbackMode = MpvPlaybackMode.D3D11Composition;
        _displaySwapChain = 0;

        SetOption("vo", "gpu-next,gpu");
        _ = TrySetOption("gpu-api", "d3d11");
        SetOption("gpu-context", "d3d11");
        SetOption("d3d11-output-mode", "composition");
        SetD3D11CompositionSizeOption(pixelWidth, pixelHeight);
        SetOption("idle", "yes");
        SetOption("keep-open", "yes");
        SetOption("input-default-bindings", "yes");
        SetOption("input-vo-keyboard", "no");
        ConfigureApplicationOwnedUi();
        _ = TrySetOption("load-scripts", "no");
        SetOption("terminal", "no");
        SetOption("msg-level", "all=warn");
        SetOption("demuxer-max-bytes", "64MiB");
        SetOption("demuxer-max-back-bytes", "16MiB");

        ThrowIfError("mpv initialize failed", MpvNative.MpvInitialize(_handle));

        _isShutdown = false;
        _isInitialized = true;
        ObserveWindowStateProperties();
        StartEventLoop();
    }

    private void SetOption(string name, string value)
    {
        ThrowIfError($"mpv set option '{name}' failed", MpvNative.MpvSetOptionString(_handle, name, value));
    }

    private void ConfigureApplicationOwnedUi()
    {
        // PlayerWindow owns the visible playback chrome. Do not let mpv's OSC or
        // automatic seek/volume OSD compete with the WinUI overlay. The built-in
        // stats overlay remains available on demand through the information button.
        _ = TrySetOption("osc", "no");
        _ = TrySetOption("osd-level", "0");
        _ = TrySetOption("osd-on-seek", "no");
        _ = TrySetOption("load-stats-overlay", "yes");
    }

    private void SetD3D11CompositionSize(int pixelWidth, int pixelHeight)
    {
        if (_compositionPixelWidth == pixelWidth && _compositionPixelHeight == pixelHeight)
        {
            return;
        }

        var size = FormatCompositionSize(pixelWidth, pixelHeight);
        var error = _isInitialized
            ? MpvNative.MpvSetPropertyString(_handle, D3D11CompositionSizeOptionName, size)
            : MpvNative.MpvSetOptionString(_handle, D3D11CompositionSizeOptionName, size);

        ThrowIfError("mpv set d3d11 composition size failed", error);
        _compositionPixelWidth = pixelWidth;
        _compositionPixelHeight = pixelHeight;
    }

    private void SetD3D11CompositionSizeOption(int pixelWidth, int pixelHeight)
    {
        SetOption(D3D11CompositionSizeOptionName, FormatCompositionSize(pixelWidth, pixelHeight));
        _compositionPixelWidth = pixelWidth;
        _compositionPixelHeight = pixelHeight;
    }

    private Task SendInputKeyAsync(string command, string keyName, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError($"mpv {command} failed", MpvCommandInvoker.Invoke(_handle, command, keyName));
        }, cancellationToken);
    }

    private Task SeekAsync(double value, string mode, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown || !IsPlaybackSeekable())
            {
                return;
            }

            var error = MpvCommandInvoker.Invoke(_handle, "seek", FormatDouble(value), mode);
            if (error == MpvError.Command)
            {
                // A playlist transition can make the file temporarily unseekable before
                // the observed seekable property reaches the UI. Treat that transient
                // user action as a no-op instead of surfacing a debugger-breaking error.
                return;
            }

            ThrowIfError("mpv seek failed", error);
        }, cancellationToken);
    }

    private bool IsPlaybackSeekable()
    {
        lock (_playbackStateLock)
        {
            return _playbackState.IsSeekable && _playbackState.Duration > 0;
        }
    }

    private Task InvokeCommandAsync(string errorMessage, CancellationToken cancellationToken, params string[] args)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(errorMessage, MpvCommandInvoker.Invoke(_handle, args));
        }, cancellationToken);
    }

    private Task SetTrackPropertyAsync(string propertyName, string value, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                $"mpv set {propertyName} failed",
                MpvNative.MpvSetPropertyString(_handle, propertyName, value));
        }, cancellationToken);
    }

    private Task AddExternalTrackAsync(
        string command,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The external track file does not exist.", filePath);
        }

        return InvokeCommandAsync(
            $"mpv {command} failed",
            cancellationToken,
            command,
            filePath,
            "select");
    }

    private IReadOnlyList<VideoTrackInfo> ReadTracksCore()
    {
        if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown ||
            !TryGetPropertyInt64(_handle, "track-list/count", out var trackCount) ||
            trackCount <= 0)
        {
            return Array.Empty<VideoTrackInfo>();
        }

        var boundedTrackCount = Math.Min(trackCount, 1024);
        var tracks = new List<VideoTrackInfo>((int)Math.Min(boundedTrackCount, 128));
        for (var index = 0L; index < boundedTrackCount; index++)
        {
            var propertyPrefix = $"track-list/{index}";
            if (!TryGetPropertyString(_handle, $"{propertyPrefix}/type", out var typeName) ||
                !TryGetPropertyInt64(_handle, $"{propertyPrefix}/id", out var id))
            {
                continue;
            }

            var type = typeName switch
            {
                "audio" => VideoTrackType.Audio,
                "sub" => VideoTrackType.Subtitle,
                _ => (VideoTrackType?)null
            };
            if (type is null)
            {
                continue;
            }

            _ = TryGetPropertyString(_handle, $"{propertyPrefix}/title", out var title);
            _ = TryGetPropertyString(_handle, $"{propertyPrefix}/lang", out var language);
            _ = TryGetPropertyString(_handle, $"{propertyPrefix}/codec", out var codec);
            var isSelected = TryGetPropertyFlag(_handle, $"{propertyPrefix}/selected", out var selected) && selected;

            tracks.Add(new VideoTrackInfo(id, type.Value, title, language, codec, isSelected));
        }

        return tracks;
    }

    private Task SetDoublePropertyAsync(
        string name,
        double value,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            var node = new MpvNode
            {
                Double = value,
                Format = MpvFormat.Double
            };

            ThrowIfError(errorMessage, MpvNative.MpvSetProperty(_handle, name, MpvFormat.Double, in node));
        }, cancellationToken);
    }

    private bool CanMovePlaylist(bool previous)
    {
        lock (_playbackStateLock)
        {
            if (_playbackState.PlaylistCount <= 1 || _playbackState.PlaylistPosition < 0)
            {
                return false;
            }

            return previous
                ? _playbackState.PlaylistPosition > 0
                : _playbackState.PlaylistPosition < _playbackState.PlaylistCount - 1;
        }
    }

    private string CreateScreenshotPath()
    {
        var picturesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(picturesDirectory))
        {
            picturesDirectory = AppContext.BaseDirectory;
        }

        var screenshotDirectory = Path.Combine(picturesDirectory, "Koukei Screenshots");
        Directory.CreateDirectory(screenshotDirectory);

        var mediaName = !string.IsNullOrWhiteSpace(_currentFilePath)
            ? Path.GetFileNameWithoutExtension(_currentFilePath)
            : "Koukei";
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return Path.Combine(screenshotDirectory, $"{SanitizeFileName(mediaName)}-{timestamp}.png");
    }

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Koukei";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var characters = fileName.Trim().ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            if (invalidCharacters.Contains(characters[i]))
            {
                characters[i] = '_';
            }
        }

        var sanitized = new string(characters).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Koukei" : sanitized;
    }

    private Task SetFlagPropertyAsync(string name, bool value, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
            {
                return;
            }

            ThrowIfError(
                $"mpv set {name} failed",
                MpvNative.MpvSetPropertyString(_handle, name, value ? "yes" : "no"));
        }, cancellationToken);
    }

    private void ObserveWindowStateProperties()
    {
        _ = MpvNative.MpvObserveProperty(_handle, FullscreenObservationId, "fullscreen", MpvFormat.Flag);
        _ = MpvNative.MpvObserveProperty(_handle, AlwaysOnTopObservationId, "ontop", MpvFormat.Flag);
        _ = MpvNative.MpvObserveProperty(_handle, PauseObservationId, "pause", MpvFormat.Flag);
        _ = MpvNative.MpvObserveProperty(_handle, TimePositionObservationId, "time-pos", MpvFormat.Double);
        _ = MpvNative.MpvObserveProperty(_handle, DurationObservationId, "duration", MpvFormat.Double);
        _ = MpvNative.MpvObserveProperty(_handle, VolumeObservationId, "volume", MpvFormat.Double);
        _ = MpvNative.MpvObserveProperty(_handle, MuteObservationId, "mute", MpvFormat.Flag);
        _ = MpvNative.MpvObserveProperty(_handle, SpeedObservationId, "speed", MpvFormat.Double);
        _ = MpvNative.MpvObserveProperty(_handle, SeekableObservationId, "seekable", MpvFormat.Flag);
        _ = MpvNative.MpvObserveProperty(_handle, PlaylistPositionObservationId, "playlist-pos", MpvFormat.Int64);
        _ = MpvNative.MpvObserveProperty(_handle, PlaylistCountObservationId, "playlist-count", MpvFormat.Int64);
        _ = MpvNative.MpvObserveProperty(_handle, EofReachedObservationId, "eof-reached", MpvFormat.Flag);
    }

    private void ConfigureBundledScripts()
    {
        _bundledScriptPaths.Clear();
        _areBundledScriptsLoaded = false;

        if (IsEnvironmentFlagEnabled("KOUKEI_MPV_DISABLE_BUNDLED_SCRIPTS") ||
            IsEnvironmentFlagEnabled("KOUKEI_MPV_DISABLE_MODERNZ"))
        {
            return;
        }

        var configurationDirectory = MpvNativeLibraryResolver.FindConfigurationDirectory();
        if (string.IsNullOrWhiteSpace(configurationDirectory))
        {
            return;
        }

        _ = TrySetOption("config-dir", configurationDirectory);
        _ = TrySetOption("config", "yes");
        _ = TrySetOption("load-scripts", "no");

        var inputConfigPath = Path.Combine(configurationDirectory, "script-opts", "input.conf");
        if (File.Exists(inputConfigPath))
        {
            _ = TrySetOption("input-conf", inputConfigPath);
        }

        var scriptsDirectory = Path.Combine(configurationDirectory, "scripts");
        foreach (var scriptFileName in BundledScriptLoadOrder)
        {
            var scriptPath = Path.Combine(scriptsDirectory, scriptFileName);
            if (File.Exists(scriptPath))
            {
                _bundledScriptPaths.Add(scriptPath);
            }
        }
    }

    private static bool IsEnvironmentFlagEnabled(string name)
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(name),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private bool TrySetOption(string name, string value)
    {
        return MpvNative.MpvSetOptionString(_handle, name, value) == MpvError.Success;
    }

    private void TryLoadBundledScripts(MpvHandle handle)
    {
        lock (_scriptLoadLock)
        {
            if (_areBundledScriptsLoaded ||
                handle.Handle == IntPtr.Zero ||
                _handle.Handle != handle.Handle ||
                _bundledScriptPaths.Count == 0)
            {
                return;
            }

            foreach (var scriptPath in _bundledScriptPaths)
            {
                if (File.Exists(scriptPath))
                {
                    _ = MpvCommandInvoker.Invoke(handle, "load-script", scriptPath);
                }
            }

            _areBundledScriptsLoaded = true;
        }
    }

    private void SetOptionInt64(string name, long value)
    {
        var node = new MpvNode
        {
            Int64 = value,
            Format = MpvFormat.Int64
        };

        ThrowIfError($"mpv set option '{name}' failed", MpvNative.MpvSetOption(_handle, name, MpvFormat.Int64, in node));
    }

    private void StartEventLoop()
    {
        _eventLoopCancellation = new CancellationTokenSource();
        var token = _eventLoopCancellation.Token;
        var handle = _handle;
        _eventLoopHandle = handle;

        _eventLoopTask = Task.Run(() =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var eventPointer = MpvNative.MpvWaitEvent(handle, 0.25);
                    if (eventPointer == IntPtr.Zero)
                    {
                        continue;
                    }

                    var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPointer);
                    if (mpvEvent.EventId == MpvEventId.CommandReply &&
                        _pendingCommands.TryRemove(mpvEvent.ReplyUserdata, out var commandCompletion))
                    {
                        commandCompletion.TrySetResult(mpvEvent.Error);
                    }

                    if (mpvEvent.EventId == MpvEventId.PropertyChange)
                    {
                        HandleObservedPropertyChange(mpvEvent);
                    }

                    if (mpvEvent.EventId == MpvEventId.StartFile)
                    {
                        _isInitialVideoSizePending = true;
                        _hasRaisedPlaybackEndedForCurrentFile = false;
                        ResetPlaybackStateForFileTransition();
                    }

                    if (mpvEvent.EventId == MpvEventId.FileLoaded &&
                        TryGetPropertyString(handle, "path", out var loadedFilePath) &&
                        !string.IsNullOrWhiteSpace(loadedFilePath))
                    {
                        _currentFilePath = loadedFilePath;
                    }

                    if (mpvEvent.EventId == MpvEventId.FileLoaded && _isInitialVideoSizePending)
                    {
                        _isInitialVideoSizePending = !TryNotifyVideoSizeChanged(handle);
                    }

                    if (mpvEvent.EventId == MpvEventId.FileLoaded)
                    {
                        UpdateChapters(ReadChapters(handle));
                    }

                    if (mpvEvent.EventId == MpvEventId.VideoReconfig)
                    {
                        if (_isInitialVideoSizePending)
                        {
                            _isInitialVideoSizePending = !TryNotifyVideoSizeChanged(handle);
                        }

                        if (_playbackMode == MpvPlaybackMode.D3D11Composition)
                        {
                            TryNotifyDisplaySwapChainChanged(handle);
                        }
                        else
                        {
                            TryLoadBundledScripts(handle);
                        }
                    }

                    if (mpvEvent.EventId == MpvEventId.EndFile)
                    {
                        HandleEndFile(mpvEvent);
                    }

                    if (mpvEvent.EventId == MpvEventId.Shutdown)
                    {
                        if (_handle.Handle == handle.Handle)
                        {
                            _isShutdown = true;
                            CancelPendingCommands();
                            // Never destroy the mpv handle from its own event-loop task.
                            _ = Task.Run(() => ReleaseHandleAfterShutdownAsync(handle));
                            RaiseEventSafely(PlaybackClosed, EventArgs.Empty);
                        }

                        break;
                    }
                }
            }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                var loopFailure = new MpvException(
                    "The mpv event loop stopped before the pending command completed.",
                    exception);
                CancelPendingCommands(loopFailure);

                if (_handle.Handle == handle.Handle)
                {
                    _isShutdown = true;
                    _ = Task.Run(() => ReleaseHandleAfterShutdownAsync(handle));
                    RaiseEventSafely(PlaybackClosed, EventArgs.Empty);
                }
            }
        }, CancellationToken.None);
    }

    private void HandleEndFile(MpvEvent mpvEvent)
    {
        if (mpvEvent.Data == IntPtr.Zero)
        {
            return;
        }

        var endFile = Marshal.PtrToStructure<MpvEventEndFile>(mpvEvent.Data);
        if (endFile.Reason == MpvEndFileReason.Eof)
        {
            RaisePlaybackEndedOnce();
        }

        if (endFile.Reason == MpvEndFileReason.Quit)
        {
            RaiseEventSafely(PlaybackClosed, EventArgs.Empty);
        }
    }

    private void HandleObservedPropertyChange(MpvEvent mpvEvent)
    {
        if (mpvEvent.Data == IntPtr.Zero)
        {
            return;
        }

        var eventProperty = Marshal.PtrToStructure<MpvEventProperty>(mpvEvent.Data);
        if (eventProperty.Data == IntPtr.Zero)
        {
            return;
        }

        switch (mpvEvent.ReplyUserdata)
        {
            case FullscreenObservationId:
                if (TryReadFlag(eventProperty, out var isFullscreen))
                {
                    RaiseEventSafely(
                        WindowStateChanged,
                        new VideoWindowStateChangedEventArgs(isFullscreen: isFullscreen));
                }

                break;
            case AlwaysOnTopObservationId:
                if (TryReadFlag(eventProperty, out var isAlwaysOnTop))
                {
                    RaiseEventSafely(
                        WindowStateChanged,
                        new VideoWindowStateChangedEventArgs(isAlwaysOnTop: isAlwaysOnTop));
                }

                break;
            case PauseObservationId:
                if (TryReadFlag(eventProperty, out var isPaused))
                {
                    UpdatePlaybackState(state => state with { IsPaused = isPaused });
                }

                break;
            case TimePositionObservationId:
                if (TryReadDouble(eventProperty, out var position))
                {
                    UpdatePlaybackState(state => state with { Position = Math.Max(0, position) });
                }

                break;
            case DurationObservationId:
                if (TryReadDouble(eventProperty, out var duration))
                {
                    UpdatePlaybackState(state => state with { Duration = Math.Max(0, duration) });
                }

                break;
            case VolumeObservationId:
                if (TryReadDouble(eventProperty, out var volume))
                {
                    UpdatePlaybackState(state => state with { Volume = Math.Clamp(volume, 0, 100) });
                }

                break;
            case MuteObservationId:
                if (TryReadFlag(eventProperty, out var isMuted))
                {
                    UpdatePlaybackState(state => state with { IsMuted = isMuted });
                }

                break;
            case SpeedObservationId:
                if (TryReadDouble(eventProperty, out var speed))
                {
                    UpdatePlaybackState(state => state with { Speed = Math.Max(0.01, speed) });
                }

                break;
            case SeekableObservationId:
                if (TryReadFlag(eventProperty, out var isSeekable))
                {
                    UpdatePlaybackState(state => state with { IsSeekable = isSeekable });
                }

                break;
            case PlaylistPositionObservationId:
                if (TryReadInt64(eventProperty, out var playlistPosition))
                {
                    UpdatePlaybackState(state => state with { PlaylistPosition = playlistPosition });
                }

                break;
            case PlaylistCountObservationId:
                if (TryReadInt64(eventProperty, out var playlistCount))
                {
                    UpdatePlaybackState(state => state with { PlaylistCount = Math.Max(0, playlistCount) });
                }

                break;
            case EofReachedObservationId:
                if (TryReadFlag(eventProperty, out var hasReachedEnd) && hasReachedEnd)
                {
                    RaisePlaybackEndedOnce();
                }

                break;
        }
    }

    private void RaisePlaybackEndedOnce()
    {
        if (_hasRaisedPlaybackEndedForCurrentFile ||
            string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        _hasRaisedPlaybackEndedForCurrentFile = true;
        RaiseEventSafely(
            PlaybackEnded,
            new VideoPlaybackEndedEventArgs(_currentFilePath));
    }

    private void ResetPlaybackStateForNewFile()
    {
        UpdateChapters(Array.Empty<VideoChapterInfo>());
        UpdatePlaybackState(state => state with
        {
            IsPaused = false,
            Position = 0,
            Duration = 0,
            IsSeekable = false,
            PlaylistPosition = -1,
            PlaylistCount = 0
        });
    }

    private void ResetPlaybackStateForFileTransition()
    {
        UpdateChapters(Array.Empty<VideoChapterInfo>());
        UpdatePlaybackState(state => state with
        {
            Position = 0,
            Duration = 0,
            IsSeekable = false
        });
    }

    private void UpdatePlaybackState(Func<VideoPlaybackState, VideoPlaybackState> update)
    {
        VideoPlaybackState updatedState;
        lock (_playbackStateLock)
        {
            updatedState = update(_playbackState);
            if (updatedState == _playbackState)
            {
                return;
            }

            _playbackState = updatedState;
        }

        RaiseEventSafely(PlaybackStateChanged, new VideoPlaybackStateChangedEventArgs(updatedState));
    }

    private void RefreshPlaylistStateFromNative()
    {
        if (!_isInitialized || _handle.Handle == IntPtr.Zero || _isShutdown)
        {
            return;
        }

        var hasPlaylistCount = TryGetPropertyInt64(_handle, "playlist-count", out var playlistCount);
        var hasPlaylistPosition = TryGetPropertyInt64(_handle, "playlist-pos", out var playlistPosition);
        if (!hasPlaylistCount && !hasPlaylistPosition)
        {
            return;
        }

        UpdatePlaybackState(state => state with
        {
            PlaylistCount = hasPlaylistCount ? Math.Max(0, playlistCount) : state.PlaylistCount,
            PlaylistPosition = hasPlaylistPosition ? playlistPosition : state.PlaylistPosition
        });
    }

    private IReadOnlyList<VideoChapterInfo> ReadChapters(MpvHandle handle)
    {
        if (handle.Handle == IntPtr.Zero ||
            handle.Handle != _handle.Handle ||
            !TryGetPropertyInt64(handle, "chapter-list/count", out var rawChapterCount) ||
            rawChapterCount <= 0)
        {
            return Array.Empty<VideoChapterInfo>();
        }

        var chapterCount = Math.Min(rawChapterCount, 2048);
        var chapters = new List<VideoChapterInfo>((int)Math.Min(chapterCount, 128));
        for (var index = 0L; index < chapterCount; index++)
        {
            if (!TryGetPropertyDouble(handle, $"chapter-list/{index}/time", out var startTime) ||
                !double.IsFinite(startTime) ||
                startTime < 0)
            {
                continue;
            }

            _ = TryGetPropertyString(handle, $"chapter-list/{index}/title", out var title);
            chapters.Add(new VideoChapterInfo((int)index, title, startTime));
        }

        return chapters;
    }

    private void UpdateChapters(IReadOnlyList<VideoChapterInfo> chapters)
    {
        var snapshot = chapters.ToArray();
        lock (_chapterLock)
        {
            _chapters = snapshot;
        }

        RaiseEventSafely(ChaptersChanged, new VideoChaptersChangedEventArgs(snapshot));
    }

    private static bool TryReadFlag(MpvEventProperty eventProperty, out bool value)
    {
        if (eventProperty.Format == MpvFormat.Flag && eventProperty.Data != IntPtr.Zero)
        {
            value = Marshal.ReadInt32(eventProperty.Data) != 0;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryReadDouble(MpvEventProperty eventProperty, out double value)
    {
        if (eventProperty.Format == MpvFormat.Double && eventProperty.Data != IntPtr.Zero)
        {
            value = Marshal.PtrToStructure<double>(eventProperty.Data);
            return !double.IsNaN(value);
        }

        value = 0;
        return false;
    }

    private static bool TryReadInt64(MpvEventProperty eventProperty, out long value)
    {
        if (eventProperty.Format == MpvFormat.Int64 && eventProperty.Data != IntPtr.Zero)
        {
            value = Marshal.ReadInt64(eventProperty.Data);
            return true;
        }

        value = 0;
        return false;
    }

    private void TryNotifyDisplaySwapChainChanged(MpvHandle handle)
    {
        if (_playbackMode != MpvPlaybackMode.D3D11Composition ||
            handle.Handle == IntPtr.Zero ||
            handle.Handle != _handle.Handle)
        {
            return;
        }

        if (!TryGetPropertyInt64(handle, DisplaySwapChainPropertyName, out var swapChain) || swapChain == 0)
        {
            return;
        }

        if (_displaySwapChain == swapChain)
        {
            return;
        }

        _displaySwapChain = swapChain;
        RaiseEventSafely(SwapChainChanged, new VideoSwapChainChangedEventArgs(new IntPtr(swapChain)));
    }

    private bool TryNotifyVideoSizeChanged(MpvHandle handle)
    {
        if (handle.Handle == IntPtr.Zero || handle.Handle != _handle.Handle)
        {
            return false;
        }

        if (!TryGetVideoDimensions(handle, "video-out-params/dw", "video-out-params/dh", out var width, out var height) &&
            !TryGetVideoDimensions(handle, "video-params/dw", "video-params/dh", out width, out height) &&
            !TryGetVideoDimensions(handle, "dwidth", "dheight", out width, out height) &&
            !TryGetVideoDimensions(handle, "video-params/w", "video-params/h", out width, out height) &&
            !TryGetVideoDimensions(handle, "width", "height", out width, out height))
        {
            return false;
        }

        RaiseEventSafely(VideoSizeChanged, new VideoSizeChangedEventArgs(_currentFilePath, width, height));
        return true;
    }

    private static bool TryGetVideoDimensions(
        MpvHandle handle,
        string widthProperty,
        string heightProperty,
        out int width,
        out int height)
    {
        if (TryGetPropertyInt64(handle, widthProperty, out var rawWidth) &&
            TryGetPropertyInt64(handle, heightProperty, out var rawHeight) &&
            rawWidth is > 0 and <= int.MaxValue &&
            rawHeight is > 0 and <= int.MaxValue)
        {
            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    private static bool TryGetPropertyInt64(MpvHandle handle, string name, out long value)
    {
        var error = MpvNative.MpvGetProperty(handle, name, MpvFormat.Int64, out var node);
        if (error == MpvError.Success)
        {
            value = node.Int64;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetPropertyDouble(MpvHandle handle, string name, out double value)
    {
        var error = MpvNative.MpvGetProperty(handle, name, MpvFormat.Double, out var node);
        if (error == MpvError.Success)
        {
            value = node.Double;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetPropertyFlag(MpvHandle handle, string name, out bool value)
    {
        var error = MpvNative.MpvGetProperty(handle, name, MpvFormat.Flag, out var node);
        if (error == MpvError.Success)
        {
            value = node.Flag != 0;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetPropertyString(MpvHandle handle, string name, out string? value)
    {
        var error = MpvNative.MpvGetProperty(handle, name, MpvFormat.Node, out var node);
        if (error != MpvError.Success)
        {
            value = null;
            return false;
        }

        try
        {
            value = node.Format == MpvFormat.String && node.String != IntPtr.Zero
                ? Marshal.PtrToStringUTF8(node.String)
                : null;
            return value is not null;
        }
        finally
        {
            MpvNative.MpvFreeNodeContents(ref node);
        }
    }

    private async Task ReleaseHandleAfterShutdownAsync(MpvHandle shutdownHandle)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_isDisposed && _isShutdown && _handle.Handle == shutdownHandle.Handle)
                {
                    DestroyCore(terminate: false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DestroyCore(bool terminate)
    {
        if (_eventLoopTask is { } eventLoopTask && Task.CurrentId == eventLoopTask.Id)
        {
            throw new InvalidOperationException("The mpv handle cannot be destroyed from its event-loop task.");
        }

        CancelPendingCommands();
        StopEventLoop();

        if (_handle.Handle != IntPtr.Zero)
        {
            try
            {
                if (terminate && !_isShutdown)
                {
                    MpvNative.MpvTerminateDestroy(_handle);
                }
                else
                {
                    MpvNative.MpvDestroy(_handle);
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        _handle = default;
        _eventLoopHandle = default;
        _windowHandle = IntPtr.Zero;
        _currentFilePath = null;
        _hasRaisedPlaybackEndedForCurrentFile = false;
        _bundledScriptPaths.Clear();
        _areBundledScriptsLoaded = false;
        _compositionPixelHeight = 0;
        _compositionPixelWidth = 0;
        _displaySwapChain = 0;
        _isInitialized = false;
        _playbackMode = MpvPlaybackMode.None;
        _isShutdown = false;
    }

    private void CancelPendingCommands(Exception? exception = null)
    {
        foreach (var entry in _pendingCommands)
        {
            if (_pendingCommands.TryRemove(entry.Key, out var completion))
            {
                completion.TrySetException(exception ??
                    new MpvException("The mpv player closed before the pending command completed."));
            }
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
                // A UI subscriber must not terminate libmpv's event loop.
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
                // A UI subscriber must not terminate libmpv's event loop.
            }
        }
    }

    private void StopEventLoop()
    {
        var cancellation = _eventLoopCancellation;
        var eventLoopTask = _eventLoopTask;

        _eventLoopCancellation = null;
        _eventLoopTask = null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();

            if (_eventLoopHandle.Handle != IntPtr.Zero)
            {
                try
                {
                    MpvNative.MpvWakeup(_eventLoopHandle);
                }
                catch (DllNotFoundException)
                {
                }
                catch (EntryPointNotFoundException)
                {
                }
            }

            if (eventLoopTask is not null)
            {
                try
                {
                    // libmpv forbids destroying a context while another thread is using it.
                    // Cancellation plus mpv_wakeup makes mpv_wait_event return immediately;
                    // wait for the loop to finish instead of destroying after an arbitrary timeout.
                    eventLoopTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    // The loop is no longer running, so native cleanup can safely continue.
                }
            }
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static void ThrowIfError(string message, MpvError error)
    {
        if (error != MpvError.Success)
        {
            throw new MpvException(message, error);
        }
    }

    private static void ValidateCompositionSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "The D3D11 composition width must be positive.");
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), "The D3D11 composition height must be positive.");
        }
    }

    private static string FormatCompositionSize(int pixelWidth, int pixelHeight)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{pixelWidth}x{pixelHeight}");
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static MpvException CreateMissingMpvException(DllNotFoundException exception)
    {
        var message = "Koukei could not load libmpv-2.dll from mpv/win-x64. "
            + "Place the MPV runtime in that directory or set KOUKEI_MPV_HOME.";

        return new MpvException(message, exception);
    }
}
