using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document;

public class SerialIssue : Document
{
    public override BaseItemKind Kind => BaseItemKind.SerialIssue;

    public DateTime PublicationDate { get; set; } = DateTime.UtcNow;

    public Guid? SerialId { get; set; }
}