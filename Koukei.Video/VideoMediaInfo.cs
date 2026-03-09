namespace Koukei.Video;

public sealed record VideoMediaInfo(
    string FilePath,
    long FileSize,
    TimeSpan? Duration,
    string? ContainerFormat,
    string? Title,
    VideoStreamMetadata? Video,
    AudioStreamMetadata? Audio,
    int VideoTrackCount,
    int AudioTrackCount,
    int SubtitleTrackCount);

public sealed record VideoStreamMetadata(
    long Id,
    string? Title,
    string? Language,
    string? Codec,
    string? CodecDescription,
    string? CodecProfile,
    int? Width,
    int? Height,
    double? FrameRate,
    long? BitRate,
    int? Rotation,
    string? PixelFormat);

public sealed record AudioStreamMetadata(
    long Id,
    string? Title,
    string? Language,
    string? Codec,
    string? CodecDescription,
    int? ChannelCount,
    string? ChannelLayout,
    int? SampleRate,
    long? BitRate);
