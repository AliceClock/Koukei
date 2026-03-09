namespace Koukei.Data.Entities;

public class ItemStudio
{
    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public Guid StudioId { get; set; }

    public Studio Studio { get; set; } = null!;

    public int SortOrder { get; set; }
}