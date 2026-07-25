using Koukei.Bus.Models;
using Koukei.Data.Entities;
using Koukei.Data.Enums;
using Koukei.Data.Services;

namespace Koukei.Bus.Services;

public sealed class PlaylistBus(
    IPlaylistService playlists,
    IMediaLibraryService mediaLibrary,
    IUserMediaStateService userMediaState) : IPlaylistBus
{
    public async Task<IReadOnlyList<PlaylistSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await playlists.ListAsync(cancellationToken);
        return summaries.Select(summary => new PlaylistSummary
            {
                Id = summary.Id,
                Name = summary.Name,
                Description = summary.Description,
                ItemCount = summary.ItemCount,
                ThumbnailPaths = summary.ThumbnailPaths,
                DateCreated = summary.DateCreated,
                DateLastSaved = summary.DateLastSaved
            })
            .ToList();
    }

    public async Task<PlaylistDetail?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var playlist = await playlists.GetAsync(id, cancellationToken);
        if (playlist is null)
        {
            return null;
        }

        var orderedItems = playlist.Items
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.DateAdded)
            .ToArray();
        var paths = orderedItems
            .Select(item => item.Item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        var mediaItems = await mediaLibrary.GetPlaybackItemsByPathsAsync(paths, cancellationToken);
        var mediaById = mediaItems.ToDictionary(item => item.Id);
        var states = await userMediaState.GetManyAsync(
            orderedItems.Select(item => item.ItemId).ToArray(),
            cancellationToken);
        var statesByItemId = states.ToDictionary(state => state.ItemId);

        return new PlaylistDetail
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            DateCreated = playlist.DateCreated,
            DateLastSaved = playlist.DateLastSaved,
            Items = orderedItems.Select(item =>
            {
                mediaById.TryGetValue(item.ItemId, out var media);
                statesByItemId.TryGetValue(item.ItemId, out var state);
                var path = media?.Path ?? item.Item.Path ?? string.Empty;
                return new PlaylistMediaItem
                {
                    PlaylistItemId = item.Id,
                    MediaId = item.ItemId,
                    Title = string.IsNullOrWhiteSpace(media?.Name)
                        ? item.Item.Name
                        : media.Name,
                    Path = path,
                    LinkedFilePath = media?.LinkedFilePath ?? item.Item.LinkedFilePath,
                    Kind = ToBusKind(media?.Kind ?? item.Item.Kind),
                    Duration = media?.DurationSeconds is > 0 and var duration
                        ? TimeSpan.FromSeconds(duration)
                        : null,
                    Artist = media?.Artist,
                    Album = media?.Album,
                    ThumbnailPath = media?.ThumbnailPath,
                    PlaybackPosition = state?.PlaybackPositionTicks is >= 0 and var ticks
                        ? TimeSpan.FromTicks(ticks)
                        : null,
                    SortOrder = item.SortOrder,
                    DateAdded = item.DateAdded,
                    Note = item.Note
                };
            }).ToArray()
        };
    }

    public async Task<PlaylistSummary> CreateAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var playlist = await playlists.CreateAsync(name, description, cancellationToken);
        return ToSummary(playlist);
    }

    public async Task<PlaylistSummary> UpdateAsync(
        Guid id,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var playlist = await playlists.UpdateAsync(id, name, description, cancellationToken);
        return ToSummary(playlist);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return playlists.DeleteAsync(id, cancellationToken);
    }

    public async Task<PlaylistItemsAddResult> AddItemsAsync(
        Guid playlistId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        var result = await playlists.AddItemsAsync(playlistId, itemIds, cancellationToken);
        return new PlaylistItemsAddResult
        {
            AddedCount = result.AddedCount,
            DuplicateCount = result.DuplicateCount,
            MissingCount = result.MissingCount
        };
    }

    public Task<bool> RemoveItemAsync(
        Guid playlistItemId,
        CancellationToken cancellationToken = default) =>
        playlists.RemoveItemAsync(playlistItemId, cancellationToken);

    public Task<bool> MoveItemAsync(
        Guid playlistItemId,
        int newSortOrder,
        CancellationToken cancellationToken = default) =>
        playlists.MoveItemAsync(playlistItemId, newSortOrder, cancellationToken);

    public Task<int> ClearAsync(
        Guid playlistId,
        CancellationToken cancellationToken = default) =>
        playlists.ClearAsync(playlistId, cancellationToken);

    private static PlaylistSummary ToSummary(Playlist playlist)
    {
        return new PlaylistSummary
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            ItemCount = playlist.Items.Count,
            DateCreated = playlist.DateCreated,
            DateLastSaved = playlist.DateLastSaved
        };
    }

    private static MediaLibraryItemKind ToBusKind(BaseItemKind kind)
    {
        return kind switch
        {
            BaseItemKind.Audio or
            BaseItemKind.AudioRecording or
            BaseItemKind.Music or
            BaseItemKind.AudioBook or
            BaseItemKind.RadioEpisode => MediaLibraryItemKind.Audio,
            BaseItemKind.Video or
            BaseItemKind.VideoRecording or
            BaseItemKind.Movie or
            BaseItemKind.TvEpisode or
            BaseItemKind.MusicVideo => MediaLibraryItemKind.Video,
            BaseItemKind.Image or
            BaseItemKind.Photo or
            BaseItemKind.Illustration => MediaLibraryItemKind.Image,
            _ => MediaLibraryItemKind.Unknown
        };
    }
}
