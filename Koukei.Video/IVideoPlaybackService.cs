namespace Koukei.Video;

public interface IVideoPlaybackService
{
    event EventHandler<VideoWindowStateChangedEventArgs>? WindowStateChanged;

    event EventHandler<VideoSwapChainChangedEventArgs>? SwapChainChanged;

    event EventHandler<VideoPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    event EventHandler<VideoSizeChangedEventArgs>? VideoSizeChanged;

    event EventHandler<VideoChaptersChangedEventArgs>? ChaptersChanged;

    event EventHandler<VideoPlaybackEndedEventArgs>? PlaybackEnded;

    event EventHandler? PlaybackClosed;

    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);

    Task PlayAsync(string filePath, IntPtr windowHandle, CancellationToken cancellationToken = default);

    Task PlayWithD3D11CompositionAsync(
        string filePath,
        int pixelWidth,
        int pixelHeight,
        CancellationToken cancellationToken = default);

    Task PlayPlaylistAsync(
        IReadOnlyList<string> filePaths,
        int startIndex,
        IntPtr windowHandle,
        CancellationToken cancellationToken = default);

    Task PlayPlaylistWithD3D11CompositionAsync(
        IReadOnlyList<string> filePaths,
        int startIndex,
        int pixelWidth,
        int pixelHeight,
        CancellationToken cancellationToken = default);

    Task SetD3D11CompositionSizeAsync(
        int pixelWidth,
        int pixelHeight,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<VideoPlaybackState> GetPlaybackStateAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VideoChapterInfo>> GetChaptersAsync(CancellationToken cancellationToken = default);

    Task SetPausedAsync(bool isPaused, CancellationToken cancellationToken = default);

    Task SeekRelativeAsync(double seconds, CancellationToken cancellationToken = default);

    Task SeekAbsoluteAsync(double seconds, CancellationToken cancellationToken = default);

    Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default);

    Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default);

    Task SetSpeedAsync(double speed, CancellationToken cancellationToken = default);

    Task PlaylistPreviousAsync(CancellationToken cancellationToken = default);

    Task PlaylistNextAsync(CancellationToken cancellationToken = default);

    Task AppendToPlaylistAsync(string filePath, CancellationToken cancellationToken = default);

    Task PlayPlaylistItemAsync(int index, CancellationToken cancellationToken = default);

    Task MovePlaylistItemAsync(
        int index,
        int targetIndex,
        CancellationToken cancellationToken = default);

    Task RemovePlaylistItemAsync(int index, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VideoTrackInfo>> GetTracksAsync(CancellationToken cancellationToken = default);

    Task SelectAudioTrackAsync(long trackId, CancellationToken cancellationToken = default);

    Task SelectSubtitleTrackAsync(long? trackId, CancellationToken cancellationToken = default);

    Task AddAudioTrackAsync(string filePath, CancellationToken cancellationToken = default);

    Task AddSubtitleTrackAsync(string filePath, CancellationToken cancellationToken = default);

    Task CycleAudioTrackAsync(CancellationToken cancellationToken = default);

    Task CycleSubtitleTrackAsync(CancellationToken cancellationToken = default);

    Task ScreenshotAsync(CancellationToken cancellationToken = default);

    Task ToggleStatisticsAsync(CancellationToken cancellationToken = default);

    Task SetFullscreenAsync(bool isFullscreen, CancellationToken cancellationToken = default);

    Task SetAlwaysOnTopAsync(bool isAlwaysOnTop, CancellationToken cancellationToken = default);

    Task SendKeyPressAsync(string keyName, CancellationToken cancellationToken = default);

    Task SendKeyDownAsync(string keyName, CancellationToken cancellationToken = default);

    Task SendKeyUpAsync(string keyName, CancellationToken cancellationToken = default);

    Task SendMouseMoveAsync(int x, int y, CancellationToken cancellationToken = default);

    Task SendMouseButtonAsync(int x, int y, string keyName, bool isPressed, CancellationToken cancellationToken = default);

    Task SendMouseKeyPressAsync(int x, int y, string keyName, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
