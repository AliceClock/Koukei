using Koukei.Data.Entities;
using Koukei.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Data.Services;

public sealed class MediaLibraryRootService(KoukeiDbContext context) : IMediaLibraryRootService
{
    public async Task<IReadOnlyList<MediaLibraryRoot>> ListAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.MediaLibraryRoots.AsNoTracking();

        if (!includeDisabled)
        {
            query = query.Where(root => root.IsEnabled);
        }

        return await query
            .OrderBy(root => root.Name)
            .ThenBy(root => root.Path)
            .ToListAsync(cancellationToken);
    }

    public Task<MediaLibraryRoot?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return context.MediaLibraryRoots.AsNoTracking()
            .Include(root => root.Scans)
            .FirstOrDefaultAsync(root => root.Id == id, cancellationToken);
    }

    public Task<MediaLibraryRoot?> GetByPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = PathNormalizer.Normalize(path);
        return context.MediaLibraryRoots.AsNoTracking()
            .FirstOrDefaultAsync(root => root.NormalizedPath == normalizedPath, cancellationToken);
    }

    public async Task<MediaLibraryRoot> RegisterAsync(
        string path,
        string? name = null,
        SourceType sourceType = SourceType.FileSystem,
        bool includeSubdirectories = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = PathNormalizer.Normalize(path);
        var root = await context.MediaLibraryRoots
            .FirstOrDefaultAsync(root => root.NormalizedPath == normalizedPath, cancellationToken);

        if (root is null)
        {
            root = new MediaLibraryRoot
            {
                Path = path.Trim(),
                NormalizedPath = normalizedPath,
                Name = NormalizeName(name, path),
                SourceType = sourceType,
                IncludeSubdirectories = includeSubdirectories
            };
            context.MediaLibraryRoots.Add(root);
        }
        else
        {
            root.Path = path.Trim();
            root.NormalizedPath = normalizedPath;
            root.Name = NormalizeName(name, path);
            root.SourceType = sourceType;
            root.IncludeSubdirectories = includeSubdirectories;
            root.IsEnabled = true;
        }

        await context.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<bool> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var root = await context.MediaLibraryRoots
            .FirstOrDefaultAsync(root => root.Id == id, cancellationToken);
        if (root is null)
        {
            return false;
        }

        root.IsEnabled = isEnabled;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var root = await context.MediaLibraryRoots
            .FirstOrDefaultAsync(root => root.Id == id, cancellationToken);
        if (root is null)
        {
            return false;
        }

        context.MediaLibraryRoots.Remove(root);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MediaLibraryScan> BeginScanAsync(
        Guid libraryRootId,
        CancellationToken cancellationToken = default)
    {
        var root = await context.MediaLibraryRoots
            .FirstOrDefaultAsync(root => root.Id == libraryRootId, cancellationToken);
        if (root is null)
        {
            throw new InvalidOperationException($"Media library root '{libraryRootId}' does not exist.");
        }

        var now = DateTimeOffset.UtcNow;
        var scan = new MediaLibraryScan
        {
            LibraryRootId = libraryRootId,
            StartedAt = now,
            Status = LibraryScanStatus.Running
        };

        root.LastScanStartedAt = now;
        root.LastScanCompletedAt = null;
        root.LastScanStatus = LibraryScanStatus.Running;
        root.LastError = null;
        context.MediaLibraryScans.Add(scan);
        await context.SaveChangesAsync(cancellationToken);
        return scan;
    }

    public async Task<MediaLibraryScan> CompleteScanAsync(
        Guid scanId,
        LibraryScanStatus status,
        int itemsDiscovered = 0,
        int itemsAdded = 0,
        int itemsUpdated = 0,
        int itemsSkipped = 0,
        int itemsFailed = 0,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (status == LibraryScanStatus.NeverScanned || status == LibraryScanStatus.Running)
        {
            throw new ArgumentException("A completed scan must use a terminal status.", nameof(status));
        }

        var scan = await context.MediaLibraryScans
            .Include(scan => scan.LibraryRoot)
            .FirstOrDefaultAsync(scan => scan.Id == scanId, cancellationToken);
        if (scan is null)
        {
            throw new InvalidOperationException($"Media library scan '{scanId}' does not exist.");
        }

        var now = DateTimeOffset.UtcNow;
        scan.CompletedAt = now;
        scan.Status = status;
        scan.ItemsDiscovered = Math.Max(0, itemsDiscovered);
        scan.ItemsAdded = Math.Max(0, itemsAdded);
        scan.ItemsUpdated = Math.Max(0, itemsUpdated);
        scan.ItemsSkipped = Math.Max(0, itemsSkipped);
        scan.ItemsFailed = Math.Max(0, itemsFailed);
        scan.ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();

        scan.LibraryRoot.LastScanCompletedAt = now;
        scan.LibraryRoot.LastScanStatus = status;
        scan.LibraryRoot.LastError = scan.ErrorMessage;

        await context.SaveChangesAsync(cancellationToken);
        return scan;
    }

    private static string NormalizeName(string? name, string path)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        var trimmedPath = path.Trim();
        var fileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(trimmedPath));
        return string.IsNullOrWhiteSpace(fileName) ? trimmedPath : fileName;
    }
}
