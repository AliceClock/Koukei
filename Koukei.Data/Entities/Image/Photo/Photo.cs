using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Image.Photo;

public class Photo : Image
{
    public override BaseItemKind Kind => BaseItemKind.Photo;
}
