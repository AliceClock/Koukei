namespace Koukei.Ffmpeg;

public enum FfmpegMediaStreamKind
{
    Unknown,
    Video,
    Audio,
    Subtitle,
    Data,
    Attachment
}

public sealed record FfmpegMediaInfo(
    string FilePath,
    long FileSize,
    TimeSpan? Duration,
    long? BitRate,
    string? ContainerFormat,
    string? ContainerDescription,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<FfmpegMediaStreamInfo> Streams)
{
    public FfmpegMediaStreamInfo? PrimaryVideo => Streams.FirstOrDefault(
            static stream => stream.Kind == FfmpegMediaStreamKind.Video &&
                             !stream.IsAttachedPicture &&
                             stream.IsDefault)
        ?? Streams.FirstOrDefault(
            static stream => stream.Kind == FfmpegMediaStreamKind.Video && !stream.IsAttachedPicture);

    public FfmpegMediaStreamInfo? PrimaryAudio => Streams.FirstOrDefault(
            static stream => stream.Kind == FfmpegMediaStreamKind.Audio && stream.IsDefault)
        ?? Streams.FirstOrDefault(static stream => stream.Kind == FfmpegMediaStreamKind.Audio);

    public int VideoTrackCount => Streams.Count(
        static stream => stream.Kind == FfmpegMediaStreamKind.Video && !stream.IsAttachedPicture);

    public int AudioTrackCount => Streams.Count(static stream => stream.Kind == FfmpegMediaStreamKind.Audio);

    public int SubtitleTrackCount => Streams.Count(static stream => stream.Kind == FfmpegMediaStreamKind.Subtitle);
}

public sealed record FfmpegMediaStreamInfo(
    int Index,
    int Id,
    FfmpegMediaStreamKind Kind,
    string? CodecName,
    string? CodecDescription,
    string? CodecProfile,
    TimeSpan? Duration,
    long? BitRate,
    int? Width,
    int? Height,
    double? FrameRate,
    string? PixelFormat,
    int? Rotation,
    int? ChannelCount,
    string? ChannelLayout,
    int? SampleRate,
    int? BitsPerSample,
    string? SampleFormat,
    bool IsDefault,
    bool IsAttachedPicture,
    IReadOnlyDictionary<string, string> Tags,
    byte[]? AttachedPicture);
