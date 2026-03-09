namespace Koukei.Data.Entities;

public class ItemGenre
{
    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public Guid GenreId { get; set; }

    public Genre Genre { get; set; } = null!;
}