using Koukei.Data.Enums;

namespace Koukei.Data.Dtos;

public sealed class MediaLibraryQuery
{
    public string? SearchText { get; set; }

    public BaseItemKind? Kind { get; set; }

    public Guid? ParentId { get; set; }

    public Guid? TopParentId { get; set; }

    public SourceType? SourceType { get; set; }

    public bool IncludeLocked { get; set; } = true;

    public MediaLibrarySortField SortField { get; set; } = MediaLibrarySortField.DateCreated;

    public SortDirection SortDirection { get; set; } = SortDirection.Descending;

    public int Skip { get; set; }

    public int Take { get; set; } = 100;
}
