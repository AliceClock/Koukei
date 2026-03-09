using Koukei.Data.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Koukei.Data.Entities.Video;

public class Video : BaseItem
{
    public override BaseItemKind Kind => BaseItemKind.Video;

    public override MediaType MediaType => MediaType.Video;

    [NotMapped]
    public ICollection<VideoStreamInfo> VideoStreams { get; set; } = [];

    [NotMapped]
    public ICollection<AudioStreamInfo> AudioStreams { get; set; } = [];
}
