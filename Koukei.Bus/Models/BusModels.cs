namespace Koukei.Bus.Models;

public enum MediaLibraryItemKind
{
    Unknown = 0,
    Video = 1,
    Audio = 2,
    Image = 3
}

public enum MediaLibraryStreamKind
{
    Unknown = 0,
    Video = 1,
    Audio = 2
}

public enum MediaLibrarySortField
{
    DateCreated = 0,
    Name = 1,
    SortName = 2,
    ProductionYear = 3,
    LastModified = 4
}

public enum SortDirection
{
    Ascending = 0,
    Descending = 1
}

public sealed class MediaLibraryQuery
{
    public string? SearchText { get; set; }

    public MediaLibraryItemKind? Kind { get; set; }

    public bool IncludeLocked { get; set; } = true;

    public MediaLibrarySortField SortField { get; set; } = MediaLibrarySortField.DateCreated;

    public SortDirection SortDirection { get; set; } = SortDirection.Descending;

    public int Skip { get; set; }

    public int Take { get; set; } = 100;
}

public sealed class MediaLibraryPlaybackQuery
{
    public string? SearchText { get; init; }

    public MediaLibraryItemKind Kind { get; init; }
}

public sealed class MediaLibraryPlaybackItem
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public DateTimeOffset DateCreated { get; init; }

    public TimeSpan? Duration { get; init; }

    public MediaLibraryItemKind Kind { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public string? ThumbnailPath { get; init; }

    public TimeSpan? PlaybackPosition { get; init; }
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public int TotalCount { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; }
}

public sealed class MediaLibraryItem
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public string? ContainerFormat { get; init; }

    public long? FileSize { get; init; }

    public DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? MetadataRefreshedAt { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public TimeSpan? Duration { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public MediaLibraryItemKind Kind { get; init; }

    public string? ThumbnailPath { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public IReadOnlyList<NewMediaLibraryStream> Streams { get; init; } = [];

    public bool IsFavorite { get; init; }

    public int UserRating { get; init; }

    public DateTimeOffset? LastOpenedAt { get; init; }

    public DateTimeOffset? LastPlayedAt { get; init; }

    public TimeSpan? PlaybackPosition { get; init; }

    public int PlayCount { get; init; }
}

public sealed class NewMediaLibraryItem
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public string? ContainerFormat { get; init; }

    public long? FileSize { get; init; }

    public DateTimeOffset DateCreated { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastModified { get; init; }

    public TimeSpan? Duration { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public MediaLibraryItemKind Kind { get; init; }

    public string? ThumbnailPath { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public IReadOnlyList<NewMediaLibraryStream> Streams { get; init; } = [];
}

public sealed class NewMediaLibraryStream
{
    public MediaLibraryStreamKind Kind { get; init; }

    public int StreamIndex { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? Codec { get; init; }

    public string? CodecProfile { get; init; }

    public string? Language { get; init; }

    public long? BitRate { get; init; }

    public bool IsDefault { get; init; }

    public string? Title { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public string? PixelFormat { get; init; }

    public int? Rotation { get; init; }

    public int? Channels { get; init; }

    public string? ChannelLayout { get; init; }

    public int? SampleRate { get; init; }

    public int? BitDepth { get; init; }
}

public sealed class MediaLibraryImportResult
{
    public required IReadOnlyList<MediaLibraryItem> AddedItems { get; init; }

    public int SkippedDuplicateCount { get; init; }
}

public sealed class MediaLibraryMetadataUpdate
{
    public Guid Id { get; init; }

    public required NewMediaLibraryItem Metadata { get; init; }
}

public sealed class PlaylistSummary
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int ItemCount { get; init; }

    public IReadOnlyList<string> ThumbnailPaths { get; init; } = [];

    public DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateLastSaved { get; init; }
}

public sealed class PlaylistDetail
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateLastSaved { get; init; }

    public IReadOnlyList<PlaylistMediaItem> Items { get; init; } = [];
}

public sealed class PlaylistMediaItem
{
    public Guid PlaylistItemId { get; init; }

    public Guid MediaId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public MediaLibraryItemKind Kind { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public string? ThumbnailPath { get; init; }

    public TimeSpan? PlaybackPosition { get; init; }

    public int SortOrder { get; init; }

    public DateTimeOffset DateAdded { get; init; }

    public string? Note { get; init; }
}

public sealed class PlaylistItemsAddResult
{
    public int AddedCount { get; init; }

    public int DuplicateCount { get; init; }

    public int MissingCount { get; init; }
}

public sealed class KoukeiSchemaStatus
{
    public required IReadOnlyList<string> MissingTables { get; init; }

    public required IReadOnlyList<string> MissingIndexes { get; init; }

    public required IReadOnlyList<string> MissingUniqueIndexes { get; init; }

    public bool IsValid => MissingTables.Count == 0 &&
        MissingIndexes.Count == 0 &&
        MissingUniqueIndexes.Count == 0;
}
