using Koukei.Data.Entities.Document.Book;
using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Audio.AudioBook;

public class AudioBookSeries : Folder<AudioBook>
{
    public override BaseItemKind Kind => BaseItemKind.AudioBookSeries;

    public Guid? LinkedBookSeriesId { get; set; }

    public BookSeries? LinkedBookSeries { get; set; }
}
