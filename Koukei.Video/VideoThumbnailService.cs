using Koukei.Ffmpeg;

namespace Koukei.Video;

public sealed class VideoThumbnailService(
    IFfmpegVideoThumbnailGenerator thumbnailGenerator) : IVideoThumbnailService, IDisposable
{
    private const int LibraryThumbnailMaximumDimension = 512;

    private readonly IFfmpegVideoThumbnailGenerator _thumbnailGenerator =
        thumbnailGenerator ?? throw new ArgumentNullException(nameof(thumbnailGenerator));
    private readonly MpvSeekPreviewThumbnailGenerator _seekPreviewGenerator = new();

    public Task<string?> CreateVideoThumbnailAsync(
        string filePath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return _thumbnailGenerator.CreateAsync(
            filePath,
            outputPath,
            position: null,
            LibraryThumbnailMaximumDimension,
            cancellationToken);
    }

    public Task<string?> CreateVideoThumbnailAtAsync(
        string filePath,
        string outputPath,
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        return _seekPreviewGenerator.CreateAsync(
            filePath,
            outputPath,
            position,
            cancellationToken);
    }

    public Task ReleaseSeekPreviewResourcesAsync(CancellationToken cancellationToken = default)
    {
        return _seekPreviewGenerator.ReleaseResourcesAsync(cancellationToken);
    }

    public void Dispose()
    {
        _seekPreviewGenerator.Dispose();
    }
}
