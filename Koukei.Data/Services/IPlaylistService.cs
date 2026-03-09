using Koukei.Data.Dtos;
using Koukei.Data.Entities;

namespace Koukei.Data.Services;

public interface IPlaylistService
{
    Task<IReadOnlyList<PlaylistSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<Playlist?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Playlist> CreateAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default);

    Task<Playlist> UpdateAsync(
        Guid id,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PlaylistItem> AddItemAsync(
        Guid playlistId,
        Guid itemId,
        string? note = null,
        bool rejectDuplicate = true,
        CancellationToken cancellationToken = default);

    Task<PlaylistItemsAddResult> AddItemsAsync(
        Guid playlistId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveItemAsync(Guid playlistItemId, CancellationToken cancellationToken = default);

    Task<bool> MoveItemAsync(
        Guid playlistItemId,
        int newSortOrder,
        CancellationToken cancellationToken = default);

    Task<int> ClearAsync(Guid playlistId, CancellationToken cancellationToken = default);
}
