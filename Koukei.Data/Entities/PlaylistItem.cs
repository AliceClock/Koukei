namespace Koukei.Data.Entities;

public class PlaylistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PlaylistId { get; set; }

    public Playlist Playlist { get; set; } = null!;

    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public int SortOrder { get; set; }

    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;

    public string? Note { get; set; }
}
