using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Newspaper;

public class Newspaper : Serial<NewspaperIssue>
{
    public override BaseItemKind Kind => BaseItemKind.Newspaper;
}