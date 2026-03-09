using Koukei.Data.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Koukei.Data.Entities.Audio;

public class Audio : BaseItem
{
    public override BaseItemKind Kind => BaseItemKind.Audio;

    public override MediaType MediaType => MediaType.Audio;

    public string? ArtistName { get; set; }

    public string? AlbumTitle { get; set; }

    [NotMapped]
    public ICollection<AudioStreamInfo> AudioStreams { get; set; } = [];
}
