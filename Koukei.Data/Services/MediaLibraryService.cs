using Koukei.Data.Dtos;
using Koukei.Data.Entities;
using Koukei.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace Koukei.Data.Services;

public sealed class MediaLibraryService(KoukeiDbContext context) : IMediaLibraryService
{
    private const int PathLookupBatchSize = 400;

    public async Task<PagedResult<BaseItem>> SearchAsync(
        MediaLibraryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 500);
        var items = ApplyQuery(context.Items.AsNoTracking(), query);
        var totalCount = await items.CountAsync(cancellationToken);
        var page = await ApplySort(IncludeMediaLibraryDetails(items), query.SortField, query.SortDirection)
            .Skip(skip)
            .Take(take)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedResult<BaseItem>
        {
            Items = page,
            TotalCount = totalCount,
            Skip = skip,
            Take = take
        };
    }

    public async Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsAsync(
        MediaLibraryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await ApplyQuery(context.Items.AsNoTracking(), query)
            .Select(item => new MediaLibraryPlaybackItem
            {
                Id = item.Id,
                Name = item.Name,
                Path = item.Path ?? string.Empty,
                DateCreated = item.DateCreated,
                DurationSeconds = item.MediaStreams
                    .Where(stream => stream.Duration > 0)
                    .OrderBy(stream => stream.StreamIndex)
                    .Select(stream => stream.Duration)
                    .FirstOrDefault(),
                Kind = EF.Property<BaseItemKind>(item, "ItemKind"),
                Artist = (item as Koukei.Data.Entities.Audio.Audio)!.ArtistName,
                Album = (item as Koukei.Data.Entities.Audio.Audio)!.AlbumTitle,
                ThumbnailPath = item.Images
                    .Where(image => image.ImageType == LinkedImageType.Thumb)
                    .OrderBy(image => image.ImageIndex)
                    .Select(image => image.Path)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsByPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var normalizedPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(PathNormalizer.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return [];
        }

        var items = new List<MediaLibraryPlaybackItem>(normalizedPaths.Length);
        for (var offset = 0; offset < normalizedPaths.Length; offset += PathLookupBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = normalizedPaths
                .Skip(offset)
                .Take(PathLookupBatchSize)
                .ToArray();
            items.AddRange(await context.Items
                .AsNoTracking()
                .Where(item => item.NormalizedPath != null && batch.Contains(item.NormalizedPath))
                .Select(item => new MediaLibraryPlaybackItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Path = item.Path ?? string.Empty,
                    DateCreated = item.DateCreated,
                    DurationSeconds = item.MediaStreams
                        .Where(stream => stream.Duration > 0)
                        .OrderBy(stream => stream.StreamIndex)
                        .Select(stream => stream.Duration)
                        .FirstOrDefault(),
                    Kind = EF.Property<BaseItemKind>(item, "ItemKind"),
                    Artist = (item as Koukei.Data.Entities.Audio.Audio)!.ArtistName,
                    Album = (item as Koukei.Data.Entities.Audio.Audio)!.AlbumTitle,
                    ThumbnailPath = item.Images
                        .Where(image => image.ImageType == LinkedImageType.Thumb)
                        .OrderBy(image => image.ImageIndex)
                        .Select(image => image.Path)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken));
        }

        return items;
    }

    public Task<BaseItem?> GetAsync(
        Guid id,
        bool includeDetails = true,
        CancellationToken cancellationToken = default)
    {
        var query = includeDetails ? IncludeDetails(context.Items) : context.Items;
        return query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task<BaseItem?> GetByPathAsync(
        string path,
        bool includeDetails = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = PathNormalizer.Normalize(path);
        var query = includeDetails ? IncludeDetails(context.Items) : context.Items;
        return query.FirstOrDefaultAsync(item => item.NormalizedPath == normalizedPath, cancellationToken);
    }

    public Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = PathNormalizer.Normalize(path);
        return context.Items.AnyAsync(item => item.NormalizedPath == normalizedPath, cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetExistingPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var normalizedToOriginalPaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalizedPath = PathNormalizer.Normalize(path);
            if (!normalizedToOriginalPaths.TryGetValue(normalizedPath, out var originalPaths))
            {
                originalPaths = [];
                normalizedToOriginalPaths.Add(normalizedPath, originalPaths);
            }

            originalPaths.Add(path);
        }

        var existingNormalizedPaths = await FindExistingNormalizedPathsAsync(
            normalizedToOriginalPaths.Keys,
            cancellationToken);
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var normalizedPath in existingNormalizedPaths)
        {
            if (normalizedToOriginalPaths.TryGetValue(normalizedPath, out var originalPaths))
            {
                existingPaths.UnionWith(originalPaths);
            }
        }

        return existingPaths;
    }

    public async Task<BaseItem> AddAsync(
        BaseItem item,
        bool rejectDuplicatePath = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.NormalizedPath = PathNormalizer.NormalizeNullable(item.Path);

        if (rejectDuplicatePath && item.NormalizedPath is not null &&
            await context.Items.AnyAsync(existing => existing.NormalizedPath == item.NormalizedPath, cancellationToken))
        {
            throw new InvalidOperationException($"An item with path '{item.Path}' already exists.");
        }

        context.Items.Add(item);
        await context.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<MediaLibraryImportResult> ImportAsync(
        IReadOnlyList<BaseItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return new MediaLibraryImportResult
            {
                AddedItems = [],
                SkippedDuplicateCount = 0
            };
        }

        var uniqueItems = new List<BaseItem>(items.Count);
        var seenNormalizedPaths = new HashSet<string>(StringComparer.Ordinal);
        var skippedDuplicateCount = 0;
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            PrepareImportedItem(item);
            if (!seenNormalizedPaths.Add(item.NormalizedPath!))
            {
                skippedDuplicateCount++;
                continue;
            }

            uniqueItems.Add(item);
        }

        var existingNormalizedPaths = await FindExistingNormalizedPathsAsync(
            seenNormalizedPaths,
            cancellationToken);
        var newItems = uniqueItems
            .Where(item => !existingNormalizedPaths.Contains(item.NormalizedPath!))
            .ToList();
        skippedDuplicateCount += uniqueItems.Count - newItems.Count;

        if (newItems.Count > 0)
        {
            context.Items.AddRange(newItems);
            await context.SaveChangesAsync(cancellationToken);
        }

        return new MediaLibraryImportResult
        {
            AddedItems = newItems,
            SkippedDuplicateCount = skippedDuplicateCount
        };
    }

    public async Task UpdateImportedMetadataAsync(
        IReadOnlyList<BaseItem> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return;
        }

        var updatesById = new Dictionary<Guid, BaseItem>(updates.Count);
        foreach (var update in updates)
        {
            ArgumentNullException.ThrowIfNull(update);
            if (update.Id == Guid.Empty || !updatesById.TryAdd(update.Id, update))
            {
                throw new ArgumentException(
                    "Metadata updates require distinct, non-empty media item ids.",
                    nameof(updates));
            }

            PrepareImportedItem(update);
        }

        var itemIds = updatesById.Keys.ToArray();
        var existingItems = await context.Items
            .Include(item => item.MediaStreams)
            .Where(item => itemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (existingItems.Count != updatesById.Count)
        {
            var foundIds = existingItems.Select(item => item.Id).ToHashSet();
            var missingIds = updatesById.Keys.Where(id => !foundIds.Contains(id));
            throw new InvalidOperationException(
                $"Media metadata update targets were not found: {string.Join(", ", missingIds)}");
        }

        var refreshedAt = DateTimeOffset.UtcNow;
        foreach (var existingItem in existingItems)
        {
            var update = updatesById[existingItem.Id];
            existingItem.Name = update.Name;
            if (string.IsNullOrWhiteSpace(existingItem.ForcedSortName))
            {
                existingItem.SortName = update.SortName;
            }

            existingItem.Path = update.Path;
            existingItem.NormalizedPath = update.NormalizedPath;
            existingItem.Container = update.Container;
            existingItem.FileSize = update.FileSize;
            existingItem.LastModified = update.LastModified;
            existingItem.DateLastRefreshed = refreshedAt;

            if (existingItem is Koukei.Data.Entities.Audio.Audio existingAudio &&
                update is Koukei.Data.Entities.Audio.Audio updatedAudio)
            {
                existingAudio.ArtistName = updatedAudio.ArtistName;
                existingAudio.AlbumTitle = updatedAudio.AlbumTitle;
            }

            context.MediaStreams.RemoveRange(existingItem.MediaStreams);
            existingItem.MediaStreams.Clear();
            foreach (var stream in update.MediaStreams)
            {
                existingItem.MediaStreams.Add(stream);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await context.Items.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (item is null)
        {
            return false;
        }

        var linkedImages = await context.LinkedImages
            .Where(image => image.ItemId == id)
            .ToListAsync(cancellationToken);
        var linkedTexts = await context.LinkedTexts
            .Where(text => text.ItemId == id)
            .ToListAsync(cancellationToken);

        context.LinkedImages.RemoveRange(linkedImages);
        context.LinkedTexts.RemoveRange(linkedTexts);
        context.Items.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task SetTagsAsync(
        Guid itemId,
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagNames);

        var itemExists = await context.Items.AnyAsync(item => item.Id == itemId, cancellationToken);
        if (!itemExists)
        {
            throw new InvalidOperationException($"Item '{itemId}' does not exist.");
        }

        var normalizedNames = tagNames
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var existingLinks = await context.ItemTags
            .Where(link => link.ItemId == itemId)
            .ToListAsync(cancellationToken);

        context.ItemTags.RemoveRange(existingLinks);

        foreach (var name in normalizedNames)
        {
            var tag = await context.Tags.FirstOrDefaultAsync(tag => tag.Name == name, cancellationToken);
            if (tag is null)
            {
                tag = new Tag { Name = name };
                context.Tags.Add(tag);
            }

            context.ItemTags.Add(new ItemTag
            {
                ItemId = itemId,
                Tag = tag
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetRatingAsync(
        Guid itemId,
        string source,
        float? value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var itemExists = await context.Items.AnyAsync(item => item.Id == itemId, cancellationToken);
        if (!itemExists)
        {
            throw new InvalidOperationException($"Item '{itemId}' does not exist.");
        }

        var rating = await context.Ratings.FirstOrDefaultAsync(rating => rating.Source == source, cancellationToken);
        if (rating is null)
        {
            rating = new Rating { Source = source };
            context.Ratings.Add(rating);
        }

        var link = await context.ItemRatings.FirstOrDefaultAsync(
            link => link.ItemId == itemId && link.RatingId == rating.Id,
            cancellationToken);

        if (value is null)
        {
            if (link is not null)
            {
                context.ItemRatings.Remove(link);
            }
        }
        else if (link is null)
        {
            context.ItemRatings.Add(new ItemRating
            {
                ItemId = itemId,
                Rating = rating,
                Value = value
            });
        }
        else
        {
            link.Value = value;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetThumbnailAsync(
        Guid itemId,
        string? thumbnailPath,
        CancellationToken cancellationToken = default)
    {
        var itemExists = await context.Items.AnyAsync(item => item.Id == itemId, cancellationToken);
        if (!itemExists)
        {
            throw new InvalidOperationException($"Item '{itemId}' does not exist.");
        }

        var thumbnail = await context.LinkedImages.FirstOrDefaultAsync(
            image => image.ItemId == itemId &&
                image.ImageType == LinkedImageType.Thumb &&
                image.ImageIndex == 0,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            if (thumbnail is not null)
            {
                context.LinkedImages.Remove(thumbnail);
                await context.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        if (thumbnail is null)
        {
            context.LinkedImages.Add(new LinkedImage
            {
                ItemId = itemId,
                ImageType = LinkedImageType.Thumb,
                ImageIndex = 0,
                Path = thumbnailPath,
                DateModified = DateTimeOffset.UtcNow
            });
        }
        else
        {
            thumbnail.Path = thumbnailPath;
            thumbnail.DateModified = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ClearThumbnailPathsUnderAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var thumbnails = await context.LinkedImages
            .Where(image =>
                image.ImageType == LinkedImageType.Thumb &&
                image.Path != null)
            .ToListAsync(cancellationToken);
        var matchingThumbnails = thumbnails
            .Where(image => IsPathUnderRoot(image.Path, rootPath))
            .ToList();
        if (matchingThumbnails.Count == 0)
        {
            return 0;
        }

        context.LinkedImages.RemoveRange(matchingThumbnails);
        await context.SaveChangesAsync(cancellationToken);
        return matchingThumbnails.Count;
    }

    private static bool IsPathUnderRoot(string? candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            var normalizedRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var normalizedCandidate = Path.GetFullPath(candidatePath);
            return normalizedCandidate.StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public Task<List<BaseItem>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default)
    {
        return context.Items.AsNoTracking()
            .Where(item => item.ParentId == parentId)
            .OrderBy(item => item.ParentIndexNumber)
            .ThenBy(item => item.SortName ?? item.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<List<BaseItem>> GetByKindAsync(BaseItemKind kind, CancellationToken cancellationToken = default)
    {
        return context.Items.AsNoTracking()
            .Where(item => EF.Property<BaseItemKind>(item, "ItemKind") == kind)
            .OrderBy(item => item.SortName ?? item.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<MediaLibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return new MediaLibraryStatistics
        {
            TotalItems = await context.Items.CountAsync(cancellationToken),
            AudioItems = await context.AudioItems.CountAsync(cancellationToken),
            VideoItems = await context.Videos.CountAsync(cancellationToken),
            ImageItems = await context.Images.CountAsync(cancellationToken),
            DocumentItems = await context.Documents.CountAsync(cancellationToken),
            Tags = await context.Tags.CountAsync(cancellationToken),
            Genres = await context.Genres.CountAsync(cancellationToken),
            People = await context.People.CountAsync(cancellationToken),
            Studios = await context.Studios.CountAsync(cancellationToken)
        };
    }

    private async Task<HashSet<string>> FindExistingNormalizedPathsAsync(
        IEnumerable<string> normalizedPaths,
        CancellationToken cancellationToken)
    {
        var paths = normalizedPaths.Distinct(StringComparer.Ordinal).ToArray();
        var existingPaths = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < paths.Length; offset += PathLookupBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = paths
                .Skip(offset)
                .Take(PathLookupBatchSize)
                .ToArray();
            var matches = await context.Items
                .AsNoTracking()
                .Where(item => item.NormalizedPath != null && batch.Contains(item.NormalizedPath))
                .Select(item => item.NormalizedPath!)
                .ToListAsync(cancellationToken);
            existingPaths.UnionWith(matches);
        }

        return existingPaths;
    }

    private static void PrepareImportedItem(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            throw new ArgumentException("Imported media items require a file path.", nameof(item));
        }

        item.Path = Path.GetFullPath(item.Path.Trim());
        item.NormalizedPath = PathNormalizer.Normalize(item.Path);
        item.Name = string.IsNullOrWhiteSpace(item.Name)
            ? Path.GetFileNameWithoutExtension(item.Path)
            : item.Name.Trim();
        item.SortName = string.IsNullOrWhiteSpace(item.SortName)
            ? item.Name
            : item.SortName.Trim();
        item.Container = string.IsNullOrWhiteSpace(item.Container)
            ? null
            : item.Container.Trim();
        item.FileSize = item.FileSize is >= 0 ? item.FileSize : null;
    }

    private static IQueryable<BaseItem> ApplyQuery(IQueryable<BaseItem> items, MediaLibraryQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var searchText = query.SearchText.Trim();
            items = items.Where(item =>
                item.Name.Contains(searchText) ||
                (item.SortName != null && item.SortName.Contains(searchText)) ||
                (item.OriginalTitle != null && item.OriginalTitle.Contains(searchText)) ||
                (item.Path != null && item.Path.Contains(searchText)));
        }

        if (query.Kind is { } kind)
        {
            items = items.Where(item => EF.Property<BaseItemKind>(item, "ItemKind") == kind);
        }

        if (query.ParentId is { } parentId)
        {
            items = items.Where(item => item.ParentId == parentId);
        }

        if (query.TopParentId is { } topParentId)
        {
            items = items.Where(item => item.TopParentId == topParentId);
        }

        if (query.SourceType is { } sourceType)
        {
            items = items.Where(item => item.SourceType == sourceType);
        }

        if (!query.IncludeLocked)
        {
            items = items.Where(item => !item.IsLocked);
        }

        return items;
    }

    private static IOrderedQueryable<BaseItem> ApplySort(
        IQueryable<BaseItem> items,
        MediaLibrarySortField sortField,
        SortDirection sortDirection)
    {
        var descending = sortDirection == SortDirection.Descending;

        var orderedItems = sortField switch
        {
            MediaLibrarySortField.Name => descending
                ? items.OrderByDescending(item => item.Name)
                : items.OrderBy(item => item.Name),
            MediaLibrarySortField.SortName => descending
                ? items.OrderByDescending(item => item.SortName ?? item.Name)
                : items.OrderBy(item => item.SortName ?? item.Name),
            MediaLibrarySortField.ProductionYear => descending
                ? items.OrderByDescending(item => item.ProductionYear)
                : items.OrderBy(item => item.ProductionYear),
            MediaLibrarySortField.LastModified => descending
                ? items.OrderByDescending(item => item.LastModified)
                : items.OrderBy(item => item.LastModified),
            _ => descending
                ? items.OrderByDescending(item => item.DateCreated)
                : items.OrderBy(item => item.DateCreated)
        };

        return orderedItems.ThenBy(item => item.Id);
    }

    private static IQueryable<BaseItem> IncludeDetails(IQueryable<BaseItem> items)
    {
        return items
            .Include(item => item.Tags).ThenInclude(link => link.Tag)
            .Include(item => item.Genres).ThenInclude(link => link.Genre)
            .Include(item => item.RelatedPeople).ThenInclude(link => link.Person)
            .Include(item => item.Studios).ThenInclude(link => link.Studio)
            .Include(item => item.Ratings).ThenInclude(link => link.Rating)
            .Include(item => item.Images)
            .Include(item => item.Texts)
            .Include(item => item.MediaStreams);
    }

    private static IQueryable<BaseItem> IncludeMediaLibraryDetails(IQueryable<BaseItem> items)
    {
        return items
            .Include(item => item.Images)
            .Include(item => item.MediaStreams);
    }
}
