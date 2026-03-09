using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Audio.Radio;

public class Radio : Folder<RadioEpisode>
{
    public override BaseItemKind Kind => BaseItemKind.Radio;
}