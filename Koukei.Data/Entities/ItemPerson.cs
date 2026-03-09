using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public class ItemPerson
{
    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public Guid PersonId { get; set; }

    public Person Person { get; set; } = null!;

    public RoleKind Role { get; set; } = RoleKind.Unknown;

    public int SortOrder { get; set; }
}