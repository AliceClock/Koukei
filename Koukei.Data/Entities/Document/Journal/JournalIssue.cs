using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Journal;

public class JournalIssue : SerialIssue
{
    public override BaseItemKind Kind => BaseItemKind.JournalIssue;

    public int? VolumeNumber { get; set; }

    public int? IssueNumber { get; set; }

    public int? Year { get; set; }

    public Guid? JournalId { get; set; }

    public Journal? Journal { get; set; }
}
