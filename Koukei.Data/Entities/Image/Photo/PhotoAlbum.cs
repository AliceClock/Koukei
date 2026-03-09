using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Image.Photo;

public class PhotoAlbum : Folder<Photo>
{
    public override BaseItemKind Kind => BaseItemKind.PhotoAlbum;
}
