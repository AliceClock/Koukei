namespace Koukei.Ffmpeg;

public interface IFfmpegMediaProbe
{
    Task<FfmpegMediaInfo> ProbeAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        bool includeAttachedPictures = true);
}
