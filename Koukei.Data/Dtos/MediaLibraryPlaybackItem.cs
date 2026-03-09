using Koukei.Data.Enums;

namespace Koukei.Data.Dtos;

public sealed class MediaLibraryPlaybackItem
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public DateTimeOffset DateCreated { get; init; }

    public double? DurationSeconds { get; init; }

    public BaseItemKind Kind { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public string? ThumbnailPath { get; init; }
}
