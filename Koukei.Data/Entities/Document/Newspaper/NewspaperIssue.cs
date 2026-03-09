using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Newspaper;

public class NewspaperIssue : SerialIssue
{
    public override BaseItemKind Kind => BaseItemKind.NewspaperIssue;

    public int? Day { get; set; }

    public int? Month { get; set; }

    public int? Year { get; set; }

    public Guid? NewspaperId { get; set; }

    public Newspaper? Newspaper { get; set; }
}
