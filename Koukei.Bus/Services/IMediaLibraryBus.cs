using Koukei.Bus.Models;

namespace Koukei.Bus.Services;

public interface IMediaLibraryBus
{
    Task<PagedResult<MediaLibraryItem>> SearchAsync(
        MediaLibraryQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsAsync(
        MediaLibraryPlaybackQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsByPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task<MediaLibraryItem?> GetByPathAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaLibraryItem>> GetRecentlyOpenedAsync(
        int take = 8,
        CancellationToken cancellationToken = default);

    Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetExistingPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task<MediaLibraryItem> AddAsync(
        NewMediaLibraryItem item,
        bool rejectDuplicatePath = true,
        CancellationToken cancellationToken = default);

    Task<MediaLibraryImportResult> ImportAsync(
        IReadOnlyList<NewMediaLibraryItem> items,
        CancellationToken cancellationToken = default);

    Task UpdateMetadataAsync(
        IReadOnlyList<MediaLibraryMetadataUpdate> updates,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Guid itemId, bool isFavorite, CancellationToken cancellationToken = default);

    Task SetUserRatingAsync(Guid itemId, int? rating, CancellationToken cancellationToken = default);

    Task SetThumbnailAsync(Guid itemId, string? thumbnailPath, CancellationToken cancellationToken = default);

    Task SetLinkedFilePathAsync(
        Guid itemId,
        string? linkedFilePath,
        CancellationToken cancellationToken = default);

    Task<int> ClearThumbnailPathsUnderAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    Task SetPlaybackPositionAsync(
        Guid itemId,
        TimeSpan? position,
        CancellationToken cancellationToken = default);

    Task RecordPlayedAsync(
        Guid itemId,
        TimeSpan? position = null,
        CancellationToken cancellationToken = default);
}
