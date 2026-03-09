using Koukei.Data.Entities.Audio.Music;
using Koukei.Data.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Koukei.Data.Entities.Video;

public class MusicVideo : Video
{
    public override BaseItemKind Kind => BaseItemKind.MusicVideo;

    public override MediaType MediaType => MediaType.Video;

    [NotMapped]
    public new ICollection<MediaStreamInfo> MediaStreams { get; set; } = [];

    public Guid? LinkedMusicId { get; set; }

    public Music? LinkedMusic { get; set; }
}
