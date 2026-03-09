using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Magazine;

public class MagazineIssue : SerialIssue
{
    public override BaseItemKind Kind => BaseItemKind.MagazineIssue;

    public int? IssueNumber { get; set; }

    public int? Year { get; set; }

    public Guid? MagazineId { get; set; }

    public Magazine? Magazine { get; set; }
}
