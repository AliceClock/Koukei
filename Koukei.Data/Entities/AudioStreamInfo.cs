using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public class AudioStreamInfo : MediaStreamInfo
{
    public override MediaStreamType Type => MediaStreamType.Audio;

    public int? Channels { get; set; }

    public string? ChannelLayout { get; set; }

    public int? SampleRate { get; set; }

    public int? BitDepth { get; set; }
}
