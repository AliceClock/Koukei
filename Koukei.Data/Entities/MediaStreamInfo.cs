using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public abstract class MediaStreamInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public int StreamIndex { get; set; }

    public virtual MediaStreamType Type { get; set; } = MediaStreamType.Unknown;

    public double? Duration { get; set; }

    public string? Codec { get; set; }

    public string? Language { get; set; }

    public long? BitRate { get; set; }

    public bool IsDefault { get; set; }

    public bool IsForced { get; set; }

    public string? Title { get; set; }
}
