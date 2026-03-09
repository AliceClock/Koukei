namespace Koukei.Audio;

public interface IAudioMetadataService
{
    Task<AudioFileMetadata> GetMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
