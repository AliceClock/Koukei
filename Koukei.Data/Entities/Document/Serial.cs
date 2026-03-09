using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document;

public abstract class Serial<TIssue> : Folder<TIssue> where TIssue : SerialIssue
{
    public override BaseItemKind Kind => BaseItemKind.Serial;

    public PublicationFrequency PublicationFrequency { get; set; } = PublicationFrequency.Unknown;

    public string? Issn { get; set; }

    public override bool IsFolder => true;
}
