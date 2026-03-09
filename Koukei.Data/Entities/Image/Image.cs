using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Image;

public class Image : BaseItem
{
    public override BaseItemKind Kind => BaseItemKind.Image;

    public override MediaType MediaType => MediaType.Image;

    public ICollection<ImageInfo> ImageInfo { get; set; } = [];
}
