namespace Koukei.Data.Entities;

public class ImageInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ItemId { get; set; }

    public Image.Image Item { get; set; } = null!;

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int? ColorDepth { get; set; }

    public string? ColorSpace { get; set; }

    public int? Channels { get; set; }

    public bool? HasAlpha { get; set; } = false;

    public string? Format { get; set; }
}
