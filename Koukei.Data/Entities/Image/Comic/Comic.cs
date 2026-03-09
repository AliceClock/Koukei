using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Image.Comic;

public class Comic : Folder<Image>
{
    public override BaseItemKind Kind => BaseItemKind.Comic;

    public bool IsBook { get; set; }

    public bool IsMagazine { get; set; }

    public int? VolumeNumber { get; set; }
    
    public int? IssueNumber { get; set; }

    public int? ChapterNumber { get; set; }
}