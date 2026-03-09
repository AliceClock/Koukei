namespace Koukei.Data.Entities;

public class Playlist
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DateLastSaved { get; set; }

    public ICollection<PlaylistItem> Items { get; set; } = [];
}
