using Koukei.Bus.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Koukei.UI.Services;

internal sealed record PlaybackQueueSelection(
    Guid Id,
    string Title,
    string FilePath,
    MediaLibraryItemKind Kind,
    string? Artist,
    string? Album,
    string? ThumbnailPath,
    TimeSpan? PlaybackPosition,
    string? LinkedFilePath);

internal sealed record PlaybackQueueEntry(
    Guid? MediaId,
    string Title,
    string FilePath,
    MediaLibraryItemKind Kind,
    string? Artist = null,
    string? Album = null,
    string? ThumbnailPath = null,
    TimeSpan? PlaybackPosition = null,
    string? LinkedFilePath = null);

internal sealed record PlaybackQueueMetadataUpdate(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    string? ThumbnailPath);

internal sealed record PlaybackQueueContext(
    MediaLibraryItemKind Kind,
    IReadOnlyList<PlaybackQueueEntry> Items,
    int StartIndex);

internal sealed class PlaybackQueueBuilder
{
    public Task<PlaybackQueueContext> BuildDisplayedQueueAsync(
        IReadOnlyList<PlaybackQueueSelection> displayedItems,
        Guid selectedId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayedItems);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = displayedItems.ToArray();

        return Task.Run(
            () => BuildDisplayedQueue(snapshot, selectedId, cancellationToken),
            cancellationToken);
    }

    private static PlaybackQueueContext BuildDisplayedQueue(
        IReadOnlyList<PlaybackQueueSelection> displayedItems,
        Guid selectedId,
        CancellationToken cancellationToken)
    {
        var selectedItem = displayedItems.FirstOrDefault(item => item.Id == selectedId)
            ?? throw new InvalidOperationException("The selected media item is not in the displayed library view.");
        if (string.IsNullOrWhiteSpace(selectedItem.FilePath) || !File.Exists(selectedItem.FilePath))
        {
            throw new FileNotFoundException("The selected media file does not exist.", selectedItem.FilePath);
        }

        var entries = new List<PlaybackQueueEntry>(displayedItems.Count);
        var queuedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startIndex = -1;
        foreach (var item in displayedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Kind != selectedItem.Kind ||
                string.IsNullOrWhiteSpace(item.FilePath) ||
                !File.Exists(item.FilePath) ||
                !queuedPaths.Add(item.FilePath))
            {
                continue;
            }

            if (item.Id == selectedId)
            {
                startIndex = entries.Count;
            }

            entries.Add(new PlaybackQueueEntry(
                item.Id,
                ResolveTitle(item.Title, item.FilePath),
                item.FilePath,
                item.Kind,
                item.Artist,
                item.Album,
                item.ThumbnailPath,
                item.PlaybackPosition,
                item.LinkedFilePath));
        }

        if (startIndex < 0)
        {
            throw new InvalidOperationException("The selected media file was not included in the playback queue.");
        }

        return new PlaybackQueueContext(
            selectedItem.Kind,
            entries,
            startIndex);
    }

    private static string ResolveTitle(string? title, string filePath)
    {
        return string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : title.Trim();
    }

}
