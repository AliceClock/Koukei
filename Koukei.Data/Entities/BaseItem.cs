using Koukei.Data.Enums;

namespace Koukei.Data.Entities;

public abstract class BaseItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ParentId { get; set; }

    public BaseItem? Parent { get; set; }

    public Guid? TopParentId { get; set; }

    public BaseItem? TopParent { get; set; }

    public ICollection<BaseItem> Children { get; set; } = [];

    public string? Path { get; set; }

    public string? NormalizedPath { get; set; }

    public string? Container { get; set; }

    public long? FileSize { get; set; }

    public SourceType SourceType { get; set; } = SourceType.Virtual;

    public virtual BaseItemKind Kind => BaseItemKind.Unknown;

    public virtual MediaType MediaType => MediaType.Unknown;

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    public string? OriginalTitle { get; set; }

    public string? ForcedSortName { get; set; }

    public string? Overview { get; set; }

    public int? ProductionYear { get; set; }

    public DateTimeOffset DateCreated { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DateLastSaved { get; set; }

    public DateTimeOffset? DateLastRefreshed { get; set; }

    public bool IsLocked { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public string? Hash { get; set; }

    public ICollection<ItemPerson> RelatedPeople { get; set; } = [];

    public ICollection<ItemGenre> Genres { get; set; } = [];

    public ICollection<ItemTag> Tags { get; set; } = [];

    public ICollection<ItemRating> Ratings { get; set; } = [];

    public ICollection<ItemStudio> Studios { get; set; } = [];

    public ICollection<LinkedImage> Images { get; set; } = [];

    public ICollection<LinkedText> Texts { get; set; } = [];

    public ICollection<MediaStreamInfo> MediaStreams { get; set; } = [];

    public int? IndexNumber { get; set; }

    public int? ParentIndexNumber { get; set; }

    public virtual bool IsFolder => false;
}
