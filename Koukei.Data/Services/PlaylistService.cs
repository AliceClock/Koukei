using Koukei.Data.Dtos;
using Koukei.Data.Entities;
using Koukei.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Data.Services;

public sealed class PlaylistService(KoukeiDbContext context) : IPlaylistService
{
    public async Task<IReadOnlyList<PlaylistSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await context.Playlists.AsNoTracking()
            .OrderBy(playlist => playlist.SortName ?? playlist.Name)
            .Select(playlist => new PlaylistSummary
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Description = playlist.Description,
                ItemCount = playlist.Items.Count,
                DateCreated = playlist.DateCreated,
                DateLastSaved = playlist.DateLastSaved
            })
            .ToListAsync(cancellationToken);

        if (summaries.Count == 0)
        {
            return summaries;
        }

        var coverCandidates = await context.PlaylistItems.AsNoTracking()
            .Select(item => new
            {
                item.PlaylistId,
                item.SortOrder,
                item.DateAdded,
                ThumbnailPath = item.Item.Images
                    .Where(image => image.ImageType == LinkedImageType.Thumb)
                    .OrderBy(image => image.ImageIndex)
                    .Select(image => image.Path)
                    .FirstOrDefault()
            })
            .Where(candidate => candidate.ThumbnailPath != null)
            .OrderBy(candidate => candidate.PlaylistId)
            .ThenBy(candidate => candidate.SortOrder)
            .ThenBy(candidate => candidate.DateAdded)
            .ToListAsync(cancellationToken);
        var coversByPlaylist = coverCandidates
            .GroupBy(candidate => candidate.PlaylistId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(candidate => candidate.ThumbnailPath!)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToArray());

        foreach (var summary in summaries)
        {
            if (coversByPlaylist.TryGetValue(summary.Id, out var thumbnailPaths))
            {
                summary.ThumbnailPaths = thumbnailPaths;
            }
        }

        return summaries;
    }

    public Task<Playlist?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return context.Playlists.AsNoTracking()
            .Include(playlist => playlist.Items.OrderBy(item => item.SortOrder))
            .ThenInclude(item => item.Item)
            .FirstOrDefaultAsync(playlist => playlist.Id == id, cancellationToken);
    }

    public async Task<Playlist> CreateAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var playlist = new Playlist
        {
            Name = NormalizeName(name),
            SortName = NormalizeSortName(name),
            Description = NormalizeOptionalText(description)
        };

        context.Playlists.Add(playlist);
        await context.SaveChangesAsync(cancellationToken);
        return playlist;
    }

    public async Task<Playlist> UpdateAsync(
        Guid id,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var playlist = await context.Playlists
            .FirstOrDefaultAsync(playlist => playlist.Id == id, cancellationToken);
        if (playlist is null)
        {
            throw new InvalidOperationException($"Playlist '{id}' does not exist.");
        }

        playlist.Name = NormalizeName(name);
        playlist.SortName = NormalizeSortName(name);
        playlist.Description = NormalizeOptionalText(description);
        await context.SaveChangesAsync(cancellationToken);
        return playlist;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var playlist = await context.Playlists
            .FirstOrDefaultAsync(playlist => playlist.Id == id, cancellationToken);
        if (playlist is null)
        {
            return false;
        }

        context.Playlists.Remove(playlist);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PlaylistItem> AddItemAsync(
        Guid playlistId,
        Guid itemId,
        string? note = null,
        bool rejectDuplicate = true,
        CancellationToken cancellationToken = default)
    {
        var playlist = await context.Playlists
            .FirstOrDefaultAsync(playlist => playlist.Id == playlistId, cancellationToken);
        if (playlist is null)
        {
            throw new InvalidOperationException($"Playlist '{playlistId}' does not exist.");
        }

        var itemExists = await context.Items.AnyAsync(item => item.Id == itemId, cancellationToken);
        if (!itemExists)
        {
            throw new InvalidOperationException($"Item '{itemId}' does not exist.");
        }

        if (rejectDuplicate &&
            await context.PlaylistItems.AnyAsync(
                item => item.PlaylistId == playlistId && item.ItemId == itemId,
                cancellationToken))
        {
            throw new InvalidOperationException($"Item '{itemId}' already exists in playlist '{playlistId}'.");
        }

        var nextSortOrder = await context.PlaylistItems
            .Where(item => item.PlaylistId == playlistId)
            .Select(item => (int?)item.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var playlistItem = new PlaylistItem
        {
            PlaylistId = playlistId,
            ItemId = itemId,
            SortOrder = nextSortOrder + 1,
            Note = NormalizeOptionalText(note)
        };

        context.PlaylistItems.Add(playlistItem);
        playlist.DateLastSaved = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return playlistItem;
    }

    public async Task<PlaylistItemsAddResult> AddItemsAsync(
        Guid playlistId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var distinctItemIds = itemIds
            .Where(itemId => itemId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (distinctItemIds.Length == 0)
        {
            return new PlaylistItemsAddResult();
        }

        var playlist = await context.Playlists
            .FirstOrDefaultAsync(item => item.Id == playlistId, cancellationToken);
        if (playlist is null)
        {
            throw new InvalidOperationException($"Playlist '{playlistId}' does not exist.");
        }

        var existingMediaIds = await context.Items
            .Where(item => distinctItemIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var existingMediaIdSet = existingMediaIds.ToHashSet();
        var existingPlaylistItemIds = await context.PlaylistItems
            .Where(item =>
                item.PlaylistId == playlistId &&
                distinctItemIds.Contains(item.ItemId))
            .Select(item => item.ItemId)
            .ToListAsync(cancellationToken);
        var existingPlaylistItemIdSet = existingPlaylistItemIds.ToHashSet();
        var itemIdsToAdd = distinctItemIds
            .Where(itemId =>
                existingMediaIdSet.Contains(itemId) &&
                !existingPlaylistItemIdSet.Contains(itemId))
            .ToArray();

        if (itemIdsToAdd.Length > 0)
        {
            var nextSortOrder = (await context.PlaylistItems
                .Where(item => item.PlaylistId == playlistId)
                .Select(item => (int?)item.SortOrder)
                .MaxAsync(cancellationToken) ?? -1) + 1;
            var dateAdded = DateTimeOffset.UtcNow;
            context.PlaylistItems.AddRange(itemIdsToAdd.Select((itemId, index) =>
                new PlaylistItem
                {
                    PlaylistId = playlistId,
                    ItemId = itemId,
                    SortOrder = nextSortOrder + index,
                    DateAdded = dateAdded
                }));
            playlist.DateLastSaved = dateAdded;
            await context.SaveChangesAsync(cancellationToken);
        }

        return new PlaylistItemsAddResult
        {
            AddedCount = itemIdsToAdd.Length,
            DuplicateCount = existingPlaylistItemIdSet.Count,
            MissingCount = distinctItemIds.Length - existingMediaIdSet.Count
        };
    }

    public async Task<bool> RemoveItemAsync(
        Guid playlistItemId,
        CancellationToken cancellationToken = default)
    {
        var playlistItem = await context.PlaylistItems
            .FirstOrDefaultAsync(item => item.Id == playlistItemId, cancellationToken);
        if (playlistItem is null)
        {
            return false;
        }

        context.PlaylistItems.Remove(playlistItem);
        await context.SaveChangesAsync(cancellationToken);
        await NormalizeSortOrderAsync(playlistItem.PlaylistId, cancellationToken);
        await TouchPlaylistAsync(playlistItem.PlaylistId, cancellationToken);
        return true;
    }

    public async Task<bool> MoveItemAsync(
        Guid playlistItemId,
        int newSortOrder,
        CancellationToken cancellationToken = default)
    {
        var playlistItem = await context.PlaylistItems
            .FirstOrDefaultAsync(item => item.Id == playlistItemId, cancellationToken);
        if (playlistItem is null)
        {
            return false;
        }

        var playlistItems = await context.PlaylistItems
            .Where(item => item.PlaylistId == playlistItem.PlaylistId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.DateAdded)
            .ToListAsync(cancellationToken);

        playlistItems.RemoveAll(item => item.Id == playlistItemId);
        var boundedSortOrder = Math.Clamp(newSortOrder, 0, playlistItems.Count);
        playlistItems.Insert(boundedSortOrder, playlistItem);

        for (var index = 0; index < playlistItems.Count; index++)
        {
            playlistItems[index].SortOrder = -index - 1;
        }

        await context.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < playlistItems.Count; index++)
        {
            playlistItems[index].SortOrder = index;
        }

        await TouchPlaylistAsync(playlistItem.PlaylistId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> ClearAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        var items = await context.PlaylistItems
            .Where(item => item.PlaylistId == playlistId)
            .ToListAsync(cancellationToken);

        context.PlaylistItems.RemoveRange(items);
        await TouchPlaylistAsync(playlistId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return items.Count;
    }

    private async Task NormalizeSortOrderAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var items = await context.PlaylistItems
            .Where(item => item.PlaylistId == playlistId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.DateAdded)
            .ToListAsync(cancellationToken);

        for (var index = 0; index < items.Count; index++)
        {
            items[index].SortOrder = index;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task TouchPlaylistAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var playlist = await context.Playlists
            .FirstOrDefaultAsync(playlist => playlist.Id == playlistId, cancellationToken);
        if (playlist is not null)
        {
            playlist.DateLastSaved = DateTimeOffset.UtcNow;
        }
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    private static string NormalizeSortName(string name)
    {
        return NormalizeName(name).ToUpperInvariant();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
