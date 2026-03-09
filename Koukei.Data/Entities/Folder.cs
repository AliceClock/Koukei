using Koukei.Data.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Koukei.Data.Entities;

public abstract class Folder<T> : BaseItem where T : BaseItem
{
    [NotMapped]
    public List<T> LinkedChildren { get; set; } = [];

    public override BaseItemKind Kind => BaseItemKind.Folder;

    public override bool IsFolder => true;
}
