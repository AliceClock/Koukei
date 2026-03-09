using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Audio;

public class AudioRecording : Audio
{
    public override BaseItemKind Kind => BaseItemKind.AudioRecording;

    public DateTime? RecordingDate { get; set; }
}
