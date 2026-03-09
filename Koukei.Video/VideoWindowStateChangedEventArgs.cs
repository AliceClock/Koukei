namespace Koukei.Video;

public sealed class VideoWindowStateChangedEventArgs : EventArgs
{
    public VideoWindowStateChangedEventArgs(bool? isFullscreen = null, bool? isAlwaysOnTop = null)
    {
        IsFullscreen = isFullscreen;
        IsAlwaysOnTop = isAlwaysOnTop;
    }

    public bool? IsFullscreen { get; }

    public bool? IsAlwaysOnTop { get; }
}
