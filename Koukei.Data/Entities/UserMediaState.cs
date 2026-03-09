namespace Koukei.Data.Entities;

public class UserMediaState
{
    public Guid ItemId { get; set; }

    public BaseItem Item { get; set; } = null!;

    public bool IsFavorite { get; set; }

    public int? UserRating { get; set; }

    public long? PlaybackPositionTicks { get; set; }

    public int PlayCount { get; set; }

    public DateTimeOffset? LastPlayedAt { get; set; }

    public DateTimeOffset? LastOpenedAt { get; set; }

    public DateTimeOffset DateModified { get; set; } = DateTimeOffset.UtcNow;
}
