using Koukei.Data.Entities;

namespace Koukei.Data.Dtos;

public sealed class MediaLibraryImportResult
{
    public required IReadOnlyList<BaseItem> AddedItems { get; init; }

    public int SkippedDuplicateCount { get; init; }
}
