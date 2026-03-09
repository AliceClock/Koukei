using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Audio.Radio;

public class RadioEpisode : Audio
{
    public override BaseItemKind Kind => BaseItemKind.RadioEpisode;

    public DateTimeOffset? AirDate { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; } = 0;

    public Guid? RadioId { get; set; }

    public Radio? Radio { get; set; }
}
