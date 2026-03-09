using Koukei.Bus.Models;
using Koukei.Bus.Services;
using Koukei.UI.Helpers;
using Koukei.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Koukei.UI.Pages;

public sealed partial class PlaylistDetailPage : Page, INotifyPropertyChanged
{
    private readonly record struct PlaylistImportCounts(
        int Imported,
        int Failed,
        int AddedToPlaylist,
        int AlreadyInPlaylist)
    {
        public PlaylistImportCounts Add(PlaylistImportCounts other) => new(
            Imported + other.Imported,
            Failed + other.Failed,
            AddedToPlaylist + other.AddedToPlaylist,
            AlreadyInPlaylist + other.AlreadyInPlaylist);
    }

    private const int ImportBatchSize = 32;
    private readonly List<PlaylistDetailItemViewModel> _allItems = [];
    private readonly LatestOperationController _loadController = new();
    private readonly ResourceLoader _resourceLoader = new();
    private readonly string _retryText;
    private Guid _playlistId;
    private int _loadRequestId;
    private string _playlistName = string.Empty;
    private string _playlistDescription = string.Empty;
    private string? _loadErrorMessage;
    private bool _isLoading;
    private bool _isBusy;
    private bool _isViewInitialized;
    private bool _isSortDescending;
    private string _sortField = "PlaylistOrder";
    private CancellationTokenSource? _importCancellation;
    private PlaylistDetailItemViewModel? _draggedItem;
    private int _draggedItemOriginalIndex = -1;
    private PlaybackCoordinator? _playbackCoordinator;

    public PlaylistDetailPage()
    {
        InitializeComponent();
        _isViewInitialized = true;
        _retryText = StatePresenter.RetryText;
        UpdateSortState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlaylistDetailItemViewModel> Items { get; } = [];

    public string PlaylistName
    {
        get => _playlistName;
        private set
        {
            if (_playlistName == value)
            {
                return;
            }

            _playlistName = value;
            OnPropertyChanged();
        }
    }

    public string PlaylistDescription
    {
        get => _playlistDescription;
        private set
        {
            if (_playlistDescription == value)
            {
                return;
            }

            _playlistDescription = value;
            OnPropertyChanged();
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not Guid playlistId || playlistId == Guid.Empty)
        {
            _loadErrorMessage = GetResourceString(
                "PlaylistDetailPage_InvalidPlaylist",
                "The playlist could not be opened.");
            UpdatePageState();
            FooterStatusBar.ShowBusy(
                GetResourceString("Common_PageStatus_LoadFailed", "Failed to load"));
            return;
        }

        _playlistId = playlistId;
        if (App.Services is not null)
        {
            _playbackCoordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
            _playbackCoordinator.PlaybackQueueChanged += PlaybackCoordinator_PlaybackQueueChanged;
        }

        _ = LoadPlaylistAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _loadRequestId++;
        _loadController.Cancel();
        _importCancellation?.Cancel();
        if (_playbackCoordinator is not null)
        {
            _playbackCoordinator.PlaybackQueueChanged -= PlaybackCoordinator_PlaybackQueueChanged;
            _playbackCoordinator = null;
        }

        base.OnNavigatedFrom(e);
    }

    private async Task LoadPlaylistAsync()
    {
        if (_playlistId == Guid.Empty || App.Services is null)
        {
            return;
        }

        var requestId = ++_loadRequestId;
        var hadItems = _allItems.Count > 0;
        _isLoading = true;
        _loadErrorMessage = null;
        OperationInfoBar.IsOpen = false;
        FooterStatusBar.ShowBusy(
            GetResourceString(
                hadItems
                    ? "Common_PageStatus_Refreshing"
                    : "Common_PageStatus_Loading",
                hadItems ? "Refreshing..." : "Loading..."));
        UpdatePageState();

        await _loadController.RunAsync(
            async cancellationToken =>
            {
                using var scope = App.Services.CreateScope();
                var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
                return await playlistBus.GetAsync(_playlistId, cancellationToken)
                    ?? throw new InvalidOperationException(
                        GetResourceString(
                            "PlaylistDetailPage_NotFound",
                            "The playlist no longer exists."));
            },
            ApplyPlaylist,
            exception => _loadErrorMessage = exception.Message);

        if (requestId != _loadRequestId)
        {
            return;
        }

        _isLoading = false;
        UpdatePageState();
        if (!string.IsNullOrWhiteSpace(_loadErrorMessage) && _allItems.Count == 0)
        {
            FooterStatusBar.ShowBusy(
                GetResourceString("Common_PageStatus_LoadFailed", "Failed to load"));
        }
        else
        {
            FooterStatusBar.ClearOverride();
        }

        if (_allItems.Count > 0 && !string.IsNullOrWhiteSpace(_loadErrorMessage))
        {
            OperationInfoBar.Severity = InfoBarSeverity.Error;
            OperationInfoBar.Title = GetResourceString(
                "PlaylistDetailPage_RefreshFailed",
                "Could not refresh the playlist");
            OperationInfoBar.Message = _loadErrorMessage;
            OperationInfoBar.IsOpen = true;
        }

        if (!hadItems && Items.Count > 0)
        {
            _ = DispatcherQueue.TryEnqueue(() =>
                _ = MotionHelper.AnimateVisibleItemsEntranceAsync(ItemList));
        }
    }

    private void ApplyPlaylist(PlaylistDetail playlist)
    {
        PlaylistName = playlist.Name;
        PlaylistDescription = string.IsNullOrWhiteSpace(playlist.Description)
            ? GetResourceString("PlaylistsPage_NoDescription", "No description")
            : playlist.Description;
        ReconcileAllItems(playlist.Items.Select(item => new PlaylistDetailItemViewModel(item)).ToArray());
        RefreshVisibleItems();
        UpdatePlaybackState();
        UpdateStatus();
    }

    private void ReconcileAllItems(IReadOnlyList<PlaylistDetailItemViewModel> desiredItems)
    {
        for (var targetIndex = 0; targetIndex < desiredItems.Count; targetIndex++)
        {
            var desiredItem = desiredItems[targetIndex];
            if (targetIndex < _allItems.Count &&
                _allItems[targetIndex].PlaylistItemId == desiredItem.PlaylistItemId)
            {
                _allItems[targetIndex].ApplySnapshot(desiredItem);
                continue;
            }

            var currentIndex = -1;
            for (var index = targetIndex + 1; index < _allItems.Count; index++)
            {
                if (_allItems[index].PlaylistItemId == desiredItem.PlaylistItemId)
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                var existing = _allItems[currentIndex];
                _allItems.RemoveAt(currentIndex);
                _allItems.Insert(targetIndex, existing);
                existing.ApplySnapshot(desiredItem);
            }
            else
            {
                _allItems.Insert(targetIndex, desiredItem);
            }
        }

        while (_allItems.Count > desiredItems.Count)
        {
            _allItems.RemoveAt(_allItems.Count - 1);
        }
    }

    private void RefreshVisibleItems()
    {
        var selectedIds = ItemList.SelectedItems
            .OfType<PlaylistDetailItemViewModel>()
            .Select(item => item.PlaylistItemId)
            .ToHashSet();
        ItemList.SelectedItems.Clear();
        var searchText = SearchBox.Text.Trim();
        IEnumerable<PlaylistDetailItemViewModel> query = _allItems;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(item =>
                item.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                item.SecondaryText.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                item.FilePath.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                item.KindLabel.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));
        }

        query = _sortField switch
        {
            "Title" when _isSortDescending => query
                .OrderByDescending(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            "Title" => query.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            "Duration" when _isSortDescending => query
                .OrderByDescending(item => item.Duration ?? TimeSpan.MinValue),
            "Duration" => query.OrderBy(item => item.Duration ?? TimeSpan.MaxValue),
            "Kind" when _isSortDescending => query
                .OrderByDescending(item => item.KindLabel, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            "Kind" => query
                .OrderBy(item => item.KindLabel, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
            "PlaylistOrder" when _isSortDescending => query.Reverse(),
            _ => query
        };

        ReconcileVisibleItems(query.ToArray());
        foreach (var item in Items.Where(item => selectedIds.Contains(item.PlaylistItemId)))
        {
            ItemList.SelectedItems.Add(item);
        }

        UpdatePageState();
        UpdateStatus();
    }

    private void ReconcileVisibleItems(IReadOnlyList<PlaylistDetailItemViewModel> desiredItems)
    {
        for (var targetIndex = 0; targetIndex < desiredItems.Count; targetIndex++)
        {
            var desiredItem = desiredItems[targetIndex];
            if (targetIndex < Items.Count &&
                Items[targetIndex].PlaylistItemId == desiredItem.PlaylistItemId)
            {
                continue;
            }

            var currentIndex = -1;
            for (var index = targetIndex + 1; index < Items.Count; index++)
            {
                if (Items[index].PlaylistItemId == desiredItem.PlaylistItemId)
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                Items.Move(currentIndex, targetIndex);
            }
            else
            {
                Items.Insert(targetIndex, desiredItem);
            }
        }

        while (Items.Count > desiredItems.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }
    }

    private void UpdatePageState()
    {
        var hasAnyItems = _allItems.Count > 0;
        var hasVisibleItems = Items.Count > 0;
        var hasSearch = !string.IsNullOrWhiteSpace(SearchBox.Text);
        var isInitialLoading = _isLoading && !hasAnyItems;
        var isInitialError = !_isLoading &&
            !hasAnyItems &&
            !string.IsNullOrWhiteSpace(_loadErrorMessage);
        StatePresenter.State = isInitialLoading
            ? PageViewState.InitialLoading
            : isInitialError
                ? PageViewState.Error
                : !hasAnyItems
                    ? PageViewState.Empty
                    : !hasVisibleItems && hasSearch
                        ? PageViewState.NoResults
                    : _isLoading
                        ? PageViewState.Refreshing
                        : PageViewState.Content;
        StatePresenter.Title = isInitialLoading
            ? GetResourceString("PlaylistDetailPage_Loading", "Loading playlist")
            : isInitialError
                ? GetResourceString("PlaylistDetailPage_LoadFailed", "Could not load playlist")
                : !hasAnyItems
                    ? GetResourceString("PlaylistDetailPage_EmptyTitleMessage", "This playlist is empty")
                    : !hasVisibleItems && hasSearch
                        ? GetResourceString("PlaylistDetailPage_NoResultsTitle", "No matching items")
                    : string.Empty;
        StatePresenter.Description = isInitialError
            ? _loadErrorMessage ?? string.Empty
            : !hasAnyItems
                ? GetResourceString(
                    "PlaylistDetailPage_EmptyDescriptionMessage",
                    "Use Add files or Add folder to add audio and video.")
                : !hasVisibleItems && hasSearch
                    ? GetResourceString(
                        "PlaylistDetailPage_NoResultsDescription",
                        "Try another title, artist, album, file name, or media type.")
                : string.Empty;
        StatePresenter.RetryText = isInitialError ? _retryText : string.Empty;
        UpdateCommandState();
    }

    private void UpdateCommandState()
    {
        AddFilesButton.IsEnabled = !_isBusy && App.Services is not null;
        AddFolderButton.IsEnabled = !_isBusy && App.Services is not null;
        RefreshButton.IsEnabled = !_isBusy && !_isLoading;
        SearchBox.IsEnabled = !_isBusy && !_isLoading;
        SortButton.IsEnabled = !_isBusy && !_isLoading;
        ItemList.IsEnabled = !_isBusy;
        var canReorder = !_isBusy &&
            string.IsNullOrWhiteSpace(SearchBox.Text) &&
            _sortField == "PlaylistOrder" &&
            !_isSortDescending;
        ItemList.CanDragItems = canReorder;
        ItemList.CanReorderItems = canReorder;
        ItemList.ReorderMode = canReorder ? ListViewReorderMode.Enabled : ListViewReorderMode.Disabled;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        if (!_isViewInitialized ||
            ItemList is null ||
            SelectedCountText is null ||
            SelectionActionBar is null)
        {
            return;
        }

        var selectedItems = GetOrderedSelectedItems();
        var selectedIds = selectedItems
            .Select(item => item.PlaylistItemId)
            .ToHashSet();
        foreach (var item in _allItems)
        {
            item.IsSelected = selectedIds.Contains(item.PlaylistItemId);
        }

        var hasSelection = selectedItems.Count > 0;
        var hasPlayableSelection = selectedItems.Any(item => item.IsPlayable);
        SelectedCountText.Text = selectedItems.Count.ToString();
        SelectionActionBar.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        SelectionActionBar.IsEnabled = !_isBusy;
        PlaySelectedButton.IsEnabled = !_isBusy && hasPlayableSelection;
        QueueSelectedButton.IsEnabled = !_isBusy && hasPlayableSelection;
        RemoveSelectedButton.IsEnabled = !_isBusy && hasSelection;
        UpdateFooterSummary();
    }

    private void UpdateStatus()
    {
        TotalCountText.Text = _allItems.Count.ToString();
        UpdateSelectionState();
    }

    private void UpdateFooterSummary()
    {
        if (!_isViewInitialized || FooterStatusBar is null || ItemList is null)
        {
            return;
        }

        if (_allItems.Count == 0)
        {
            FooterStatusBar.SetSummary(
                GetResourceString("Common_PageStatus_Empty", "0 items"));
            return;
        }

        var selectedCount = ItemList.SelectedItems.Count;
        var playableCount = _allItems.Count(item => item.IsPlayable);
        FooterStatusBar.SetSummary(
            selectedCount > 0
                ? string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_StatusSummaryWithSelection",
                        "{0} shown / {1} total · {2} playable · {3} selected"),
                    Items.Count,
                    _allItems.Count,
                    playableCount,
                    selectedCount)
                : string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_StatusSummary",
                        "{0} shown / {1} total · {2} playable"),
                    Items.Count,
                    _allItems.Count,
                    playableCount));
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isViewInitialized ||
            HeaderPanel is null ||
            OperationInfoBar is null ||
            ContentRegion is null ||
            SearchAndSortPanel is null)
        {
            return;
        }

        var breakpoint = ResponsiveLayout.Resolve(e.NewSize.Width);
        var stackCommands = breakpoint is UiBreakpoint.Compact or UiBreakpoint.Medium;
        var padding = ResponsiveLayout.GetPagePadding(breakpoint);

        Grid.SetRow(SearchAndSortPanel, stackCommands ? 1 : 0);
        Grid.SetColumn(SearchAndSortPanel, stackCommands ? 0 : 1);
        Grid.SetColumnSpan(SearchAndSortPanel, stackCommands ? 2 : 1);
        SearchAndSortPanel.Margin = stackCommands
            ? new Thickness(0)
            : new Thickness(16, 0, 0, 0);
        SearchBox.Width = stackCommands
            ? double.NaN
            : breakpoint == UiBreakpoint.Wide ? 320 : 240;
        SearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        SortButtonLabel.Visibility = breakpoint == UiBreakpoint.Compact
            ? Visibility.Collapsed
            : Visibility.Visible;
        HeaderPanel.Padding = new Thickness(padding.Left, 20, padding.Right, 0);
        OperationInfoBar.Margin = new Thickness(padding.Left, 8, padding.Right, 0);
        ContentRegion.Padding = new Thickness(padding.Left, 0, padding.Right, 16);
        StatisticsPanel.Orientation = stackCommands
            ? Orientation.Vertical
            : Orientation.Horizontal;
    }

    private void SearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            RefreshVisibleItems();
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
        RefreshVisibleItems();
    }

    private void UpdateSortState()
    {
        if (!_isViewInitialized || SortButtonLabel is null || SortDirectionIcon is null)
        {
            return;
        }

        SortButtonLabel.Text = _sortField switch
        {
            "Title" => GetResourceString("LibraryPage_SortCurrent_Title", "Title"),
            "Duration" => GetResourceString("LibraryPage_SortCurrent_Duration", "Duration"),
            "Kind" => GetResourceString("PlaylistDetailPage_SortCurrent_Kind", "Media type"),
            _ => GetResourceString("PlaylistDetailPage_SortCurrent_PlaylistOrder", "Playlist order")
        };
        SortDirectionIcon.Glyph = _isSortDescending ? "\uE70D" : "\uE70E";
    }

    private Task AddFilesAsync()
    {
        return RunImportOperationAsync(
            async cancellationToken =>
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.Thumbnail,
                    SuggestedStartLocation = PickerLocationId.VideosLibrary
                };
                foreach (var extension in MediaLibraryPage
                    .GetSupportedMediaExtensions()
                    .OrderBy(static extension => extension))
                {
                    picker.FileTypeFilter.Add(extension);
                }

                InitializePicker(picker);
                var files = await picker.PickMultipleFilesAsync();
                if (files is not { Count: > 0 })
                {
                    return;
                }

                var paths = files
                    .Select(file =>
                    {
                        try
                        {
                            return file.Path;
                        }
                        catch
                        {
                            return string.Empty;
                        }
                    })
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();
                await ImportPathsToPlaylistAsync(paths, cancellationToken);
            },
            GetResourceString(
                "PlaylistDetailPage_AddFilesFailed",
                "Could not add files to the playlist"));
    }

    private Task AddFolderAsync()
    {
        return RunImportOperationAsync(
            async cancellationToken =>
            {
                var picker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.VideosLibrary
                };
                picker.FileTypeFilter.Add("*");
                InitializePicker(picker);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
                {
                    return;
                }

                FooterStatusBar.ShowBusy(
                    GetResourceString(
                        "Common_PageStatus_Scanning",
                        "Scanning folder..."));
                var supportedExtensions = MediaLibraryPage.GetSupportedMediaExtensions();
                var paths = await Task.Run(
                    () => Directory
                        .EnumerateFiles(
                            folder.Path,
                            "*.*",
                            new EnumerationOptions
                            {
                                RecurseSubdirectories = true,
                                IgnoreInaccessible = true
                            })
                        .Where(path => supportedExtensions.Contains(Path.GetExtension(path)))
                        .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
                        .ToArray(),
                    cancellationToken);
                await ImportPathsToPlaylistAsync(paths, cancellationToken);
            },
            GetResourceString(
                "PlaylistDetailPage_AddFolderFailed",
                "Could not add the folder to the playlist"));
    }

    private async Task RunImportOperationAsync(
        Func<CancellationToken, Task> operation,
        string errorTitle)
    {
        if (_isBusy || App.Services is null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        var previousCancellation = _importCancellation;
        _importCancellation = cancellation;
        previousCancellation?.Cancel();

        try
        {
            await RunOperationAsync(
                () => operation(cancellation.Token),
                errorTitle);
        }
        finally
        {
            if (ReferenceEquals(_importCancellation, cancellation))
            {
                _importCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task ImportPathsToPlaylistAsync(
        IReadOnlyList<string> requestedPaths,
        CancellationToken cancellationToken)
    {
        var supportedExtensions = MediaLibraryPage.GetSupportedMediaExtensions();
        var paths = requestedPaths
            .Where(path =>
                !string.IsNullOrWhiteSpace(path) &&
                supportedExtensions.Contains(Path.GetExtension(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            OperationInfoBar.Severity = InfoBarSeverity.Informational;
            OperationInfoBar.Title = GetResourceString(
                "PlaylistDetailPage_NoFilesTitle",
                "No media files");
            OperationInfoBar.Message = GetResourceString(
                "PlaylistDetailPage_NoFilesMessage",
                "No supported audio or video files were found.");
            OperationInfoBar.IsOpen = true;
            FooterStatusBar.ClearOverride();
            return;
        }

        FooterStatusBar.ShowBusy(
            GetResourceString("Common_PageStatus_Working", "Working..."));
        var counts = new PlaylistImportCounts();
        using var scope = App.Services.CreateScope();
        var mediaLibrary = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
        var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
        var progressFormat = GetResourceString(
            "PlaylistDetailPage_ImportProgress",
            "Processing {0}/{1}: {2}");

        for (var offset = 0; offset < paths.Length; offset += ImportBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = paths
                .Skip(offset)
                .Take(ImportBatchSize)
                .ToArray();
            var existingPaths = await mediaLibrary.GetExistingPathsAsync(
                batch,
                cancellationToken);
            var itemsToImport = new List<NewMediaLibraryItem>(batch.Length);
            var failedCount = 0;

            for (var index = 0; index < batch.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = batch[index];
                FooterStatusBar.ShowBusy(string.Format(
                    progressFormat,
                    offset + index + 1,
                    paths.Length,
                    Path.GetFileName(path)));
                if (existingPaths.Contains(path))
                {
                    continue;
                }

                try
                {
                    var item = await MediaLibraryPage.CreateMediaItemAsync(
                        path,
                        cancellationToken);
                    if (item.Kind is MediaLibraryItemKind.Audio or MediaLibraryItemKind.Video)
                    {
                        itemsToImport.Add(MediaLibraryPage.CreateNewMediaLibraryItem(item));
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to import '{path}' into playlist: {ex.Message}");
                    failedCount++;
                }
            }

            MediaLibraryImportResult? importResult = null;
            if (itemsToImport.Count > 0)
            {
                importResult = await mediaLibrary.ImportAsync(
                    itemsToImport,
                    cancellationToken);
                App.Services
                    .GetRequiredService<PlaybackCoordinator>()
                    .SynchronizeQueueItems(
                        importResult.AddedItems
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
            }

            var persistedItems = await mediaLibrary.GetPlaybackItemsByPathsAsync(
                batch,
                cancellationToken);
            var persistedByPath = persistedItems
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            var orderedPersistedItems = batch
                .Where(persistedByPath.ContainsKey)
                .Select(path => persistedByPath[path])
                .ToArray();
            var addResult = await playlistBus.AddItemsAsync(
                _playlistId,
                orderedPersistedItems.Select(item => item.Id).ToArray(),
                cancellationToken);
            counts = counts.Add(new PlaylistImportCounts(
                importResult?.AddedItems.Count ?? 0,
                failedCount + addResult.MissingCount,
                addResult.AddedCount,
                addResult.DuplicateCount));
        }

        await LoadPlaylistAsync();
        FooterStatusBar.ShowTransient(string.Format(
            GetResourceString(
                "PlaylistDetailPage_ImportResult",
                "Added {0} items; {1} already present; imported {2}; failed {3}"),
            counts.AddedToPlaylist,
            counts.AlreadyInPlaylist,
            counts.Imported,
            counts.Failed));
    }

    private void InitializePicker(object picker)
    {
        if (App.MainWindow is not { } window)
        {
            throw new InvalidOperationException(
                GetResourceString(
                    "LibraryPage_FilePickerNoWindow",
                    "The file picker could not find the main application window."));
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                GetResourceString(
                    "LibraryPage_FilePickerNoWindow",
                    "The file picker could not find the main application window."));
        }

        try
        {
            InitializeWithWindow.Initialize(picker, hwnd);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                string.Format(
                    GetResourceString(
                        "LibraryPage_FilePickerInitializeFailed",
                        "Failed to initialize the file picker: {0}"),
                    ex.Message),
                ex);
        }
    }

    private IReadOnlyList<PlaylistDetailItemViewModel> GetOrderedSelectedItems()
    {
        var selectedIds = ItemList.SelectedItems
            .OfType<PlaylistDetailItemViewModel>()
            .Select(item => item.PlaylistItemId)
            .ToHashSet();
        return Items
            .Where(item => selectedIds.Contains(item.PlaylistItemId))
            .ToArray();
    }

    private IReadOnlyList<PlaybackQueueEntry> CreatePlayableEntries() =>
        CreatePlayableEntries(Items);

    private static IReadOnlyList<PlaybackQueueEntry> CreatePlayableEntries(
        IEnumerable<PlaylistDetailItemViewModel> source)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return source
            .Where(item => item.IsPlayable && paths.Add(item.FilePath))
            .Select(CreatePlaybackQueueEntry)
            .ToArray();
    }

    private static PlaybackQueueEntry CreatePlaybackQueueEntry(
        PlaylistDetailItemViewModel item) =>
        new(
            item.MediaId,
            item.Title,
            item.FilePath,
            item.Kind,
            item.Artist,
            item.Album,
            item.ThumbnailPath,
            item.PlaybackPosition);

    private async Task PlayAsync(PlaylistDetailItemViewModel? startItem)
    {
        if (_isBusy || _playbackCoordinator is null)
        {
            return;
        }

        var entries = CreatePlayableEntries();
        if (entries.Count == 0)
        {
            return;
        }

        var startIndex = 0;
        if (startItem is not null)
        {
            startIndex = entries
                .Select((item, index) => (item, index))
                .FirstOrDefault(pair =>
                    pair.item.MediaId == startItem.MediaId ||
                    string.Equals(
                        pair.item.FilePath,
                        startItem.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                .index;
        }

        await RunOperationAsync(
            async () =>
            {
                await _playbackCoordinator.PlayPlaylistAsync(entries, startIndex);
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_Playing",
                        "Playing {0} items"),
                    entries.Count));
            },
            GetResourceString(
                "PlaylistDetailPage_PlayFailed",
                "Could not play the playlist"));
    }

    private async Task QueueAllAsync()
    {
        if (_isBusy || _playbackCoordinator is null)
        {
            return;
        }

        var entries = CreatePlayableEntries();
        if (entries.Count == 0)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                foreach (var entry in entries)
                {
                    await _playbackCoordinator.EnqueueItemAsync(entry);
                }

                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_Queued",
                        "Added {0} items to the playback queue"),
                    entries.Count));
            },
            GetResourceString(
                "PlaylistDetailPage_QueueFailed",
                "Could not add the playlist to the playback queue"));
    }

    private async Task PlaySelectedAsync()
    {
        if (_isBusy || _playbackCoordinator is null)
        {
            return;
        }

        var entries = CreatePlayableEntries(GetOrderedSelectedItems());
        if (entries.Count == 0)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                await _playbackCoordinator.PlayPlaylistAsync(entries, startIndex: 0);
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_PlayingSelected",
                        "Playing {0} selected items"),
                    entries.Count));
            },
            GetResourceString(
                "PlaylistDetailPage_PlaySelectedFailed",
                "Could not play the selected items"));
    }

    private async Task QueueSelectedAsync()
    {
        if (_isBusy || _playbackCoordinator is null)
        {
            return;
        }

        var entries = CreatePlayableEntries(GetOrderedSelectedItems());
        if (entries.Count == 0)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                foreach (var entry in entries)
                {
                    await _playbackCoordinator.EnqueueItemAsync(entry);
                }

                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_QueuedSelected",
                        "Added {0} selected items to the playback queue"),
                    entries.Count));
            },
            GetResourceString(
                "PlaylistDetailPage_QueueSelectedFailed",
                "Could not add the selected items to the playback queue"));
    }

    private async Task QueueItemAsync(PlaylistDetailItemViewModel item)
    {
        if (_isBusy || _playbackCoordinator is null || !item.IsPlayable)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                await _playbackCoordinator.EnqueueItemAsync(CreatePlaybackQueueEntry(item));
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_QueuedItem",
                        "Added \"{0}\" to the playback queue"),
                    item.Title));
            },
            GetResourceString(
                "PlaylistDetailPage_QueueItemFailed",
                "Could not add the item to the playback queue"));
    }

    private async Task RemoveItemAsync(PlaylistDetailItemViewModel item)
    {
        await RunOperationAsync(
            async () =>
            {
                using var scope = App.Services.CreateScope();
                var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
                await playlistBus.RemoveItemAsync(item.PlaylistItemId);
                await LoadPlaylistAsync();
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_RemovedItem",
                        "Removed \"{0}\""),
                    item.Title));
            },
            GetResourceString(
                "PlaylistDetailPage_RemoveFailed",
                "Could not remove the item"));
    }

    private async Task RemoveSelectedAsync()
    {
        var selectedItems = GetOrderedSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                using var scope = App.Services.CreateScope();
                var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
                foreach (var item in selectedItems)
                {
                    await playlistBus.RemoveItemAsync(item.PlaylistItemId);
                }

                await LoadPlaylistAsync();
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistDetailPage_RemovedSelected",
                        "Removed {0} selected items"),
                    selectedItems.Count));
            },
            GetResourceString(
                "PlaylistDetailPage_RemoveSelectedFailed",
                "Could not remove the selected items"));
    }

    private async Task ClearAsync()
    {
        var dialog = new ContentDialog
        {
            Title = GetResourceString("PlaylistDetailPage_ClearDialogTitle", "Clear playlist"),
            Content = GetResourceString(
                "PlaylistDetailPage_ClearDialogMessage",
                "Remove every item from this playlist?"),
            PrimaryButtonText = GetResourceString("PlaylistDetailPage_ClearButtonText", "Clear"),
            CloseButtonText = GetResourceString("PlaylistsPage_Dialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                using var scope = App.Services.CreateScope();
                var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
                await playlistBus.ClearAsync(_playlistId);
                await LoadPlaylistAsync();
                FooterStatusBar.ShowTransient(
                    GetResourceString(
                        "PlaylistDetailPage_Cleared",
                        "Playlist cleared"));
            },
            GetResourceString(
                "PlaylistDetailPage_ClearFailed",
                "Could not clear the playlist"));
    }

    private async Task RunOperationAsync(Func<Task> operation, string errorTitle)
    {
        if (_isBusy || App.Services is null)
        {
            return;
        }

        _isBusy = true;
        OperationInfoBar.IsOpen = false;
        UpdateCommandState();
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            FooterStatusBar.ClearOverride();
        }
        catch (Exception ex)
        {
            FooterStatusBar.ClearOverride();
            OperationInfoBar.Severity = InfoBarSeverity.Error;
            OperationInfoBar.Title = errorTitle;
            OperationInfoBar.Message = ex.Message;
            OperationInfoBar.IsOpen = true;
        }
        finally
        {
            _isBusy = false;
            UpdatePageState();
        }
    }

    private void PlaybackCoordinator_PlaybackQueueChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(UpdatePlaybackState);
    }

    private void UpdatePlaybackState()
    {
        var current = _playbackCoordinator?.PlaybackQueue.FirstOrDefault(item => item.IsCurrent);
        foreach (var item in _allItems)
        {
            item.IsCurrent = current is not null &&
                ((current.MediaId is not null && current.MediaId == item.MediaId) ||
                 string.Equals(current.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        NavigateBack();

    private async void AddFilesButton_Click(object sender, RoutedEventArgs e) =>
        await AddFilesAsync();

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e) =>
        await AddFolderAsync();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        _ = LoadPlaylistAsync();

    private async void PlaySelectedButton_Click(object sender, RoutedEventArgs e) =>
        await PlaySelectedAsync();

    private async void QueueSelectedButton_Click(object sender, RoutedEventArgs e) =>
        await QueueSelectedAsync();

    private async void RemoveSelectedButton_Click(object sender, RoutedEventArgs e) =>
        await RemoveSelectedAsync();

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        ItemList.SelectedItems.Clear();
        ItemList.Focus(FocusState.Programmatic);
    }

    private void StatePresenter_RetryRequested(object? sender, EventArgs e) =>
        _ = LoadPlaylistAsync();

    private async void PlayItemMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PlaylistDetailItemViewModel item })
        {
            await PlayAsync(item);
        }
    }

    private async void QueueItemMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PlaylistDetailItemViewModel item })
        {
            await QueueItemAsync(item);
        }
    }

    private async void RemoveItemMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PlaylistDetailItemViewModel item })
        {
            await RemoveItemAsync(item);
        }
    }

    private async void ItemList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetItemFromOriginalSource(e.OriginalSource) is { IsPlayable: true } item)
        {
            e.Handled = true;
            await PlayAsync(item);
        }
    }

    private async void ItemList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not PlaylistDetailItemViewModel item)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter && item.IsPlayable)
        {
            e.Handled = true;
            await PlayAsync(item);
        }
        else if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            await RemoveSelectedAsync();
        }
    }

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionState();

    private void ItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: PlaylistDetailItemViewModel item } checkBox)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            if (!ItemList.SelectedItems.Contains(item))
            {
                ItemList.SelectedItems.Add(item);
            }
        }
        else
        {
            ItemList.SelectedItems.Remove(item);
        }
    }

    private void ItemList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count != 1)
        {
            e.Cancel = true;
            _draggedItem = null;
            _draggedItemOriginalIndex = -1;
            return;
        }

        _draggedItem = e.Items.OfType<PlaylistDetailItemViewModel>().FirstOrDefault();
        _draggedItemOriginalIndex = _draggedItem is null ? -1 : Items.IndexOf(_draggedItem);
    }

    private async void ItemList_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        var item = _draggedItem;
        var originalIndex = _draggedItemOriginalIndex;
        _draggedItem = null;
        _draggedItemOriginalIndex = -1;
        if (item is null || originalIndex < 0)
        {
            return;
        }

        var newIndex = Items.IndexOf(item);
        if (newIndex < 0 || newIndex == originalIndex)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                using var scope = App.Services.CreateScope();
                var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
                await playlistBus.MoveItemAsync(item.PlaylistItemId, newIndex);
                await LoadPlaylistAsync();
            },
            GetResourceString(
                "PlaylistDetailPage_ReorderFailed",
                "Could not reorder the playlist"));
    }

    private async void KeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        switch (sender.Key)
        {
            case VirtualKey.F when sender.Modifiers.HasFlag(VirtualKeyModifiers.Control):
                SearchBox.Focus(FocusState.Keyboard);
                args.Handled = true;
                break;
            case VirtualKey.F5:
                _ = LoadPlaylistAsync();
                args.Handled = true;
                break;
            case VirtualKey.Enter when ItemList.SelectedItem is PlaylistDetailItemViewModel { IsPlayable: true } item:
                await PlayAsync(item);
                args.Handled = true;
                break;
            case VirtualKey.Delete when ItemList.SelectedItems.Count > 0:
                await RemoveSelectedAsync();
                args.Handled = true;
                break;
            case VirtualKey.Escape:
                NavigateBack();
                args.Handled = true;
                break;
        }
    }

    private void NavigateBack()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        Frame.Navigate(
            typeof(PlaylistsPage),
            null,
            MotionHelper.AnimationsEnabled
                ? new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromLeft
                }
                : new SuppressNavigationTransitionInfo());
    }

    private static PlaylistDetailItemViewModel? GetItemFromOriginalSource(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return null;
            }

            if (current is FrameworkElement { Tag: PlaylistDetailItemViewModel taggedItem })
            {
                return taggedItem;
            }

            if (current is ListViewItem { Content: PlaylistDetailItemViewModel item })
            {
                return item;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private string GetResourceString(string key, string fallback)
    {
        var value = _resourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class PlaylistDetailItemViewModel : INotifyPropertyChanged
{
    private static readonly ResourceLoader ResourceLoader = new();
    private bool _isCurrent;
    private bool _isSelected;
    private ImageSource? _thumbnailSource;

    public PlaylistDetailItemViewModel(PlaylistMediaItem item)
    {
        PlaylistItemId = item.PlaylistItemId;
        MediaId = item.MediaId;
        Title = string.IsNullOrWhiteSpace(item.Title)
            ? Path.GetFileNameWithoutExtension(item.Path)
            : item.Title;
        FilePath = item.Path;
        Kind = item.Kind;
        Duration = item.Duration;
        Artist = item.Artist;
        Album = item.Album;
        ThumbnailPath = item.ThumbnailPath;
        PlaybackPosition = item.PlaybackPosition;
        _thumbnailSource = CreateThumbnailSource(item.ThumbnailPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid PlaylistItemId { get; private set; }

    public Guid MediaId { get; private set; }

    public string Title { get; private set; }

    public string FilePath { get; private set; }

    public MediaLibraryItemKind Kind { get; private set; }

    public TimeSpan? Duration { get; private set; }

    public string? Artist { get; private set; }

    public string? Album { get; private set; }

    public string? ThumbnailPath { get; private set; }

    public TimeSpan? PlaybackPosition { get; private set; }

    public bool IsPlayable =>
        Kind is MediaLibraryItemKind.Audio or MediaLibraryItemKind.Video &&
        !string.IsNullOrWhiteSpace(FilePath) &&
        File.Exists(FilePath);

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
        }
    }

    public string SecondaryText
    {
        get
        {
            var details = new[] { Artist, Album }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            return details.Length > 0
                ? string.Join(" · ", details)
                : Path.GetFileName(FilePath);
        }
    }

    public string DurationText => MediaInfoFormatter.FormatDuration(Duration);

    public string KindGlyph => Kind == MediaLibraryItemKind.Audio ? "\uE8D6" : "\uE714";

    public double ThumbnailWidth => Kind == MediaLibraryItemKind.Video ? 120 : 68;

    public ImageSource? ThumbnailSource => _thumbnailSource;

    public double ThumbnailOpacity => _thumbnailSource is null ? 0 : 1;

    public Visibility ThumbnailVisibility => _thumbnailSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PlaceholderVisibility => _thumbnailSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string KindLabel => Kind == MediaLibraryItemKind.Audio
        ? GetResourceString("LibraryPage_KindAudio", "Audio")
        : GetResourceString("LibraryPage_KindVideo", "Video");

    public Visibility UnavailableVisibility => IsPlayable
        ? Visibility.Collapsed
        : Visibility.Visible;

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentIndicatorVisibility));
        }
    }

    public Visibility CurrentIndicatorVisibility => IsCurrent
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void ApplySnapshot(PlaylistDetailItemViewModel source)
    {
        var thumbnailChanged = !string.Equals(
            ThumbnailPath,
            source.ThumbnailPath,
            StringComparison.OrdinalIgnoreCase);
        MediaId = source.MediaId;
        Title = source.Title;
        FilePath = source.FilePath;
        Kind = source.Kind;
        Duration = source.Duration;
        Artist = source.Artist;
        Album = source.Album;
        ThumbnailPath = source.ThumbnailPath;
        PlaybackPosition = source.PlaybackPosition;
        if (thumbnailChanged)
        {
            _thumbnailSource = CreateThumbnailSource(ThumbnailPath);
        }

        OnPropertyChanged(string.Empty);
    }

    internal static ImageSource? CreateThumbnailSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var uri = Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(Path.GetFullPath(path));
            return new BitmapImage
            {
                DecodePixelWidth = 112,
                UriSource = uri
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            UriFormatException or
            NotSupportedException)
        {
            return null;
        }
    }

    private static string GetResourceString(string key, string fallback)
    {
        var value = ResourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
