using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Video.Movie;

public class MovieSeries : Folder<Movie>
{
    public override BaseItemKind Kind => BaseItemKind.MovieSeries;
}