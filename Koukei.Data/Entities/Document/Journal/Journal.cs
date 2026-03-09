using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Journal;

public class Journal : Serial<JournalIssue>
{
    public override BaseItemKind Kind => BaseItemKind.Journal;
}