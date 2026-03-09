namespace Koukei.Audio;

public sealed record AudioMediaMetadata(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    byte[]? AlbumArt,
    string? Lyrics = null);
