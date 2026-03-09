using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Image.Illustration;

public class IllustrationCollection : Folder<Illustration>
{
    public override BaseItemKind Kind => BaseItemKind.Artbook;
}