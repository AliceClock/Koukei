using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document;

public class Document : BaseItem
{
    public override BaseItemKind Kind => BaseItemKind.Document;

    public override MediaType MediaType => MediaType.Document;

    public ICollection<DocumentInfo> DocumentInfo { get; set; } = [];
}