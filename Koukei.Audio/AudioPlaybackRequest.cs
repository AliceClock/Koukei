namespace Koukei.Audio;

public sealed record AudioPlaybackRequest(
    string FilePath,
    string? Title = null,
    Guid PlaybackId = default);
