namespace Koukei.Video;

public sealed class VideoChaptersChangedEventArgs(IReadOnlyList<VideoChapterInfo> chapters) : EventArgs
{
    public IReadOnlyList<VideoChapterInfo> Chapters { get; } = chapters;
}
