using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Video.TV;

public class TvEpisode : Video
{
    public override BaseItemKind Kind => BaseItemKind.TvEpisode;

    public DateTimeOffset? PremiereDate { get; set; }

    public Guid? SeriesId { get; set; }

    public TvSeries? Series { get; set; }

    public Guid? SeasonId { get; set; }

    public TvSeason? Season { get; set; }
}
