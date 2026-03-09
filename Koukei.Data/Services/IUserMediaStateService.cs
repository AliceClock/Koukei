using Koukei.Data.Entities;

namespace Koukei.Data.Services;

public interface IUserMediaStateService
{
    Task<UserMediaState?> GetAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserMediaState>> GetManyAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserMediaState>> GetRecentlyOpenedAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<UserMediaState> SetFavoriteAsync(
        Guid itemId,
        bool isFavorite,
        CancellationToken cancellationToken = default);

    Task<UserMediaState> SetUserRatingAsync(
        Guid itemId,
        int? rating,
        CancellationToken cancellationToken = default);

    Task<UserMediaState> SetPlaybackPositionAsync(
        Guid itemId,
        TimeSpan? position,
        CancellationToken cancellationToken = default);

    Task<UserMediaState> RecordPlayedAsync(
        Guid itemId,
        TimeSpan? position = null,
        DateTimeOffset? playedAt = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid itemId, CancellationToken cancellationToken = default);
}
