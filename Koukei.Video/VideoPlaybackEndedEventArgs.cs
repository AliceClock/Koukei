namespace Koukei.Video;

public sealed class VideoPlaybackEndedEventArgs(string filePath) : EventArgs
{
    public string FilePath { get; } = filePath;
}
