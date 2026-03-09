using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Video.Movie;

public class Movie : Video
{
    public override BaseItemKind Kind => BaseItemKind.Movie;

    public DateOnly? PremiereDate { get; set; }

    public Guid? MovieSeriesId { get; set; }

    public MovieSeries? MovieSeries { get; set; }
}
