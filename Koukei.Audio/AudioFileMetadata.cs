namespace Koukei.Audio;

public sealed record AudioFileMetadata(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    TimeSpan? Duration,
    string? FormatName,
    string? CodecName,
    int? ChannelCount,
    int? SampleRate,
    int? BitsPerSample,
    long? BitRate,
    byte[]? AlbumArt,
    string? Lyrics = null);
