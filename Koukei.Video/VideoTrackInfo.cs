namespace Koukei.Video;

public sealed record VideoTrackInfo(
    long Id,
    VideoTrackType Type,
    string? Title,
    string? Language,
    string? Codec,
    bool IsSelected);
