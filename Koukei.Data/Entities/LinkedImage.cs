using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public class LinkedImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ItemId { get; set; }

    public BaseItem? Item { get; set; }

    public Guid? PersonId { get; set; }

    public Person? Person { get; set; }

    public Guid? StudioId { get; set; }

    public Studio? Studio { get; set; }

    public LinkedImageType ImageType { get; set; }

    public int ImageIndex { get; set; }

    public string? Path { get; set; }

    public DateTimeOffset? DateModified { get; set; }

    public string? BlurHash { get; set; }
}
