using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Audio.Music;

public class Music : Audio
{
    public override BaseItemKind Kind => BaseItemKind.Music;

    public DateOnly? ReleaseDate { get; set; }

    public int? TrackNumber { get; set; } = 0;

    public Guid? AlbumId { get; set; }

    public MusicAlbum? Album { get; set; }
}
