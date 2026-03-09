using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Audio.Music;

public class MusicAlbum : Folder<Music>
{
    public override BaseItemKind Kind => BaseItemKind.MusicAlbum;

    public DateTimeOffset? ReleaseDate { get; set; }
}