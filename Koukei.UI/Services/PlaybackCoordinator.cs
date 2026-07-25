using Koukei.Audio;
using Koukei.Bus.Models;
using Koukei.Bus.Services;
using Koukei.Video;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Koukei.UI.Services;

public enum AudioRepeatMode
{
    Off,
    All,
    One
}

internal enum PlayerActivationIntent
{
    UserInitiated,
    BackgroundContinuation
}

internal sealed class PlaybackCoordinator
{
    private const string AudioShuffleSettingKey = "AudioPlayback_IsShuffleEnabled";
    private const string AudioRepeatModeSettingKey = "AudioPlayback_RepeatMode";
    private const int MaximumShuffleHistoryLength = 512;
    private static readonly TimeSpan PlaybackPositionSaveInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumResumePosition = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CompletedMediaMargin = TimeSpan.FromSeconds(10);

    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IVideoPlaybackService _videoPlaybackService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _queueLock = new();
    private readonly object _progressLock = new();
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private readonly SemaphoreSlim _progressPersistenceGate = new(1, 1);
    private readonly HashSet<int> _shuffleVisitedIndices = [];
    private readonly List<int> _shufflePreviousHistory = [];
    private readonly List<int> _shuffleForwardHistory = [];

    private List<PlaybackQueueEntry> _queue = [];
    private int _currentIndex = -1;
    private AudioPlaybackRequest? _activeAudioPlaybackRequest;
    private string? _activeVideoPath;
    private bool _isShuffleEnabled;
    private AudioRepeatMode _repeatMode;
    private ActivePlaybackProgress? _activeProgress;
    private PendingPlaybackResume? _pendingResume;
    private DateTimeOffset _lastPositionPersistedAt = DateTimeOffset.MinValue;
    private int _positionSaveQueued;
    private int _pendingResumeApplyQueued;

    public event EventHandler? PlaybackQueueChanged;

    public event EventHandler? AudioPlaybackOptionsChanged;

    public bool IsShuffleEnabled
    {
        get
        {
            lock (_queueLock)
            {
                return _isShuffleEnabled;
            }
        }
        set => SetPlaybackOptions(isShuffleEnabled: value, repeatMode: null);
    }

    public AudioRepeatMode RepeatMode
    {
        get
        {
            lock (_queueLock)
            {
                return _repeatMode;
            }
        }
        set
        {
            if (value is not AudioRepeatMode.Off and
                not AudioRepeatMode.All and
                not AudioRepeatMode.One)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetPlaybackOptions(isShuffleEnabled: null, repeatMode: value);
        }
    }

    public bool CanPlayPrevious
    {
        get
        {
            lock (_queueLock)
            {
                return CanNavigateNoLock(PlaybackNavigationDirection.Previous);
            }
        }
    }

    public bool CanPlayNext
    {
        get
        {
            lock (_queueLock)
            {
                return CanNavigateNoLock(PlaybackNavigationDirection.Next);
            }
        }
    }

    public IReadOnlyList<PlaybackQueueItem> PlaybackQueue
    {
        get
        {
            lock (_queueLock)
            {
                return _queue
                    .Select((item, index) => new PlaybackQueueItem(
                        item.MediaId,
                        item.Title,
                        item.FilePath,
                        item.Kind,
                        index == _currentIndex,
                        item.Artist,
                        item.Album,
                        item.ThumbnailPath,
                        item.PlaybackPosition,
                        item.LinkedFilePath))
                    .ToList();
            }
        }
    }

    public void UpdateQueueItemMetadata(
        IReadOnlyList<PlaybackQueueMetadataUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return;
        }

        var updatesByPath = updates
            .Where(update =>
                !string.IsNullOrWhiteSpace(update.FilePath) &&
                !string.IsNullOrWhiteSpace(update.Title))
            .GroupBy(update => update.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

        var changed = false;
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var entry = _queue[index];
                if (updatesByPath.TryGetValue(entry.FilePath, out var update))
                {
                    var updatedEntry = entry with
                    {
                        Title = update.Title.Trim(),
                        Artist = update.Artist,
                        Album = update.Album,
                        ThumbnailPath = update.ThumbnailPath
                    };
                    if (updatedEntry != entry)
                    {
                        _queue[index] = updatedEntry;
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            RaisePlaybackQueueChanged();
        }
    }

    public void SynchronizeQueueItems(
        IReadOnlyList<PlaybackQueueEntry> libraryItems)
    {
        ArgumentNullException.ThrowIfNull(libraryItems);
        if (libraryItems.Count == 0)
        {
            return;
        }

        var itemsByPath = libraryItems
            .Where(item =>
                item.MediaId is not null &&
                !string.IsNullOrWhiteSpace(item.FilePath))
            .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
        if (itemsByPath.Count == 0)
        {
            return;
        }

        var changed = false;
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var queuedItem = _queue[index];
                if (!itemsByPath.TryGetValue(queuedItem.FilePath, out var libraryItem) ||
                    queuedItem.Kind != libraryItem.Kind)
                {
                    continue;
                }

                var replacement = queuedItem with
                {
                    MediaId = libraryItem.MediaId,
                    Title = libraryItem.Title,
                    Artist = libraryItem.Artist,
                    Album = libraryItem.Album,
                    ThumbnailPath = libraryItem.ThumbnailPath,
                    PlaybackPosition = libraryItem.PlaybackPosition,
                    LinkedFilePath = libraryItem.LinkedFilePath
                };
                if (replacement != queuedItem)
                {
                    _queue[index] = replacement;
                    changed = true;
                }
            }
        }

        PlaybackQueueEntry? newlyBoundActiveItem = null;
        var newlyBoundActivePosition = 0d;
        lock (_progressLock)
        {
            if (_activeProgress is { } active &&
                itemsByPath.TryGetValue(active.FilePath, out var activeLibraryItem) &&
                active.Kind == activeLibraryItem.Kind)
            {
                if (active.MediaId is null && activeLibraryItem.MediaId is not null)
                {
                    newlyBoundActiveItem = activeLibraryItem;
                    newlyBoundActivePosition = active.Position;
                }
                _activeProgress = active with { MediaId = activeLibraryItem.MediaId };
            }

            if (_pendingResume is { } pending &&
                itemsByPath.TryGetValue(pending.FilePath, out var pendingLibraryItem) &&
                pending.Kind == pendingLibraryItem.Kind)
            {
                _pendingResume = pending with { MediaId = pendingLibraryItem.MediaId };
            }
        }

        if (changed)
        {
            RaisePlaybackQueueChanged();
        }
        if (newlyBoundActiveItem is not null)
        {
            _ = RecordPlaybackStartedAsync(
                newlyBoundActiveItem,
                newlyBoundActivePosition);
        }
    }

    public void DetachQueueItemFromLibrary(Guid mediaId)
    {
        var changed = false;
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var queuedItem = _queue[index];
                if (queuedItem.MediaId != mediaId)
                {
                    continue;
                }

                _queue[index] = queuedItem with
                {
                    MediaId = null,
                    PlaybackPosition = null
                };
                changed = true;
            }
        }

        lock (_progressLock)
        {
            if (_activeProgress is { MediaId: var activeMediaId } active &&
                activeMediaId == mediaId)
            {
                _activeProgress = active with { MediaId = null };
            }
            if (_pendingResume is { MediaId: var pendingMediaId } pending &&
                pendingMediaId == mediaId)
            {
                _pendingResume = pending with { MediaId = null };
            }
        }

        if (changed)
        {
            RaisePlaybackQueueChanged();
        }
    }

    public void ClearQueueThumbnailsUnder(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var changed = false;
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var queuedItem = _queue[index];
                if (!IsPathUnderRoot(queuedItem.ThumbnailPath, rootPath))
                {
                    continue;
                }

                _queue[index] = queuedItem with { ThumbnailPath = null };
                changed = true;
            }
        }

        if (changed)
        {
            RaisePlaybackQueueChanged();
        }
    }

    public void UpdateQueueItemThumbnail(
        Guid? mediaId,
        string filePath,
        string? thumbnailPath)
    {
        var changed = false;
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var entry = _queue[index];
                if ((mediaId is not null && entry.MediaId == mediaId) ||
                    IsSameMediaPath(entry.FilePath, filePath))
                {
                    var updatedEntry = entry with { ThumbnailPath = thumbnailPath };
                    if (updatedEntry != entry)
                    {
                        _queue[index] = updatedEntry;
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            RaisePlaybackQueueChanged();
        }
    }

    public void UpdateQueueItemLinkedFile(Guid mediaId, string? linkedFilePath)
    {
        var changed = false;
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var entry = _queue[index];
                if (entry.MediaId != mediaId)
                {
                    continue;
                }

                var updatedEntry = entry with { LinkedFilePath = linkedFilePath };
                if (updatedEntry != entry)
                {
                    _queue[index] = updatedEntry;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            RaisePlaybackQueueChanged();
        }
    }

    public PlaybackCoordinator(
        IAudioPlaybackService audioPlaybackService,
        IVideoPlaybackService videoPlaybackService,
        IServiceScopeFactory scopeFactory)
    {
        _audioPlaybackService = audioPlaybackService;
        _videoPlaybackService = videoPlaybackService;
        _scopeFactory = scopeFactory;
        (_isShuffleEnabled, _repeatMode) = LoadPlaybackOptions();
        _audioPlaybackService.StateChanged += AudioPlaybackService_StateChanged;
        _audioPlaybackService.PlaybackEnded += AudioPlaybackService_PlaybackEnded;
        _videoPlaybackService.PlaybackStateChanged += VideoPlaybackService_PlaybackStateChanged;
        _videoPlaybackService.PlaybackEnded += VideoPlaybackService_PlaybackEnded;
        _videoPlaybackService.PlaybackClosed += VideoPlaybackService_PlaybackClosed;
    }

    public Task PlayItemAsync(
        PlaybackQueueEntry item,
        CancellationToken cancellationToken = default) =>
        PlayOrAppendAsync(item, cancellationToken);

    public Task PlayPlaylistAsync(
        IReadOnlyList<PlaybackQueueEntry> items,
        int startIndex = 0,
        CancellationToken cancellationToken = default) =>
        ReplaceQueueAndPlayAsync(items, startIndex, cancellationToken);

    public Task EnqueueItemAsync(
        PlaybackQueueEntry item,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(item, cancellationToken);

    public Task EnqueuePlaylistAsync(
        IReadOnlyList<PlaybackQueueEntry> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_queueLock)
        {
            _queue.AddRange(items);
            if (_currentIndex < 0)
            {
                ResetShuffleStateNoLock();
            }
        }

        RaisePlaybackQueueChanged();
        return Task.CompletedTask;
    }

    public Task PlayContextAsync(
        PlaybackQueueContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items.Count == 0 ||
            context.StartIndex < 0 ||
            context.StartIndex >= context.Items.Count)
        {
            throw new ArgumentException("The playback queue context is invalid.", nameof(context));
        }
        if (context.Kind is not MediaLibraryItemKind.Audio and not MediaLibraryItemKind.Video)
        {
            throw new ArgumentException("Only audio and video queues can be played.", nameof(context));
        }

        return ReplaceQueueAndPlayAsync(
            context.Items,
            context.StartIndex,
            cancellationToken);
    }

    public async Task PlayQueueItemAsync(
        int index,
        CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            PlaybackQueueEntry? item;
            lock (_queueLock)
            {
                if (index < 0 || index >= _queue.Count)
                {
                    return;
                }

                _currentIndex = index;
                item = _queue[index];
                ResetShuffleStateNoLock();
                InvalidateActivePlaybackNoLock();
            }

            RaisePlaybackQueueChanged();
            await ActivateItemAsync(
                item,
                PlayerActivationIntent.UserInitiated,
                cancellationToken);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public Task PlayPreviousAsync(CancellationToken cancellationToken = default)
    {
        return NavigateAsync(PlaybackNavigationDirection.Previous, cancellationToken);
    }

    public Task PlayNextAsync(CancellationToken cancellationToken = default)
    {
        return NavigateAsync(PlaybackNavigationDirection.Next, cancellationToken);
    }

    public async Task ReplayCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            await ReplayCurrentCoreAsync(
                PlayerActivationIntent.UserInitiated,
                cancellationToken);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public Task MoveQueueItemAsync(
        int index,
        int targetIndex,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_queueLock)
        {
            if (index < 0 ||
                index >= _queue.Count ||
                targetIndex < 0 ||
                targetIndex >= _queue.Count ||
                index == targetIndex)
            {
                return Task.CompletedTask;
            }

            var item = _queue[index];
            _queue.RemoveAt(index);
            _queue.Insert(targetIndex, item);

            if (_currentIndex == index)
            {
                _currentIndex = targetIndex;
            }
            else if (index < _currentIndex && targetIndex >= _currentIndex)
            {
                _currentIndex--;
            }
            else if (index > _currentIndex && targetIndex <= _currentIndex)
            {
                _currentIndex++;
            }

            ResetShuffleStateNoLock();
        }

        RaisePlaybackQueueChanged();
        return Task.CompletedTask;
    }

    public async Task RemoveQueueItemAsync(
        int index,
        CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            PlaybackQueueEntry? replacement = null;
            var removedCurrent = false;
            var queueIsEmpty = false;
            lock (_queueLock)
            {
                if (index < 0 || index >= _queue.Count)
                {
                    return;
                }

                removedCurrent = index == _currentIndex;
                _queue.RemoveAt(index);
                if (_queue.Count == 0)
                {
                    _currentIndex = -1;
                    queueIsEmpty = true;
                }
                else if (index < _currentIndex)
                {
                    _currentIndex--;
                }
                else if (removedCurrent)
                {
                    _currentIndex = Math.Min(index, _queue.Count - 1);
                    replacement = _queue[_currentIndex];
                }

                if (removedCurrent)
                {
                    InvalidateActivePlaybackNoLock();
                }
                ResetShuffleStateNoLock();
            }

            RaisePlaybackQueueChanged();
            if (queueIsEmpty)
            {
                await StopEnginesAsync();
            }
            else if (replacement is not null)
            {
                await ActivateItemAsync(
                    replacement,
                    PlayerActivationIntent.UserInitiated,
                    cancellationToken);
            }
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public async Task ClearQueueAsync(CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            lock (_queueLock)
            {
                _queue.Clear();
                _currentIndex = -1;
                ResetShuffleStateNoLock();
                InvalidateActivePlaybackNoLock();
            }

            RaisePlaybackQueueChanged();
            await StopEnginesAsync();
            await PlayerWindow.ClearQueueAsync();
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            lock (_queueLock)
            {
                _currentIndex = -1;
                InvalidateActivePlaybackNoLock();
                ResetShuffleStateNoLock();
            }
            RaisePlaybackQueueChanged();
            await StopEnginesAsync();
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public async Task CloseAudioPlaybackAsync(CancellationToken cancellationToken = default)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            var currentWasAudio = false;
            lock (_queueLock)
            {
                currentWasAudio = _currentIndex >= 0 &&
                    _currentIndex < _queue.Count &&
                    _queue[_currentIndex].Kind == MediaLibraryItemKind.Audio;
                if (currentWasAudio)
                {
                    _currentIndex = -1;
                    ResetShuffleStateNoLock();
                }
                _activeAudioPlaybackRequest = null;
            }

            if (currentWasAudio)
            {
                RaisePlaybackQueueChanged();
                await PersistAndClearActivePlaybackPositionAsync();
            }
            await _audioPlaybackService.CloseAsync(CancellationToken.None);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private async Task PlayOrAppendAsync(
        PlaybackQueueEntry item,
        CancellationToken cancellationToken)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            lock (_queueLock)
            {
                var existingIndex = _queue.FindIndex(candidate =>
                    candidate.Kind == item.Kind &&
                    string.Equals(candidate.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
                if (existingIndex < 0)
                {
                    _queue.Add(item);
                    existingIndex = _queue.Count - 1;
                }
                else
                {
                    _queue[existingIndex] = item;
                }

                _currentIndex = existingIndex;
                ResetShuffleStateNoLock();
                InvalidateActivePlaybackNoLock();
            }

            RaisePlaybackQueueChanged();
            await ActivateItemAsync(
                item,
                PlayerActivationIntent.UserInitiated,
                cancellationToken);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private Task EnqueueAsync(
        PlaybackQueueEntry item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_queueLock)
        {
            _queue.Add(item);
            if (_currentIndex < 0)
            {
                ResetShuffleStateNoLock();
            }
        }
        RaisePlaybackQueueChanged();
        return Task.CompletedTask;
    }

    private async Task ReplaceQueueAndPlayAsync(
        IReadOnlyList<PlaybackQueueEntry> items,
        int startIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }
        if (startIndex < 0 || startIndex >= items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            lock (_queueLock)
            {
                _queue = items.ToList();
                _currentIndex = startIndex;
                ResetShuffleStateNoLock();
                InvalidateActivePlaybackNoLock();
            }

            RaisePlaybackQueueChanged();
            await ActivateItemAsync(
                items[startIndex],
                PlayerActivationIntent.UserInitiated,
                cancellationToken);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private async Task NavigateAsync(
        PlaybackNavigationDirection direction,
        CancellationToken cancellationToken)
    {
        await _playbackGate.WaitAsync(cancellationToken);
        try
        {
            await NavigateCoreAsync(
                direction,
                PlayerActivationIntent.UserInitiated,
                cancellationToken);
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private async Task<bool> NavigateCoreAsync(
        PlaybackNavigationDirection direction,
        PlayerActivationIntent activationIntent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaybackQueueEntry? item;
        var currentIndexChanged = false;
        lock (_queueLock)
        {
            var previousIndex = _currentIndex;
            item = SelectNavigationTargetNoLock(direction);
            currentIndexChanged = previousIndex != _currentIndex;
            if (item is not null)
            {
                InvalidateActivePlaybackNoLock();
            }
        }

        if (item is null)
        {
            return false;
        }
        if (currentIndexChanged)
        {
            RaisePlaybackQueueChanged();
        }
        await ActivateItemAsync(item, activationIntent, cancellationToken);
        return true;
    }

    private async Task<bool> ReplayCurrentCoreAsync(
        PlayerActivationIntent activationIntent,
        CancellationToken cancellationToken)
    {
        PlaybackQueueEntry? item = null;
        lock (_queueLock)
        {
            if (_currentIndex >= 0 && _currentIndex < _queue.Count)
            {
                item = _queue[_currentIndex];
                InvalidateActivePlaybackNoLock();
            }
        }

        if (item is null)
        {
            return false;
        }
        await ActivateItemAsync(item, activationIntent, cancellationToken);
        return true;
    }

    private async Task ActivateItemAsync(
        PlaybackQueueEntry item,
        PlayerActivationIntent activationIntent,
        CancellationToken cancellationToken)
    {
        await PersistAndClearActivePlaybackPositionAsync();
        item = await HydrateQueueEntryAsync(item, cancellationToken);
        var resumePosition = item.PlaybackPosition;
        if (item.Kind == MediaLibraryItemKind.Audio)
        {
            await PlayerWindow.CloseCurrentAsync();
            cancellationToken.ThrowIfCancellationRequested();
            await PlayAudioEntryAsync(item);
            var state = await _audioPlaybackService.GetPlaybackStateAsync(cancellationToken);
            var resumedPosition = await ApplyResumePositionAsync(
                item,
                resumePosition,
                state.Duration,
                state.IsSeekable,
                cancellationToken);
            TrackActivatedItem(
                item,
                resumePosition,
                resumedPosition,
                state.Duration,
                state.IsSeekable);
            await RecordPlaybackStartedAsync(item, resumedPosition);
            return;
        }

        lock (_queueLock)
        {
            _activeAudioPlaybackRequest = null;
            _activeVideoPath = item.FilePath;
        }
        await _audioPlaybackService.CloseAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        await PlayerWindow.ShowPlaylistAsync(
            [(item.Title, item.FilePath)],
            startIndex: 0,
            activationIntent: activationIntent,
            deferForegroundActivation:
                activationIntent == PlayerActivationIntent.UserInitiated,
            cancellationToken: cancellationToken);
        try
        {
            await TryLoadLinkedSubtitleAsync(item, cancellationToken);
            var videoState = await _videoPlaybackService.GetPlaybackStateAsync(cancellationToken);
            var resumedVideoPosition = await ApplyResumePositionAsync(
                item,
                resumePosition,
                videoState.Duration,
                videoState.IsSeekable,
                cancellationToken);
            TrackActivatedItem(
                item,
                resumePosition,
                resumedVideoPosition,
                videoState.Duration,
                videoState.IsSeekable);
            await RecordPlaybackStartedAsync(item, resumedVideoPosition);
        }
        finally
        {
            if (activationIntent == PlayerActivationIntent.UserInitiated)
            {
                try
                {
                    await PlayerWindow.RequestCurrentForegroundActivationAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Failed to request player foreground activation: {ex.Message}");
                }
            }
        }
    }

    private async Task TryLoadLinkedSubtitleAsync(
        PlaybackQueueEntry item,
        CancellationToken cancellationToken)
    {
        var automaticSubtitlePath = VideoSubtitleSidecar.FindMatch(item.FilePath);
        var linkedSubtitlePath = !string.IsNullOrWhiteSpace(item.LinkedFilePath) &&
            File.Exists(item.LinkedFilePath)
                ? Path.GetFullPath(item.LinkedFilePath)
                : null;
        if (linkedSubtitlePath is null)
        {
            if (automaticSubtitlePath is not null && item.MediaId is { } mediaId)
            {
                await PersistDiscoveredLinkedFileAsync(
                    mediaId,
                    automaticSubtitlePath,
                    cancellationToken);
            }
            return;
        }

        if (automaticSubtitlePath is not null &&
            string.Equals(
                linkedSubtitlePath,
                automaticSubtitlePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _videoPlaybackService.AddSubtitleTrackAsync(
                linkedSubtitlePath,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Failed to load linked subtitle '{linkedSubtitlePath}': {ex.Message}");
        }
    }

    private async Task PersistDiscoveredLinkedFileAsync(
        Guid mediaId,
        string linkedFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.SetLinkedFilePathAsync(mediaId, linkedFilePath, cancellationToken);
            UpdateQueueItemLinkedFile(mediaId, linkedFilePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Failed to persist linked subtitle '{linkedFilePath}': {ex.Message}");
        }
    }

    private async Task PlayAudioEntryAsync(PlaybackQueueEntry item)
    {
        var request = new AudioPlaybackRequest(
            item.FilePath,
            item.Title,
            Guid.NewGuid());
        lock (_queueLock)
        {
            _activeAudioPlaybackRequest = request;
            _activeVideoPath = null;
        }

        try
        {
            await _audioPlaybackService.PlayAsync(request, CancellationToken.None);
        }
        catch
        {
            lock (_queueLock)
            {
                if (ReferenceEquals(_activeAudioPlaybackRequest, request))
                {
                    _activeAudioPlaybackRequest = null;
                }
            }
            try
            {
                await _audioPlaybackService.CloseAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original playback failure.
            }
            throw;
        }
    }

    private async Task StopEnginesAsync()
    {
        await PersistAndClearActivePlaybackPositionAsync();
        await _audioPlaybackService.CloseAsync(CancellationToken.None);
        await PlayerWindow.CloseCurrentAsync();
    }

    private async Task<PlaybackQueueEntry> HydrateQueueEntryAsync(
        PlaybackQueueEntry item,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            var persistedItems = await library.GetPlaybackItemsByPathsAsync(
                [item.FilePath],
                cancellationToken);
            var persistedItem = persistedItems.FirstOrDefault(candidate =>
                candidate.Kind == item.Kind &&
                IsSameMediaPath(candidate.Path, item.FilePath));
            if (persistedItem is null)
            {
                return item;
            }

            var hydratedItem = item with
            {
                MediaId = persistedItem.Id,
                Title = string.IsNullOrWhiteSpace(persistedItem.Title)
                    ? item.Title
                    : persistedItem.Title.Trim(),
                Artist = persistedItem.Artist,
                Album = persistedItem.Album,
                ThumbnailPath = persistedItem.ThumbnailPath,
                PlaybackPosition = persistedItem.PlaybackPosition,
                LinkedFilePath = persistedItem.LinkedFilePath
            };
            ReplaceQueueEntryFromLibrary(item, hydratedItem);
            return hydratedItem;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to hydrate playback data for '{item.FilePath}': {ex.Message}");
            return item;
        }
    }

    private void ReplaceQueueEntryFromLibrary(
        PlaybackQueueEntry originalItem,
        PlaybackQueueEntry hydratedItem)
    {
        var changed = false;
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var queuedItem = _queue[index];
                if (queuedItem.Kind != originalItem.Kind ||
                    !IsSameMediaPath(queuedItem.FilePath, originalItem.FilePath))
                {
                    continue;
                }

                var replacement = queuedItem with
                {
                    MediaId = hydratedItem.MediaId,
                    Title = hydratedItem.Title,
                    Artist = hydratedItem.Artist,
                    Album = hydratedItem.Album,
                    ThumbnailPath = hydratedItem.ThumbnailPath,
                    PlaybackPosition = hydratedItem.PlaybackPosition,
                    LinkedFilePath = hydratedItem.LinkedFilePath
                };
                if (replacement != queuedItem)
                {
                    _queue[index] = replacement;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            RaisePlaybackQueueChanged();
        }
    }

    private async Task<double> ApplyResumePositionAsync(
        PlaybackQueueEntry item,
        TimeSpan? resumePosition,
        double duration,
        bool isSeekable,
        CancellationToken cancellationToken)
    {
        if (resumePosition is not { } position ||
            position < MinimumResumePosition)
        {
            return 0;
        }

        var seconds = Math.Max(0, position.TotalSeconds);
        if (duration > 0 && seconds >= Math.Max(0, duration - CompletedMediaMargin.TotalSeconds))
        {
            await ClearStoredPlaybackPositionAsync(item.MediaId, cancellationToken);
            return 0;
        }

        if (!isSeekable)
        {
            return 0;
        }

        try
        {
            if (item.Kind == MediaLibraryItemKind.Audio)
            {
                await _audioPlaybackService.SeekAbsoluteAsync(seconds, cancellationToken);
            }
            else
            {
                await _videoPlaybackService.SeekAbsoluteAsync(seconds, cancellationToken);
            }
            return seconds;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to resume '{item.FilePath}' at {seconds:0.###} seconds: {ex.Message}");
            return 0;
        }
    }

    private void TrackActivatedItem(
        PlaybackQueueEntry item,
        TimeSpan? requestedResumePosition,
        double appliedPosition,
        double duration,
        bool isSeekable)
    {
        PendingPlaybackResume? pendingResume = null;
        if (requestedResumePosition is { } requestedPosition &&
            requestedPosition >= MinimumResumePosition &&
            appliedPosition < MinimumResumePosition.TotalSeconds &&
            (duration <= 0 ||
             requestedPosition.TotalSeconds <
             Math.Max(0, duration - CompletedMediaMargin.TotalSeconds)))
        {
            pendingResume = new PendingPlaybackResume(
                item.MediaId,
                item.FilePath,
                item.Kind,
                requestedPosition.TotalSeconds);
        }

        lock (_progressLock)
        {
            _activeProgress = new ActivePlaybackProgress(
                item.MediaId,
                item.FilePath,
                item.Kind,
                Math.Max(
                    0,
                    pendingResume?.Position ?? appliedPosition),
                Math.Max(0, duration));
            _pendingResume = pendingResume;
            _lastPositionPersistedAt = DateTimeOffset.UtcNow;
        }

        QueuePendingResumeIfReady(item.Kind, duration, isSeekable);
    }

    private async Task RecordPlaybackStartedAsync(
        PlaybackQueueEntry item,
        double resumedPosition)
    {
        if (item.MediaId is not { } mediaId)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.RecordPlayedAsync(
                mediaId,
                resumedPosition >= MinimumResumePosition.TotalSeconds
                    ? TimeSpan.FromSeconds(resumedPosition)
                    : null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to record playback for '{item.FilePath}': {ex.Message}");
        }
    }

    private void UpdateActivePlaybackProgress(
        MediaLibraryItemKind kind,
        double position,
        double duration,
        bool isSeekable,
        bool persistImmediately)
    {
        if (!double.IsFinite(position) || !double.IsFinite(duration))
        {
            return;
        }

        var shouldPersist = persistImmediately;
        lock (_progressLock)
        {
            if (_activeProgress is not { } active || active.Kind != kind)
            {
                return;
            }

            var hasPendingResume = _pendingResume is { } pending &&
                pending.Kind == kind &&
                IsSameMediaPath(pending.FilePath, active.FilePath);
            _activeProgress = active with
            {
                Position = hasPendingResume
                    ? active.Position
                    : Math.Max(0, position),
                Duration = Math.Max(0, duration)
            };
            shouldPersist |= DateTimeOffset.UtcNow - _lastPositionPersistedAt >=
                PlaybackPositionSaveInterval;
        }

        QueuePendingResumeIfReady(kind, duration, isSeekable);

        if (shouldPersist &&
            Interlocked.CompareExchange(ref _positionSaveQueued, 1, 0) == 0)
        {
            _ = PersistActivePlaybackPositionAsync();
        }
    }

    private void QueuePendingResumeIfReady(
        MediaLibraryItemKind kind,
        double duration,
        bool isSeekable)
    {
        if (!isSeekable)
        {
            return;
        }

        PendingPlaybackResume? pendingResume;
        lock (_progressLock)
        {
            pendingResume = _pendingResume;
            if (pendingResume is null ||
                pendingResume.Kind != kind ||
                _activeProgress is not { } active ||
                active.Kind != kind ||
                !IsSameMediaPath(active.FilePath, pendingResume.FilePath))
            {
                return;
            }
        }

        if (Interlocked.CompareExchange(ref _pendingResumeApplyQueued, 1, 0) == 0)
        {
            _ = ApplyPendingResumeAsync(pendingResume, duration);
        }
    }

    private async Task ApplyPendingResumeAsync(
        PendingPlaybackResume pendingResume,
        double observedDuration)
    {
        var acquiredPlaybackGate = false;
        try
        {
            await _playbackGate.WaitAsync();
            acquiredPlaybackGate = true;

            double duration;
            lock (_progressLock)
            {
                if (_pendingResume != pendingResume ||
                    _activeProgress is not { } active ||
                    active.Kind != pendingResume.Kind ||
                    !IsSameMediaPath(active.FilePath, pendingResume.FilePath))
                {
                    return;
                }

                duration = active.Duration > 0
                    ? active.Duration
                    : Math.Max(0, observedDuration);
            }

            if (duration > 0 &&
                pendingResume.Position >=
                Math.Max(0, duration - CompletedMediaMargin.TotalSeconds))
            {
                lock (_progressLock)
                {
                    if (_pendingResume == pendingResume &&
                        _activeProgress is { } active)
                    {
                        _pendingResume = null;
                        _activeProgress = active with { Position = 0, Duration = duration };
                    }
                }
                await ClearStoredPlaybackPositionAsync(pendingResume.MediaId);
                return;
            }

            if (pendingResume.Kind == MediaLibraryItemKind.Audio)
            {
                await _audioPlaybackService.SeekAbsoluteAsync(
                    pendingResume.Position,
                    CancellationToken.None);
            }
            else
            {
                await _videoPlaybackService.SeekAbsoluteAsync(
                    pendingResume.Position,
                    CancellationToken.None);
            }

            lock (_progressLock)
            {
                if (_pendingResume == pendingResume &&
                    _activeProgress is { } active)
                {
                    _pendingResume = null;
                    _activeProgress = active with
                    {
                        Position = pendingResume.Position,
                        Duration = duration
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Failed to apply deferred resume for '{pendingResume.FilePath}': {ex.Message}");
        }
        finally
        {
            if (acquiredPlaybackGate)
            {
                _playbackGate.Release();
            }
            Interlocked.Exchange(ref _pendingResumeApplyQueued, 0);
        }
    }

    private async Task PersistActivePlaybackPositionAsync()
    {
        try
        {
            ActivePlaybackProgress? snapshot;
            lock (_progressLock)
            {
                snapshot = _activeProgress;
            }
            await PersistPlaybackPositionAsync(snapshot);
        }
        finally
        {
            Interlocked.Exchange(ref _positionSaveQueued, 0);
        }
    }

    private async Task PersistAndClearActivePlaybackPositionAsync()
    {
        ActivePlaybackProgress? snapshot;
        lock (_progressLock)
        {
            snapshot = _activeProgress;
            _activeProgress = null;
            _pendingResume = null;
        }
        await PersistPlaybackPositionAsync(snapshot);
    }

    private async Task PersistPlaybackPositionAsync(ActivePlaybackProgress? snapshot)
    {
        if (snapshot?.MediaId is not { } mediaId)
        {
            return;
        }

        await _progressPersistenceGate.WaitAsync();
        try
        {
            var position = GetPersistedPosition(snapshot);
            using var scope = _scopeFactory.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.SetPlaybackPositionAsync(mediaId, position);
            UpdateQueuePlaybackPosition(mediaId, snapshot.FilePath, position);
            lock (_progressLock)
            {
                _lastPositionPersistedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save playback position for '{snapshot.FilePath}': {ex.Message}");
        }
        finally
        {
            _progressPersistenceGate.Release();
        }
    }

    private static TimeSpan? GetPersistedPosition(ActivePlaybackProgress snapshot)
    {
        if (snapshot.Position < MinimumResumePosition.TotalSeconds)
        {
            return null;
        }

        if (snapshot.Duration > 0 &&
            snapshot.Position >= Math.Max(
                0,
                snapshot.Duration - CompletedMediaMargin.TotalSeconds))
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Max(0, snapshot.Position));
    }

    private async Task ClearStoredPlaybackPositionAsync(
        Guid? mediaId,
        CancellationToken cancellationToken = default)
    {
        if (mediaId is not { } id)
        {
            return;
        }

        var acquiredPersistenceGate = false;
        try
        {
            await _progressPersistenceGate.WaitAsync(cancellationToken);
            acquiredPersistenceGate = true;
            using var scope = _scopeFactory.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.SetPlaybackPositionAsync(
                id,
                position: null,
                cancellationToken: cancellationToken);
            UpdateQueuePlaybackPosition(id, filePath: null, position: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clear playback position for '{id}': {ex.Message}");
        }
        finally
        {
            if (acquiredPersistenceGate)
            {
                _progressPersistenceGate.Release();
            }
        }
    }

    private void UpdateQueuePlaybackPosition(
        Guid mediaId,
        string? filePath,
        TimeSpan? position)
    {
        lock (_queueLock)
        {
            for (var index = 0; index < _queue.Count; index++)
            {
                var entry = _queue[index];
                if (entry.MediaId == mediaId ||
                    (!string.IsNullOrWhiteSpace(filePath) &&
                     IsSameMediaPath(entry.FilePath, filePath)))
                {
                    _queue[index] = entry with { PlaybackPosition = position };
                }
            }
        }
    }

    private void AudioPlaybackService_StateChanged(
        object? sender,
        AudioPlaybackStateChangedEventArgs args)
    {
        UpdateActivePlaybackProgress(
            MediaLibraryItemKind.Audio,
            args.State.Position,
            args.State.Duration,
            args.State.IsSeekable,
            persistImmediately: args.State.Status == AudioPlaybackStatus.Paused);
    }

    private void VideoPlaybackService_PlaybackStateChanged(
        object? sender,
        VideoPlaybackStateChangedEventArgs args)
    {
        UpdateActivePlaybackProgress(
            MediaLibraryItemKind.Video,
            args.State.Position,
            args.State.Duration,
            args.State.IsSeekable,
            persistImmediately: args.State.IsPaused);
    }

    private async void VideoPlaybackService_PlaybackClosed(object? sender, EventArgs args)
    {
        ActivePlaybackProgress? active;
        lock (_progressLock)
        {
            active = _activeProgress;
        }
        if (active?.Kind == MediaLibraryItemKind.Video)
        {
            await PersistAndClearActivePlaybackPositionAsync();
        }
    }

    private PlaybackQueueEntry? SelectNavigationTargetNoLock(
        PlaybackNavigationDirection direction)
    {
        if (_queue.Count == 0)
        {
            return null;
        }

        var targetIndex = _isShuffleEnabled
            ? SelectShuffleTargetIndexNoLock(direction)
            : SelectLinearTargetIndexNoLock(direction);
        if (targetIndex < 0 || targetIndex >= _queue.Count)
        {
            return null;
        }

        _currentIndex = targetIndex;
        return _queue[targetIndex];
    }

    private int SelectLinearTargetIndexNoLock(PlaybackNavigationDirection direction)
    {
        if (_currentIndex < 0 || _currentIndex >= _queue.Count)
        {
            return direction == PlaybackNavigationDirection.Next ? 0 : -1;
        }
        if (direction == PlaybackNavigationDirection.Previous)
        {
            if (_currentIndex > 0)
            {
                return _currentIndex - 1;
            }
            return _repeatMode == AudioRepeatMode.All ? _queue.Count - 1 : -1;
        }
        if (_currentIndex + 1 < _queue.Count)
        {
            return _currentIndex + 1;
        }
        return _repeatMode == AudioRepeatMode.All ? 0 : -1;
    }

    private int SelectShuffleTargetIndexNoLock(PlaybackNavigationDirection direction)
    {
        if (direction == PlaybackNavigationDirection.Previous)
        {
            if (!TryPopHistoryIndex(_shufflePreviousHistory, out var previousIndex))
            {
                return _queue.Count == 1 && _repeatMode == AudioRepeatMode.All ? 0 : -1;
            }
            PushHistoryIndex(_shuffleForwardHistory, _currentIndex);
            _shuffleVisitedIndices.Add(previousIndex);
            return previousIndex;
        }

        if (TryPopHistoryIndex(_shuffleForwardHistory, out var forwardIndex))
        {
            PushHistoryIndex(_shufflePreviousHistory, _currentIndex);
            _shuffleVisitedIndices.Add(forwardIndex);
            return forwardIndex;
        }

        var targetIndex = ChooseUnvisitedShuffleIndexNoLock();
        if (targetIndex < 0 && _repeatMode == AudioRepeatMode.All)
        {
            _shuffleVisitedIndices.Clear();
            targetIndex = ChooseUnvisitedShuffleIndexNoLock();
            if (targetIndex < 0 && _queue.Count == 1)
            {
                targetIndex = 0;
            }
        }
        if (targetIndex < 0)
        {
            return -1;
        }

        _shuffleForwardHistory.Clear();
        if (targetIndex != _currentIndex)
        {
            PushHistoryIndex(_shufflePreviousHistory, _currentIndex);
        }
        _shuffleVisitedIndices.Add(targetIndex);
        return targetIndex;
    }

    private int ChooseUnvisitedShuffleIndexNoLock()
    {
        var selectedIndex = -1;
        var candidateCount = 0;
        for (var index = 0; index < _queue.Count; index++)
        {
            if (index == _currentIndex || _shuffleVisitedIndices.Contains(index))
            {
                continue;
            }
            candidateCount++;
            if (Random.Shared.Next(candidateCount) == 0)
            {
                selectedIndex = index;
            }
        }
        return selectedIndex;
    }

    private bool CanNavigateNoLock(PlaybackNavigationDirection direction)
    {
        if (_queue.Count == 0)
        {
            return false;
        }
        if (!_isShuffleEnabled)
        {
            if (_currentIndex < 0 || _currentIndex >= _queue.Count)
            {
                return direction == PlaybackNavigationDirection.Next;
            }
            if (direction == PlaybackNavigationDirection.Previous)
            {
                return _currentIndex > 0 || _repeatMode == AudioRepeatMode.All;
            }
            return _currentIndex + 1 < _queue.Count || _repeatMode == AudioRepeatMode.All;
        }
        if (direction == PlaybackNavigationDirection.Previous)
        {
            return _shufflePreviousHistory.Count > 0 ||
                (_queue.Count == 1 && _repeatMode == AudioRepeatMode.All);
        }
        if (_shuffleForwardHistory.Count > 0)
        {
            return true;
        }
        for (var index = 0; index < _queue.Count; index++)
        {
            if (index != _currentIndex && !_shuffleVisitedIndices.Contains(index))
            {
                return true;
            }
        }
        return _repeatMode == AudioRepeatMode.All;
    }

    private void ResetShuffleStateNoLock()
    {
        _shuffleVisitedIndices.Clear();
        _shufflePreviousHistory.Clear();
        _shuffleForwardHistory.Clear();
        if (_currentIndex >= 0 && _currentIndex < _queue.Count)
        {
            _shuffleVisitedIndices.Add(_currentIndex);
        }
    }

    private static bool TryPopHistoryIndex(List<int> history, out int index)
    {
        if (history.Count == 0)
        {
            index = -1;
            return false;
        }
        var lastIndex = history.Count - 1;
        index = history[lastIndex];
        history.RemoveAt(lastIndex);
        return true;
    }

    private static void PushHistoryIndex(List<int> history, int index)
    {
        if (index < 0)
        {
            return;
        }
        if (history.Count == MaximumShuffleHistoryLength)
        {
            history.RemoveAt(0);
        }
        history.Add(index);
    }

    private void InvalidateActivePlaybackNoLock()
    {
        _activeAudioPlaybackRequest = null;
        _activeVideoPath = null;
    }

    private void RaisePlaybackQueueChanged() =>
        PlaybackQueueChanged?.Invoke(this, EventArgs.Empty);

    private async void AudioPlaybackService_PlaybackEnded(
        object? sender,
        AudioPlaybackEndedEventArgs args)
    {
        await Task.Yield();
        await ContinueAfterPlaybackEndedAsync(
            MediaLibraryItemKind.Audio,
            args.Request.FilePath,
            args.Request);
    }

    private async void VideoPlaybackService_PlaybackEnded(
        object? sender,
        VideoPlaybackEndedEventArgs args)
    {
        await Task.Yield();
        await ContinueAfterPlaybackEndedAsync(
            MediaLibraryItemKind.Video,
            args.FilePath,
            audioRequest: null);
    }

    private async Task ContinueAfterPlaybackEndedAsync(
        MediaLibraryItemKind kind,
        string filePath,
        AudioPlaybackRequest? audioRequest)
    {
        await _playbackGate.WaitAsync();
        try
        {
            var isCurrentPlayback = false;
            var repeatCurrent = false;
            PlaybackQueueEntry? completedItem = null;
            lock (_queueLock)
            {
                isCurrentPlayback = _currentIndex >= 0 &&
                    _currentIndex < _queue.Count &&
                    _queue[_currentIndex].Kind == kind &&
                    IsSameMediaPath(_queue[_currentIndex].FilePath, filePath) &&
                    (kind == MediaLibraryItemKind.Audio
                        ? IsSameAudioPlayback(_activeAudioPlaybackRequest, audioRequest)
                        : IsSameMediaPath(_activeVideoPath, filePath));
                repeatCurrent = isCurrentPlayback && _repeatMode == AudioRepeatMode.One;
                if (isCurrentPlayback)
                {
                    completedItem = _queue[_currentIndex];
                    InvalidateActivePlaybackNoLock();
                }
            }

            if (!isCurrentPlayback)
            {
                return;
            }

            lock (_progressLock)
            {
                _activeProgress = null;
                _pendingResume = null;
            }
            await ClearStoredPlaybackPositionAsync(completedItem?.MediaId);

            if (repeatCurrent)
            {
                await ReplayCurrentCoreAsync(
                    PlayerActivationIntent.BackgroundContinuation,
                    CancellationToken.None);
            }
            else
            {
                await NavigateCoreAsync(
                    PlaybackNavigationDirection.Next,
                    PlayerActivationIntent.BackgroundContinuation,
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to continue the playback queue: {ex}");
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private static bool IsSameAudioPlayback(
        AudioPlaybackRequest? activeRequest,
        AudioPlaybackRequest? endedRequest)
    {
        if (activeRequest is null || endedRequest is null)
        {
            return false;
        }

        if (activeRequest.PlaybackId != Guid.Empty || endedRequest.PlaybackId != Guid.Empty)
        {
            return activeRequest.PlaybackId != Guid.Empty &&
                activeRequest.PlaybackId == endedRequest.PlaybackId;
        }

        return string.Equals(
            activeRequest.FilePath,
            endedRequest.FilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameMediaPath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
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
            return Path.GetFullPath(candidatePath).StartsWith(
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void SetPlaybackOptions(bool? isShuffleEnabled, AudioRepeatMode? repeatMode)
    {
        bool shuffleSnapshot;
        AudioRepeatMode repeatSnapshot;
        lock (_queueLock)
        {
            var nextShuffle = isShuffleEnabled ?? _isShuffleEnabled;
            var nextRepeat = repeatMode ?? _repeatMode;
            if (nextShuffle == _isShuffleEnabled && nextRepeat == _repeatMode)
            {
                return;
            }
            var shuffleChanged = nextShuffle != _isShuffleEnabled;
            _isShuffleEnabled = nextShuffle;
            _repeatMode = nextRepeat;
            if (shuffleChanged)
            {
                ResetShuffleStateNoLock();
            }
            shuffleSnapshot = _isShuffleEnabled;
            repeatSnapshot = _repeatMode;
        }

        SavePlaybackOptions(shuffleSnapshot, repeatSnapshot);
        AudioPlaybackOptionsChanged?.Invoke(this, EventArgs.Empty);
        RaisePlaybackQueueChanged();
    }

    private static (bool IsShuffleEnabled, AudioRepeatMode RepeatMode) LoadPlaybackOptions()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            var shuffle = values[AudioShuffleSettingKey] switch
            {
                bool value => value,
                string value when bool.TryParse(value, out var parsed) => parsed,
                _ => false
            };
            return (shuffle, ParseRepeatMode(values[AudioRepeatModeSettingKey]));
        }
        catch
        {
            return (false, AudioRepeatMode.Off);
        }
    }

    private static AudioRepeatMode ParseRepeatMode(object? value)
    {
        if (value is string text &&
            Enum.TryParse<AudioRepeatMode>(text, ignoreCase: true, out var parsed) &&
            parsed is AudioRepeatMode.Off or AudioRepeatMode.All or AudioRepeatMode.One)
        {
            return parsed;
        }
        if (value is int number && Enum.IsDefined(typeof(AudioRepeatMode), number))
        {
            return (AudioRepeatMode)number;
        }
        return AudioRepeatMode.Off;
    }

    private static void SavePlaybackOptions(bool shuffle, AudioRepeatMode repeatMode)
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values[AudioShuffleSettingKey] = shuffle;
            values[AudioRepeatModeSettingKey] = repeatMode.ToString();
        }
        catch
        {
            // Playback options remain usable when app settings are unavailable.
        }
    }

    private sealed record ActivePlaybackProgress(
        Guid? MediaId,
        string FilePath,
        MediaLibraryItemKind Kind,
        double Position,
        double Duration);

    private sealed record PendingPlaybackResume(
        Guid? MediaId,
        string FilePath,
        MediaLibraryItemKind Kind,
        double Position);

    private enum PlaybackNavigationDirection
    {
        Previous,
        Next
    }
}

public sealed class PlaybackQueueItem(
    Guid? mediaId,
    string title,
    string filePath,
    MediaLibraryItemKind kind,
    bool isCurrent,
    string? artist,
    string? album,
    string? thumbnailPath,
    TimeSpan? playbackPosition,
    string? linkedFilePath)
{
    public Guid? MediaId { get; } = mediaId;

    public string Title { get; } = string.IsNullOrWhiteSpace(title)
        ? Path.GetFileNameWithoutExtension(filePath)
        : title;

    public string FilePath { get; } = filePath;

    public MediaLibraryItemKind Kind { get; } = kind;

    public bool IsCurrent { get; } = isCurrent;

    public string? Artist { get; } = artist;

    public string? Album { get; } = album;

    public string? ThumbnailPath { get; } = thumbnailPath;

    public TimeSpan? PlaybackPosition { get; } = playbackPosition;

    public string? LinkedFilePath { get; } = linkedFilePath;
}

// Retained for PlayerWindow's internal mpv playlist bookkeeping. The public
// application queue is PlaybackQueueItem and may contain both media kinds.
public sealed class VideoQueueItem(string title, string filePath, bool isCurrent)
{
    public string Title { get; } = title;

    public string FilePath { get; } = filePath;

    public bool IsCurrent { get; } = isCurrent;
}
