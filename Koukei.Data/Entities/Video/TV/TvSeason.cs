using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Video.TV;

public class TvSeason : Folder<TvEpisode>
{
    public override BaseItemKind Kind => BaseItemKind.TvSeason;

    public Guid? SeriesId { get; set; }

    public TvSeries? Series { get; set; }

    public List<DayOfWeek> AirDays { get; set; } = [];

    public TimeOnly? AirTime { get; set; }
}
