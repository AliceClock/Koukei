namespace Koukei.Video;

public sealed class VideoPlaybackStateChangedEventArgs(VideoPlaybackState state) : EventArgs
{
    public VideoPlaybackState State { get; } = state;
}
