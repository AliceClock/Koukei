using Koukei.Ffmpeg;

namespace Koukei.Audio;

public sealed class FfmpegAudioMetadataService : IAudioMetadataService
{
    private readonly IFfmpegMediaProbe _mediaProbe;
    private readonly bool _includeAttachedPictures;

    public FfmpegAudioMetadataService(IFfmpegMediaProbe mediaProbe)
        : this(mediaProbe, includeAttachedPictures: true)
    {
    }

    public FfmpegAudioMetadataService(
        IFfmpegMediaProbe mediaProbe,
        bool includeAttachedPictures)
    {
        _mediaProbe = mediaProbe ?? throw new ArgumentNullException(nameof(mediaProbe));
        _includeAttachedPictures = includeAttachedPictures;
    }

    public async Task<AudioFileMetadata> GetMetadataAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var mediaInfo = await _mediaProbe
            .ProbeAsync(filePath, cancellationToken, _includeAttachedPictures)
            .ConfigureAwait(false);
        var audio = mediaInfo.Streams.FirstOrDefault(
                static stream => stream.Kind == FfmpegMediaStreamKind.Audio && stream.IsDefault)
            ?? mediaInfo.PrimaryAudio;
        if (audio is null)
        {
            throw new InvalidDataException($"FFmpeg found no audio stream in '{mediaInfo.FilePath}'.");
        }

        var albumArt = mediaInfo.Streams
            .Where(static stream => stream.IsAttachedPicture)
            .Select(static stream => stream.AttachedPicture)
            .FirstOrDefault(static picture => picture is { Length: > 0 });

        return new AudioFileMetadata(
            mediaInfo.FilePath,
            FirstNonEmpty(
                GetTag(mediaInfo.Tags, "title"),
                GetTag(audio.Tags, "title"),
                Path.GetFileNameWithoutExtension(mediaInfo.FilePath)),
            FirstNonEmptyOrNull(
                GetTag(audio.Tags, "artist", "album_artist", "albumartist"),
                GetTag(mediaInfo.Tags, "artist", "album_artist", "albumartist")),
            FirstNonEmptyOrNull(
                GetTag(audio.Tags, "album"),
                GetTag(mediaInfo.Tags, "album")),
            audio.Duration ?? mediaInfo.Duration,
            mediaInfo.ContainerFormat,
            audio.CodecName,
            audio.ChannelCount,
            audio.SampleRate,
            audio.BitsPerSample,
            audio.BitRate ?? mediaInfo.BitRate,
            albumArt,
            FirstNonEmptyOrNull(
                GetTag(audio.Tags, "syncedlyrics", "synced_lyrics", "lyrics", "unsyncedlyrics", "unsynced_lyrics"),
                GetTag(mediaInfo.Tags, "syncedlyrics", "synced_lyrics", "lyrics", "unsyncedlyrics", "unsynced_lyrics")));
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim()
            ?? string.Empty;
    }

    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
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
