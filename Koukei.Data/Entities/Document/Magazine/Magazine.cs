using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Magazine;

public class Magazine : Serial<MagazineIssue>
{
    public override BaseItemKind Kind => BaseItemKind.Magazine;
}