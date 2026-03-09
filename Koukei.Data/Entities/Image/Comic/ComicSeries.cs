using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Image.Comic;

public class ComicSeries : Folder<Comic>
{
    public override BaseItemKind Kind => BaseItemKind.ComicSeries;
}