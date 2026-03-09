using Koukei.Data.Entities.Document.Book;
using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Audio.AudioBook;

public class AudioBook : Audio
{
    public override BaseItemKind Kind => BaseItemKind.AudioBook;

    public DateOnly? ReleaseDate { get; set; }

    public int? VolumeNumber { get; set; } = 0;

    public Guid? AudioBookSeriesId { get; set; }

    public AudioBookSeries? AudioBookSeries { get; set; }

    public Guid? LinkedBookId { get; set; }

    public Book? LinkedBook { get; set; }
}
