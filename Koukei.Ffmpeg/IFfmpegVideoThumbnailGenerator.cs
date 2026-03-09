namespace Koukei.Ffmpeg;

public interface IFfmpegVideoThumbnailGenerator
{
    Task<string?> CreateAsync(
        string filePath,
        string outputPath,
        TimeSpan? position = null,
        int maximumDimension = 512,
        CancellationToken cancellationToken = default);
}
