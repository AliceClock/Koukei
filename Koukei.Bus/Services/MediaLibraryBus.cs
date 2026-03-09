using Koukei.Bus.Models;
using Koukei.Data.Entities;
using Koukei.Data.Entities.Audio;
using Koukei.Data.Entities.Audio.Music;
using Koukei.Data.Enums;
using Koukei.Data.Services;

using DataImage = Koukei.Data.Entities.Image.Image;
using DataMediaLibraryQuery = Koukei.Data.Dtos.MediaLibraryQuery;
using DataMediaLibrarySortField = Koukei.Data.Dtos.MediaLibrarySortField;
using DataSortDirection = Koukei.Data.Dtos.SortDirection;
using DataBaseItemKind = Koukei.Data.Enums.BaseItemKind;
using DataVideo = Koukei.Data.Entities.Video.Video;

namespace Koukei.Bus.Services;

public sealed class MediaLibraryBus(
    IMediaLibraryService mediaLibrary,
    IUserMediaStateService userMediaState) : IMediaLibraryBus
{
    public async Task<PagedResult<MediaLibraryItem>> SearchAsync(
        MediaLibraryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await mediaLibrary.SearchAsync(ToDataQuery(query), cancellationToken);
        var itemIds = page.Items.Select(item => item.Id).ToArray();
        var states = await userMediaState.GetManyAsync(itemIds, cancellationToken);
        var statesByItemId = states.ToDictionary(state => state.ItemId);
        var items = new List<MediaLibraryItem>(page.Items.Count);

        foreach (var item in page.Items)
        {
            statesByItemId.TryGetValue(item.Id, out var state);
            items.Add(ToBusItem(item, state));
        }

        return new PagedResult<MediaLibraryItem>
        {
            Items = items,
            TotalCount = page.TotalCount,
            Skip = page.Skip,
            Take = page.Take
        };
    }

    public async Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsAsync(
        MediaLibraryPlaybackQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = await mediaLibrary.GetPlaybackItemsAsync(
            new DataMediaLibraryQuery
            {
                SearchText = query.SearchText,
                Kind = query.Kind switch
                {
                    MediaLibraryItemKind.Video => DataBaseItemKind.Video,
                    MediaLibraryItemKind.Audio => DataBaseItemKind.Music,
                    _ => null
                },
                IncludeLocked = true
            },
            cancellationToken);

        var states = await userMediaState.GetManyAsync(
            items.Select(item => item.Id).ToArray(),
            cancellationToken);
        var statesByItemId = states.ToDictionary(state => state.ItemId);
        return items
            .Select(item =>
            {
                statesByItemId.TryGetValue(item.Id, out var state);
                return ToPlaybackItem(item, state);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<MediaLibraryPlaybackItem>> GetPlaybackItemsByPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var items = await mediaLibrary.GetPlaybackItemsByPathsAsync(paths, cancellationToken);
        var states = await userMediaState.GetManyAsync(
            items.Select(item => item.Id).ToArray(),
            cancellationToken);
        var statesByItemId = states.ToDictionary(state => state.ItemId);
        return items
            .Select(item =>
            {
                statesByItemId.TryGetValue(item.Id, out var state);
                return ToPlaybackItem(item, state);
            })
            .ToList();
    }

    public async Task<MediaLibraryItem?> GetByPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var item = await mediaLibrary.GetByPathAsync(path, includeDetails: true, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var state = await userMediaState.GetAsync(item.Id, cancellationToken);
        return ToBusItem(item, state);
    }

    public async Task<IReadOnlyList<MediaLibraryItem>> GetRecentlyOpenedAsync(
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        var states = await userMediaState.GetRecentlyOpenedAsync(take, cancellationToken);
        return states
            .Select(state => ToBusItem(state.Item, state))
            .ToList();
    }

    public Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        return mediaLibrary.PathExistsAsync(path, cancellationToken);
    }

    public Task<IReadOnlySet<string>> GetExistingPathsAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        return mediaLibrary.GetExistingPathsAsync(paths, cancellationToken);
    }

    public async Task<MediaLibraryItem> AddAsync(
        NewMediaLibraryItem item,
        bool rejectDuplicatePath = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var persisted = await mediaLibrary.AddAsync(ToDataItem(item), rejectDuplicatePath, cancellationToken);
        return ToBusItem(persisted, state: null);
    }

    public async Task<MediaLibraryImportResult> ImportAsync(
        IReadOnlyList<NewMediaLibraryItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var result = await mediaLibrary.ImportAsync(
            items.Select(ToDataItem).ToList(),
            cancellationToken);
        return new MediaLibraryImportResult
        {
            AddedItems = result.AddedItems
                .Select(item => ToBusItem(item, state: null))
                .ToList(),
            SkippedDuplicateCount = result.SkippedDuplicateCount
        };
    }

    public Task UpdateMetadataAsync(
        IReadOnlyList<MediaLibraryMetadataUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var dataUpdates = updates.Select(update =>
        {
            if (update.Id == Guid.Empty)
            {
                throw new ArgumentException("Metadata updates require a media item id.", nameof(updates));
            }

            var dataItem = ToDataItem(update.Metadata);
            dataItem.Id = update.Id;
            return dataItem;
        }).ToList();
        return mediaLibrary.UpdateImportedMetadataAsync(dataUpdates, cancellationToken);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return mediaLibrary.DeleteAsync(id, cancellationToken);
    }

    public Task SetFavoriteAsync(Guid itemId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        return userMediaState.SetFavoriteAsync(itemId, isFavorite, cancellationToken);
    }

    public Task SetUserRatingAsync(Guid itemId, int? rating, CancellationToken cancellationToken = default)
    {
        return userMediaState.SetUserRatingAsync(itemId, rating, cancellationToken);
    }

    public Task SetThumbnailAsync(Guid itemId, string? thumbnailPath, CancellationToken cancellationToken = default)
    {
        return mediaLibrary.SetThumbnailAsync(itemId, thumbnailPath, cancellationToken);
    }

    public Task<int> ClearThumbnailPathsUnderAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        return mediaLibrary.ClearThumbnailPathsUnderAsync(rootPath, cancellationToken);
    }

    public Task SetPlaybackPositionAsync(
        Guid itemId,
        TimeSpan? position,
        CancellationToken cancellationToken = default)
    {
        return userMediaState.SetPlaybackPositionAsync(itemId, position, cancellationToken);
    }

    public Task RecordPlayedAsync(
        Guid itemId,
        TimeSpan? position = null,
        CancellationToken cancellationToken = default)
    {
        return userMediaState.RecordPlayedAsync(
            itemId,
            position,
            cancellationToken: cancellationToken);
    }

    private static DataMediaLibraryQuery ToDataQuery(MediaLibraryQuery query)
    {
        return new DataMediaLibraryQuery
        {
            SearchText = query.SearchText,
            Kind = query.Kind switch
            {
                MediaLibraryItemKind.Video => DataBaseItemKind.Video,
                MediaLibraryItemKind.Audio => DataBaseItemKind.Music,
                MediaLibraryItemKind.Image => DataBaseItemKind.Image,
                _ => null
            },
            IncludeLocked = query.IncludeLocked,
            SortField = query.SortField switch
            {
                MediaLibrarySortField.Name => DataMediaLibrarySortField.Name,
                MediaLibrarySortField.SortName => DataMediaLibrarySortField.SortName,
                MediaLibrarySortField.ProductionYear => DataMediaLibrarySortField.ProductionYear,
                MediaLibrarySortField.LastModified => DataMediaLibrarySortField.LastModified,
                _ => DataMediaLibrarySortField.DateCreated
            },
            SortDirection = query.SortDirection == SortDirection.Ascending
                ? DataSortDirection.Ascending
                : DataSortDirection.Descending,
            Skip = query.Skip,
            Take = query.Take
        };
    }

    private static MediaLibraryPlaybackItem ToPlaybackItem(
        Koukei.Data.Dtos.MediaLibraryPlaybackItem item,
        UserMediaState? state)
    {
        return new MediaLibraryPlaybackItem
        {
            Id = item.Id,
            Title = item.Name,
            Path = item.Path,
            DateCreated = item.DateCreated,
            Duration = item.DurationSeconds is > 0 and var duration
                ? TimeSpan.FromSeconds(duration)
                : null,
            Kind = item.Kind switch
            {
                DataBaseItemKind.Video => MediaLibraryItemKind.Video,
                DataBaseItemKind.Music => MediaLibraryItemKind.Audio,
                _ => MediaLibraryItemKind.Unknown
            },
            Artist = item.Artist,
            Album = item.Album,
            ThumbnailPath = item.ThumbnailPath,
            PlaybackPosition = state?.PlaybackPositionTicks is >= 0 and var ticks
                ? TimeSpan.FromTicks(ticks)
                : null
        };
    }

    private static BaseItem ToDataItem(NewMediaLibraryItem item)
    {
        BaseItem dataItem = item.Kind switch
        {
            MediaLibraryItemKind.Audio => new Music(),
            MediaLibraryItemKind.Image => new DataImage(),
            MediaLibraryItemKind.Video => new DataVideo(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(item),
                item.Kind,
                "Only audio, video, and image items can be imported.")
        };

        dataItem.Name = item.Name;
        dataItem.SortName = item.Name;
        dataItem.Path = item.Path;
        dataItem.Container = FirstNonEmptyOrNull(item.ContainerFormat, item.Extension.TrimStart('.'));
        dataItem.FileSize = item.FileSize is >= 0 ? item.FileSize : null;
        dataItem.SourceType = SourceType.FileSystem;
        dataItem.DateCreated = item.DateCreated;
        dataItem.DateLastRefreshed = DateTimeOffset.UtcNow;
        dataItem.LastModified = item.LastModified;

        if (dataItem is Audio audio)
        {
            audio.ArtistName = NormalizeOptionalText(item.Artist);
            audio.AlbumTitle = NormalizeOptionalText(item.Album);
        }

        if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
        {
            dataItem.Images.Add(new LinkedImage
            {
                ImageType = LinkedImageType.Thumb,
                ImageIndex = 0,
                Path = item.ThumbnailPath,
                DateModified = DateTimeOffset.UtcNow
            });
        }

        foreach (var stream in item.Streams)
        {
            var dataStream = ToDataStream(stream);
            if (dataStream is not null)
            {
                dataItem.MediaStreams.Add(dataStream);
            }
        }

        if (dataItem.MediaStreams.Count == 0 && dataItem is DataVideo &&
            (item.Duration is not null || item.Width is not null || item.Height is not null))
        {
            dataItem.MediaStreams.Add(new VideoStreamInfo
            {
                StreamIndex = 0,
                Duration = item.Duration is { } duration ? duration.TotalSeconds : null,
                Width = item.Width,
                Height = item.Height
            });
        }
        else if (dataItem.MediaStreams.Count == 0 && dataItem is Music && item.Duration is { } audioDuration)
        {
            dataItem.MediaStreams.Add(new AudioStreamInfo
            {
                StreamIndex = 0,
                Duration = audioDuration.TotalSeconds
            });
        }

        if (dataItem is DataImage image && (item.Width is not null || item.Height is not null))
        {
            image.ImageInfo.Add(new ImageInfo
            {
                Width = item.Width,
                Height = item.Height,
                Format = item.Extension.TrimStart('.')
            });
        }

        return dataItem;
    }

    private static MediaStreamInfo? ToDataStream(NewMediaLibraryStream stream)
    {
        MediaStreamInfo? dataStream = stream.Kind switch
        {
            MediaLibraryStreamKind.Video => new VideoStreamInfo
            {
                Width = stream.Width is > 0 ? stream.Width : null,
                Height = stream.Height is > 0 ? stream.Height : null,
                FrameRate = stream.FrameRate is > 0 ? stream.FrameRate : null,
                CodecProfile = NormalizeOptionalText(stream.CodecProfile),
                PixelFormat = NormalizeOptionalText(stream.PixelFormat),
                Rotation = stream.Rotation
            },
            MediaLibraryStreamKind.Audio => new AudioStreamInfo
            {
                Channels = stream.Channels is > 0 ? stream.Channels : null,
                ChannelLayout = NormalizeOptionalText(stream.ChannelLayout),
                SampleRate = stream.SampleRate is > 0 ? stream.SampleRate : null,
                BitDepth = stream.BitDepth is > 0 ? stream.BitDepth : null
            },
            _ => null
        };
        if (dataStream is null)
        {
            return null;
        }

        dataStream.StreamIndex = Math.Max(0, stream.StreamIndex);
        dataStream.Duration = stream.Duration is { } duration && duration > TimeSpan.Zero
            ? duration.TotalSeconds
            : null;
        dataStream.Codec = NormalizeOptionalText(stream.Codec);
        dataStream.Language = NormalizeOptionalText(stream.Language);
        dataStream.BitRate = stream.BitRate is > 0 ? stream.BitRate : null;
        dataStream.IsDefault = stream.IsDefault;
        dataStream.Title = NormalizeOptionalText(stream.Title);
        return dataStream;
    }

    private static MediaLibraryItem ToBusItem(
        BaseItem item,
        UserMediaState? state)
    {
        var path = item.Path ?? string.Empty;
        var extension = string.IsNullOrWhiteSpace(item.Container)
            ? Path.GetExtension(path)
            : Path.GetExtension(path) is { Length: > 0 } pathExtension
                ? pathExtension
                : $".{item.Container.TrimStart('.')}";
        var videoStream = item.MediaStreams.OfType<VideoStreamInfo>().FirstOrDefault();
        var streamDuration = item.MediaStreams
            .Select(stream => stream.Duration)
            .FirstOrDefault(duration => duration is > 0);
        var imageInfo = item is DataImage image ? image.ImageInfo.FirstOrDefault() : null;
        var thumbnailPath = item.Images
            .Where(image => image.ImageType == LinkedImageType.Thumb)
            .OrderBy(image => image.ImageIndex)
            .Select(image => image.Path)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        return new MediaLibraryItem
        {
            Id = item.Id,
            Name = item.Name,
            Path = path,
            Extension = extension,
            ContainerFormat = item.Container,
            FileSize = item.FileSize,
            DateCreated = item.DateCreated,
            MetadataRefreshedAt = item.DateLastRefreshed,
            LastModified = item.LastModified,
            Duration = streamDuration is > 0 ? TimeSpan.FromSeconds(streamDuration.Value) : null,
            Width = videoStream?.Width ?? imageInfo?.Width,
            Height = videoStream?.Height ?? imageInfo?.Height,
            Kind = GetMediaKind(item.MediaType, extension),
            ThumbnailPath = thumbnailPath,
            Artist = (item as Audio)?.ArtistName,
            Album = (item as Audio)?.AlbumTitle,
            Streams = item.MediaStreams
                .OrderBy(stream => stream.StreamIndex)
                .Select(ToBusStream)
                .Where(stream => stream is not null)
                .Cast<NewMediaLibraryStream>()
                .ToList(),
            IsFavorite = state?.IsFavorite ?? false,
            UserRating = state?.UserRating ?? 0,
            LastOpenedAt = state?.LastOpenedAt,
            LastPlayedAt = state?.LastPlayedAt,
            PlaybackPosition = state?.PlaybackPositionTicks is >= 0 and var ticks
                ? TimeSpan.FromTicks(ticks)
                : null,
            PlayCount = state?.PlayCount ?? 0
        };
    }

    private static NewMediaLibraryStream? ToBusStream(MediaStreamInfo stream)
    {
        var kind = stream switch
        {
            VideoStreamInfo => MediaLibraryStreamKind.Video,
            AudioStreamInfo => MediaLibraryStreamKind.Audio,
            _ => MediaLibraryStreamKind.Unknown
        };
        if (kind == MediaLibraryStreamKind.Unknown)
        {
            return null;
        }

        var video = stream as VideoStreamInfo;
        var audio = stream as AudioStreamInfo;
        return new NewMediaLibraryStream
        {
            Kind = kind,
            StreamIndex = stream.StreamIndex,
            Duration = stream.Duration is > 0 ? TimeSpan.FromSeconds(stream.Duration.Value) : null,
            Codec = stream.Codec,
            CodecProfile = video?.CodecProfile,
            Language = stream.Language,
            BitRate = stream.BitRate,
            IsDefault = stream.IsDefault,
            Title = stream.Title,
            Width = video?.Width,
            Height = video?.Height,
            FrameRate = video?.FrameRate,
            PixelFormat = video?.PixelFormat,
            Rotation = video?.Rotation,
            Channels = audio?.Channels,
            ChannelLayout = audio?.ChannelLayout,
            SampleRate = audio?.SampleRate,
            BitDepth = audio?.BitDepth
        };
    }

    private static MediaLibraryItemKind GetMediaKind(MediaType mediaType, string? extension)
    {
        return mediaType switch
        {
            MediaType.Video => MediaLibraryItemKind.Video,
            MediaType.Audio => MediaLibraryItemKind.Audio,
            MediaType.Image => MediaLibraryItemKind.Image,
            _ => GetMediaKind(extension)
        };
    }

    private static MediaLibraryItemKind GetMediaKind(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return MediaLibraryItemKind.Unknown;
        }

        return extension.ToLowerInvariant() switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" => MediaLibraryItemKind.Video,
            ".mp3" or ".flac" or ".wav" or ".aac" or ".m4a" or ".ogg" or ".wma" => MediaLibraryItemKind.Audio,
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" => MediaLibraryItemKind.Image,
            _ => MediaLibraryItemKind.Unknown
        };
    }

    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
