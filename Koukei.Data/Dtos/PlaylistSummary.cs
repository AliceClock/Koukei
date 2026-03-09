namespace Koukei.Data.Dtos;

public sealed class PlaylistSummary
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int ItemCount { get; set; }

    public IReadOnlyList<string> ThumbnailPaths { get; set; } = [];

    public DateTimeOffset DateCreated { get; set; }

    public DateTimeOffset? DateLastSaved { get; set; }
}
