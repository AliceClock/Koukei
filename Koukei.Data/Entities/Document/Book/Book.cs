using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Book;

public class Book : Document
{
    public override BaseItemKind Kind => BaseItemKind.Book;

    public DateOnly? PublicationDate { get; set; }

    public int? VolumeNumber { get; set; } = 0;

    public Guid? BookSeriesId { get; set; }

    public BookSeries? BookSeries { get; set; }

    public string? Isbn { get; set; }
}
