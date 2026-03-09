using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Document.Book;

public class BookSeries : Folder<Book>
{
    public override BaseItemKind Kind => BaseItemKind.BookSeries;
}