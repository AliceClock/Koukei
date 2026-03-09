using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public class VideoStreamInfo : MediaStreamInfo
{
    public override MediaStreamType Type => MediaStreamType.Video;

    public int? Width { get; set; }

    public int? Height { get; set; }

    public double? FrameRate { get; set; }

    public string? CodecProfile { get; set; }

    public string? PixelFormat { get; set; }

    public int? Rotation { get; set; }

    public string? ColorDepth { get; set; }

    public bool? IsHdr { get; set; } = false;

    public bool? IsDolbyVision { get; set; } = false;
}
