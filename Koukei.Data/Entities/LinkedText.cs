using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public class LinkedText
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ItemId { get; set; }

    public BaseItem? Item { get; set; }

    public Guid? PersonId { get; set; }

    public Person? Person { get; set; }

    public Guid? GenreId { get; set; }

    public Genre? Genre { get; set; }

    public Guid? TagId { get; set; }

    public Tag? Tag { get; set; }

    public Guid? StudioId { get; set; }

    public Studio? Studio { get; set; }

    public Guid? RatingId { get; set; }

    public Rating? Rating { get; set; }

    public Guid? ImageId { get; set; }

    public ImageInfo? Image { get; set; }

    public Guid? DocumentId { get; set; }

    public DocumentInfo? Document { get; set; }

    public LinkedTextType TextType { get; set; }

    public int TextIndex { get; set; }

    public string? Text { get; set; }

    public DateTimeOffset? DateModified { get; set; }
}
