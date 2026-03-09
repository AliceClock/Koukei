namespace Koukei.Audio;

public sealed record AudioPlaybackState(
    AudioPlaybackStatus Status,
    double Position,
    double Duration,
    double Volume,
    bool IsMuted,
    double Speed,
    bool IsSeekable,
    AudioPlaybackBackend Backend,
    string? ErrorMessage)
{
    public static AudioPlaybackState Empty { get; } = new(
        AudioPlaybackStatus.None,
        Position: 0,
        Duration: 0,
        Volume: 100,
        IsMuted: false,
        Speed: 1,
        IsSeekable: false,
        AudioPlaybackBackend.None,
        ErrorMessage: null);
}
