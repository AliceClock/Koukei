using Koukei.Data.Dtos;
using Koukei.Data.Entities;
using Koukei.Data.Enums;

namespace Koukei.Data.Services;

public interface IMediaLibraryService
{
    Task<PagedResult<BaseItem>> SearchAsync(MediaLibraryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsAsync(
        MediaLibraryQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsByPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task<BaseItem?> GetAsync(Guid id, bool includeDetails = true, CancellationToken cancellationToken = default);

    Task<BaseItem?> GetByPathAsync(string path, bool includeDetails = true, CancellationToken cancellationToken = default);

    Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetExistingPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    Task<BaseItem> AddAsync(BaseItem item, bool rejectDuplicatePath = true, CancellationToken cancellationToken = default);

    Task<MediaLibraryImportResult> ImportAsync(
        IReadOnlyList<BaseItem> items,
        CancellationToken cancellationToken = default);

    Task UpdateImportedMetadataAsync(
        IReadOnlyList<BaseItem> updates,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetTagsAsync(Guid itemId, IEnumerable<string> tagNames, CancellationToken cancellationToken = default);

    Task SetRatingAsync(Guid itemId, string source, float? value, CancellationToken cancellationToken = default);

    Task SetThumbnailAsync(Guid itemId, string? thumbnailPath, CancellationToken cancellationToken = default);

    Task<int> ClearThumbnailPathsUnderAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    Task<List<BaseItem>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);

    Task<List<BaseItem>> GetByKindAsync(BaseItemKind kind, CancellationToken cancellationToken = default);

    Task<MediaLibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
