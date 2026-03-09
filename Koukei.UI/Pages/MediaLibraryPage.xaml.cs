using Koukei.Audio;
using Koukei.Bus.Models;
using Koukei.Bus.Services;
using Koukei.Ffmpeg;
using Koukei.Video;
using Koukei.UI.Helpers;
using Koukei.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Koukei.UI.Pages;

/// <summary>
/// Media library page.
/// </summary>
public sealed partial class MediaLibraryPage : UserControl, INotifyPropertyChanged
{
    private sealed record PreparedMediaItem(
        MediaLibraryItem Item,
        long FileSize,
        DateTimeOffset? ObservedLastModified);
    private sealed record PendingMetadataUpdate(
        MediaItemViewModel Item,
        MediaLibraryMetadataUpdate Update);
    private sealed record StorageImportCandidate(StorageFile File, string FilePath, string FileName);
    private sealed record PathImportCandidate(string FilePath, string FileName);
    private readonly record struct ImportCounts(int Added, int Skipped, int Failed)
    {
        public ImportCounts Add(ImportCounts other) => new(
            Added + other.Added,
            Skipped + other.Skipped,
            Failed + other.Failed);
    }
    private sealed record PropertyDetail(string Label, string Value, bool Wrap = false);
    private sealed record PropertySection(
        string Title,
        string Glyph,
        IReadOnlyList<PropertyDetail> Details);

    private enum MediaLibraryShortcutCommand
    {
        SelectAll,
        FocusSearch,
        AddFiles,
        AddFolder,
        Refresh,
        PlaySelected,
        ShowProperties,
        DeleteSelected,
        ClearSelection
    }

    private const uint ThumbnailSize = 320;
    private const string ThumbnailCacheFolderName = "MediaThumbnails";
    private const double GridItemMinWidth = 260;
    private const double GridItemFixedHeight = 196;
    private const double GridItemScaledTextHeight = 60;
    private const double GridItemHorizontalSpacing = 12;
    private const double GridItemVerticalSpacing = 12;
    private const double GridItemLayoutWidthReduction = 2;
    private const int MediaPageSize = 60;
    private const int FolderImportBatchSize = 32;
    private const int FolderImportQueueCapacity = 128;
    private const int MetadataRefreshBatchSize = 8;
    private const int IncrementalLoadTriggerItemCount = 12;
    private static readonly TimeSpan SearchRefreshDelay = TimeSpan.FromMilliseconds(250);

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

    private readonly List<MediaItemViewModel> _allItems = [];
    private readonly SemaphoreSlim _metadataRefreshGate = new(1, 1);
    private readonly List<MediaItemViewModel> _selectedItems = [];
    private Queue<MediaItemViewModel> _thumbnailQueue = [];
    private HashSet<Guid> _queuedThumbnailIds = [];
    private static readonly ResourceLoader StaticResourceLoader = new();
    private readonly ResourceLoader _resourceLoader = new();
    private readonly UISettings _uiSettings = new();
    private ObservableCollection<MediaItemViewModel> _mediaItems = [];
    private bool _hasMoreMediaItems;
    private bool _hasCompletedInitialLoad;
    private bool _isDeletingItems;
    private bool _isAddingToPlaybackQueue;
    private bool _isAddingToSavedPlaylist;
    private bool _isLoading;
    private bool _isLoadingNextPage;
    private bool _isIncrementalLoadSuspended;
    private bool _isPageActive;
    private bool _isRunningSelectionAction;
    private bool _isRefreshingMetadata;
    private bool _isSelectingAll;
    private bool _isSortDescending = true;
    private bool _isSyncingSelection;
    private bool _isThumbnailWorkerRunning;
    private bool _isTextScaleSubscribed;
    private bool _isSelectionActionBarVisible;
    private MediaLibraryItemKind _libraryKind = MediaLibraryItemKind.Video;
    private int _nextMediaItemOffset;
    private string _sortField = "AddedTime";
    private int _totalMediaItemCount;
    private string? _importStatusText;
    private string? _loadErrorMessage;
    private UiBreakpoint _currentBreakpoint = UiBreakpoint.Expanded;
    private CancellationTokenSource? _loadMediaItemsCts;
    private CancellationTokenSource? _importCts;
    private CancellationTokenSource? _manualMetadataRefreshCts;
    private CancellationTokenSource? _playbackQueueBuildCts;
    private CancellationTokenSource? _searchRefreshCts;
    private CancellationTokenSource? _thumbnailGenerationCts;
    private CancellationTokenSource? _initialEntranceMotionCancellation;
    private CancellationTokenSource? _libraryStateMotionCancellation;
    private UIElement? _visibleLibraryStateSurface;
    private long _playbackQueueRequestId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MediaItemViewModel> MediaItems
    {
        get => _mediaItems;
        set
        {
            if (_mediaItems == value)
            {
                return;
            }

            _mediaItems = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public Visibility HasSelection => _selectedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsEmpty => !_isLoading && MediaItems.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public MediaLibraryItemKind LibraryKind
    {
        get => _libraryKind;
        set
        {
            var normalizedKind = value == MediaLibraryItemKind.Audio
                ? MediaLibraryItemKind.Audio
                : MediaLibraryItemKind.Video;
            if (_libraryKind == normalizedKind)
            {
                return;
            }

            _libraryKind = normalizedKind;
            UpdateLibraryTitle();
            OnPropertyChanged(nameof(IsGridView));
            OnPropertyChanged(nameof(IsListView));

            if (_isPageActive)
            {
                ClearSelection();
                _ = LoadMediaItemsAsync();
            }
        }
    }

    public Visibility IsGridView => _libraryKind == MediaLibraryItemKind.Video
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility IsListView => _libraryKind == MediaLibraryItemKind.Audio
        ? Visibility.Visible
        : Visibility.Collapsed;

    public MediaLibraryPage()
    {
        InitializeComponent();
        DataContext = this;
        UpdateLibraryTitle();
        UpdateSortState();
        UpdateSelectionBar();
        MotionHelper.SetVisibleInstant(InitialLoadingOverlay, isVisible: false);
        MotionHelper.SetVisibleInstant(EmptyStatePanel, isVisible: false);
        MotionHelper.SetVisibleInstant(NoResultsPanel, isVisible: false);
        MotionHelper.SetVisibleInstant(InitialErrorPanel, isVisible: false);
        MediaGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(MediaView_PointerPressed), true);
        MediaList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(MediaView_PointerPressed), true);
        Loaded += MediaLibraryPage_Loaded;
        Unloaded += MediaLibraryPage_Unloaded;
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _currentBreakpoint = ResponsiveLayout.Resolve(e.NewSize.Width);
        var stackCommands = _currentBreakpoint is UiBreakpoint.Compact or UiBreakpoint.Medium;
        var padding = ResponsiveLayout.GetPagePadding(_currentBreakpoint);

        Grid.SetRow(SearchAndSortPanel, stackCommands ? 1 : 0);
        Grid.SetColumn(SearchAndSortPanel, stackCommands ? 0 : 1);
        Grid.SetColumnSpan(SearchAndSortPanel, stackCommands ? 2 : 1);
        SearchAndSortPanel.Margin = stackCommands ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        SearchBox.Width = stackCommands ? double.NaN : _currentBreakpoint == UiBreakpoint.Wide ? 320 : 240;
        SearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        SortButtonLabel.Visibility = _currentBreakpoint == UiBreakpoint.Compact
            ? Visibility.Collapsed
            : Visibility.Visible;

        LibraryHeader.Padding = new Thickness(padding.Left, 20, padding.Right, 0);
        ContentRegion.Padding = new Thickness(padding.Left, 0, padding.Right, 16);
        StatisticsPanel.Orientation = _currentBreakpoint is UiBreakpoint.Compact or UiBreakpoint.Medium
            ? Orientation.Vertical
            : Orientation.Horizontal;

        foreach (var item in _allItems)
        {
            item.SetLayoutBreakpoint(_currentBreakpoint);
        }

        UpdateGridItemSize();
    }

    private void UpdateLibraryTitle()
    {
        if (LibraryTitleText is null)
        {
            return;
        }

        LibraryTitleText.Text = _libraryKind == MediaLibraryItemKind.Audio
            ? GetResourceString("LibraryPage_AudioTitle", "Audio library")
            : GetResourceString("LibraryPage_VideoTitle", "Video library");
    }

    private void UpdateSortState()
    {
        if (SortButtonLabel is null)
        {
            return;
        }

        SortButtonLabel.Text = _sortField switch
        {
            "Title" => GetResourceString("LibraryPage_SortCurrent_Title", "Title"),
            "Duration" => GetResourceString("LibraryPage_SortCurrent_Duration", "Duration"),
            "FileSize" => GetResourceString("LibraryPage_SortCurrent_FileSize", "File size"),
            _ => GetResourceString("LibraryPage_SortCurrent_AddedTime", "Added")
        };
        SortDirectionIcon.Glyph = _isSortDescending ? "\uE70D" : "\uE70E";
    }

    private void ResumeCachedMediaPage()
    {
        if (_importCts is not null)
        {
            _isLoading = true;
            _isLoadingNextPage = false;
            ShowImportStatus(
                _importStatusText ??
                GetResourceString("Common_PageStatus_Working", "Working..."));
            SetInitialLoadingIndicator(isVisible: false);
            SetLoadingProgressVisible(isVisible: true);
            UpdateContentState();
            return;
        }

        var previousLoad = _loadMediaItemsCts;
        previousLoad?.Cancel();
        previousLoad?.Dispose();
        _loadMediaItemsCts = new CancellationTokenSource();
        _isLoading = false;
        _isLoadingNextPage = false;
        SetLoadingProgressVisible(isVisible: false);
        SetInitialLoadingIndicator(isVisible: false);
        UpdateContentState();
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            QueueVisibleThumbnailGeneration);
    }

    private void MediaLibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isTextScaleSubscribed)
        {
            _uiSettings.TextScaleFactorChanged += UiSettings_TextScaleFactorChanged;
            _isTextScaleSubscribed = true;
        }

        if (!_isPageActive)
        {
            _isPageActive = true;
            if (_importCts is not null || _hasCompletedInitialLoad)
            {
                ResumeCachedMediaPage();
            }
            else if (!_isLoading)
            {
                QueueInitialMediaLoad();
            }
        }

        if (App.DataInitializationException is { } exception)
        {
            _loadErrorMessage = exception.Message;
            UpdateContentState();
        }

        if (!_isLoading)
        {
            UpdateLoadStatus();
        }
    }

    private async Task LoadMediaItemsAsync()
    {
        if (App.DataInitializationException is not null || App.Services is null)
        {
            _loadErrorMessage = App.DataInitializationException?.Message ??
                GetResourceString("LibraryPage_ServiceUnavailable", "The media library is not available.");
            UpdateContentState();
            FooterStatusBar.ShowBusy(
                GetResourceString("Common_PageStatus_LoadFailed", "Failed to load"));
            return;
        }

        var previousLoad = _loadMediaItemsCts;
        previousLoad?.Cancel();
        previousLoad?.Dispose();
        var previousEntrance = _initialEntranceMotionCancellation;
        _initialEntranceMotionCancellation = null;
        previousEntrance?.Cancel();
        previousEntrance?.Dispose();
        ResetThumbnailGeneration();

        var loadCts = new CancellationTokenSource();
        _loadMediaItemsCts = loadCts;
        var hadExistingItems = _allItems.Count > 0;

        try
        {
            _isLoading = true;
            _loadErrorMessage = null;
            _isIncrementalLoadSuspended = false;
            if (_importCts is null)
            {
                FooterStatusBar.ShowBusy(
                    GetResourceString(
                        hadExistingItems
                            ? "Common_PageStatus_Refreshing"
                            : "Common_PageStatus_Loading",
                        hadExistingItems ? "Refreshing..." : "Loading..."));
            }
            SetInitialLoadingIndicator(isVisible: !hadExistingItems && _isPageActive);
            _isLoadingNextPage = false;
            SetLoadingProgressVisible(hadExistingItems && _isPageActive);
            UpdateContentState();

            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            var page = await library.SearchAsync(CreateMediaLibraryQuery(skip: 0), loadCts.Token);
            var preparedItems = await PrepareMediaItemsAsync(page.Items, loadCts.Token);
            loadCts.Token.ThrowIfCancellationRequested();

            if (!ReferenceEquals(_loadMediaItemsCts, loadCts) ||
                (!_isPageActive && _importCts is null))
            {
                return;
            }

            var loadedItems = preparedItems
                .Select(item => CreateMediaItemViewModel(
                    item.Item,
                    item.FileSize,
                    item.ObservedLastModified))
                .ToList();
            var selectedIds = _selectedItems.Select(item => item.Id).ToHashSet();
            var existingItemsById = _allItems.ToDictionary(item => item.Id);
            var reusedItemIds = new HashSet<Guid>();
            for (var index = 0; index < loadedItems.Count; index++)
            {
                var loadedItem = loadedItems[index];
                if (!existingItemsById.TryGetValue(loadedItem.Id, out var existingItem))
                {
                    continue;
                }

                existingItem.ApplySnapshot(loadedItem);
                loadedItems[index] = existingItem;
                reusedItemIds.Add(existingItem.Id);
            }

            foreach (var item in _allItems)
            {
                item.PropertyChanged -= MediaItem_PropertyChanged;
                if (!reusedItemIds.Contains(item.Id))
                {
                    item.ReleaseThumbnailSource();
                }
            }

            _allItems.Clear();
            _selectedItems.Clear();
            foreach (var item in loadedItems)
            {
                item.SetLayoutBreakpoint(_currentBreakpoint);
                RegisterMediaItem(item);
                item.IsSelected = selectedIds.Contains(item.Id);
                if (item.IsSelected)
                {
                    _selectedItems.Add(item);
                }
            }

            loadedItems.Sort(CompareMediaItems);
            ReconcileMediaItems(loadedItems);
            App.Services
                .GetRequiredService<PlaybackCoordinator>()
                .SynchronizeQueueItems(
                    loadedItems.Select(CreatePlaybackQueueEntry).ToArray());
            RestoreSelectionToActiveView();
            OnPropertyChanged(nameof(HasSelection));
            UpdateSelectionBar();

            _totalMediaItemCount = page.TotalCount;
            _nextMediaItemOffset = page.Items.Count;
            _hasMoreMediaItems = page.Items.Count > 0 && _nextMediaItemOffset < page.TotalCount;
            _hasCompletedInitialLoad = true;
            _ = PopulateMediaMetadataSafelyAsync(loadedItems, loadCts);
            UpdateStatistics();
            UpdateLoadStatus();
            DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                QueueVisibleThumbnailGeneration);
        }
        catch (OperationCanceledException)
        {
            // A newer load request superseded this one.
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadMediaItemsCts, loadCts))
            {
                _loadErrorMessage = ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_loadMediaItemsCts, loadCts))
            {
                if (_importCts is null)
                {
                    _isLoading = false;
                }

                SetInitialLoadingIndicator(isVisible: false);
                if (_importCts is null && !_isLoadingNextPage)
                {
                    SetLoadingProgressVisible(isVisible: false);
                }

                OnPropertyChanged(nameof(IsEmpty));
                UpdateContentState();
                if (_importCts is null &&
                    !string.IsNullOrWhiteSpace(_loadErrorMessage) &&
                    MediaItems.Count == 0)
                {
                    FooterStatusBar.ShowBusy(
                        GetResourceString("Common_PageStatus_LoadFailed", "Failed to load"));
                }
                else if (_importCts is null)
                {
                    FooterStatusBar.ClearOverride();
                }

                if (!hadExistingItems &&
                    MediaItems.Count > 0 &&
                    string.IsNullOrWhiteSpace(_loadErrorMessage))
                {
                    var entranceCancellation = new CancellationTokenSource();
                    var entranceToken = entranceCancellation.Token;
                    _initialEntranceMotionCancellation = entranceCancellation;
                    _ = DispatcherQueue.TryEnqueue(
                        DispatcherQueuePriority.Low,
                        () =>
                        {
                            if (!_isPageActive ||
                                !ReferenceEquals(
                                    _initialEntranceMotionCancellation,
                                    entranceCancellation))
                            {
                                return;
                            }

                            _ = MotionHelper.AnimateVisibleItemsEntranceAsync(
                                GetActiveView(),
                                cancellationToken: entranceToken);
                        });
                }
            }
        }
    }

    private void QueueInitialMediaLoad()
    {
        if (DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () =>
                {
                    if (_isPageActive && !_hasCompletedInitialLoad && !_isLoading)
                    {
                        _ = LoadMediaItemsAsync();
                    }
                }))
        {
            return;
        }

        _ = LoadMediaItemsAsync();
    }

    private void MediaLibraryPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isPageActive = false;
        if (_isTextScaleSubscribed)
        {
            _uiSettings.TextScaleFactorChanged -= UiSettings_TextScaleFactorChanged;
            _isTextScaleSubscribed = false;
        }
        if (_importCts is null)
        {
            _loadMediaItemsCts?.Cancel();
            _isLoading = false;
        }

        _isLoadingNextPage = false;
        _manualMetadataRefreshCts?.Cancel();
        _playbackQueueBuildCts?.Cancel();
        _searchRefreshCts?.Cancel();
        ResetThumbnailGeneration();

        var entranceCancellation = _initialEntranceMotionCancellation;
        _initialEntranceMotionCancellation = null;
        entranceCancellation?.Cancel();
        entranceCancellation?.Dispose();
        _libraryStateMotionCancellation?.Cancel();
        _libraryStateMotionCancellation?.Dispose();
        _libraryStateMotionCancellation = null;
        SetLoadingProgressVisible(isVisible: false);
        SetInitialLoadingIndicator(isVisible: false);
    }

    private void SetInitialLoadingIndicator(bool isVisible)
    {
        InitialLoadingRing.IsActive = isVisible;
    }

    private void UpdateContentState()
    {
        if (EmptyStatePanel is null || SearchBox is null)
        {
            return;
        }

        var hasItems = MediaItems.Count > 0;
        var hasSearch = !string.IsNullOrWhiteSpace(SearchBox.Text);
        var isInitialLoading = _isLoading && !hasItems;
        var isInitialError = !_isLoading && !hasItems && !string.IsNullOrWhiteSpace(_loadErrorMessage);

        UIElement? targetStateSurface = isInitialLoading
            ? InitialLoadingOverlay
            : isInitialError
                ? InitialErrorPanel
                : !hasItems && !hasSearch
                    ? EmptyStatePanel
                    : !hasItems && hasSearch
                        ? NoResultsPanel
                        : null;
        SetLibraryStateSurface(targetStateSurface, animate: _isPageActive);
        InitialErrorTitle.Text = GetResourceString(
            "LibraryPage_FailedToLoadDialog_Title",
            "Failed to load library");
        InitialErrorDescription.Text = _loadErrorMessage ?? string.Empty;

        var showRefreshError = !_isLoading && hasItems && !string.IsNullOrWhiteSpace(_loadErrorMessage);
        RefreshErrorInfoBar.Title = GetResourceString(
            "LibraryPage_RefreshFailed",
            "Could not refresh the library");
        RefreshErrorInfoBar.Message = _loadErrorMessage ?? string.Empty;
        RefreshErrorInfoBar.IsOpen = showRefreshError;
        RefreshButton.IsEnabled = !_isLoading;
        SearchBox.IsEnabled = !isInitialLoading;
        SortButton.IsEnabled = !isInitialLoading;
    }

    private void SetLoadingProgressVisible(bool isVisible)
    {
        _ = isVisible
            ? MotionHelper.ShowAsync(
                LoadingProgress,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0)
            : MotionHelper.HideAsync(
                LoadingProgress,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0);
    }

    private void ShowImportStatus(string text)
    {
        _importStatusText = text;
        FooterStatusBar.ShowBusy(text);
        ImportStatusTextBlock.Text = text;
        ImportProgressRing.IsActive = true;
        ImportInfoBar.IsOpen = true;
        AddFilesButton.IsEnabled = false;
        AddFolderButton.IsEnabled = false;
    }

    private void HideImportStatus()
    {
        _importStatusText = null;
        ImportProgressRing.IsActive = false;
        ImportInfoBar.IsOpen = false;
        AddFilesButton.IsEnabled = true;
        AddFolderButton.IsEnabled = true;
    }

    private void SetLibraryStateSurface(UIElement? target, bool animate)
    {
        if (ReferenceEquals(_visibleLibraryStateSurface, target))
        {
            return;
        }

        var previous = _visibleLibraryStateSurface;
        _visibleLibraryStateSurface = target;
        _libraryStateMotionCancellation?.Cancel();
        _libraryStateMotionCancellation?.Dispose();
        _libraryStateMotionCancellation = new CancellationTokenSource();

        if (!animate || !_isPageActive || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(
                InitialLoadingOverlay,
                ReferenceEquals(target, InitialLoadingOverlay));
            MotionHelper.SetVisibleInstant(
                EmptyStatePanel,
                ReferenceEquals(target, EmptyStatePanel));
            MotionHelper.SetVisibleInstant(
                NoResultsPanel,
                ReferenceEquals(target, NoResultsPanel));
            MotionHelper.SetVisibleInstant(
                InitialErrorPanel,
                ReferenceEquals(target, InitialErrorPanel));
            return;
        }

        _ = MotionHelper.CrossFadeAsync(
            previous,
            target,
            MotionPreset.Standard,
            MotionDirection.Down,
            _libraryStateMotionCancellation.Token);
    }

    private async Task LoadNextMediaPageAsync(
        CancellationTokenSource loadCts,
        bool isInitialLoad = false,
        bool suppressFeedback = false)
    {
        if (!ReferenceEquals(_loadMediaItemsCts, loadCts) ||
            loadCts.IsCancellationRequested ||
            _isLoadingNextPage ||
            !_hasMoreMediaItems ||
            !_isPageActive ||
            App.Services is null)
        {
            return;
        }

        _isLoadingNextPage = true;
        if (!suppressFeedback)
        {
            SetLoadingProgressVisible(isVisible: true);
        }
        try
        {
            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            var page = await library.SearchAsync(
                CreateMediaLibraryQuery(_nextMediaItemOffset),
                loadCts.Token);
            var preparedItems = await PrepareMediaItemsAsync(page.Items, loadCts.Token);

            loadCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadMediaItemsCts, loadCts) || !_isPageActive)
            {
                return;
            }

            var loadedItems = preparedItems
                .Select(item => CreateMediaItemViewModel(
                    item.Item,
                    item.FileSize,
                    item.ObservedLastModified))
                .ToList();
            await AppendLoadedMediaItemsAsync(loadedItems, loadCts.Token);
            _ = PopulateMediaMetadataSafelyAsync(loadedItems, loadCts);

            _totalMediaItemCount = page.TotalCount;
            _nextMediaItemOffset += page.Items.Count;
            _hasMoreMediaItems = page.Items.Count > 0 &&
                _nextMediaItemOffset < page.TotalCount;
            _isIncrementalLoadSuspended = false;
            if (isInitialLoad)
            {
                _hasCompletedInitialLoad = true;
            }

            UpdateStatistics();
            UpdateContentState();
            if (!suppressFeedback)
            {
                UpdateLoadStatus();
            }
        }
        finally
        {
            if (ReferenceEquals(_loadMediaItemsCts, loadCts))
            {
                _isLoadingNextPage = false;
                if (!suppressFeedback)
                {
                    SetLoadingProgressVisible(isVisible: false);
                }

                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private async Task PopulateMediaMetadataSafelyAsync(
        IReadOnlyList<MediaItemViewModel> items,
        CancellationTokenSource loadCts)
    {
        try
        {
            await RefreshMediaMetadataAsync(
                items,
                forceRefresh: false,
                loadCts.Token);

            if (_sortField == "Title" &&
                ReferenceEquals(_loadMediaItemsCts, loadCts) &&
                !loadCts.IsCancellationRequested &&
                _isPageActive)
            {
                RefreshMediaList();
            }
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to populate media metadata: {ex.Message}");
        }
    }

    private async Task<ImportCounts> RefreshMediaMetadataAsync(
        IReadOnlyList<MediaItemViewModel> items,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await _metadataRefreshGate.WaitAsync(cancellationToken);
        try
        {
            var pendingUpdates = new List<PendingMetadataUpdate>(MetadataRefreshBatchSize);
            var updatedCount = 0;
            var failedCount = 0;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!forceRefresh && !NeedsMetadataRefresh(item))
                {
                    continue;
                }

                try
                {
                    var mediaInfo = await TryGetVideoMediaInfoAsync(item.FilePath, cancellationToken)
                        ?? throw new InvalidDataException(
                            $"FFmpeg could not read media information from '{item.FilePath}'.");
                    var audioMetadata = item.Kind == MediaLibraryItemKind.Audio
                        ? await TryGetAudioMetadataAsync(item.FilePath, cancellationToken)
                        : null;
                    cancellationToken.ThrowIfCancellationRequested();
                    pendingUpdates.Add(new PendingMetadataUpdate(
                        item,
                        CreateMediaLibraryMetadataUpdate(item, mediaInfo, audioMetadata)));

                    if (pendingUpdates.Count >= MetadataRefreshBatchSize)
                    {
                        var persistCounts = await TryPersistMetadataUpdatesAsync(
                            pendingUpdates,
                            cancellationToken);
                        updatedCount += persistCounts.Added;
                        failedCount += persistCounts.Failed;
                        pendingUpdates.Clear();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to refresh metadata for '{item.FilePath}': {ex.Message}");
                    failedCount++;
                }
            }

            if (pendingUpdates.Count > 0)
            {
                var persistCounts = await TryPersistMetadataUpdatesAsync(
                    pendingUpdates,
                    cancellationToken);
                updatedCount += persistCounts.Added;
                failedCount += persistCounts.Failed;
            }

            return new ImportCounts(updatedCount, Skipped: 0, Failed: failedCount);
        }
        finally
        {
            _metadataRefreshGate.Release();
        }
    }

    private static MediaLibraryMetadataUpdate CreateMediaLibraryMetadataUpdate(
        MediaItemViewModel item,
        VideoMediaInfo mediaInfo,
        AudioFileMetadata? audioMetadata)
    {
        var resolvedTitle = FirstNonEmptyOrNull(
                audioMetadata?.Title,
                mediaInfo.Title,
                item.Title,
                Path.GetFileNameWithoutExtension(item.FilePath))
            ?? Path.GetFileNameWithoutExtension(item.FilePath);
        return new MediaLibraryMetadataUpdate
        {
            Id = item.Id,
            Metadata = new NewMediaLibraryItem
            {
                Name = resolvedTitle,
                Path = item.FilePath,
                Extension = item.Extension,
                ContainerFormat = audioMetadata?.FormatName ?? mediaInfo.ContainerFormat,
                FileSize = mediaInfo.FileSize,
                DateCreated = item.DateAdded,
                LastModified = TryGetLastModified(item.FilePath),
                Duration = audioMetadata?.Duration ?? mediaInfo.Duration,
                Width = mediaInfo.Video?.Width,
                Height = mediaInfo.Video?.Height,
                Kind = item.Kind,
                Artist = audioMetadata?.Artist,
                Album = audioMetadata?.Album,
                Streams = CreateImportedStreams(mediaInfo, audioMetadata)
            }
        };
    }

    private static async Task<int> PersistMetadataUpdatesAsync(
        IReadOnlyList<PendingMetadataUpdate> pendingUpdates,
        CancellationToken cancellationToken)
    {
        if (pendingUpdates.Count == 0 || App.Services is null)
        {
            return 0;
        }

        using var scope = App.Services.CreateScope();
        var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
        await library.UpdateMetadataAsync(
            pendingUpdates.Select(pending => pending.Update).ToList(),
            cancellationToken);
        var refreshedAt = DateTimeOffset.UtcNow;
        foreach (var pending in pendingUpdates)
        {
            pending.Item.ApplyPersistedMetadata(pending.Update.Metadata, refreshedAt);
        }

        scope.ServiceProvider
            .GetRequiredService<PlaybackCoordinator>()
            .UpdateQueueItemMetadata(
                pendingUpdates
                    .Select(pending => new PlaybackQueueMetadataUpdate(
                        pending.Update.Metadata.Path,
                        pending.Update.Metadata.Name,
                        pending.Update.Metadata.Artist,
                        pending.Update.Metadata.Album,
                        pending.Item.ThumbnailPath))
                    .ToList());

        return pendingUpdates.Count;
    }

    private static async Task<ImportCounts> TryPersistMetadataUpdatesAsync(
        IReadOnlyList<PendingMetadataUpdate> pendingUpdates,
        CancellationToken cancellationToken)
    {
        try
        {
            var updatedCount = await PersistMetadataUpdatesAsync(
                pendingUpdates,
                cancellationToken);
            return new ImportCounts(updatedCount, Skipped: 0, Failed: 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to persist metadata refresh batch: {ex.Message}");
            return new ImportCounts(Added: 0, Skipped: 0, Failed: pendingUpdates.Count);
        }
    }

    private static Task<IReadOnlyList<PreparedMediaItem>> PrepareMediaItemsAsync(
        IReadOnlyList<MediaLibraryItem> items,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<PreparedMediaItem>>(() =>
        {
            var preparedItems = new List<PreparedMediaItem>(items.Count);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var observedLastModified = TryGetLastModified(item.Path);
                preparedItems.Add(new PreparedMediaItem(
                    item,
                    item.FileSize is >= 0 ? item.FileSize.Value : TryGetFileSize(item.Path),
                    observedLastModified));
            }

            return preparedItems;
        }, cancellationToken);
    }

    private static bool NeedsMetadataRefresh(MediaItemViewModel item)
    {
        if (item.ObservedLastModified is null)
        {
            return false;
        }

        if (item.MetadataRefreshedAt is null || item.LastModified is null)
        {
            return true;
        }

        var expectedStreamKind = item.Kind switch
        {
            MediaLibraryItemKind.Video => MediaLibraryStreamKind.Video,
            MediaLibraryItemKind.Audio => MediaLibraryStreamKind.Audio,
            _ => MediaLibraryStreamKind.Unknown
        };
        if (expectedStreamKind != MediaLibraryStreamKind.Unknown &&
            !item.Streams.Any(stream =>
                stream.Kind == expectedStreamKind &&
                !string.IsNullOrWhiteSpace(stream.Codec)))
        {
            return true;
        }

        return Math.Abs(
            (item.ObservedLastModified.Value - item.LastModified.Value).TotalSeconds) > 1;
    }

    private MediaLibraryQuery CreateMediaLibraryQuery(int skip)
    {
        var keyword = SearchBox?.Text?.Trim();
        return new MediaLibraryQuery
        {
            SearchText = !string.IsNullOrWhiteSpace(keyword) ? keyword : null,
            Kind = _libraryKind,
            Skip = skip,
            Take = MediaPageSize,
            SortField = _sortField switch
            {
                "Title" => MediaLibrarySortField.Name,
                _ => MediaLibrarySortField.DateCreated
            },
            SortDirection = _isSortDescending
                ? SortDirection.Descending
                : SortDirection.Ascending
        };
    }

    private void ResetThumbnailGeneration()
    {
        _thumbnailGenerationCts?.Cancel();
        _thumbnailGenerationCts = null;
        _thumbnailQueue = [];
        _queuedThumbnailIds = [];
        _isThumbnailWorkerRunning = false;
    }

    private void PauseMediaBackgroundWorkForImport()
    {
        _loadMediaItemsCts?.Cancel();
        ResetThumbnailGeneration();
        SetInitialLoadingIndicator(isVisible: false);
    }

    private bool ShouldResumeMediaBackgroundWorkAfterImport()
    {
        return _isPageActive &&
            (_loadMediaItemsCts?.IsCancellationRequested ?? false);
    }

    private void QueueMissingThumbnailGeneration(IReadOnlyList<MediaItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item.Kind is (MediaLibraryItemKind.Video or MediaLibraryItemKind.Audio or MediaLibraryItemKind.Image) &&
                _queuedThumbnailIds.Add(item.Id))
            {
                _thumbnailQueue.Enqueue(item);
            }
        }

        if (_thumbnailQueue.Count == 0 || _isThumbnailWorkerRunning)
        {
            return;
        }

        var thumbnailCts = new CancellationTokenSource();
        _thumbnailGenerationCts = thumbnailCts;
        _isThumbnailWorkerRunning = true;
        _ = GenerateMissingThumbnailsAsync(
            _thumbnailQueue,
            _queuedThumbnailIds,
            thumbnailCts);
    }

    private async Task GenerateMissingThumbnailsAsync(
        Queue<MediaItemViewModel> thumbnailQueue,
        HashSet<Guid> queuedThumbnailIds,
        CancellationTokenSource thumbnailCts)
    {
        var cancellationToken = thumbnailCts.Token;

        try
        {
            if (App.Services is null)
            {
                return;
            }

            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            var playbackCoordinator = App.Services.GetRequiredService<PlaybackCoordinator>();

            while (thumbnailQueue.TryDequeue(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var needsThumbnail = await Task.Run(
                        () => NeedsThumbnailGeneration(item),
                        cancellationToken);
                    if (!needsThumbnail)
                    {
                        continue;
                    }

                    var thumbnailPath = await TryCreateThumbnailAsync(item, cancellationToken);
                    if (string.IsNullOrWhiteSpace(thumbnailPath))
                    {
                        if (item.Kind == MediaLibraryItemKind.Video &&
                            !string.IsNullOrWhiteSpace(item.ThumbnailPath) &&
                            !string.Equals(
                                item.ThumbnailPath,
                                GetVideoThumbnailCachePath(item.FilePath),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            item.ThumbnailPath = null;
                            await library.SetThumbnailAsync(item.Id, null, cancellationToken);
                            playbackCoordinator.UpdateQueueItemThumbnail(
                                item.Id,
                                item.FilePath,
                                thumbnailPath: null);
                        }

                        if (item.Kind == MediaLibraryItemKind.Audio &&
                            !string.IsNullOrWhiteSpace(item.ThumbnailPath) &&
                            File.Exists(GetAudioThumbnailMissingMarkerPath(item.FilePath)))
                        {
                            item.ThumbnailPath = null;
                            await library.SetThumbnailAsync(item.Id, null, cancellationToken);
                            playbackCoordinator.UpdateQueueItemThumbnail(
                                item.Id,
                                item.FilePath,
                                thumbnailPath: null);
                        }

                        continue;
                    }

                    item.ThumbnailPath = thumbnailPath;
                    await library.SetThumbnailAsync(item.Id, thumbnailPath, cancellationToken);
                    playbackCoordinator.UpdateQueueItemThumbnail(
                        item.Id,
                        item.FilePath,
                        thumbnailPath);
                }
                finally
                {
                    queuedThumbnailIds.Remove(item.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The page navigated away or a newer load started.
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Failed to generate missing media thumbnails:");
            Debug.WriteLine(ex);
        }
        finally
        {
            if (ReferenceEquals(_thumbnailGenerationCts, thumbnailCts))
            {
                _thumbnailGenerationCts = null;
                _isThumbnailWorkerRunning = false;
            }

            thumbnailCts.Dispose();

            if (_thumbnailQueue.Count > 0 && !_isThumbnailWorkerRunning)
            {
                QueueMissingThumbnailGeneration([]);
            }
        }
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        _ = AddFilesButtonClickAsync();
    }

    private async Task AddFilesButtonClickAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = _libraryKind == MediaLibraryItemKind.Audio
                    ? PickerLocationId.MusicLibrary
                    : PickerLocationId.VideosLibrary
            };

            foreach (var extension in GetCurrentMediaExtensions().OrderBy(static extension => extension))
            {
                picker.FileTypeFilter.Add(extension);
            }

            if (!TryInitializePickerWithMainWindow(picker, out var pickerError))
            {
                await ShowErrorDialog(
                    GetResourceString("LibraryPage_FailedToAddFilesDialog_Title", "Failed to add files"),
                    pickerError);
                return;
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                await AddMediaItemsAsync(files);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToAddFilesDialog_Title", "Failed to add files"), ex.Message);
        }
    }

    private void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _ = AddFolderButtonClickAsync();
    }

    private async Task AddFolderButtonClickAsync()
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = _libraryKind == MediaLibraryItemKind.Audio
                    ? PickerLocationId.MusicLibrary
                    : PickerLocationId.VideosLibrary
            };
            picker.FileTypeFilter.Add("*");

            if (!TryInitializePickerWithMainWindow(picker, out var pickerError))
            {
                await ShowErrorDialog(
                    GetResourceString("LibraryPage_FailedToAddFolderDialog_Title", "Failed to add folder"),
                    pickerError);
                return;
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                await ScanFolderAsync(folder.Path);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToAddFolderDialog_Title", "Failed to add folder"), ex.Message);
        }
    }

    private async Task ScanFolderAsync(string folderPath)
    {
        _importCts?.Cancel();
        var importCts = new CancellationTokenSource();
        _importCts = importCts;
        var cancellationToken = importCts.Token;
        Task? producer = null;
        PauseMediaBackgroundWorkForImport();

        try
        {
            _isLoading = true;
            ShowImportStatus(
                GetResourceString(
                    "Common_PageStatus_Scanning",
                    "Scanning folder..."));
            SetLoadingProgressVisible(isVisible: true);
            var extensions = GetCurrentMediaExtensions();
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(FolderImportQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            producer = ProduceMediaFilePathsAsync(
                folderPath,
                extensions,
                channel.Writer,
                cancellationToken);
            var batch = new List<string>(FolderImportBatchSize);
            var counts = new ImportCounts();
            var foundCount = 0;

            await foreach (var mediaPath in channel.Reader.ReadAllAsync(cancellationToken))
            {
                foundCount++;
                batch.Add(mediaPath);
                if (batch.Count < FolderImportBatchSize)
                {
                    continue;
                }

                counts = counts.Add(await ImportMediaPathBatchAsync(
                    batch,
                    foundCount - batch.Count,
                    importCts));
                batch.Clear();
            }

            await producer;
            if (batch.Count > 0)
            {
                counts = counts.Add(await ImportMediaPathBatchAsync(
                    batch,
                    foundCount - batch.Count,
                    importCts));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (foundCount == 0)
            {
                await ShowInfoDialog(
                    GetResourceString("LibraryPage_NoFilesDialog_Title", "No media items"),
                    GetResourceString("LibraryPage_NoFilesDialog_Message", "There are no media items in the selected folder"));
                FooterStatusBar.ClearOverride();
                return;
            }

            await LoadMediaItemsAsync();
            FooterStatusBar.ShowTransient(string.Format(
                counts.Failed == 0
                    ? GetResourceString("LibraryPage_Status_Added", "Added {0} files, skipped {1} duplicates")
                    : GetResourceString("LibraryPage_Status_AddedWithFailures", "Added {0} files, skipped {1} duplicates, failed {2}"),
                counts.Added,
                counts.Skipped,
                counts.Failed));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FooterStatusBar.ClearOverride();
        }
        catch (Exception ex)
        {
            FooterStatusBar.ClearOverride();
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToScanFolderDialog_Title", "Failed to scan folder"), ex.Message);
        }
        finally
        {
            var ownsImport = ReferenceEquals(_importCts, importCts);
            var shouldResumeBackgroundWork = ownsImport &&
                ShouldResumeMediaBackgroundWorkAfterImport();
            importCts.Cancel();
            if (producer is not null)
            {
                try
                {
                    await producer;
                }
                catch
                {
                    // Cancellation or enumeration failures are handled by the scan path.
                }
            }

            if (ownsImport)
            {
                _importCts = null;
                _isLoading = false;
                SetLoadingProgressVisible(isVisible: false);
                HideImportStatus();
            }

            importCts.Dispose();
            if (shouldResumeBackgroundWork)
            {
                _ = LoadMediaItemsAsync();
            }
        }
    }

    private static Task ProduceMediaFilePathsAsync(
        string folderPath,
        IReadOnlySet<string> extensions,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            Exception? completionError = null;
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(
                    folderPath,
                    "*.*",
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (extensions.Contains(Path.GetExtension(filePath)))
                    {
                        await writer.WriteAsync(filePath, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                completionError = ex;
            }
            finally
            {
                writer.TryComplete(completionError);
            }
        }, CancellationToken.None);
    }

    private async Task AddMediaItemsAsync(IReadOnlyList<StorageFile> files)
    {
        _importCts?.Cancel();
        var importCts = new CancellationTokenSource();
        _importCts = importCts;
        var cancellationToken = importCts.Token;
        PauseMediaBackgroundWorkForImport();

        try
        {
            _isLoading = true;
            ShowImportStatus(
                GetResourceString("Common_PageStatus_Working", "Working..."));
            SetLoadingProgressVisible(isVisible: true);

            var candidates = new List<StorageImportCandidate>(files.Count);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skippedCount = 0;
            var failedCount = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = GetStorageFileName(file);
                var filePath = GetStorageFilePath(file);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    failedCount++;
                    continue;
                }

                if (!seenPaths.Add(filePath) || IsDuplicate(filePath, fileName))
                {
                    skippedCount++;
                    continue;
                }

                candidates.Add(new StorageImportCandidate(file, filePath, fileName));
            }

            var counts = await ImportCandidatesAsync(
                candidates,
                static candidate => candidate.FilePath,
                static candidate => candidate.FileName,
                static (candidate, token) => CreateMediaItemAsync(
                    candidate.File,
                    candidate.FilePath,
                    candidate.FileName,
                    token),
                importCts,
                progressOffset: 0,
                progressTotal: files.Count,
                initialSkipped: skippedCount,
                initialFailed: failedCount);
            cancellationToken.ThrowIfCancellationRequested();
            await LoadMediaItemsAsync();

            FooterStatusBar.ShowTransient(string.Format(
                counts.Failed == 0
                    ? GetResourceString("LibraryPage_Status_Added", "Added {0} files, skipped {1} duplicates")
                    : GetResourceString("LibraryPage_Status_AddedWithFailures", "Added {0} files, skipped {1} duplicates, failed {2}"),
                counts.Added,
                counts.Skipped,
                counts.Failed));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FooterStatusBar.ClearOverride();
        }
        catch (Exception ex)
        {
            FooterStatusBar.ClearOverride();
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToAddFilesDialog_Title", "Failed to add files"), ex.Message);
        }
        finally
        {
            var ownsImport = ReferenceEquals(_importCts, importCts);
            var shouldResumeBackgroundWork = ownsImport &&
                ShouldResumeMediaBackgroundWorkAfterImport();
            if (ownsImport)
            {
                _importCts = null;
                _isLoading = false;
                SetLoadingProgressVisible(isVisible: false);
                HideImportStatus();
            }

            importCts.Dispose();
            if (shouldResumeBackgroundWork)
            {
                _ = LoadMediaItemsAsync();
            }
        }
    }

    private async Task<ImportCounts> ImportMediaPathBatchAsync(
        IReadOnlyList<string> filePaths,
        int processedBeforeBatch,
        CancellationTokenSource importCts)
    {
        var cancellationToken = importCts.Token;
        var candidates = new List<PathImportCandidate>(filePaths.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedCount = 0;
        var failedCount = 0;
        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                failedCount++;
                continue;
            }

            if (!seenPaths.Add(filePath) || IsDuplicate(filePath, fileName))
            {
                skippedCount++;
                continue;
            }

            candidates.Add(new PathImportCandidate(filePath, fileName));
        }

        return await ImportCandidatesAsync(
            candidates,
            static candidate => candidate.FilePath,
            static candidate => candidate.FileName,
            (candidate, token) => CreateMediaItemAsync(candidate.FilePath, token),
            importCts,
            progressOffset: processedBeforeBatch,
            progressTotal: processedBeforeBatch + filePaths.Count,
            initialSkipped: skippedCount,
            initialFailed: failedCount);
    }

    private async Task<ImportCounts> ImportCandidatesAsync<TCandidate>(
        IReadOnlyList<TCandidate> candidates,
        Func<TCandidate, string> getPath,
        Func<TCandidate, string> getName,
        Func<TCandidate, CancellationToken, Task<MediaItemViewModel>> createItemAsync,
        CancellationTokenSource importCts,
        int progressOffset,
        int progressTotal,
        int initialSkipped,
        int initialFailed)
    {
        var services = App.Services ?? throw new InvalidOperationException(
            GetResourceString("LibraryPage_ServiceUnavailable", "The media library is not available."));
        var cancellationToken = importCts.Token;
        var libraryKind = _libraryKind;
        var progressFormat = GetResourceString(
            "LibraryPage_Status_ProcessingFiles",
            "Processing {0}/{1}: {2}");
        var addedCount = 0;
        var skippedCount = initialSkipped;
        var failedCount = initialFailed;
        var processedCount = initialSkipped + initialFailed;
        var createdItems = new List<MediaItemViewModel>(candidates.Count);

        using var scope = services.CreateScope();
        var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
        var existingPaths = await library.GetExistingPathsAsync(
            candidates.Select(getPath).ToArray(),
            cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = getPath(candidate);
            try
            {
                if (existingPaths.Contains(filePath))
                {
                    skippedCount++;
                    continue;
                }

                var item = await createItemAsync(candidate, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Kind != libraryKind)
                {
                    skippedCount++;
                    continue;
                }

                createdItems.Add(item);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to import '{filePath}': {ex.Message}");
                failedCount++;
            }
            finally
            {
                processedCount++;
                QueueImportProgress(
                    importCts,
                    progressFormat,
                    progressOffset + processedCount,
                    progressTotal,
                    getName(candidate));
            }
        }

        if (createdItems.Count > 0)
        {
            try
            {
                var result = await library.ImportAsync(
                    createdItems.Select(CreateNewMediaLibraryItem).ToList(),
                    cancellationToken);
                App.Services
                    .GetRequiredService<PlaybackCoordinator>()
                    .SynchronizeQueueItems(
                        result.AddedItems
                            .Select(item => new PlaybackQueueEntry(
                                item.Id,
                                item.Name,
                                item.Path,
                                item.Kind,
                                item.Artist,
                                item.Album,
                                item.ThumbnailPath,
                                item.PlaybackPosition))
                            .ToArray());
                var persistedByPath = result.AddedItems.ToDictionary(
                    item => item.Path,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var item in createdItems)
                {
                    if (persistedByPath.TryGetValue(item.FilePath, out var persisted))
                    {
                        item.Id = persisted.Id;
                        item.ThumbnailPath = persisted.ThumbnailPath ?? item.ThumbnailPath;
                        addedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to persist media import batch: {ex.Message}");
                failedCount += createdItems.Count;
            }
        }

        return new ImportCounts(addedCount, skippedCount, failedCount);
    }

    private void QueueImportProgress(
        CancellationTokenSource importCts,
        string format,
        int processed,
        int total,
        string fileName)
    {
        var status = string.Format(format, Math.Min(processed, total), total, fileName);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_importCts, importCts))
            {
                ShowImportStatus(status);
            }
        });
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadMediaItemsAsync();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        _searchRefreshCts?.Cancel();
        _ = LoadMediaItemsAsync();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ScheduleSearchRefresh();
        }
    }

    private void ScheduleSearchRefresh()
    {
        _loadMediaItemsCts?.Cancel();
        _searchRefreshCts?.Cancel();
        _searchRefreshCts?.Dispose();
        var searchCts = new CancellationTokenSource();
        _searchRefreshCts = searchCts;
        _ = RefreshSearchAfterDelayAsync(searchCts);
    }

    private async Task RefreshSearchAfterDelayAsync(CancellationTokenSource searchCts)
    {
        try
        {
            await Task.Delay(SearchRefreshDelay, searchCts.Token);
            if (ReferenceEquals(_searchRefreshCts, searchCts))
            {
                await LoadMediaItemsAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_searchRefreshCts, searchCts))
            {
                _searchRefreshCts = null;
            }

            searchCts.Dispose();
        }
    }

    private void SortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item || item.Tag is not string value)
        {
            return;
        }

        if (value is "Ascending" or "Descending")
        {
            _isSortDescending = value == "Descending";
        }
        else
        {
            _sortField = value;
        }

        UpdateSortState();
        _ = LoadMediaItemsAsync();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _ = DeleteItemsAsync(_selectedItems.ToList());
    }

    private async void PlaySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunningSelectionAction || App.Services is null)
        {
            return;
        }

        var selectedItems = GetOrderedSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        _isRunningSelectionAction = true;
        UpdateSelectionBar();
        try
        {
            var playlist = selectedItems
                .Where(item => item.Kind == _libraryKind && File.Exists(item.FilePath))
                .Select(CreatePlaybackQueueEntry)
                .ToList();
            if (playlist.Count == 0)
            {
                return;
            }

            var coordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
            await coordinator.PlayPlaylistAsync(playlist);

            FooterStatusBar.ShowTransient(string.Format(
                GetResourceString("LibraryPage_Status_PlayingSelected", "Playing {0} selected items"),
                playlist.Count));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(
                GetResourceString("LibraryPage_PlaySelectedFailed", "Could not play the selected items"),
                ex.Message);
        }
        finally
        {
            _isRunningSelectionAction = false;
            UpdateSelectionBar();
        }
    }

    private async void QueueSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunningSelectionAction)
        {
            return;
        }

        _isRunningSelectionAction = true;
        UpdateSelectionBar();
        try
        {
            await AddItemsToPlaylistAsync(GetOrderedSelectedItems());
        }
        finally
        {
            _isRunningSelectionAction = false;
            UpdateSelectionBar();
        }
    }

    private async void AddSelectedToSavedPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunningSelectionAction)
        {
            return;
        }

        _isRunningSelectionAction = true;
        UpdateSelectionBar();
        try
        {
            await AddItemsToSavedPlaylistAsync(GetOrderedSelectedItems());
        }
        finally
        {
            _isRunningSelectionAction = false;
            UpdateSelectionBar();
        }
    }

    private List<MediaItemViewModel> GetOrderedSelectedItems()
    {
        var selected = _selectedItems.ToHashSet();
        return MediaItems.Where(selected.Contains).ToList();
    }

    private void UpdateSelectionBar()
    {
        if (SelectedCountText is null || SelectionActionBar is null)
        {
            return;
        }

        SelectedCountText.Text = _selectedItems.Count.ToString();
        SelectionActionBar.IsEnabled = !_isRunningSelectionAction && !_isDeletingItems;
        UpdateFooterSummary();
        var shouldShowSelectionActions = _selectedItems.Count > 0;
        if (_isSelectionActionBarVisible == shouldShowSelectionActions)
        {
            return;
        }

        _isSelectionActionBarVisible = shouldShowSelectionActions;
        if (!IsLoaded || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(SelectionActionBar, shouldShowSelectionActions);
            return;
        }

        _ = shouldShowSelectionActions
            ? MotionHelper.ShowAsync(
                SelectionActionBar,
                MotionPreset.Fast,
                MotionDirection.Down,
                distance: 4)
            : MotionHelper.HideAsync(
                SelectionActionBar,
                MotionPreset.Fast,
                MotionDirection.Up,
                distance: 4);
    }

    private void MediaLibraryKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        var command = ResolveMediaLibraryShortcut(sender);
        if (command is null)
        {
            return;
        }

        args.Handled = TryExecuteMediaLibraryShortcut(command.Value);
    }

    private static MediaLibraryShortcutCommand? ResolveMediaLibraryShortcut(
        KeyboardAccelerator accelerator)
    {
        return (accelerator.Key, accelerator.Modifiers) switch
        {
            (VirtualKey.A, VirtualKeyModifiers.Control) => MediaLibraryShortcutCommand.SelectAll,
            (VirtualKey.F, VirtualKeyModifiers.Control) => MediaLibraryShortcutCommand.FocusSearch,
            (VirtualKey.O, VirtualKeyModifiers.Control) => MediaLibraryShortcutCommand.AddFiles,
            (VirtualKey.O, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift) => MediaLibraryShortcutCommand.AddFolder,
            (VirtualKey.F5, VirtualKeyModifiers.None) => MediaLibraryShortcutCommand.Refresh,
            (VirtualKey.Enter, VirtualKeyModifiers.None) => MediaLibraryShortcutCommand.PlaySelected,
            (VirtualKey.Enter, VirtualKeyModifiers.Menu) => MediaLibraryShortcutCommand.ShowProperties,
            (VirtualKey.Delete, VirtualKeyModifiers.None) => MediaLibraryShortcutCommand.DeleteSelected,
            (VirtualKey.Escape, VirtualKeyModifiers.None) => MediaLibraryShortcutCommand.ClearSelection,
            _ => null
        };
    }

    private bool TryExecuteMediaLibraryShortcut(MediaLibraryShortcutCommand command)
    {
        switch (command)
        {
            case MediaLibraryShortcutCommand.SelectAll:
                if (IsTextInputFocused() || _isDeletingItems)
                {
                    return false;
                }

                _ = SelectAllMediaItemsAsync();
                return true;

            case MediaLibraryShortcutCommand.FocusSearch:
                if (!SearchBox.IsEnabled)
                {
                    return false;
                }

                SearchBox.Focus(FocusState.Keyboard);
                DispatcherQueue.TryEnqueue(() =>
                    FindVisualDescendant<TextBox>(SearchBox)?.SelectAll());
                return true;

            case MediaLibraryShortcutCommand.AddFiles:
                if (_isLoading || _isDeletingItems)
                {
                    return false;
                }

                _ = AddFilesButtonClickAsync();
                return true;

            case MediaLibraryShortcutCommand.AddFolder:
                if (_isLoading || _isDeletingItems)
                {
                    return false;
                }

                _ = AddFolderButtonClickAsync();
                return true;

            case MediaLibraryShortcutCommand.Refresh:
                if (_isLoading || !RefreshButton.IsEnabled)
                {
                    return false;
                }

                _ = LoadMediaItemsAsync();
                return true;

            case MediaLibraryShortcutCommand.PlaySelected:
                if (IsItemCommandFocusBlocked() || GetKeyboardTargetItem() is not { } playItem)
                {
                    return false;
                }

                _ = PlayAsync(playItem);
                return true;

            case MediaLibraryShortcutCommand.ShowProperties:
                if (IsItemCommandFocusBlocked() || GetKeyboardTargetItem() is not { } propertyItem)
                {
                    return false;
                }

                _ = ShowPropertiesDialog(propertyItem);
                return true;

            case MediaLibraryShortcutCommand.DeleteSelected:
                if (IsTextInputFocused() || _isSelectingAll || _selectedItems.Count == 0)
                {
                    return false;
                }

                _ = DeleteItemsAsync(_selectedItems.ToList());
                return true;

            case MediaLibraryShortcutCommand.ClearSelection:
                if (IsTextInputFocused() || _selectedItems.Count == 0)
                {
                    return false;
                }

                ClearSelection();
                return true;

            default:
                return false;
        }
    }

    private MediaItemViewModel? GetKeyboardTargetItem()
    {
        var activeView = GetActiveView();
        return activeView.SelectedItem as MediaItemViewModel ??
            activeView.SelectedItems.OfType<MediaItemViewModel>().FirstOrDefault();
    }

    private bool IsItemCommandFocusBlocked()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        return focused is Button or CheckBox or RatingControl or TextBox or ComboBox or AutoSuggestBox ||
            (focused is not null &&
             (HasVisualAncestor<Button>(focused) ||
              HasVisualAncestor<CheckBox>(focused) ||
              HasVisualAncestor<RatingControl>(focused)));
    }

    private bool IsTextInputFocused()
    {
        return FocusManager.GetFocusedElement(XamlRoot) is
            TextBox or RichEditBox or PasswordBox or NumberBox or AutoSuggestBox;
    }

    private async Task SelectAllMediaItemsAsync()
    {
        if (_isSelectingAll || !_isPageActive)
        {
            return;
        }

        _isSelectingAll = true;
        try
        {
            if (_loadMediaItemsCts is not { IsCancellationRequested: false } loadCts)
            {
                ResumeCachedMediaPage();
                loadCts = _loadMediaItemsCts!;
            }

            while (_hasMoreMediaItems && ReferenceEquals(_loadMediaItemsCts, loadCts))
            {
                loadCts.Token.ThrowIfCancellationRequested();
                if (_isLoadingNextPage)
                {
                    await Task.Delay(20, loadCts.Token);
                    continue;
                }

                var previousOffset = _nextMediaItemOffset;
                await LoadNextMediaPageAsync(loadCts, suppressFeedback: true);
                if (previousOffset == _nextMediaItemOffset && !_isLoadingNextPage)
                {
                    break;
                }
            }

            loadCts.Token.ThrowIfCancellationRequested();
            if (!_isPageActive || !ReferenceEquals(_loadMediaItemsCts, loadCts))
            {
                return;
            }

            _isSyncingSelection = true;
            try
            {
                MediaGrid.SelectedItems.Clear();
                MediaList.SelectedItems.Clear();
                _selectedItems.Clear();
                foreach (var item in MediaItems)
                {
                    item.IsSelected = true;
                    _selectedItems.Add(item);
                }

                GetActiveView().SelectAll();
            }
            finally
            {
                _isSyncingSelection = false;
                OnPropertyChanged(nameof(HasSelection));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Failed to select all media-library items:");
            Debug.WriteLine(ex);
        }
        finally
        {
            _isSelectingAll = false;
        }
    }

    private void MediaGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelectionFromView(e);
    }

    private void MediaGrid_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateGridItemSize();
        DispatcherQueue.TryEnqueue(() => UpdateGridItemSize());
    }

    private void MediaView_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.Item is MediaItemViewModel recycledItem)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isPageActive || GetActiveView().ContainerFromItem(recycledItem) is null)
                    {
                        recycledItem.ReleaseThumbnailSource();
                    }
                });
            }

            return;
        }

        if (args.Item is MediaItemViewModel item)
        {
            item.EnsureThumbnailSource();
            QueueMissingThumbnailGeneration([item]);
        }

        if (args.ItemIndex < Math.Max(0, MediaItems.Count - IncrementalLoadTriggerItemCount) ||
            _isIncrementalLoadSuspended ||
            _isSelectingAll ||
            _loadMediaItemsCts is not { } loadCts)
        {
            return;
        }

        _ = LoadNextMediaPageSafelyAsync(loadCts);
    }

    private void QueueVisibleThumbnailGeneration()
    {
        if (!_isPageActive || MediaItems.Count == 0)
        {
            return;
        }

        var activeView = GetActiveView();
        var visibleItems = MediaItems
            .Where(item => activeView.ContainerFromItem(item) is not null)
            .ToList();
        foreach (var item in visibleItems)
        {
            item.EnsureThumbnailSource();
        }

        QueueMissingThumbnailGeneration(visibleItems);
    }

    private async Task LoadNextMediaPageSafelyAsync(CancellationTokenSource loadCts)
    {
        try
        {
            await LoadNextMediaPageAsync(loadCts);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (ReferenceEquals(_loadMediaItemsCts, loadCts))
            {
                _isIncrementalLoadSuspended = true;
            }
        }
    }

    private void MediaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGridItemSize(e.NewSize.Width);
        DispatcherQueue.TryEnqueue(() => UpdateGridItemSize());
    }

    private void UiSettings_TextScaleFactorChanged(UISettings sender, object args)
    {
        _ = DispatcherQueue.TryEnqueue(() => UpdateGridItemSize());
    }

    private void UpdateGridItemSize(double? availableWidth = null)
    {
        if (MediaGrid?.ItemsPanelRoot is not ItemsWrapGrid itemsPanel)
        {
            return;
        }

        var width = availableWidth is > 0 and var currentWidth && !double.IsNaN(currentWidth)
            ? currentWidth
            : GetMediaGridViewportWidth(itemsPanel);
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        var layoutWidth = Math.Max(1, width - GridItemLayoutWidthReduction);

        // ItemsWrapGrid item slots include each GridViewItem's right margin.
        var itemSlotMinWidth = GridItemMinWidth + GridItemHorizontalSpacing;
        var columnCount = Math.Max(1, (int)Math.Floor(layoutWidth / itemSlotMinWidth));
        var itemWidth = layoutWidth / columnCount;

        itemsPanel.ItemWidth = itemWidth;
        var textScale = Math.Max(1, _uiSettings.TextScaleFactor);
        itemsPanel.ItemHeight = GridItemFixedHeight +
            (GridItemScaledTextHeight * textScale) +
            GridItemVerticalSpacing;
    }

    private double GetMediaGridViewportWidth(ItemsWrapGrid itemsPanel)
    {
        var scrollViewer = FindVisualDescendant<ScrollViewer>(MediaGrid);
        if (scrollViewer?.ViewportWidth is > 0 and var viewportWidth && !double.IsNaN(viewportWidth))
        {
            return viewportWidth;
        }

        return itemsPanel.ActualWidth > 0 ? itemsPanel.ActualWidth : MediaGrid.ActualWidth;
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

    private void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelectionFromView(e);
    }

    private void MediaView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement source || HasVisualAncestor<ScrollBar>(e.OriginalSource))
        {
            return;
        }

        var pointerProperties = e.GetCurrentPoint(source).Properties;
        if (pointerProperties.IsRightButtonPressed)
        {
            var rightClickedItem = GetMediaItemFromOriginalSource(e.OriginalSource);
            ClearSelection();
            if (rightClickedItem is not null && MediaItems.Contains(rightClickedItem))
            {
                rightClickedItem.IsSelected = true;
            }

            return;
        }

        if (pointerProperties.IsLeftButtonPressed &&
            GetMediaItemFromOriginalSource(e.OriginalSource) is null)
        {
            ClearSelection();
        }
    }

    private async void MediaGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetMediaItemFromOriginalSource(e.OriginalSource) is { } item)
        {
            e.Handled = true;
            await PlayAsync(item);
        }
    }

    private async void MediaList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetMediaItemFromOriginalSource(e.OriginalSource) is { } item)
        {
            e.Handled = true;
            await PlayAsync(item);
        }
    }

    private void MediaItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MediaItemViewModel item })
        {
            item.IsPointerOver = true;
        }
    }

    private void MediaItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MediaItemViewModel item })
        {
            item.IsPointerOver = false;
        }
    }

    private async void PlayMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: MediaItemViewModel item })
        {
            await PlayAsync(item);
        }
    }

    private async void AddToPlaylistMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: MediaItemViewModel item })
        {
            await AddItemsToPlaylistAsync([item]);
        }
    }

    private async void AddToSavedPlaylistMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: MediaItemViewModel item })
        {
            await AddItemsToSavedPlaylistAsync([item]);
        }
    }

    private async Task AddItemsToSavedPlaylistAsync(IReadOnlyList<MediaItemViewModel> items)
    {
        if (items.Count == 0 ||
            App.Services is null ||
            _isAddingToSavedPlaylist)
        {
            return;
        }

        var itemIds = items
            .Where(item => item.Id != Guid.Empty)
            .Select(item => item.Id)
            .Distinct()
            .ToArray();
        if (itemIds.Length == 0)
        {
            return;
        }

        _isAddingToSavedPlaylist = true;
        try
        {
            using var scope = App.Services.CreateScope();
            var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
            var playlist = await PlaylistPickerHelper.ChooseOrCreateAsync(
                Content.XamlRoot,
                playlistBus,
                GetResourceString);
            if (playlist is null)
            {
                return;
            }

            var result = await playlistBus.AddItemsAsync(playlist.Id, itemIds);
            FooterStatusBar.ShowTransient(string.Format(
                GetResourceString(
                    "LibraryPage_Status_AddedToSavedPlaylist",
                    "Added {0} items to \"{1}\"; {2} already present"),
                result.AddedCount,
                playlist.Name,
                result.DuplicateCount));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(
                GetResourceString(
                    "LibraryPage_AddToSavedPlaylistFailed",
                    "Could not add items to the playlist"),
                ex.Message);
        }
        finally
        {
            _isAddingToSavedPlaylist = false;
        }
    }

    private async Task AddItemsToPlaylistAsync(IReadOnlyList<MediaItemViewModel> items)
    {
        if (items.Count == 0 || App.Services is null || _isAddingToPlaybackQueue)
        {
            return;
        }

        _isAddingToPlaybackQueue = true;
        try
        {
            var coordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
            var added = 0;
            foreach (var item in items)
            {
                if (item.Kind is not (MediaLibraryItemKind.Video or MediaLibraryItemKind.Audio) ||
                    string.IsNullOrWhiteSpace(item.FilePath) ||
                    !File.Exists(item.FilePath))
                {
                    continue;
                }

                await coordinator.EnqueueItemAsync(CreatePlaybackQueueEntry(item));
                added++;
            }

            FooterStatusBar.ShowTransient(string.Format(
                GetResourceString("LibraryPage_Status_Queued", "Added {0} items to the playback queue"),
                added));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(
                GetResourceString("LibraryPage_QueueSelectedFailed", "Could not add items to the playback queue"),
                ex.Message);
        }
        finally
        {
            _isAddingToPlaybackQueue = false;
        }
    }

    private async Task PlayAsync(MediaItemViewModel item)
    {
        if (item.Kind is MediaLibraryItemKind.Video or MediaLibraryItemKind.Audio)
        {
            await PlayLibraryMediaAsync(item);
            return;
        }

        try
        {
            StorageFile? file = item.StorageFile;
            if (file is null && !string.IsNullOrWhiteSpace(item.FilePath))
            {
                if (!File.Exists(item.FilePath))
                {
                    throw new FileNotFoundException(item.FilePath);
                }

                file = await StorageFile.GetFileFromPathAsync(item.FilePath);
            }

            if (file is null)
            {
                throw new FileNotFoundException(item.FilePath);
            }

            var launched = await Launcher.LaunchFileAsync(file);
            if (launched)
            {
                await RecordPlayedAsync(item);
            }

            if (!launched)
            {
                await ShowInfoDialog(
                    GetResourceString("LibraryPage_PlayDialog_Title", "Play"),
                    string.Format(GetResourceString("LibraryPage_PlayDialog_Message", "Ready to play: {0}"), item.Title));
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToPlayDialog_Title", "Failed to play"), ex.Message);
        }
    }

    private async Task PlayLibraryMediaAsync(MediaItemViewModel item)
    {
        var requestId = Interlocked.Increment(ref _playbackQueueRequestId);
        _playbackQueueBuildCts?.Cancel();
        var cancellation = new CancellationTokenSource();
        _playbackQueueBuildCts = cancellation;

        try
        {
            if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
            {
                throw new FileNotFoundException(item.FilePath);
            }

            var queueBuilder = App.Services.GetRequiredService<PlaybackQueueBuilder>();
            var displayedItems = MediaItems
                .Select(displayedItem => new PlaybackQueueSelection(
                    displayedItem.Id,
                    displayedItem.Title,
                    displayedItem.FilePath,
                    displayedItem.Kind,
                    displayedItem.Artist,
                    displayedItem.Album,
                    displayedItem.ThumbnailPath,
                    displayedItem.PlaybackPosition))
                .ToArray();
            var context = await queueBuilder.BuildDisplayedQueueAsync(
                displayedItems,
                item.Id,
                cancellation.Token);

            if (cancellation.IsCancellationRequested ||
                requestId != Interlocked.Read(ref _playbackQueueRequestId))
            {
                return;
            }

            var playbackCoordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
            await playbackCoordinator.PlayContextAsync(context, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestId == Interlocked.Read(ref _playbackQueueRequestId))
            {
                await ShowErrorDialog(
                    GetResourceString("LibraryPage_FailedToPlayDialog_Title", "Failed to play"),
                    ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_playbackQueueBuildCts, cancellation))
            {
                _playbackQueueBuildCts = null;
            }

            cancellation.Dispose();
        }
    }

    private void LikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaItemViewModel item })
        {
            _ = ToggleLikeAsync(item);
        }
    }

    private void LikeMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: MediaItemViewModel item })
        {
            _ = ToggleLikeAsync(item);
        }
    }

    private void RefreshMetadataMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: MediaItemViewModel item })
        {
            _ = RefreshMetadataManuallyAsync([item]);
        }
    }

    private void RefreshSelectedMetadataButton_Click(object sender, RoutedEventArgs e) =>
        _ = RefreshMetadataManuallyAsync(_selectedItems.ToList());

    private async Task RefreshMetadataManuallyAsync(
        IReadOnlyList<MediaItemViewModel> items)
    {
        if (_isRefreshingMetadata || items.Count == 0)
        {
            return;
        }

        _isRefreshingMetadata = true;
        RefreshSelectedMetadataButton.IsEnabled = false;
        var previousCancellation = _manualMetadataRefreshCts;
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _manualMetadataRefreshCts = cancellation;
        try
        {
            var counts = await RefreshMediaMetadataAsync(
                items,
                forceRefresh: true,
                cancellation.Token);
            if (counts.Added > 0)
            {
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "LibraryPage_Status_MetadataRefreshed",
                        "Refreshed metadata for {0} item(s)"),
                    counts.Added));
                if (_sortField == "Title")
                {
                    RefreshMediaList();
                }
            }
            else if (counts.Failed > 0)
            {
                FooterStatusBar.ShowTransient(GetResourceString(
                    "LibraryPage_Status_MetadataRefreshFailed",
                    "Could not refresh media metadata"));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to refresh media metadata: {ex.Message}");
            FooterStatusBar.ShowTransient(GetResourceString(
                "LibraryPage_Status_MetadataRefreshFailed",
                "Could not refresh media metadata"));
        }
        finally
        {
            if (ReferenceEquals(_manualMetadataRefreshCts, cancellation))
            {
                _manualMetadataRefreshCts = null;
            }

            cancellation.Dispose();
            _isRefreshingMetadata = false;
            RefreshSelectedMetadataButton.IsEnabled = true;
        }
    }

    private async Task ToggleLikeAsync(MediaItemViewModel item)
    {
        var oldValue = item.IsLike;
        item.IsLike = !item.IsLike;

        try
        {
            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.SetFavoriteAsync(item.Id, item.IsLike);
        }
        catch (Exception ex)
        {
            item.IsLike = oldValue;
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToLikeDialog_Title", "Failed to like"), ex.Message);
        }
    }

    private void RatingControl_ValueChanged(RatingControl sender, object args)
    {
        if (_isLoading || sender.Tag is not MediaItemViewModel item)
        {
            return;
        }

        var newRating = (int)sender.Value;
        if (item.Rating == newRating)
        {
            return;
        }

        _ = SetRatingAsync(item, newRating);
    }

    private async Task SetRatingAsync(MediaItemViewModel item, int newRating)
    {
        var oldRating = item.Rating;
        item.Rating = newRating;

        try
        {
            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.SetUserRatingAsync(item.Id, newRating == 0 ? null : newRating);

            if (newRating == 0)
            {
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "LibraryPage_Status_ClearedRating",
                        "Cleared rating for {0}"),
                    item.Title));
            }
        }
        catch (Exception ex)
        {
            item.Rating = oldRating;
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToRateDialog_Title", "Failed to rate"), ex.Message);
        }
    }

    private void DeleteMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: MediaItemViewModel item })
        {
            _ = DeleteItemsAsync([item]);
        }
    }

    private async Task DeleteItemsAsync(List<MediaItemViewModel> items)
    {
        if (_isDeletingItems || items.Count == 0)
        {
            return;
        }

        _isDeletingItems = true;
        UpdateSelectionBar();
        try
        {
            var dialog = new ContentDialog
            {
                Title = GetResourceString("LibraryPage_DeleteDialog_Title", "Confirm delete"),
                Content = items.Count == 1
                    ? string.Format(GetResourceString("LibraryPage_DeleteDialog_SingleMessage", "Remove {0} from the library?"), items[0].Title)
                    : string.Format(GetResourceString("LibraryPage_DeleteDialog_MultipleMessage", "Remove {0} items from the library?"), items.Count),
                PrimaryButtonText = GetResourceString("LibraryPage_DeleteDialog_DeleteButton", "Delete"),
                CloseButtonText = GetResourceString("LibraryPage_DeleteDialog_CancelButton", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var successCount = 0;
            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();

            foreach (var item in items)
            {
                if (!await library.DeleteAsync(item.Id))
                {
                    continue;
                }

                item.PropertyChanged -= MediaItem_PropertyChanged;
                _allItems.Remove(item);
                _selectedItems.Remove(item);
                App.Services
                    .GetRequiredService<PlaybackCoordinator>()
                    .DetachQueueItemFromLibrary(item.Id);
                successCount++;
            }

            if (successCount > 0)
            {
                await LoadMediaItemsAsync();
            }

            FooterStatusBar.ShowTransient(string.Format(
                GetResourceString(
                    "LibraryPage_Status_Deleted",
                    "Removed {0} items"),
                successCount));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(GetResourceString("LibraryPage_FailedToDeleteDialog_Title", "Failed to delete"), ex.Message);
        }
        finally
        {
            _isDeletingItems = false;
            UpdateSelectionBar();
        }
    }

    private void PropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaItemViewModel item })
        {
            _ = ShowPropertiesDialog(item);
        }
    }

    private void PropertiesMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: MediaItemViewModel item })
        {
            _ = ShowPropertiesDialog(item);
        }
    }

    private async Task ShowPropertiesDialog(MediaItemViewModel item)
    {
        var hasPersistedTechnicalMetadata =
            item.Streams.Count > 0 ||
            !string.IsNullOrWhiteSpace(item.ContainerFormat);
        var mediaInfo = hasPersistedTechnicalMetadata
            ? null
            : await TryGetFfmpegMediaInfoAsync(item.FilePath);
        var displayTitle = FirstNonEmptyOrNull(item.Title)
            ?? Path.GetFileNameWithoutExtension(item.FilePath);
        var formattedDuration = MediaInfoFormatter.FormatDuration(item.Duration);
        var formattedFileSize = item.FormattedFileSize;
        var mediaSections = hasPersistedTechnicalMetadata
            ? CreatePersistedPropertySections(item)
            : CreateFfmpegPropertySections(mediaInfo);

        var summarySecondaryLabel = item.Kind == MediaLibraryItemKind.Video
            ? GetPropertyLabel("LibraryPage_PropertiesDialog_Content_Resolution", "Resolution: {0}")
            : GetPropertyLabel("MediaInfo_Artist", "Artist: {0}");
        var summarySecondaryValue = item.Kind == MediaLibraryItemKind.Video
            ? item.Resolution
            : NormalizePropertyValue(item.Artist);
        var xamlRoot = Content.XamlRoot;
        // ContentDialog's visual border is sized by theme resources inside its
        // template. Keep the outer smoke-layer control unconstrained so the
        // template can center the visual border within the complete XamlRoot.
        var dialogWidth = Math.Max(360, Math.Min(700, xamlRoot.Size.Width - 48));
        var dialogMaxHeight = Math.Max(280, Math.Min(760, xamlRoot.Size.Height - 48));
        var scrollViewerWidth = dialogWidth - 48;
        var contentWidth = Math.Min(632, dialogWidth - 68);
        var contentHeight = Math.Max(160, dialogMaxHeight - 176);
        var content = CreatePropertiesDialogContent(
            item,
            displayTitle,
            formattedDuration,
            summarySecondaryLabel,
            summarySecondaryValue,
            formattedFileSize,
            mediaSections,
            [
                new PropertyDetail(
                    GetPropertyLabel("LibraryPage_PropertiesDialog_Content_FileSize", "File size: {0}"),
                    formattedFileSize),
                new PropertyDetail(
                    GetPropertyLabel("LibraryPage_PropertiesDialog_Content_AddedTime", "Added time: {0}"),
                    item.DateAdded.ToLocalTime().ToString("g")),
                new PropertyDetail(
                    GetPropertyLabel("LibraryPage_PropertiesDialog_Content_FileLocation", "File location: {0}"),
                    item.FilePath,
                    Wrap: true)
            ],
            contentWidth);

        var dialog = new ContentDialog
        {
            Title = GetResourceString("LibraryPage_PropertiesDialog_Title", "Properties"),
            Content = new ScrollViewer
            {
                Content = content,
                Width = scrollViewerWidth,
                Height = contentHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            CloseButtonText = GetResourceString("LibraryPage_Dialog_CloseButton", "Close"),
            XamlRoot = xamlRoot,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            FullSizeDesired = false
        };
        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxHeight"] = dialogMaxHeight;

        await dialog.ShowAsync();
    }

    private List<PropertySection> CreatePersistedPropertySections(MediaItemViewModel item)
    {
        var sections = new List<PropertySection>();
        var videoStreams = item.Streams
            .Where(stream => stream.Kind == MediaLibraryStreamKind.Video)
            .OrderBy(stream => stream.StreamIndex)
            .ToArray();
        var audioStreams = item.Streams
            .Where(stream => stream.Kind == MediaLibraryStreamKind.Audio)
            .OrderBy(stream => stream.StreamIndex)
            .ToArray();

        sections.Add(new PropertySection(
            GetResourceString("LibraryPage_PropertiesDialog_ContainerDetails", "Container information"),
            "\uE7C3",
            [
                new PropertyDetail(
                    GetPropertyLabel("MediaInfo_ContainerFormat", "Container: {0}"),
                    NormalizePropertyValue(item.ContainerFormat))
            ]));

        if (videoStreams.FirstOrDefault() is { } video)
        {
            var details = new List<PropertyDetail>
            {
                new(
                    GetPropertyLabel("MediaInfo_VideoCodec", "Codec: {0}"),
                    MediaInfoFormatter.FormatCodec(video.Codec, description: null)),
                new(
                    GetPropertyLabel("MediaInfo_CodecProfile", "Profile: {0}"),
                    NormalizePropertyValue(video.CodecProfile)),
                new(
                    GetPropertyLabel("MediaInfo_Resolution", "Resolution: {0}"),
                    MediaInfoFormatter.FormatResolution(video.Width, video.Height)),
                new(
                    GetPropertyLabel("MediaInfo_FrameRate", "Frame rate: {0}"),
                    MediaInfoFormatter.FormatFrameRate(video.FrameRate)),
                new(
                    GetPropertyLabel("MediaInfo_VideoBitRate", "Bit rate: {0}"),
                    MediaInfoFormatter.FormatBitRate(video.BitRate)),
                new(
                    GetPropertyLabel("MediaInfo_PixelFormat", "Pixel format: {0}"),
                    NormalizePropertyValue(video.PixelFormat))
            };
            AddOptionalProperty(
                details,
                "MediaInfo_Rotation",
                "Rotation: {0}",
                MediaInfoFormatter.FormatRotation(video.Rotation));
            AddOptionalProperty(
                details,
                "MediaInfo_StreamTitle",
                "Stream title: {0}",
                video.Title);
            AddOptionalProperty(
                details,
                "MediaInfo_StreamLanguage",
                "Language: {0}",
                video.Language);
            sections.Add(new PropertySection(
                GetResourceString("LibraryPage_PropertiesDialog_VideoDetails", "Video stream"),
                "\uE714",
                details));
        }

        if (audioStreams.FirstOrDefault() is { } audio)
        {
            var details = new List<PropertyDetail>
            {
                new(
                    GetPropertyLabel("MediaInfo_AudioCodec", "Codec: {0}"),
                    MediaInfoFormatter.FormatCodec(audio.Codec, description: null)),
                new(
                    GetPropertyLabel("MediaInfo_ChannelCount", "Channels: {0}"),
                    MediaInfoFormatter.FormatChannelCount(audio.Channels)),
                new(
                    GetPropertyLabel("MediaInfo_ChannelLayout", "Channel layout: {0}"),
                    NormalizePropertyValue(audio.ChannelLayout)),
                new(
                    GetPropertyLabel("MediaInfo_SampleRate", "Sample rate: {0}"),
                    MediaInfoFormatter.FormatSampleRate(audio.SampleRate)),
                new(
                    GetPropertyLabel("MediaInfo_BitsPerSample", "Bit depth: {0}"),
                    MediaInfoFormatter.FormatBitsPerSample(audio.BitDepth)),
                new(
                    GetPropertyLabel("MediaInfo_AudioBitRate", "Bit rate: {0}"),
                    MediaInfoFormatter.FormatBitRate(audio.BitRate))
            };
            AddOptionalProperty(
                details,
                "MediaInfo_CodecProfile",
                "Profile: {0}",
                audio.CodecProfile);
            AddOptionalProperty(
                details,
                "MediaInfo_StreamTitle",
                "Stream title: {0}",
                audio.Title);
            AddOptionalProperty(
                details,
                "MediaInfo_StreamLanguage",
                "Language: {0}",
                audio.Language);
            sections.Add(new PropertySection(
                GetResourceString("LibraryPage_PropertiesDialog_AudioDetails", "Audio stream"),
                "\uE8D6",
                details));
        }

        return sections;
    }

    private List<PropertySection> CreateFfmpegPropertySections(FfmpegMediaInfo? mediaInfo)
    {
        if (mediaInfo is null)
        {
            return [];
        }

        var sections = new List<PropertySection>();
        sections.Add(new PropertySection(
            GetResourceString("LibraryPage_PropertiesDialog_ContainerDetails", "Container information"),
            "\uE7C3",
            [
                new PropertyDetail(
                    GetPropertyLabel("MediaInfo_ContainerFormat", "Container: {0}"),
                    NormalizePropertyValue(mediaInfo.ContainerFormat)),
                new PropertyDetail(
                    GetPropertyLabel("MediaInfo_ContainerDescription", "Format description: {0}"),
                    NormalizePropertyValue(mediaInfo.ContainerDescription),
                    Wrap: true),
                new PropertyDetail(
                    GetPropertyLabel("MediaInfo_OverallBitRate", "Overall bit rate: {0}"),
                    MediaInfoFormatter.FormatBitRate(mediaInfo.BitRate)),
                new PropertyDetail(
                    GetResourceString("LibraryPage_PropertiesDialog_TracksLabel", "Streams"),
                    string.Format(
                        GetResourceString(
                            "LibraryPage_PropertiesDialog_TrackSummary",
                            "Video {0} · Audio {1} · Subtitles {2}"),
                        mediaInfo.VideoTrackCount,
                        mediaInfo.AudioTrackCount,
                        mediaInfo.SubtitleTrackCount))
            ]));

        if (mediaInfo.PrimaryVideo is { } video)
        {
            var details = new List<PropertyDetail>
            {
                new(
                    GetPropertyLabel("MediaInfo_VideoCodec", "Codec: {0}"),
                    MediaInfoFormatter.FormatCodec(video.CodecName, video.CodecDescription)),
                new(
                    GetPropertyLabel("MediaInfo_CodecProfile", "Profile: {0}"),
                    NormalizePropertyValue(video.CodecProfile)),
                new(
                    GetPropertyLabel("MediaInfo_Resolution", "Resolution: {0}"),
                    MediaInfoFormatter.FormatResolution(video.Width, video.Height)),
                new(
                    GetPropertyLabel("MediaInfo_FrameRate", "Frame rate: {0}"),
                    MediaInfoFormatter.FormatFrameRate(video.FrameRate)),
                new(
                    GetPropertyLabel("MediaInfo_VideoBitRate", "Bit rate: {0}"),
                    MediaInfoFormatter.FormatBitRate(video.BitRate)),
                new(
                    GetPropertyLabel("MediaInfo_PixelFormat", "Pixel format: {0}"),
                    NormalizePropertyValue(video.PixelFormat))
            };
            AddOptionalProperty(
                details,
                "MediaInfo_Rotation",
                "Rotation: {0}",
                MediaInfoFormatter.FormatRotation(video.Rotation));
            AddOptionalProperty(
                details,
                "MediaInfo_StreamTitle",
                "Stream title: {0}",
                GetTag(video.Tags, "title"));
            AddOptionalProperty(
                details,
                "MediaInfo_StreamLanguage",
                "Language: {0}",
                GetTag(video.Tags, "language", "lang"));
            sections.Add(new PropertySection(
                GetResourceString("LibraryPage_PropertiesDialog_VideoDetails", "Video stream"),
                "\uE714",
                details));
        }

        if (mediaInfo.PrimaryAudio is { } audio)
        {
            var details = new List<PropertyDetail>
            {
                new(
                    GetPropertyLabel("MediaInfo_AudioCodec", "Codec: {0}"),
                    MediaInfoFormatter.FormatCodec(audio.CodecName, audio.CodecDescription)),
                new(
                    GetPropertyLabel("MediaInfo_ChannelCount", "Channels: {0}"),
                    MediaInfoFormatter.FormatChannelCount(audio.ChannelCount)),
                new(
                    GetPropertyLabel("MediaInfo_ChannelLayout", "Channel layout: {0}"),
                    NormalizePropertyValue(audio.ChannelLayout)),
                new(
                    GetPropertyLabel("MediaInfo_SampleRate", "Sample rate: {0}"),
                    MediaInfoFormatter.FormatSampleRate(audio.SampleRate)),
                new(
                    GetPropertyLabel("MediaInfo_SampleFormat", "Sample format: {0}"),
                    NormalizePropertyValue(audio.SampleFormat)),
                new(
                    GetPropertyLabel("MediaInfo_BitsPerSample", "Bit depth: {0}"),
                    MediaInfoFormatter.FormatBitsPerSample(audio.BitsPerSample)),
                new(
                    GetPropertyLabel("MediaInfo_AudioBitRate", "Bit rate: {0}"),
                    MediaInfoFormatter.FormatBitRate(audio.BitRate))
            };
            AddOptionalProperty(
                details,
                "MediaInfo_CodecProfile",
                "Profile: {0}",
                audio.CodecProfile);
            AddOptionalProperty(
                details,
                "MediaInfo_StreamTitle",
                "Stream title: {0}",
                GetTag(audio.Tags, "title"));
            AddOptionalProperty(
                details,
                "MediaInfo_StreamLanguage",
                "Language: {0}",
                GetTag(audio.Tags, "language", "lang"));
            sections.Add(new PropertySection(
                GetResourceString("LibraryPage_PropertiesDialog_AudioDetails", "Audio stream"),
                "\uE8D6",
                details));
        }

        var title = FirstNonEmptyOrNull(
            GetTag(mediaInfo.Tags, "title"));
        var artist = FirstNonEmptyOrNull(
            GetTag(mediaInfo.PrimaryAudio?.Tags, "artist", "album_artist", "albumartist"),
            GetTag(mediaInfo.Tags, "artist", "album_artist", "albumartist"));
        var album = FirstNonEmptyOrNull(
            GetTag(mediaInfo.PrimaryAudio?.Tags, "album"),
            GetTag(mediaInfo.Tags, "album"));
        var metadataDetails = new List<PropertyDetail>();
        AddOptionalProperty(metadataDetails, "MediaInfo_Title", "Title: {0}", title, wrap: true);
        AddOptionalProperty(metadataDetails, "MediaInfo_Artist", "Artist: {0}", artist, wrap: true);
        AddOptionalProperty(metadataDetails, "MediaInfo_Album", "Album: {0}", album, wrap: true);
        if (metadataDetails.Count > 0)
        {
            sections.Add(new PropertySection(
                GetResourceString("LibraryPage_PropertiesDialog_MetadataDetails", "Media tags"),
                "\uE8EC",
                metadataDetails));
        }

        return sections;
    }

    private void AddOptionalProperty(
        ICollection<PropertyDetail> details,
        string resourceKey,
        string fallback,
        string? value,
        bool wrap = false)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details.Add(new PropertyDetail(
                GetPropertyLabel(resourceKey, fallback),
                value.Trim(),
                wrap));
        }
    }

    private StackPanel CreatePropertiesDialogContent(
        MediaItemViewModel item,
        string displayTitle,
        string duration,
        string secondaryLabel,
        string secondaryValue,
        string fileSize,
        IReadOnlyList<PropertySection> mediaSections,
        IReadOnlyList<PropertyDetail> fileDetails,
        double contentWidth)
    {
        var content = new StackPanel
        {
            Width = contentWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 14
        };
        var useCompactLayout = contentWidth < 500;
        content.Children.Add(CreatePropertiesHeader(item, displayTitle, contentWidth));

        var summaryCards = new[]
        {
            CreatePropertySummaryCard(
                GetPropertyLabel("LibraryPage_PropertiesDialog_Content_Duration", "Duration: {0}"),
                duration,
                "\uE916"),
            CreatePropertySummaryCard(secondaryLabel, secondaryValue, item.Kind == MediaLibraryItemKind.Video ? "\uE7F4" : "\uE8D6"),
            CreatePropertySummaryCard(
                GetPropertyLabel("LibraryPage_PropertiesDialog_Content_FileSize", "File size: {0}"),
                fileSize,
                "\uE7C3")
        };
        if (useCompactLayout)
        {
            var summary = new StackPanel { Spacing = 8 };
            foreach (var card in summaryCards)
            {
                card.MinHeight = 76;
                summary.Children.Add(card);
            }
            content.Children.Add(summary);
        }
        else
        {
            var summary = new Grid { ColumnSpacing = 12 };
            for (var index = 0; index < summaryCards.Length; index++)
            {
                summary.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(summaryCards[index], index);
                summary.Children.Add(summaryCards[index]);
            }
            content.Children.Add(summary);
        }

        foreach (var section in mediaSections)
        {
            content.Children.Add(CreatePropertySection(
                section.Title,
                section.Glyph,
                section.Details,
                useCompactLayout));
        }

        content.Children.Add(CreatePropertySection(
            GetResourceString("LibraryPage_PropertiesDialog_FileDetails", "File information"),
            "\uE8A5",
            fileDetails,
            useCompactLayout));
        return content;
    }

    private StackPanel CreatePropertiesHeader(
        MediaItemViewModel item,
        string displayTitle,
        double contentWidth)
    {
        var fileName = Path.GetFileName(item.FilePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = displayTitle;
        }
        var title = string.IsNullOrWhiteSpace(displayTitle)
            ? fileName
            : displayTitle;

        var header = new StackPanel
        {
            Width = contentWidth,
            Spacing = 6
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            MaxLines = 0,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTextSelectionEnabled = true
        });

        if (!string.IsNullOrWhiteSpace(fileName)
            && !string.Equals(fileName, title, StringComparison.OrdinalIgnoreCase))
        {
            header.Children.Add(new TextBlock
            {
                Text = fileName,
                FontSize = 13,
                Foreground = GetPropertiesBrush("TextFillColorSecondaryBrush"),
                MaxLines = 0,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTextSelectionEnabled = true
            });
        }

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        badges.Children.Add(CreatePropertyBadge(
            item.Kind == MediaLibraryItemKind.Video
                ? GetResourceString("LibraryPage_PropertiesDialog_VideoKind", "Video")
                : GetResourceString("LibraryPage_PropertiesDialog_AudioKind", "Audio")));
        var extension = string.IsNullOrWhiteSpace(item.Extension)
            ? Path.GetExtension(item.FilePath)
            : item.Extension;
        if (!string.IsNullOrWhiteSpace(extension))
        {
            badges.Children.Add(CreatePropertyBadge(extension.TrimStart('.').ToUpperInvariant()));
        }
        header.Children.Add(badges);
        return header;
    }

    private Border CreatePropertyBadge(string text)
    {
        return new Border
        {
            Padding = new Thickness(9, 4, 9, 4),
            Background = GetPropertiesBrush("AccentFillColorSecondaryBrush"),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetPropertiesBrush("TextOnAccentFillColorPrimaryBrush")
            }
        };
    }

    private Border CreatePropertySummaryCard(string label, string value, string glyph)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = GetPropertiesBrush("AccentTextFillColorPrimaryBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = NormalizePropertyValue(value),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = GetPropertiesBrush("TextFillColorSecondaryBrush")
        });
        return new Border
        {
            MinHeight = 84,
            Padding = new Thickness(12),
            Background = GetPropertiesBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = GetPropertiesBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = panel
        };
    }

    private Border CreatePropertySection(
        string title,
        string glyph,
        IReadOnlyList<PropertyDetail> details,
        bool useCompactLayout)
    {
        var content = new StackPanel { Spacing = 0 };
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 10), ColumnSpacing = 10 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 17,
            Foreground = GetPropertiesBrush("AccentTextFillColorPrimaryBrush")
        });
        var headingText = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headingText, 1);
        heading.Children.Add(headingText);
        content.Children.Add(heading);

        for (var index = 0; index < details.Count; index++)
        {
            if (index > 0)
            {
                content.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 2, 0, 2),
                    Background = GetPropertiesBrush("DividerStrokeColorDefaultBrush")
                });
            }
            content.Children.Add(CreatePropertyDetailRow(details[index], useCompactLayout));
        }

        return new Border
        {
            Padding = new Thickness(16, 14, 16, 12),
            Background = GetPropertiesBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = GetPropertiesBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = content
        };
    }

    private Grid CreatePropertyDetailRow(PropertyDetail detail, bool useCompactLayout)
    {
        var row = new Grid
        {
            MinHeight = 34,
            Padding = new Thickness(2, 5, 2, 5),
            ColumnSpacing = useCompactLayout ? 0 : 18,
            RowSpacing = useCompactLayout ? 4 : 0
        };
        if (useCompactLayout)
        {
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
        }

        var label = new TextBlock
        {
            Text = detail.Label,
            FontSize = 13,
            Foreground = GetPropertiesBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Top
        };
        row.Children.Add(label);
        var value = new TextBlock
        {
            Text = NormalizePropertyValue(detail.Value),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            IsTextSelectionEnabled = true,
            TextWrapping = detail.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = detail.Wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Top
        };
        if (useCompactLayout)
        {
            Grid.SetRow(value, 1);
        }
        else
        {
            Grid.SetColumn(value, 1);
        }
        row.Children.Add(value);
        return row;
    }

    private string GetPropertyLabel(string key, string fallback)
    {
        var template = GetResourceString(key, fallback);
        var placeholderIndex = template.IndexOf('{');
        var label = placeholderIndex >= 0 ? template[..placeholderIndex] : template;
        return label.Trim().TrimEnd(':', '：');
    }

    private static string? GetTag(
        IReadOnlyDictionary<string, string>? tags,
        params string[] names)
    {
        if (tags is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (tags.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string NormalizePropertyValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
    }

    private static Brush GetPropertiesBrush(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void RefreshMediaList()
    {
        if (SearchBox is null)
        {
            return;
        }

        var sortedItems = _allItems.ToList();
        sortedItems.Sort(CompareMediaItems);
        if (MediaItems.Count != sortedItems.Count || sortedItems.Any(item => !MediaItems.Contains(item)))
        {
            ReconcileMediaItems(sortedItems);
        }
        else
        {
            for (var targetIndex = 0; targetIndex < sortedItems.Count; targetIndex++)
            {
                var currentIndex = MediaItems.IndexOf(sortedItems[targetIndex]);
                if (currentIndex >= 0 && currentIndex != targetIndex)
                {
                    MediaItems.Move(currentIndex, targetIndex);
                }
            }
        }

        TrimSelectionToVisibleItems();
        RestoreSelectionToActiveView();
        OnPropertyChanged(nameof(IsEmpty));
        UpdateContentState();
    }

    private void ReconcileMediaItems(IReadOnlyList<MediaItemViewModel> desiredItems)
    {
        for (var targetIndex = 0; targetIndex < desiredItems.Count; targetIndex++)
        {
            var desiredItem = desiredItems[targetIndex];
            if (targetIndex < MediaItems.Count &&
                ReferenceEquals(MediaItems[targetIndex], desiredItem))
            {
                continue;
            }

            var currentIndex = MediaItems.IndexOf(desiredItem);
            if (currentIndex >= 0)
            {
                MediaItems.Move(currentIndex, targetIndex);
            }
            else
            {
                MediaItems.Insert(targetIndex, desiredItem);
            }
        }

        while (MediaItems.Count > desiredItems.Count)
        {
            MediaItems.RemoveAt(MediaItems.Count - 1);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task AppendLoadedMediaItemsAsync(
        IReadOnlyList<MediaItemViewModel> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (MediaItems.Count == 0)
        {
            foreach (var item in items)
            {
                RegisterMediaItem(item);
            }

            var sortedItems = items.ToList();
            sortedItems.Sort(CompareMediaItems);
            ReconcileMediaItems(sortedItems);
            return;
        }

        var addedItems = new List<MediaItemViewModel>(items.Count);
        try
        {
            for (var index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[index];
                RegisterMediaItem(item);
                addedItems.Add(item);
                var insertionIndex = FindMediaItemInsertionIndex(item);
                MediaItems.Insert(insertionIndex, item);

                if ((index + 1) % 8 == 0)
                {
                    await Task.Yield();
                }
            }
        }
        catch
        {
            foreach (var item in addedItems)
            {
                item.PropertyChanged -= MediaItem_PropertyChanged;
                _allItems.Remove(item);
                MediaItems.Remove(item);
            }

            throw;
        }

    }

    private int FindMediaItemInsertionIndex(MediaItemViewModel item)
    {
        var low = 0;
        var high = MediaItems.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (CompareMediaItems(MediaItems[middle], item) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private int CompareMediaItems(MediaItemViewModel left, MediaItemViewModel right)
    {
        var comparison = _sortField switch
        {
            "Title" => StringComparer.CurrentCulture.Compare(left.Title, right.Title),
            "Duration" => Nullable.Compare(left.Duration, right.Duration),
            "FileSize" => left.FileSize.CompareTo(right.FileSize),
            _ => left.DateAdded.CompareTo(right.DateAdded)
        };
        if (_isSortDescending)
        {
            comparison = -comparison;
        }

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.CurrentCulture.Compare(left.Title, right.Title);
        return comparison != 0 ? comparison : left.Id.CompareTo(right.Id);
    }

    private void UpdateLoadStatus()
    {
        UpdateFooterSummary();
    }

    private void UpdateFooterSummary()
    {
        if (FooterStatusBar is null)
        {
            return;
        }

        var totalCount = Math.Max(_totalMediaItemCount, _allItems.Count);
        if (totalCount == 0 && string.IsNullOrWhiteSpace(SearchBox?.Text))
        {
            FooterStatusBar.SetSummary(
                GetResourceString("Common_PageStatus_Empty", "0 items"));
            return;
        }

        FooterStatusBar.SetSummary(
            _selectedItems.Count > 0
                ? string.Format(
                    GetResourceString(
                        "Common_PageStatus_SummaryWithSelection",
                        "{0} shown / {1} total · {2} selected"),
                    MediaItems.Count,
                    totalCount,
                    _selectedItems.Count)
                : string.Format(
                    GetResourceString(
                        "Common_PageStatus_Summary",
                        "{0} shown / {1} total"),
                    MediaItems.Count,
                    totalCount));
    }

    private void UpdateStatistics()
    {
        if (TotalCountText is null)
        {
            return;
        }

        TotalCountText.Text = Math.Max(_totalMediaItemCount, _allItems.Count).ToString();
    }

    private void RegisterMediaItem(MediaItemViewModel item)
    {
        item.SetLayoutBreakpoint(_currentBreakpoint);
        item.PropertyChanged += MediaItem_PropertyChanged;
        _allItems.Add(item);
    }

    private void MediaItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaItemViewModel.IsSelected) ||
            sender is not MediaItemViewModel item ||
            _isSyncingSelection)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            if (item.IsSelected)
            {
                if (!_selectedItems.Contains(item))
                {
                    _selectedItems.Add(item);
                }

                var activeView = GetActiveView();
                if (MediaItems.Contains(item) && !activeView.SelectedItems.Contains(item))
                {
                    activeView.SelectedItems.Add(item);
                }
            }
            else
            {
                _selectedItems.Remove(item);
                MediaGrid.SelectedItems.Remove(item);
                MediaList.SelectedItems.Remove(item);
            }
        }
        finally
        {
            _isSyncingSelection = false;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    private void SyncSelectionFromView(SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            foreach (var removedItem in e.RemovedItems.OfType<MediaItemViewModel>())
            {
                removedItem.IsSelected = false;
                _selectedItems.Remove(removedItem);
            }

            foreach (var addedItem in e.AddedItems.OfType<MediaItemViewModel>())
            {
                addedItem.IsSelected = true;
                if (!_selectedItems.Contains(addedItem))
                {
                    _selectedItems.Add(addedItem);
                }
            }
        }
        finally
        {
            _isSyncingSelection = false;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    private void ClearSelection()
    {
        _isSyncingSelection = true;
        try
        {
            foreach (var item in _selectedItems.ToList())
            {
                item.IsSelected = false;
            }

            _selectedItems.Clear();
            MediaGrid.SelectedItems.Clear();
            MediaList.SelectedItems.Clear();
        }
        finally
        {
            _isSyncingSelection = false;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    private void TrimSelectionToVisibleItems()
    {
        var visibleItems = MediaItems.ToHashSet();
        foreach (var selectedItem in _selectedItems.ToList())
        {
            if (visibleItems.Contains(selectedItem))
            {
                continue;
            }

            selectedItem.IsSelected = false;
            _selectedItems.Remove(selectedItem);
        }

        OnPropertyChanged(nameof(HasSelection));
    }

    private void RestoreSelectionToActiveView()
    {
        if (MediaGrid is null || MediaList is null)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            MediaGrid.SelectedItems.Clear();
            MediaList.SelectedItems.Clear();

            var activeView = GetActiveView();
            foreach (var item in _selectedItems.Where(MediaItems.Contains))
            {
                activeView.SelectedItems.Add(item);
            }
        }
        finally
        {
            _isSyncingSelection = false;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    private ListViewBase GetActiveView()
    {
        return _libraryKind == MediaLibraryItemKind.Audio
            ? MediaList
            : MediaGrid;
    }

    private bool IsDuplicate(string? filePath, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return _allItems.Any(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        return _allItems.Any(item => string.Equals(item.Title, Path.GetFileNameWithoutExtension(fallbackName), StringComparison.CurrentCultureIgnoreCase));
    }

    private async Task RecordPlayedAsync(MediaItemViewModel item)
    {
        try
        {
            using var scope = App.Services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
            await library.RecordPlayedAsync(item.Id);
        }
        catch
        {
            // Playback should not fail just because history persistence failed.
        }
    }

    internal static NewMediaLibraryItem CreateNewMediaLibraryItem(MediaItemViewModel item)
    {
        return new NewMediaLibraryItem
        {
            Name = item.Title,
            Path = item.FilePath,
            Extension = item.Extension,
            ContainerFormat = item.ContainerFormat,
            FileSize = item.FileSize,
            DateCreated = item.DateAdded,
            LastModified = TryGetLastModified(item.FilePath),
            Duration = item.Duration,
            Width = item.Width,
            Height = item.Height,
            Kind = item.Kind,
            ThumbnailPath = item.ThumbnailPath,
            Artist = item.Artist,
            Album = item.Album,
            Streams = item.Streams
        };
    }

    private static PlaybackQueueEntry CreatePlaybackQueueEntry(MediaItemViewModel item)
    {
        return new PlaybackQueueEntry(
            item.Id == Guid.Empty ? null : item.Id,
            item.Title,
            item.FilePath,
            item.Kind,
            item.Artist,
            item.Album,
            item.ThumbnailPath,
            item.PlaybackPosition);
    }

    private MediaItemViewModel CreateMediaItemViewModel(
        MediaLibraryItem item,
        long fileSize,
        DateTimeOffset? observedLastModified)
    {
        var title = FirstNonEmptyOrNull(
                item.Name,
                Path.GetFileNameWithoutExtension(item.Path))
            ?? string.Empty;

        return new MediaItemViewModel
        {
            Id = item.Id,
            Title = title,
            FilePath = item.Path,
            FileSize = fileSize,
            ContainerFormat = item.ContainerFormat,
            Streams = item.Streams,
            DateAdded = item.DateCreated,
            MetadataRefreshedAt = item.MetadataRefreshedAt,
            LastModified = item.LastModified,
            ObservedLastModified = observedLastModified,
            Duration = item.Duration,
            Width = item.Width,
            Height = item.Height,
            Kind = item.Kind,
            Extension = item.Extension,
            ThumbnailPath = item.ThumbnailPath,
            Artist = item.Artist,
            Album = item.Album,
            IsLike = item.IsFavorite,
            Rating = item.UserRating,
            PlaybackPosition = item.PlaybackPosition
        };
    }

    private static long TryGetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0L;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0L;
        }
    }

    private static DateTimeOffset? TryGetLastModified(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static bool NeedsThumbnailGeneration(MediaItemViewModel item)
    {
        if (item.Kind is not (MediaLibraryItemKind.Video or MediaLibraryItemKind.Audio or MediaLibraryItemKind.Image))
        {
            return false;
        }

        if (item.Kind == MediaLibraryItemKind.Video)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
            {
                return false;
            }

            var expectedThumbnailPath = GetVideoThumbnailCachePath(item.FilePath);
            return string.IsNullOrWhiteSpace(item.ThumbnailPath) ||
                !string.Equals(item.ThumbnailPath, expectedThumbnailPath, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(item.ThumbnailPath);
        }

        if (item.Kind == MediaLibraryItemKind.Audio)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
            {
                return false;
            }

            if (File.Exists(GetAudioThumbnailMissingMarkerPath(item.FilePath)))
            {
                return !string.IsNullOrWhiteSpace(item.ThumbnailPath);
            }

            return string.IsNullOrWhiteSpace(item.ThumbnailPath) ||
                !IsCurrentAudioThumbnailCachePath(item.FilePath, item.ThumbnailPath) ||
                !File.Exists(item.ThumbnailPath);
        }

        if (!string.IsNullOrWhiteSpace(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath);
    }

    private static async Task<string?> TryCreateThumbnailAsync(
        MediaItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (item.Kind == MediaLibraryItemKind.Audio)
            {
                return await TryCreateAudioThumbnailAsync(item.FilePath, metadata: null, cancellationToken);
            }

            var file = item.StorageFile;
            if (file is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                file = await StorageFile.GetFileFromPathAsync(item.FilePath);
            }

            return await TryCreateThumbnailAsync(file, item.Kind, item.FilePath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Failed to create thumbnail for '{item.FilePath}': {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> TryCreateThumbnailAsync(
        StorageFile file,
        MediaLibraryItemKind kind,
        string? fallbackPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (kind == MediaLibraryItemKind.Video)
            {
                var sourcePath = string.IsNullOrWhiteSpace(fallbackPath) ? file.Path : fallbackPath;
                return await TryCreateVideoThumbnailAsync(sourcePath, cancellationToken);
            }

            if (kind == MediaLibraryItemKind.Audio)
            {
                var sourcePath = string.IsNullOrWhiteSpace(fallbackPath) ? file.Path : fallbackPath;
                return await TryCreateAudioThumbnailAsync(sourcePath, metadata: null, cancellationToken);
            }

            var mode = GetThumbnailMode(kind);
            using var thumbnail = await file.GetThumbnailAsync(mode, ThumbnailSize);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return kind == MediaLibraryItemKind.Image ? fallbackPath : null;
            }

            var cacheFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
                ThumbnailCacheFolderName,
                CreationCollisionOption.OpenIfExists);
            var cacheFileName = CreateThumbnailCacheFileName(
                string.IsNullOrWhiteSpace(fallbackPath) ? file.Path : fallbackPath,
                thumbnail.ContentType);
            var cacheFile = await cacheFolder.CreateFileAsync(cacheFileName, CreationCollisionOption.ReplaceExisting);

            thumbnail.Seek(0);
            using var output = await cacheFile.OpenAsync(FileAccessMode.ReadWrite);
            await RandomAccessStream.CopyAsync(thumbnail, output);
            await output.FlushAsync();

            return cacheFile.Path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Failed to save thumbnail for '{fallbackPath ?? file.Path}': {ex.Message}");
            return kind == MediaLibraryItemKind.Image ? fallbackPath : null;
        }
    }

    private static async Task<string?> TryCreateVideoThumbnailAsync(
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        return await MediaThumbnailResolver.TryCreateVideoAsync(sourcePath, cancellationToken);
    }

    private static async Task<string?> TryCreateAudioThumbnailAsync(
        string? sourcePath,
        AudioFileMetadata? metadata,
        CancellationToken cancellationToken)
    {
        return await MediaThumbnailResolver.TryCreateAudioAsync(
            sourcePath,
            metadata,
            cancellationToken);
    }

    private static ThumbnailMode GetThumbnailMode(MediaLibraryItemKind kind)
    {
        return kind switch
        {
            MediaLibraryItemKind.Video => ThumbnailMode.VideosView,
            MediaLibraryItemKind.Image => ThumbnailMode.PicturesView,
            _ => ThumbnailMode.SingleItem
        };
    }

    private static string CreateThumbnailCacheFileName(string? sourcePath, string? contentType)
    {
        var lastModifiedTicks = TryGetLastModified(sourcePath)?.UtcTicks ?? 0L;
        var cacheKey = $"{sourcePath ?? string.Empty}|{lastModifiedTicks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
        return $"{hash}{GetThumbnailFileExtension(contentType)}";
    }

    private static string GetVideoThumbnailCachePath(string sourcePath)
    {
        return VideoThumbnailCache.GetPath(sourcePath);
    }

    private static bool IsCurrentAudioThumbnailCachePath(string sourcePath, string? thumbnailPath)
    {
        return MediaThumbnailResolver.IsCurrentAudioThumbnail(sourcePath, thumbnailPath);
    }

    private static string GetAudioThumbnailMissingMarkerPath(string sourcePath)
    {
        return MediaThumbnailResolver.GetAudioMissingMarkerPath(sourcePath);
    }

    private static string GetThumbnailFileExtension(string? contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            "image/tiff" => ".tiff",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
    }

    internal static async Task<MediaItemViewModel> CreateMediaItemAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        StorageFile? storageFile = null;
        try
        {
            storageFile = await StorageFile.GetFileFromPathAsync(filePath);
        }
        catch
        {
        }

        if (storageFile is not null)
        {
            return await CreateMediaItemAsync(storageFile, cancellationToken: cancellationToken);
        }

        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The media file does not exist.", filePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var mediaInfo = await TryGetVideoMediaInfoAsync(info.FullName, cancellationToken)
            ?? throw new InvalidDataException(
                $"FFmpeg could not read media information from '{info.FullName}'.");
        cancellationToken.ThrowIfCancellationRequested();
        var mediaKind = GetProbedMediaKind(mediaInfo, info.FullName);
        var audioMetadata = mediaKind == MediaLibraryItemKind.Audio
            ? await TryGetAudioMetadataAsync(info.FullName, cancellationToken)
            : null;
        var thumbnailPath = mediaKind == MediaLibraryItemKind.Audio
            ? await TryCreateAudioThumbnailAsync(info.FullName, audioMetadata, cancellationToken)
            : null;
        cancellationToken.ThrowIfCancellationRequested();
        return new MediaItemViewModel
        {
            Id = Guid.NewGuid(),
            Title = FirstNonEmptyOrNull(
                    audioMetadata?.Title,
                    mediaInfo.Title,
                    Path.GetFileNameWithoutExtension(info.Name))
                ?? Path.GetFileNameWithoutExtension(info.Name),
            FilePath = info.FullName,
            FileSize = mediaInfo.FileSize,
            DateAdded = DateTimeOffset.UtcNow,
            Duration = audioMetadata?.Duration ?? mediaInfo.Duration,
            Width = mediaInfo.Video?.Width,
            Height = mediaInfo.Video?.Height,
            Kind = mediaKind,
            Extension = info.Extension,
            ContainerFormat = audioMetadata?.FormatName ?? mediaInfo.ContainerFormat,
            ThumbnailPath = thumbnailPath,
            Artist = audioMetadata?.Artist,
            Album = audioMetadata?.Album,
            Streams = CreateImportedStreams(mediaInfo, audioMetadata)
        };
    }

    private static async Task<MediaItemViewModel> CreateMediaItemAsync(
        StorageFile file,
        string? knownPath = null,
        string? knownName = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = string.IsNullOrWhiteSpace(knownName) ? GetStorageFileName(file) : knownName;
        var filePath = string.IsNullOrWhiteSpace(knownPath) ? GetStorageFilePath(file) : knownPath;
        var fileType = GetStorageFileType(file);
        var extension = string.IsNullOrWhiteSpace(fileType) ? Path.GetExtension(fileName) : fileType;
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(filePath);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var mediaInfo = await TryGetVideoMediaInfoAsync(filePath, cancellationToken)
            ?? throw new InvalidDataException(
                $"FFmpeg could not read media information from '{filePath}'.");
        cancellationToken.ThrowIfCancellationRequested();
        var mediaKind = GetProbedMediaKind(mediaInfo, filePath);
        var audioMetadata = mediaKind == MediaLibraryItemKind.Audio
            ? await TryGetAudioMetadataAsync(filePath, cancellationToken)
            : null;
        var thumbnailPath = mediaKind == MediaLibraryItemKind.Audio
            ? await TryCreateAudioThumbnailAsync(filePath, audioMetadata, cancellationToken)
            : null;
        cancellationToken.ThrowIfCancellationRequested();

        return new MediaItemViewModel
        {
            Id = Guid.NewGuid(),
            StorageFile = file,
            Title = FirstNonEmptyOrNull(
                    audioMetadata?.Title,
                    mediaInfo.Title,
                    Path.GetFileNameWithoutExtension(fileName))
                ?? Path.GetFileNameWithoutExtension(fileName),
            FilePath = filePath,
            FileSize = mediaInfo.FileSize,
            DateAdded = DateTimeOffset.UtcNow,
            Duration = audioMetadata?.Duration ?? mediaInfo.Duration,
            Width = mediaInfo.Video?.Width,
            Height = mediaInfo.Video?.Height,
            Kind = mediaKind,
            Extension = extension,
            ContainerFormat = audioMetadata?.FormatName ?? mediaInfo.ContainerFormat,
            ThumbnailPath = thumbnailPath,
            Artist = audioMetadata?.Artist,
            Album = audioMetadata?.Album,
            Streams = CreateImportedStreams(mediaInfo, audioMetadata)
        };
    }

    private static IReadOnlyList<NewMediaLibraryStream> CreateImportedStreams(
        VideoMediaInfo mediaInfo,
        AudioFileMetadata? audioMetadata)
    {
        var streams = new List<NewMediaLibraryStream>(2);
        var usedIndexes = new HashSet<int>();
        if (mediaInfo.Video is { } video)
        {
            streams.Add(new NewMediaLibraryStream
            {
                Kind = MediaLibraryStreamKind.Video,
                StreamIndex = GetUniqueStreamIndex(video.Id, usedIndexes),
                Duration = mediaInfo.Duration,
                Codec = video.Codec,
                CodecProfile = video.CodecProfile,
                Language = video.Language,
                BitRate = video.BitRate,
                IsDefault = true,
                Title = video.Title,
                Width = video.Width,
                Height = video.Height,
                FrameRate = video.FrameRate,
                PixelFormat = video.PixelFormat,
                Rotation = video.Rotation
            });
        }

        if (mediaInfo.Audio is { } audio)
        {
            streams.Add(new NewMediaLibraryStream
            {
                Kind = MediaLibraryStreamKind.Audio,
                StreamIndex = GetUniqueStreamIndex(audio.Id, usedIndexes),
                Duration = audioMetadata?.Duration ?? mediaInfo.Duration,
                Codec = audioMetadata?.CodecName ?? audio.Codec,
                Language = audio.Language,
                BitRate = audioMetadata?.BitRate ?? audio.BitRate,
                IsDefault = true,
                Title = audio.Title,
                Channels = audioMetadata?.ChannelCount ?? audio.ChannelCount,
                ChannelLayout = audio.ChannelLayout,
                SampleRate = audioMetadata?.SampleRate ?? audio.SampleRate,
                BitDepth = audioMetadata?.BitsPerSample
            });
        }

        return streams;
    }

    private static int GetUniqueStreamIndex(long sourceIndex, HashSet<int> usedIndexes)
    {
        var index = sourceIndex is >= 0 and <= int.MaxValue ? (int)sourceIndex : 0;
        while (!usedIndexes.Add(index))
        {
            index++;
        }

        return index;
    }

    private static MediaLibraryItemKind GetProbedMediaKind(VideoMediaInfo? mediaInfo, string? filePath)
    {
        if (mediaInfo?.VideoTrackCount > 0)
        {
            return MediaLibraryItemKind.Video;
        }

        if (mediaInfo?.AudioTrackCount > 0)
        {
            return MediaLibraryItemKind.Audio;
        }

        throw new InvalidDataException(
            $"FFmpeg did not find a playable audio or video track in '{filePath}'.");
    }

    private static async Task<FfmpegMediaInfo?> TryGetFfmpegMediaInfoAsync(
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || App.Services is null)
        {
            return null;
        }

        var service = App.Services.GetService<IFfmpegMediaProbe>();
        if (service is null)
        {
            return null;
        }

        try
        {
            return await service.ProbeAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FFmpeg property probe failed for '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static async Task<VideoMediaInfo?> TryGetVideoMediaInfoAsync(
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || App.Services is null)
        {
            return null;
        }

        var service = App.Services.GetService<IVideoMediaInfoService>();
        if (service is null)
        {
            return null;
        }

        try
        {
            return await service.GetMediaInfoAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FFmpeg media information probe failed for '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static async Task<AudioFileMetadata?> TryGetAudioMetadataAsync(
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || App.Services is null)
        {
            return null;
        }

        var service = App.Services.GetService<IAudioMetadataService>();
        if (service is null)
        {
            return null;
        }

        try
        {
            return await service.GetMetadataAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FFmpeg audio metadata probe failed for '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static string GetStorageFileName(StorageFile file)
    {
        var localizedName = StaticResourceLoader.GetString("LibraryPage_UnknownFile");
        var unknownFileName = string.IsNullOrWhiteSpace(localizedName)
            ? "Unknown file"
            : localizedName;
        try
        {
            return string.IsNullOrWhiteSpace(file.Name) ? unknownFileName : file.Name;
        }
        catch
        {
            return unknownFileName;
        }
    }

    private static string GetStorageFilePath(StorageFile file)
    {
        try
        {
            return file.Path ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetStorageFileType(StorageFile file)
    {
        try
        {
            return file.FileType ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private IReadOnlySet<string> GetCurrentMediaExtensions()
    {
        return _libraryKind == MediaLibraryItemKind.Audio
            ? AudioExtensions
            : VideoExtensions;
    }

    internal static IReadOnlySet<string> GetSupportedMediaExtensions()
    {
        return AudioExtensions
            .Concat(VideoExtensions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool IsSupportedMediaFileForCurrentView(string filePath)
    {
        return GetCurrentMediaExtensions().Contains(Path.GetExtension(filePath));
    }

    private static MediaItemViewModel? GetMediaItemFromOriginalSource(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: MediaItemViewModel taggedItem })
            {
                return taggedItem;
            }

            if (current is SelectorItem { Content: MediaItemViewModel selectorItem })
            {
                return selectorItem;
            }

            if (current is FrameworkElement { DataContext: MediaItemViewModel dataContextItem })
            {
                return dataContextItem;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool HasVisualAncestor<T>(object originalSource)
        where T : DependencyObject
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is T)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryInitializePickerWithMainWindow(object picker, out string errorMessage)
    {
        if (App.MainWindow is not { } window)
        {
            errorMessage = GetResourceString(
                "LibraryPage_FilePickerNoWindow",
                "The file picker could not find the main application window.");
            return false;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            errorMessage = GetResourceString(
                "LibraryPage_FilePickerNoWindow",
                "The file picker could not find the main application window.");
            return false;
        }

        try
        {
            InitializeWithWindow.Initialize(picker, hwnd);
        }
        catch (ArgumentException ex)
        {
            errorMessage = string.Format(
                GetResourceString("LibraryPage_FilePickerInitializeFailed", "Failed to initialize the file picker: {0}"),
                ex.Message);
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private string GetResourceString(string key, string fallback)
    {
        try
        {
            var value = _resourceLoader.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load resource '{key}': {ex.Message}");
            return fallback;
        }
    }

    private async Task ShowErrorDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = GetResourceString("LibraryPage_Dialog_CloseButton", "Close"),
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowInfoDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = GetResourceString("LibraryPage_Dialog_CloseButton", "Close"),
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(bytes, 0);
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (propertyName == nameof(HasSelection))
        {
            UpdateSelectionBar();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class MediaItemViewModel : INotifyPropertyChanged
{
    private const int ThumbnailDecodePixelWidth = 320;
    private static readonly ResourceLoader ResourceLoader = new();
    private bool _isLike;
    private bool _isPointerOver;
    private bool _isSelected;
    private bool _isThumbnailSourceRequested;
    private bool _hasPresentedThumbnail;
    private UiBreakpoint _layoutBreakpoint = UiBreakpoint.Expanded;
    private string? _album;
    private string? _artist;
    private string _title = string.Empty;
    private int _rating;
    private string? _thumbnailPath;
    private ImageSource? _thumbnailSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; }

    public StorageFile? StorageFile { get; set; }

    public string Title
    {
        get => _title;
        set
        {
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (string.Equals(_title, normalizedValue, StringComparison.Ordinal))
            {
                return;
            }

            _title = normalizedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleToolTipText));
        }
    }

    public string FilePath { get; set; } = string.Empty;

    public string TitleToolTipText => LongTextToolTip.CreateMediaText(Title, FilePath);

    public string? Artist
    {
        get => _artist;
        set
        {
            if (_artist == value)
            {
                return;
            }

            _artist = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArtistDisplay));
        }
    }

    public string? Album
    {
        get => _album;
        set
        {
            if (_album == value)
            {
                return;
            }

            _album = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AlbumDisplay));
        }
    }

    public string ArtistDisplay => FormatAudioMetadata(
        "MediaInfo_Artist",
        "Artist: {0}",
        Artist,
        "AudioPlayer_UnknownArtist",
        "Unknown artist");

    public string AlbumDisplay => FormatAudioMetadata(
        "MediaInfo_Album",
        "Album: {0}",
        Album,
        "AudioPlayer_UnknownAlbum",
        "Unknown album");

    public long FileSize { get; set; }

    public string? ContainerFormat { get; set; }

    public IReadOnlyList<NewMediaLibraryStream> Streams { get; set; } = [];

    public DateTimeOffset DateAdded { get; set; }

    public DateTimeOffset? MetadataRefreshedAt { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public DateTimeOffset? ObservedLastModified { get; set; }

    public TimeSpan? PlaybackPosition { get; set; }

    public TimeSpan? Duration { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public MediaLibraryItemKind Kind { get; set; }

    public double ListThumbnailWidth => _layoutBreakpoint == UiBreakpoint.Compact
        ? 68
        : Kind == MediaLibraryItemKind.Audio ? 68 : 120;

    public string Extension { get; set; } = string.Empty;

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (_thumbnailPath == value)
            {
                return;
            }

            _thumbnailPath = value;
            _thumbnailSource = _isThumbnailSourceRequested
                ? CreateThumbnailSource(value)
                : null;
            if (_thumbnailSource is not null)
            {
                _hasPresentedThumbnail = true;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailSource));
            OnPropertyChanged(nameof(ThumbnailOpacity));
            OnPropertyChanged(nameof(HasThumbnail));
            OnPropertyChanged(nameof(NoThumbnailVisibility));
        }
    }

    public ImageSource? ThumbnailSource => _thumbnailSource;

    public double ThumbnailOpacity => _thumbnailSource is not null || _hasPresentedThumbnail
        ? 1
        : 0;

    public void ApplySnapshot(MediaItemViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Id = source.Id;
        StorageFile = source.StorageFile;
        Title = source.Title;
        FilePath = source.FilePath;
        Artist = source.Artist;
        Album = source.Album;
        FileSize = source.FileSize;
        ContainerFormat = source.ContainerFormat;
        Streams = source.Streams;
        DateAdded = source.DateAdded;
        MetadataRefreshedAt = source.MetadataRefreshedAt;
        LastModified = source.LastModified;
        ObservedLastModified = source.ObservedLastModified;
        PlaybackPosition = source.PlaybackPosition;
        Duration = source.Duration;
        Width = source.Width;
        Height = source.Height;
        Kind = source.Kind;
        Extension = source.Extension;
        ThumbnailPath = source.ThumbnailPath;
        IsLike = source.IsLike;
        Rating = source.Rating;

        OnPropertyChanged(nameof(TitleToolTipText));
        OnPropertyChanged(nameof(FormattedFileSize));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(PlaceholderGlyph));
        OnPropertyChanged(nameof(ResolutionVisibility));
        OnPropertyChanged(nameof(AudioMetadataVisibility));
        OnPropertyChanged(nameof(AlbumMetadataVisibility));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(KindLabelVisibility));
        OnPropertyChanged(nameof(ExtendedKindLabelVisibility));
        OnPropertyChanged(nameof(ListThumbnailWidth));
    }

    public void ApplyPersistedMetadata(
        NewMediaLibraryItem metadata,
        DateTimeOffset refreshedAt)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        Title = metadata.Name;
        Artist = metadata.Artist;
        Album = metadata.Album;
        FileSize = metadata.FileSize ?? FileSize;
        Duration = metadata.Duration;
        Width = metadata.Width;
        Height = metadata.Height;
        Extension = metadata.Extension;
        ContainerFormat = metadata.ContainerFormat;
        Streams = metadata.Streams;
        LastModified = metadata.LastModified;
        ObservedLastModified = metadata.LastModified;
        MetadataRefreshedAt = refreshedAt;
        OnPropertyChanged(nameof(FormattedFileSize));
        OnPropertyChanged(nameof(FormattedDuration));
        OnPropertyChanged(nameof(Resolution));
    }

    public bool HasThumbnail => ThumbnailSource is not null;

    public Visibility NoThumbnailVisibility => HasThumbnail ? Visibility.Collapsed : Visibility.Visible;

    public void EnsureThumbnailSource()
    {
        if (_isThumbnailSourceRequested &&
            (_thumbnailSource is not null || string.IsNullOrWhiteSpace(_thumbnailPath)))
        {
            return;
        }

        _isThumbnailSourceRequested = true;
        _thumbnailSource = CreateThumbnailSource(_thumbnailPath);
        if (_thumbnailSource is not null)
        {
            _hasPresentedThumbnail = true;
        }
        OnPropertyChanged(nameof(ThumbnailSource));
        OnPropertyChanged(nameof(ThumbnailOpacity));
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(NoThumbnailVisibility));
    }

    public void ReleaseThumbnailSource()
    {
        _isThumbnailSourceRequested = false;
        if (_thumbnailSource is null)
        {
            return;
        }

        _thumbnailSource = null;
        OnPropertyChanged(nameof(ThumbnailSource));
        OnPropertyChanged(nameof(ThumbnailOpacity));
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(NoThumbnailVisibility));
    }

    private static ImageSource? CreateThumbnailSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                uri = new Uri(Path.GetFullPath(path));
            }

            return new BitmapImage
            {
                DecodePixelWidth = ThumbnailDecodePixelWidth,
                UriSource = uri
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (UriFormatException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public string PlaceholderGlyph => Kind switch
    {
        MediaLibraryItemKind.Audio => "\uE8D6",
        MediaLibraryItemKind.Image => "\uEB9F",
        _ => "\uE714"
    };

    public string FormattedDuration => Duration is { } duration && duration > TimeSpan.Zero
        ? FormatDuration(duration)
        : "--:--";

    public string Resolution => Width is > 0 && Height is > 0 ? $"{Width}x{Height}" : "--";

    public Visibility ResolutionVisibility => Kind == MediaLibraryItemKind.Video
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility AudioMetadataVisibility => Kind == MediaLibraryItemKind.Audio
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility AlbumMetadataVisibility =>
        Kind == MediaLibraryItemKind.Audio && (int)_layoutBreakpoint >= (int)UiBreakpoint.Expanded
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility ExtendedMetadataVisibility => (int)_layoutBreakpoint >= (int)UiBreakpoint.Expanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RatingVisibility => (int)_layoutBreakpoint >= (int)UiBreakpoint.Expanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SecondaryActionVisibility => _layoutBreakpoint == UiBreakpoint.Compact
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string FormattedFileSize => FormatFileSize(FileSize);

    public string KindLabel => Kind switch
    {
        MediaLibraryItemKind.Video => GetResourceString("LibraryPage_KindVideo", "Video"),
        MediaLibraryItemKind.Audio => GetResourceString("LibraryPage_KindAudio", "Audio"),
        MediaLibraryItemKind.Image => GetResourceString("LibraryPage_KindImage", "Image"),
        _ => string.IsNullOrWhiteSpace(Extension)
            ? GetResourceString("LibraryPage_KindMedia", "Media")
            : Extension.TrimStart('.').ToUpperInvariant()
    };

    public Visibility KindLabelVisibility => Kind is MediaLibraryItemKind.Video or MediaLibraryItemKind.Audio
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ExtendedKindLabelVisibility =>
        ExtendedMetadataVisibility == Visibility.Visible
            ? KindLabelVisibility
            : Visibility.Collapsed;

    public void SetLayoutBreakpoint(UiBreakpoint breakpoint)
    {
        if (_layoutBreakpoint == breakpoint)
        {
            return;
        }

        _layoutBreakpoint = breakpoint;
        OnPropertyChanged(nameof(ListThumbnailWidth));
        OnPropertyChanged(nameof(AlbumMetadataVisibility));
        OnPropertyChanged(nameof(ExtendedMetadataVisibility));
        OnPropertyChanged(nameof(RatingVisibility));
        OnPropertyChanged(nameof(SecondaryActionVisibility));
        OnPropertyChanged(nameof(ExtendedKindLabelVisibility));
    }

    private static string FormatAudioMetadata(
        string formatResourceKey,
        string fallbackFormat,
        string? value,
        string unknownResourceKey,
        string fallbackUnknown)
    {
        var format = ResourceLoader.GetString(formatResourceKey);
        if (string.IsNullOrWhiteSpace(format))
        {
            format = fallbackFormat;
        }

        var displayValue = string.IsNullOrWhiteSpace(value)
            ? ResourceLoader.GetString(unknownResourceKey)
            : value.Trim();
        if (string.IsNullOrWhiteSpace(displayValue))
        {
            displayValue = fallbackUnknown;
        }

        return string.Format(format, displayValue);
    }

    public bool IsLike
    {
        get => _isLike;
        set
        {
            if (_isLike == value)
            {
                return;
            }

            _isLike = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LikeText));
            OnPropertyChanged(nameof(LikeIconGlyph));
            OnPropertyChanged(nameof(LikeForegroundBrush));
        }
    }

    public int Rating
    {
        get => _rating;
        set
        {
            if (_rating == value)
            {
                return;
            }

            _rating = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RatingValue));
        }
    }

    public double RatingValue => Rating;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckBoxOpacity));
            OnPropertyChanged(nameof(IsCheckBoxVisible));
        }
    }

    public bool IsPointerOver
    {
        get => _isPointerOver;
        set
        {
            if (_isPointerOver == value)
            {
                return;
            }

            _isPointerOver = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckBoxOpacity));
            OnPropertyChanged(nameof(IsCheckBoxVisible));
        }
    }

    public double CheckBoxOpacity => IsPointerOver || IsSelected ? 1.0 : 0.0;

    public bool IsCheckBoxVisible => IsPointerOver || IsSelected;

    public string LikeText => IsLike
        ? GetResourceString("LibraryPage_Unlike", "Unlike")
        : GetResourceString("LibraryPage_Like", "Like");

    public string LikeIconGlyph => IsLike ? "\uEB52" : "\uEB51";

    public Brush LikeForegroundBrush => GetBrush(
        IsLike ? "KoukeiFavoriteActiveBrush" : "KoukeiPlaybackAccentBrush");

    private static Brush GetBrush(string resourceKey)
    {
        return Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private static string GetResourceString(string key, string fallback)
    {
        var value = ResourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(bytes, 0);
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
