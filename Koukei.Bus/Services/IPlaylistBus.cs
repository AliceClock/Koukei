using Koukei.Bus.Models;

namespace Koukei.Bus.Services;

public interface IPlaylistBus
{
    Task<IReadOnlyList<PlaylistSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<PlaylistDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PlaylistSummary> CreateAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default);

    Task<PlaylistSummary> UpdateAsync(
        Guid id,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PlaylistItemsAddResult> AddItemsAsync(
        Guid playlistId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveItemAsync(
        Guid playlistItemId,
        CancellationToken cancellationToken = default);

    Task<bool> MoveItemAsync(
        Guid playlistItemId,
        int newSortOrder,
        CancellationToken cancellationToken = default);

    Task<int> ClearAsync(Guid playlistId, CancellationToken cancellationToken = default);
}
