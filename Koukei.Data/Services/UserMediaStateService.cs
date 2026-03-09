using Koukei.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Data.Services;

public sealed class UserMediaStateService(KoukeiDbContext context) : IUserMediaStateService
{
    public Task<UserMediaState?> GetAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        return context.UserMediaStates.AsNoTracking()
            .FirstOrDefaultAsync(state => state.ItemId == itemId, cancellationToken);
    }

    public async Task<IReadOnlyList<UserMediaState>> GetManyAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var ids = itemIds.ToArray();
        return await context.UserMediaStates.AsNoTracking()
            .Where(state => ids.Contains(state.ItemId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserMediaState>> GetRecentlyOpenedAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var resultCount = Math.Clamp(take, 1, 50);
        return await context.UserMediaStates.AsNoTracking()
            .Where(state => state.LastOpenedAt != null)
            .OrderByDescending(state => state.LastOpenedAt)
            .ThenBy(state => state.ItemId)
            .Include(state => state.Item)
                .ThenInclude(item => item.Images)
            .Include(state => state.Item)
                .ThenInclude(item => item.MediaStreams)
            .Take(resultCount)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task<UserMediaState> SetFavoriteAsync(
        Guid itemId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateAsync(itemId, cancellationToken);
        state.IsFavorite = isFavorite;
        await context.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<UserMediaState> SetUserRatingAsync(
        Guid itemId,
        int? rating,
        CancellationToken cancellationToken = default)
    {
        if (rating is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "User rating must be between 0 and 5.");
        }

        var state = await GetOrCreateAsync(itemId, cancellationToken);
        state.UserRating = rating is 0 ? null : rating;
        await context.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<UserMediaState> SetPlaybackPositionAsync(
        Guid itemId,
        TimeSpan? position,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateAsync(itemId, cancellationToken);
        state.PlaybackPositionTicks = position is null ? null : Math.Max(0L, position.Value.Ticks);
        await context.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<UserMediaState> RecordPlayedAsync(
        Guid itemId,
        TimeSpan? position = null,
        DateTimeOffset? playedAt = null,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateAsync(itemId, cancellationToken);
        state.PlayCount++;
        state.LastPlayedAt = playedAt ?? DateTimeOffset.UtcNow;
        state.LastOpenedAt = state.LastPlayedAt;
        state.PlaybackPositionTicks = position is null ? state.PlaybackPositionTicks : Math.Max(0L, position.Value.Ticks);
        await context.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<bool> DeleteAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var state = await context.UserMediaStates
            .FirstOrDefaultAsync(state => state.ItemId == itemId, cancellationToken);
        if (state is null)
        {
            return false;
        }

        context.UserMediaStates.Remove(state);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<UserMediaState> GetOrCreateAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var state = await context.UserMediaStates
            .FirstOrDefaultAsync(state => state.ItemId == itemId, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        var itemExists = await context.Items.AnyAsync(item => item.Id == itemId, cancellationToken);
        if (!itemExists)
        {
            throw new InvalidOperationException($"Item '{itemId}' does not exist.");
        }

        state = new UserMediaState { ItemId = itemId };
        context.UserMediaStates.Add(state);
        return state;
    }
}
