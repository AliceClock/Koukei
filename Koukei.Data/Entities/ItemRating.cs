namespace Koukei.Data.Entities;

public class ItemRating
{
    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public Guid RatingId { get; set; }

    public Rating Rating { get; set; } = null!;

    public float? Value { get; set; }
}