using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Video.TV;

public class TvSeries : Folder<TvSeason>
{
    public override BaseItemKind Kind => BaseItemKind.TvSeries;
}