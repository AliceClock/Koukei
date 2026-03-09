namespace Koukei.Data.Entities;

public class ItemTag
{
    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public Guid TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}