using Koukei.Data.Entities;
using Koukei.Data.Enums;

namespace Koukei.Data.Services;

public interface IMediaLibraryRootService
{
    Task<IReadOnlyList<MediaLibraryRoot>> ListAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default);

    Task<MediaLibraryRoot?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MediaLibraryRoot?> GetByPathAsync(string path, CancellationToken cancellationToken = default);

    Task<MediaLibraryRoot> RegisterAsync(
        string path,
        string? name = null,
        SourceType sourceType = SourceType.FileSystem,
        bool includeSubdirectories = true,
        CancellationToken cancellationToken = default);

    Task<bool> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MediaLibraryScan> BeginScanAsync(Guid libraryRootId, CancellationToken cancellationToken = default);

    Task<MediaLibraryScan> CompleteScanAsync(
        Guid scanId,
        LibraryScanStatus status,
        int itemsDiscovered = 0,
        int itemsAdded = 0,
        int itemsUpdated = 0,
        int itemsSkipped = 0,
        int itemsFailed = 0,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);
}
