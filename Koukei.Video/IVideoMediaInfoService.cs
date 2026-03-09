namespace Koukei.Video;

public interface IVideoMediaInfoService
{
    Task<VideoMediaInfo> GetMediaInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
