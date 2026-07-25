using Koukei.Audio;
using Koukei.Bus.Models;
using Koukei.Bus.Services;
using Koukei.UI.Helpers;
using Koukei.UI.Services;
using Koukei.Video;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Koukei.UI.Pages;

/// <summary>
/// Home page.
/// </summary>
public sealed partial class HomePage : Page
{
    private const int RecentItemCountPerSection = 10;
    private const int RecentItemQueryCount = 50;
    private const int MaximumFolderPlaylistItems = 20_000;
    private const double RecentVideoCardMinWidth = 260;
    private const double RecentAudioCardMinWidth = 210;
    private const double RecentVideoFixedHeight = 188;
    private const double RecentVideoScaledTextHeight = 40;
    private const double RecentAudioFixedHeightOffset = 16;
    private const double RecentAudioScaledTextHeight = 38;
    private const double RecentCardHorizontalSpacing = 12;
    private const double RecentCardVerticalSpacing = 12;
    private const double RecentCardLayoutWidthReduction = 2;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3g2", ".3gp", ".asf", ".avi", ".divx", ".f4v", ".flv", ".m2ts",
        ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts", ".ogv",
        ".rm", ".rmvb", ".ts", ".vob", ".webm", ".wmv"
    };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".ac3", ".aif", ".aiff", ".alac", ".amr", ".ape", ".au",
        ".caf", ".dff", ".dsf", ".dts", ".eac3", ".flac", ".m4a", ".mka",
        ".mp3", ".mpc", ".oga", ".ogg", ".opus", ".ra", ".tak", ".tta",
        ".wav", ".weba", ".wma", ".wv"
    };
    private readonly ResourceLoader _resourceLoader = new();
    private readonly UISettings _uiSettings = new();
    private readonly string _emptyTitle;
    private readonly string _emptyDescription;
    private CancellationTokenSource? _folderOpenCts;
    private CancellationTokenSource? _recentItemsLoadCts;
    private CancellationTokenSource? _recentItemsEntranceCancellation;
    private CancellationTokenSource? _recentStateMotionCancellation;
    private UIElement? _recentVisibleSurface;
    private bool _isLoaded;
    private bool _isOpeningRecentItem;
    private bool _isRunningQuickAction;
    private bool _isTextScaleSubscribed;

    public ObservableCollection<RecentMediaItemViewModel> RecentVideoItems { get; } = [];

    public ObservableCollection<RecentMediaItemViewModel> RecentAudioItems { get; } = [];

    public HomePage()
    {
        InitializeComponent();
        _emptyTitle = RecentEmptyTitle.Text;
        _emptyDescription = RecentEmptyDescription.Text;
        MotionHelper.SetVisibleInstant(RecentContentPanel, isVisible: false);
        MotionHelper.SetVisibleInstant(RecentEmptyPanel, isVisible: false);
        MotionHelper.SetVisibleInstant(
            RecentItemsProgressRing,
            isVisible: true,
            isHitTestVisible: false);
        _recentVisibleSurface = RecentItemsProgressRing;
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (!_isTextScaleSubscribed)
        {
            _uiSettings.TextScaleFactorChanged += UiSettings_TextScaleFactorChanged;
            _isTextScaleSubscribed = true;
        }
        _ = ReloadRecentItemsAsync();
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        if (_isTextScaleSubscribed)
        {
            _uiSettings.TextScaleFactorChanged -= UiSettings_TextScaleFactorChanged;
            _isTextScaleSubscribed = false;
        }
        _recentItemsLoadCts?.Cancel();
        _folderOpenCts?.Cancel();
        var entranceCancellation = _recentItemsEntranceCancellation;
        _recentItemsEntranceCancellation = null;
        entranceCancellation?.Cancel();
        entranceCancellation?.Dispose();
        _recentStateMotionCancellation?.Cancel();
        _recentStateMotionCancellation?.Dispose();
        _recentStateMotionCancellation = null;
        RecentVideoItems.Clear();
        RecentAudioItems.Clear();
    }

    private void UiSettings_TextScaleFactorChanged(UISettings sender, object args)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            UpdateRecentGridItemSize(
                RecentVideoItemsGrid,
                RecentVideoCardMinWidth,
                false);
            UpdateRecentGridItemSize(
                RecentAudioItemsGrid,
                RecentAudioCardMinWidth,
                true);
        });
    }

    private void RefreshRecentItemsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ReloadRecentItemsAsync();
    }

    private void HomeRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var breakpoint = ResponsiveLayout.Resolve(e.NewSize.Width);
        HomeScrollViewer.Padding = ResponsiveLayout.GetPagePadding(breakpoint);

        var isCompact = breakpoint == UiBreakpoint.Compact;
        Grid.SetRow(RefreshRecentItemsButton, isCompact ? 1 : 0);
        Grid.SetColumn(RefreshRecentItemsButton, isCompact ? 0 : 1);
        Grid.SetColumnSpan(RefreshRecentItemsButton, isCompact ? 2 : 1);
        RefreshRecentItemsButton.HorizontalAlignment = isCompact
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;

        Grid.SetRow(OpenFolderButton, isCompact ? 1 : 0);
        Grid.SetColumn(OpenFolderButton, isCompact ? 0 : 1);
        Grid.SetColumnSpan(OpenFolderButton, isCompact ? 3 : 1);
        Grid.SetRow(QuickActionBusyPanel, isCompact ? 2 : 0);
        Grid.SetColumn(QuickActionBusyPanel, isCompact ? 0 : 2);
        Grid.SetColumnSpan(QuickActionBusyPanel, isCompact ? 3 : 1);
        Grid.SetColumnSpan(OpenFilesButton, isCompact ? 3 : 1);
        OpenFilesButton.HorizontalAlignment = isCompact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        OpenFolderButton.HorizontalAlignment = isCompact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    private void SetQuickActionBusy(bool isBusy)
    {
        OpenFilesButton.IsEnabled = !isBusy;
        OpenFolderButton.IsEnabled = !isBusy;
        QuickActionProgressRing.IsActive = isBusy;
        _ = isBusy
            ? MotionHelper.ShowAsync(
                QuickActionBusyPanel,
                MotionPreset.Fast,
                MotionDirection.Down,
                distance: 4)
            : MotionHelper.HideAsync(
                QuickActionBusyPanel,
                MotionPreset.Fast,
                MotionDirection.Up,
                distance: 4);
    }

    private async void OpenFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunningQuickAction)
        {
            return;
        }

        _isRunningQuickAction = true;
        SetQuickActionBusy(true);
        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            foreach (var extension in VideoExtensions.Concat(AudioExtensions).OrderBy(extension => extension))
            {
                picker.FileTypeFilter.Add(extension);
            }

            if (!TryInitializePickerWithMainWindow(picker, out var pickerError))
            {
                throw new InvalidOperationException(pickerError);
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                await PlayMediaPathsAsync(
                    files.Select(file => file.Path).Where(path => !string.IsNullOrWhiteSpace(path)).ToList());
            }
        }
        catch (Exception ex)
        {
            await ShowQuickActionErrorAsync(
                GetResourceString("HomePage_OpenFilesFailed", "Could not open files"),
                ex.Message);
        }
        finally
        {
            _isRunningQuickAction = false;
            SetQuickActionBusy(false);
        }
    }

    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunningQuickAction)
        {
            return;
        }

        _isRunningQuickAction = true;
        SetQuickActionBusy(true);
        CancellationTokenSource? folderOpenCts = null;
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            picker.FileTypeFilter.Add("*");

            if (!TryInitializePickerWithMainWindow(picker, out var pickerError))
            {
                throw new InvalidOperationException(pickerError);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null || !_isLoaded)
            {
                return;
            }

            _folderOpenCts?.Cancel();
            folderOpenCts = new CancellationTokenSource();
            _folderOpenCts = folderOpenCts;
            var cancellationToken = folderOpenCts.Token;
            var mediaCounts = await Task.Run(
                () => CountFolderMediaFiles(folder.Path, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (mediaCounts.VideoCount == 0 && mediaCounts.AudioCount == 0)
            {
                await ShowQuickActionErrorAsync(
                    GetResourceString("HomePage_NoPlayableMediaTitle", "No playable media"),
                    GetResourceString(
                        "HomePage_NoPlayableMediaDescription",
                        "The selected folder does not contain supported audio or video files."));
                return;
            }

            var playVideos = await ChooseVideoPlaybackAsync(
                mediaCounts.VideoCount,
                mediaCounts.AudioCount);
            cancellationToken.ThrowIfCancellationRequested();
            if (playVideos is null)
            {
                return;
            }

            var selectedCount = playVideos.Value
                ? mediaCounts.VideoCount
                : mediaCounts.AudioCount;
            if (selectedCount > MaximumFolderPlaylistItems)
            {
                throw new FolderPlaylistLimitExceededException();
            }

            var playlist = await Task.Run(
                () => CollectFolderPlaylist(
                    folder.Path,
                    playVideos.Value,
                    selectedCount,
                    cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (playlist.Count == 0)
            {
                await ShowQuickActionErrorAsync(
                    GetResourceString("HomePage_NoPlayableMediaTitle", "No playable media"),
                    GetResourceString(
                        "HomePage_NoPlayableMediaDescription",
                        "The selected folder does not contain supported audio or video files."));
                return;
            }

            var resolvedPlaylist = await ResolvePlaybackTitlesAsync(
                playlist,
                playVideos.Value,
                cancellationToken);
            await PlayClassifiedPlaylistAsync(resolvedPlaylist, cancellationToken);
        }
        catch (OperationCanceledException) when (folderOpenCts?.IsCancellationRequested == true)
        {
        }
        catch (FolderPlaylistLimitExceededException)
        {
            await ShowQuickActionErrorAsync(
                GetResourceString("HomePage_FolderPlaylistTooLargeTitle", "Too many media files"),
                string.Format(
                    GetResourceString(
                        "HomePage_FolderPlaylistTooLargeDescription",
                        "A folder playlist can contain at most {0:N0} files. Choose a smaller folder and try again."),
                    MaximumFolderPlaylistItems));
        }
        catch (Exception ex)
        {
            await ShowQuickActionErrorAsync(
                GetResourceString("HomePage_OpenFolderFailed", "Could not open folder"),
                ex.Message);
        }
        finally
        {
            if (folderOpenCts is not null && ReferenceEquals(_folderOpenCts, folderOpenCts))
            {
                _folderOpenCts = null;
            }

            folderOpenCts?.Dispose();
            _isRunningQuickAction = false;
            SetQuickActionBusy(false);
        }
    }

    private async Task PlayMediaPathsAsync(IReadOnlyList<string> mediaPaths)
    {
        if (App.Services is null || mediaPaths.Count == 0)
        {
            return;
        }

        var videos = mediaPaths
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .Select(path => (Title: Path.GetFileNameWithoutExtension(path), FilePath: path))
            .ToList();
        var audio = mediaPaths
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
            .Select(path => (Title: Path.GetFileNameWithoutExtension(path), FilePath: path))
            .ToList();

        var playVideos = await ChooseVideoPlaybackAsync(videos.Count, audio.Count);
        if (playVideos is null)
        {
            return;
        }

        var selectedItems = playVideos.Value ? videos : audio;
        var resolvedItems = await ResolvePlaybackTitlesAsync(
            selectedItems,
            playVideos.Value,
            CancellationToken.None);
        await PlayClassifiedPlaylistAsync(
            resolvedItems,
            CancellationToken.None);
    }

    private async Task<bool?> ChooseVideoPlaybackAsync(int videoCount, int audioCount)
    {
        if (videoCount == 0)
        {
            return false;
        }
        if (audioCount == 0)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = GetResourceString("HomePage_MixedMediaTitle", "Choose what to play"),
            Content = GetResourceString(
                "HomePage_MixedMediaDescription",
                "The selection contains both video and audio files."),
            PrimaryButtonText = GetResourceString("HomePage_PlayVideos", "Play videos"),
            SecondaryButtonText = GetResourceString("HomePage_PlayAudio", "Play audio"),
            CloseButtonText = GetResourceString("HomePage_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null
        };
    }

    private static async Task PlayClassifiedPlaylistAsync(
        IReadOnlyList<PlaybackQueueEntry> playlist,
        CancellationToken cancellationToken)
    {
        if (App.Services is null || playlist.Count == 0)
        {
            return;
        }

        var coordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
        await coordinator.PlayPlaylistAsync(playlist, cancellationToken: cancellationToken);
    }

    private static FolderMediaCounts CountFolderMediaFiles(
        string folderPath,
        CancellationToken cancellationToken)
    {
        var videoCount = 0;
        var audioCount = 0;
        foreach (var path in EnumerateFolderFiles(folderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(path);
            if (VideoExtensions.Contains(extension))
            {
                videoCount = Math.Min(videoCount + 1, MaximumFolderPlaylistItems + 1);
            }
            else if (AudioExtensions.Contains(extension))
            {
                audioCount = Math.Min(audioCount + 1, MaximumFolderPlaylistItems + 1);
            }
        }

        return new FolderMediaCounts(videoCount, audioCount);
    }

    private static List<(string Title, string FilePath)> CollectFolderPlaylist(
        string folderPath,
        bool collectVideos,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        var extensions = collectVideos ? VideoExtensions : AudioExtensions;
        var playlist = new List<(string Title, string FilePath)>(expectedCount);
        foreach (var path in EnumerateFolderFiles(folderPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!extensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            if (playlist.Count >= MaximumFolderPlaylistItems)
            {
                throw new FolderPlaylistLimitExceededException();
            }

            playlist.Add((Path.GetFileNameWithoutExtension(path), path));
        }

        playlist.Sort(static (left, right) =>
            StringComparer.CurrentCultureIgnoreCase.Compare(left.FilePath, right.FilePath));
        return playlist;
    }

    private static async Task<IReadOnlyList<PlaybackQueueEntry>> ResolvePlaybackTitlesAsync(
        IReadOnlyList<(string Title, string FilePath)> items,
        bool isVideo,
        CancellationToken cancellationToken)
    {
        var expectedKind = isVideo
            ? MediaLibraryItemKind.Video
            : MediaLibraryItemKind.Audio;
        if (items.Count == 0 || App.Services is null)
        {
            return items
                .Select(item => new PlaybackQueueEntry(
                    null,
                    item.Title,
                    item.FilePath,
                    expectedKind))
                .ToArray();
        }

        IReadOnlyList<MediaLibraryPlaybackItem> persistedItems = [];
        try
        {
            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            persistedItems = await library.GetPlaybackItemsByPathsAsync(
                items.Select(item => item.FilePath).ToArray(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Direct playback remains available when the media library is unavailable.
        }

        var persistedByPath = persistedItems
            .Where(item => item.Kind == expectedKind && !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var audioMetadataService = isVideo
            ? null
            : App.Services.GetService<IAudioMetadataService>();
        var videoMediaInfoService = isVideo
            ? App.Services.GetService<IVideoMediaInfoService>()
            : null;
        var resolvedItems = new List<PlaybackQueueEntry>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (persistedByPath.TryGetValue(item.FilePath, out var persistedItem) &&
                !string.IsNullOrWhiteSpace(persistedItem.Title))
            {
                resolvedItems.Add(new PlaybackQueueEntry(
                    persistedItem.Id,
                    persistedItem.Title.Trim(),
                    item.FilePath,
                    expectedKind,
                    persistedItem.Artist,
                    persistedItem.Album,
                    persistedItem.ThumbnailPath,
                    persistedItem.PlaybackPosition,
                    persistedItem.LinkedFilePath));
                continue;
            }

            var resolvedTitle = item.Title;
            try
            {
                if (isVideo && videoMediaInfoService is not null)
                {
                    resolvedTitle = (await videoMediaInfoService.GetMediaInfoAsync(
                        item.FilePath,
                        cancellationToken)).Title;
                }
                else if (!isVideo && audioMetadataService is not null)
                {
                    resolvedTitle = (await audioMetadataService.GetMetadataAsync(
                        item.FilePath,
                        cancellationToken)).Title;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A readable filename is the final fallback for unsupported or damaged metadata.
            }

            resolvedItems.Add(new PlaybackQueueEntry(
                null,
                string.IsNullOrWhiteSpace(resolvedTitle)
                    ? Path.GetFileNameWithoutExtension(item.FilePath)
                    : resolvedTitle.Trim(),
                item.FilePath,
                expectedKind));
        }

        return resolvedItems;
    }

    private static IEnumerable<string> EnumerateFolderFiles(string folderPath) =>
        Directory.EnumerateFiles(
            folderPath,
            "*.*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            });

    private readonly record struct FolderMediaCounts(int VideoCount, int AudioCount);

    private sealed class FolderPlaylistLimitExceededException : Exception
    {
    }

    private bool TryInitializePickerWithMainWindow(object picker, out string errorMessage)
    {
        if (App.MainWindow is not { } window)
        {
            errorMessage = GetResourceString(
                "HomePage_FilePickerNoWindow",
                "The file picker could not find the main application window.");
            return false;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            errorMessage = GetResourceString(
                "HomePage_FilePickerNoWindow",
                "The file picker could not find the main application window.");
            return false;
        }

        try
        {
            InitializeWithWindow.Initialize(picker, hwnd);
            errorMessage = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            errorMessage = string.Format(
                GetResourceString(
                    "HomePage_FilePickerInitializeFailed",
                    "Failed to initialize the file picker: {0}"),
                ex.Message);
            return false;
        }
    }

    private async Task ReloadRecentItemsAsync()
    {
        var previousLoad = _recentItemsLoadCts;
        previousLoad?.Cancel();
        previousLoad?.Dispose();
        var previousEntrance = _recentItemsEntranceCancellation;
        _recentItemsEntranceCancellation = null;
        previousEntrance?.Cancel();
        previousEntrance?.Dispose();

        var loadCts = new CancellationTokenSource();
        _recentItemsLoadCts = loadCts;
        var hadVisibleItems = RecentVideoItems.Count > 0 || RecentAudioItems.Count > 0;
        SetRecentItemsLoadingState();

        try
        {
            if (App.DataInitializationException is { } initializationException)
            {
                throw new InvalidOperationException(initializationException.Message, initializationException);
            }

            if (App.Services is null)
            {
                throw new InvalidOperationException(
                    GetResourceString("HomePage_RecentServiceUnavailable", "The media library is not available."));
            }

            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            var items = await library.GetRecentlyOpenedAsync(RecentItemQueryCount, loadCts.Token);
            loadCts.Token.ThrowIfCancellationRequested();

            var recentVideoItems = items
                .Where(item => item.Kind == MediaLibraryItemKind.Video)
                .Take(RecentItemCountPerSection)
                .ToArray();
            var recentAudioItems = items
                .Where(item => item.Kind == MediaLibraryItemKind.Audio)
                .Take(RecentItemCountPerSection)
                .ToArray();
            var recentVideos = recentVideoItems
                .Select(item => new RecentMediaItemViewModel(item))
                .ToArray();
            var unknownArtist = GetResourceString("AudioPlayer_UnknownArtist", "Unknown artist");
            var recentAudio = recentAudioItems
                .Select(item => new RecentMediaItemViewModel(item, unknownArtist))
                .ToArray();

            if (!ReferenceEquals(_recentItemsLoadCts, loadCts))
            {
                return;
            }

            var displayedVideos = ReconcileRecentItems(RecentVideoItems, recentVideos);
            var displayedAudio = ReconcileRecentItems(RecentAudioItems, recentAudio);

            ShowRecentItemsOrEmptyState();
            if (!hadVisibleItems)
            {
                var entranceCancellation = new CancellationTokenSource();
                var entranceToken = entranceCancellation.Token;
                _recentItemsEntranceCancellation = entranceCancellation;
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isLoaded ||
                        !ReferenceEquals(
                            _recentItemsEntranceCancellation,
                            entranceCancellation))
                    {
                        return;
                    }

                    _ = MotionHelper.AnimateVisibleItemsEntranceAsync(
                        RecentVideoItemsGrid,
                        cancellationToken: entranceToken);
                    _ = MotionHelper.AnimateVisibleItemsEntranceAsync(
                        RecentAudioItemsGrid,
                        cancellationToken: entranceToken);
                });
            }
            await RepairRecentThumbnailsAsync(
                recentVideoItems.Concat(recentAudioItems).ToArray(),
                displayedVideos.Concat(displayedAudio).ToArray(),
                library,
                loadCts.Token);
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_recentItemsLoadCts, loadCts))
            {
                ShowRecentItemsErrorState(ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_recentItemsLoadCts, loadCts))
            {
                _recentItemsLoadCts = null;
                loadCts.Dispose();
            }
        }
    }

    private static IReadOnlyList<RecentMediaItemViewModel> ReconcileRecentItems(
        ObservableCollection<RecentMediaItemViewModel> collection,
        IReadOnlyList<RecentMediaItemViewModel> desiredItems)
    {
        for (var targetIndex = 0; targetIndex < desiredItems.Count; targetIndex++)
        {
            var desiredItem = desiredItems[targetIndex];
            if (targetIndex < collection.Count &&
                collection[targetIndex].Id == desiredItem.Id)
            {
                collection[targetIndex] = desiredItem;
                continue;
            }

            var currentIndex = -1;
            for (var index = targetIndex + 1; index < collection.Count; index++)
            {
                if (collection[index].Id == desiredItem.Id)
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                collection.Move(currentIndex, targetIndex);
                collection[targetIndex] = desiredItem;
            }
            else
            {
                collection.Insert(targetIndex, desiredItem);
            }
        }

        while (collection.Count > desiredItems.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }

        return collection.ToArray();
    }

    private static async Task RepairRecentThumbnailsAsync(
        IReadOnlyList<MediaLibraryItem> items,
        IReadOnlyList<RecentMediaItemViewModel> viewModels,
        IMediaLibraryBus library,
        CancellationToken cancellationToken)
    {
        var viewModelsById = viewModels.ToDictionary(item => item.Id);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!viewModelsById.TryGetValue(item.Id, out var viewModel))
            {
                continue;
            }

            var resolvedPath = await MediaThumbnailResolver.ResolveOrCreateAsync(
                item,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(
                    item.ThumbnailPath,
                    resolvedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                await library.SetThumbnailAsync(item.Id, resolvedPath, cancellationToken);
                App.Services
                    .GetService<PlaybackCoordinator>()?
                    .UpdateQueueItemThumbnail(item.Id, item.Path, resolvedPath);
            }

            viewModel.UpdateThumbnailPath(resolvedPath);
        }
    }

    private async void RecentItemsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentMediaItemViewModel item)
        {
            await OpenRecentItemFromUiAsync(item);
        }
    }

    private void RecentVideoItemsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRecentGridItemSize(
            RecentVideoItemsGrid,
            RecentVideoCardMinWidth,
            false,
            e.NewSize.Width);
        DispatcherQueue.TryEnqueue(() => UpdateRecentGridItemSize(
            RecentVideoItemsGrid,
            RecentVideoCardMinWidth,
            false));
    }

    private void RecentAudioItemsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRecentGridItemSize(
            RecentAudioItemsGrid,
            RecentAudioCardMinWidth,
            true,
            e.NewSize.Width);
        DispatcherQueue.TryEnqueue(() => UpdateRecentGridItemSize(
            RecentAudioItemsGrid,
            RecentAudioCardMinWidth,
            true));
    }

    private void RecentAudioArtwork_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement artwork &&
            e.NewSize.Width > 0 &&
            (double.IsNaN(artwork.Height) || Math.Abs(artwork.Height - e.NewSize.Width) > 0.5))
        {
            artwork.Height = e.NewSize.Width;
        }
    }

    private void UpdateRecentGridItemSize(
        GridView grid,
        double minimumCardWidth,
        bool heightTracksWidth,
        double? availableWidth = null)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid itemsPanel)
        {
            return;
        }

        var width = availableWidth is > 0 and var currentWidth && !double.IsNaN(currentWidth)
            ? currentWidth
            : GetRecentGridViewportWidth(grid, itemsPanel);
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        var layoutWidth = Math.Max(1, width - RecentCardLayoutWidthReduction);
        var minimumItemSlotWidth = minimumCardWidth + RecentCardHorizontalSpacing;
        var columnCount = Math.Max(1, (int)Math.Floor(layoutWidth / minimumItemSlotWidth));
        var itemSlotWidth = layoutWidth / columnCount;
        var cardWidth = Math.Max(1, itemSlotWidth - RecentCardHorizontalSpacing);
        var textScale = Math.Max(1, _uiSettings.TextScaleFactor);
        var cardHeight = heightTracksWidth
            ? cardWidth + RecentAudioFixedHeightOffset + (RecentAudioScaledTextHeight * textScale)
            : RecentVideoFixedHeight + (RecentVideoScaledTextHeight * textScale);

        itemsPanel.ItemWidth = itemSlotWidth;
        itemsPanel.ItemHeight = cardHeight + RecentCardVerticalSpacing;
    }

    private static double GetRecentGridViewportWidth(GridView grid, ItemsWrapGrid itemsPanel)
    {
        var scrollViewer = FindVisualDescendant<ScrollViewer>(grid);
        if (scrollViewer?.ViewportWidth is > 0 and var viewportWidth && !double.IsNaN(viewportWidth))
        {
            return viewportWidth;
        }

        return itemsPanel.ActualWidth > 0 ? itemsPanel.ActualWidth : grid.ActualWidth;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private async Task OpenRecentItemFromUiAsync(RecentMediaItemViewModel item)
    {
        if (_isOpeningRecentItem)
        {
            return;
        }

        _isOpeningRecentItem = true;
        SetRecentItemsHitTestVisible(false);
        try
        {
            await OpenRecentItemAsync(item);
            await ReloadRecentItemsAsync();
        }
        catch (Exception ex)
        {
            await ShowOpenErrorAsync(ex.Message);
        }
        finally
        {
            _isOpeningRecentItem = false;
            SetRecentItemsHitTestVisible(true);
        }
    }

    private void SetRecentItemsHitTestVisible(bool isVisible)
    {
        RecentVideoItemsGrid.IsHitTestVisible = isVisible;
        RecentAudioItemsGrid.IsHitTestVisible = isVisible;
    }

    private async Task OpenRecentItemAsync(RecentMediaItemViewModel item)
    {
        if (App.Services is null)
        {
            throw new InvalidOperationException(
                GetResourceString("HomePage_RecentServiceUnavailable", "The media library is not available."));
        }

        if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            throw new FileNotFoundException(
                GetResourceString("HomePage_RecentFileMissing", "The selected file no longer exists."),
                item.FilePath);
        }

        var playbackCoordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
        switch (item.Kind)
        {
            case MediaLibraryItemKind.Audio:
            case MediaLibraryItemKind.Video:
                await playbackCoordinator.PlayItemAsync(new PlaybackQueueEntry(
                    item.Id,
                    item.Title,
                    item.FilePath,
                    item.Kind,
                    item.Artist,
                    item.Album,
                    item.ThumbnailPath,
                    item.PlaybackPosition,
                    item.LinkedFilePath));
                return;
            default:
                var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                if (!await Launcher.LaunchFileAsync(file))
                {
                    throw new InvalidOperationException(
                        GetResourceString("HomePage_RecentOpenRejected", "Windows could not open the selected file."));
                }
                break;
        }

        try
        {
            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.RecordPlayedAsync(item.Id);
        }
        catch
        {
            // Opening the item should still succeed if history persistence fails.
        }
    }

    private void SetRecentItemsLoadingState()
    {
        var hasItems = RecentVideoItems.Count > 0 || RecentAudioItems.Count > 0;
        RefreshRecentItemsButton.IsEnabled = false;
        SetRecentRefreshProgressVisible(hasItems);
        RecentItemsProgressRing.IsActive = !hasItems;
        SetRecentSurface(
            hasItems ? RecentContentPanel : RecentItemsProgressRing,
            animate: true);
        RecentRetryButton.Visibility = Visibility.Collapsed;
        RecentErrorInfoBar.IsOpen = false;
    }

    private void ShowRecentItemsOrEmptyState()
    {
        RefreshRecentItemsButton.IsEnabled = true;
        SetRecentRefreshProgressVisible(isVisible: false);
        RecentItemsProgressRing.IsActive = false;
        var hasVideos = RecentVideoItems.Count > 0;
        var hasAudio = RecentAudioItems.Count > 0;
        var hasItems = hasVideos || hasAudio;
        SetRecentSurface(
            hasItems ? RecentContentPanel : RecentEmptyPanel,
            animate: true);
        RecentVideoSection.Visibility = hasVideos ? Visibility.Visible : Visibility.Collapsed;
        RecentAudioSection.Visibility = hasAudio ? Visibility.Visible : Visibility.Collapsed;
        RecentRetryButton.Visibility = Visibility.Collapsed;
        RecentErrorInfoBar.IsOpen = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateRecentGridItemSize(RecentVideoItemsGrid, RecentVideoCardMinWidth, false);
            UpdateRecentGridItemSize(RecentAudioItemsGrid, RecentAudioCardMinWidth, true);
        });
        RecentEmptyIcon.Glyph = "\uE81C";
        RecentEmptyTitle.Text = _emptyTitle;
        RecentEmptyDescription.Text = _emptyDescription;
    }

    private void ShowRecentItemsErrorState(string message)
    {
        var hasItems = RecentVideoItems.Count > 0 || RecentAudioItems.Count > 0;
        RefreshRecentItemsButton.IsEnabled = true;
        SetRecentRefreshProgressVisible(isVisible: false);
        RecentItemsProgressRing.IsActive = false;
        SetRecentSurface(
            hasItems ? RecentContentPanel : RecentEmptyPanel,
            animate: true);
        RecentRetryButton.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

        var title = GetResourceString("HomePage_RecentLoadFailed", "Could not load recent items");
        if (hasItems)
        {
            RecentErrorInfoBar.Title = title;
            RecentErrorInfoBar.Message = message;
            RecentErrorInfoBar.IsOpen = true;
        }
        else
        {
            RecentErrorInfoBar.IsOpen = false;
            RecentEmptyIcon.Glyph = "\uE783";
            RecentEmptyTitle.Text = title;
            RecentEmptyDescription.Text = message;
        }
    }

    private void SetRecentRefreshProgressVisible(bool isVisible)
    {
        _ = isVisible
            ? MotionHelper.ShowAsync(
                RecentRefreshProgressBar,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0)
            : MotionHelper.HideAsync(
                RecentRefreshProgressBar,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0);
    }

    private void SetRecentSurface(UIElement target, bool animate)
    {
        if (ReferenceEquals(_recentVisibleSurface, target))
        {
            return;
        }

        var previous = _recentVisibleSurface;
        _recentVisibleSurface = target;
        _recentStateMotionCancellation?.Cancel();
        _recentStateMotionCancellation?.Dispose();
        _recentStateMotionCancellation = new CancellationTokenSource();

        if (!animate || !IsLoaded || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(
                RecentContentPanel,
                ReferenceEquals(target, RecentContentPanel));
            MotionHelper.SetVisibleInstant(
                RecentEmptyPanel,
                ReferenceEquals(target, RecentEmptyPanel));
            MotionHelper.SetVisibleInstant(
                RecentItemsProgressRing,
                ReferenceEquals(target, RecentItemsProgressRing),
                isHitTestVisible: false);
            return;
        }

        _ = MotionHelper.CrossFadeAsync(
            previous,
            target,
            MotionPreset.Standard,
            MotionDirection.Down,
            _recentStateMotionCancellation.Token);
    }

    private async Task ShowOpenErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = GetResourceString("HomePage_RecentOpenFailed", "Could not open item"),
            Content = message,
            CloseButtonText = GetResourceString("HomePage_Close", "Close")
        };
        await dialog.ShowAsync();
    }

    private async Task ShowQuickActionErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = GetResourceString("HomePage_Close", "Close")
        };
        await dialog.ShowAsync();
    }

    private string GetResourceString(string key, string fallback)
    {
        var value = _resourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

public sealed class RecentMediaItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnailSource;
    private bool _hasPresentedThumbnail;

    public RecentMediaItemViewModel(
        MediaLibraryItem item,
        string unknownArtist = "")
    {
        Id = item.Id;
        FilePath = item.Path;
        Kind = item.Kind;
        Title = string.IsNullOrWhiteSpace(item.Name)
            ? Path.GetFileNameWithoutExtension(item.Path)
            : item.Name.Trim();
        Artist = item.Kind == MediaLibraryItemKind.Audio
            ? string.IsNullOrWhiteSpace(item.Artist) ? unknownArtist : item.Artist
            : string.Empty;
        Album = item.Album;
        ThumbnailPath = item.ThumbnailPath;
        PlaybackPosition = item.PlaybackPosition;
        LinkedFilePath = item.LinkedFilePath;
        _thumbnailSource = CreateThumbnailSource(item.ThumbnailPath);
        _hasPresentedThumbnail = _thumbnailSource is not null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Title { get; }

    public string FilePath { get; }

    public string TitleToolTipText => LongTextToolTip.CreateMediaText(Title, FilePath);

    public MediaLibraryItemKind Kind { get; }

    public string Artist { get; }

    public string? Album { get; }

    public string? ThumbnailPath { get; private set; }

    public TimeSpan? PlaybackPosition { get; }

    public string? LinkedFilePath { get; }

    public ImageSource? ThumbnailSource => _thumbnailSource;

    public double ThumbnailOpacity => _thumbnailSource is not null || _hasPresentedThumbnail
        ? 1
        : 0;

    public Visibility ThumbnailVisibility => ThumbnailSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PlaceholderVisibility => ThumbnailSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void UpdateThumbnailPath(string? path)
    {
        ThumbnailPath = path;
        _thumbnailSource = CreateThumbnailSource(path);
        if (_thumbnailSource is not null)
        {
            _hasPresentedThumbnail = true;
        }
        OnPropertyChanged(nameof(ThumbnailSource));
        OnPropertyChanged(nameof(ThumbnailOpacity));
        OnPropertyChanged(nameof(ThumbnailVisibility));
        OnPropertyChanged(nameof(PlaceholderVisibility));
    }

    internal static ImageSource? CreateThumbnailSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage
            {
                DecodePixelWidth = 384,
                UriSource = new Uri(Path.GetFullPath(path))
            };
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException)
        {
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
