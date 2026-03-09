namespace Koukei.Video;

public interface IVideoThumbnailService
{
    Task<string?> CreateVideoThumbnailAsync(
        string filePath,
        string outputPath,
        CancellationToken cancellationToken = default);

    Task<string?> CreateVideoThumbnailAtAsync(
        string filePath,
        string outputPath,
        TimeSpan position,
        CancellationToken cancellationToken = default);

    Task ReleaseSeekPreviewResourcesAsync(CancellationToken cancellationToken = default);
}
