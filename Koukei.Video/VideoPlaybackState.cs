namespace Koukei.Video;

public sealed record VideoPlaybackState(
    bool IsPaused,
    double Position,
    double Duration,
    double Volume,
    bool IsMuted,
    double Speed,
    bool IsSeekable,
    long PlaylistPosition,
    long PlaylistCount)
{
    public static VideoPlaybackState Empty { get; } = new(
        IsPaused: false,
        Position: 0,
        Duration: 0,
        Volume: 100,
        IsMuted: false,
        Speed: 1,
        IsSeekable: false,
        PlaylistPosition: -1,
        PlaylistCount: 0);
}
