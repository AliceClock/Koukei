using Koukei.Ffmpeg;

namespace Koukei.Video;

public sealed class FfmpegVideoMediaInfoService(IFfmpegMediaProbe mediaProbe) : IVideoMediaInfoService
{
    private readonly IFfmpegMediaProbe _mediaProbe =
        mediaProbe ?? throw new ArgumentNullException(nameof(mediaProbe));

    public async Task<VideoMediaInfo> GetMediaInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var mediaInfo = await _mediaProbe
            .ProbeAsync(filePath, cancellationToken)
            .ConfigureAwait(false);
        var video = SelectPrimaryStream(
            mediaInfo.Streams,
            static stream => stream.Kind == FfmpegMediaStreamKind.Video && !stream.IsAttachedPicture);
        var audio = SelectPrimaryStream(
            mediaInfo.Streams,
            static stream => stream.Kind == FfmpegMediaStreamKind.Audio);

        return new VideoMediaInfo(
            mediaInfo.FilePath,
            mediaInfo.FileSize,
            mediaInfo.Duration,
            mediaInfo.ContainerFormat,
            GetMediaTitle(mediaInfo),
            video is null ? null : MapVideoStream(video),
            audio is null ? null : MapAudioStream(audio),
            mediaInfo.VideoTrackCount,
            mediaInfo.AudioTrackCount,
            mediaInfo.SubtitleTrackCount);
    }

    private static string? GetMediaTitle(FfmpegMediaInfo mediaInfo)
    {
        // Stream titles describe individual tracks (for example "VideoHandler" or
        // a language/codec label), not the title of the media item. They remain
        // available through VideoStreamMetadata and AudioStreamMetadata.
        return GetTag(mediaInfo.Tags, "title");
    }

    private static FfmpegMediaStreamInfo? SelectPrimaryStream(
        IReadOnlyList<FfmpegMediaStreamInfo> streams,
        Func<FfmpegMediaStreamInfo, bool> predicate)
    {
        return streams.FirstOrDefault(stream => predicate(stream) && stream.IsDefault)
            ?? streams.FirstOrDefault(predicate);
    }

    private static VideoStreamMetadata MapVideoStream(FfmpegMediaStreamInfo stream)
    {
        return new VideoStreamMetadata(
            stream.Id,
            GetTag(stream.Tags, "title"),
            GetTag(stream.Tags, "language", "lang"),
            stream.CodecName,
            stream.CodecDescription,
            stream.CodecProfile,
            stream.Width,
            stream.Height,
            stream.FrameRate,
            stream.BitRate,
            stream.Rotation,
            stream.PixelFormat);
    }

    private static AudioStreamMetadata MapAudioStream(FfmpegMediaStreamInfo stream)
    {
        return new AudioStreamMetadata(
            stream.Id,
            GetTag(stream.Tags, "title"),
            GetTag(stream.Tags, "language", "lang"),
            stream.CodecName,
            stream.CodecDescription,
            stream.ChannelCount,
            stream.ChannelLayout,
            stream.SampleRate,
            stream.BitRate);
    }

    private static string? GetTag(
        IReadOnlyDictionary<string, string> tags,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (tags.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
