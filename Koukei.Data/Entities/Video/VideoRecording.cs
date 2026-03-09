using System;
using System.Collections.Generic;
using System.Text;
using Koukei.Data.Enums;

namespace Koukei.Data.Entities.Video;

public class VideoRecording : Video
{
    public override BaseItemKind Kind => BaseItemKind.VideoRecording;

    public DateTime? RecordingDate { get; set; }
}