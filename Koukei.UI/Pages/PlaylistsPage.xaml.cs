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
using Microsoft.UI.Xaml.Navigation;
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
using Windows.System;

namespace Koukei.UI.Pages;

public sealed partial class PlaylistsPage : Page, INotifyPropertyChanged
{
    private readonly List<PlaylistViewModel> _allPlaylists = [];
    private readonly LatestOperationController _loadController = new();
    private readonly ResourceLoader _resourceLoader = new();
    private readonly string _retryText;
    private ObservableCollection<PlaylistViewModel> _playlists = [];
    private bool _isLoading;
    private bool _isSortDescending;
    private int _loadRequestId;
    private string _sortField = "Name";
    private string? _loadErrorMessage;
    private CancellationTokenSource? _bodyStateMotionCancellation;
    private CancellationTokenSource? _playlistPlaybackCancellation;
    private UIElement? _visibleBodySurface;
    private bool _isViewInitialized;
    private bool _isPlaylistPlaybackBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlaylistViewModel> Playlists
    {
        get => _playlists;
        set
        {
            if (_playlists == value)
            {
                return;
            }

            _playlists = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public Visibility IsEmpty => Playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public PlaylistsPage()
    {
        InitializeComponent();
        _isViewInitialized = true;
        _retryText = StatePresenter.RetryText;
        UpdateSortState();
        DataContext = this;
        MotionHelper.SetVisibleInstant(PlaylistList, isVisible: false);
        MotionHelper.SetVisibleInstant(EmptyPanel, isVisible: false);
        MotionHelper.SetVisibleInstant(NoResultsPanel, isVisible: false);
        Loaded += PlaylistsPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = LoadPlaylistsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _loadRequestId++;
        _loadController.Cancel();
        _bodyStateMotionCancellation?.Cancel();
        _bodyStateMotionCancellation?.Dispose();
        _bodyStateMotionCancellation = null;
        _playlistPlaybackCancellation?.Cancel();
        _playlistPlaybackCancellation = null;
        base.OnNavigatedFrom(e);
    }

    private void PlaylistsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (App.DataInitializationException is { } exception)
        {
            _loadErrorMessage = exception.Message;
            UpdatePageState();
        }
    }

    private async Task LoadPlaylistsAsync()
    {
        if (App.DataInitializationException is not null || App.Services is null)
        {
            _loadErrorMessage = App.DataInitializationException?.Message;
            UpdatePageState();
            FooterStatusBar.ShowBusy(
                GetResourceString("Common_PageStatus_LoadFailed", "Failed to load"));
            return;
        }

        var requestId = ++_loadRequestId;
        var hadVisibleItems = Playlists.Count > 0;
        _isLoading = true;
        _loadErrorMessage = null;
        FooterStatusBar.ShowBusy(
            GetResourceString(
                hadVisibleItems
                    ? "Common_PageStatus_Refreshing"
                    : "Common_PageStatus_Loading",
                hadVisibleItems ? "Refreshing..." : "Loading..."));
        UpdatePageState();

        await _loadController.RunAsync(
            async cancellationToken =>
            {
                using var scope = App.Services.CreateScope();
                var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
                var summaries = await playlistService.ListAsync(cancellationToken);
                return summaries.Select(summary => new PlaylistViewModel(summary)).ToList();
            },
            playlists =>
            {
                _allPlaylists.Clear();
                _allPlaylists.AddRange(playlists);
                RefreshPlaylistList();
            },
            exception => _loadErrorMessage = exception.Message);

        if (requestId != _loadRequestId)
        {
            return;
        }

        _isLoading = false;
        UpdatePageState();
        if (!string.IsNullOrWhiteSpace(_loadErrorMessage) && _allPlaylists.Count == 0)
        {
            FooterStatusBar.ShowBusy(
                GetResourceString("Common_PageStatus_LoadFailed", "Failed to load"));
        }
        else
        {
            FooterStatusBar.ClearOverride();
        }

        if (!hadVisibleItems && Playlists.Count > 0)
        {
            _ = DispatcherQueue.TryEnqueue(() =>
                _ = MotionHelper.AnimateVisibleItemsEntranceAsync(PlaylistList));
        }
    }

    private void CreatePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        _ = CreatePlaylistAsync();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadPlaylistsAsync();
    }

    private void RetryLoadButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadPlaylistsAsync();
    }

    private void StatePresenter_RetryRequested(object? sender, EventArgs e)
    {
        _ = LoadPlaylistsAsync();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        RefreshPlaylistList();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var breakpoint = ResponsiveLayout.Resolve(e.NewSize.Width);
        var stackCommands = breakpoint is UiBreakpoint.Compact or UiBreakpoint.Medium;

        Grid.SetRow(SearchAndSortPanel, stackCommands ? 1 : 0);
        Grid.SetColumn(SearchAndSortPanel, stackCommands ? 0 : 1);
        Grid.SetColumnSpan(SearchAndSortPanel, stackCommands ? 2 : 1);
        SearchAndSortPanel.Margin = stackCommands ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        SearchBox.Width = stackCommands ? double.NaN : breakpoint == UiBreakpoint.Wide ? 320 : 260;
        SearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        SortButtonLabel.Visibility = breakpoint == UiBreakpoint.Compact
            ? Visibility.Collapsed
            : Visibility.Visible;

        var padding = ResponsiveLayout.GetPagePadding(breakpoint);
        HeaderPanel.Padding = new Thickness(padding.Left, 20, padding.Right, 0);
        LoadErrorInfoBar.Margin = new Thickness(padding.Left, 8, padding.Right, 0);
        StatePresenter.Margin = new Thickness(padding.Left, 8, padding.Right, 16);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            RefreshPlaylistList();
        }
    }

    private void PlaylistCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: PlaylistViewModel playlist } checkBox)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            if (!PlaylistList.SelectedItems.Contains(playlist))
            {
                PlaylistList.SelectedItems.Add(playlist);
            }
        }
        else
        {
            PlaylistList.SelectedItems.Remove(playlist);
        }
    }

    private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedIds = PlaylistList.SelectedItems
            .OfType<PlaylistViewModel>()
            .Select(playlist => playlist.Id)
            .ToHashSet();
        foreach (var playlist in _allPlaylists)
        {
            playlist.IsSelected = selectedIds.Contains(playlist.Id);
        }

        UpdateStatus();
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
        RefreshPlaylistList();
    }

    private void UpdateSortState()
    {
        if (!_isViewInitialized || SortButtonLabel is null || SortDirectionIcon is null)
        {
            return;
        }

        SortButtonLabel.Text = _sortField switch
        {
            "Modified" => GetResourceString("PlaylistsPage_SortCurrent_Modified", "Modified"),
            "Created" => GetResourceString("PlaylistsPage_SortCurrent_Created", "Created"),
            "ItemCount" => GetResourceString("PlaylistsPage_SortCurrent_ItemCount", "Item count"),
            _ => GetResourceString("PlaylistsPage_SortCurrent_Name", "Name")
        };
        SortDirectionIcon.Glyph = _isSortDescending ? "\uE70D" : "\uE70E";
    }

    private async void PlayPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaylistViewModel playlist })
        {
            await ExecutePlaylistPlaybackActionAsync(playlist, enqueue: false);
        }
    }

    private async void QueuePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaylistViewModel playlist })
        {
            await ExecutePlaylistPlaybackActionAsync(playlist, enqueue: true);
        }
    }

    private async Task ExecutePlaylistPlaybackActionAsync(
        PlaylistViewModel playlist,
        bool enqueue)
    {
        if (_isPlaylistPlaybackBusy || App.Services is null)
        {
            return;
        }

        _isPlaylistPlaybackBusy = true;
        var cancellation = new CancellationTokenSource();
        _playlistPlaybackCancellation = cancellation;
        FooterStatusBar.ShowBusy(string.Format(
            GetResourceString(
                "PlaylistsPage_PreparingPlayback",
                "Preparing \"{0}\"..."),
            playlist.Name));

        try
        {
            PlaylistDetail? detail;
            using (var scope = App.Services.CreateScope())
            {
                var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
                detail = await playlistService.GetAsync(playlist.Id, cancellation.Token);
            }

            if (detail is null)
            {
                throw new InvalidOperationException(
                    GetResourceString(
                        "PlaylistsPage_PlaylistNotFound",
                        "The playlist could not be found."));
            }

            var entries = CreatePlayableEntries(detail.Items);
            if (entries.Count == 0)
            {
                FooterStatusBar.ShowTransient(
                    GetResourceString(
                        "PlaylistsPage_NoPlayableItems",
                        "The playlist has no playable items."));
                return;
            }

            var playbackCoordinator =
                App.Services.GetRequiredService<PlaybackCoordinator>();
            if (enqueue)
            {
                await playbackCoordinator.EnqueuePlaylistAsync(
                    entries,
                    cancellation.Token);

                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistsPage_QueuedPlaylist",
                        "Added {1} items from \"{0}\" to the playback queue"),
                    playlist.Name,
                    entries.Count));
            }
            else
            {
                await playbackCoordinator.PlayPlaylistAsync(
                    entries,
                    startIndex: 0,
                    cancellationToken: cancellation.Token);
                FooterStatusBar.ShowTransient(string.Format(
                    GetResourceString(
                        "PlaylistsPage_PlayingPlaylist",
                        "Playing {1} items from \"{0}\""),
                    playlist.Name,
                    entries.Count));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            FooterStatusBar.ClearOverride();
        }
        catch (Exception ex)
        {
            FooterStatusBar.ClearOverride();
            if (IsLoaded)
            {
                await ShowErrorDialog(
                    GetResourceString(
                        enqueue
                            ? "PlaylistsPage_FailedToQueueDialog_Title"
                            : "PlaylistsPage_FailedToPlayDialog_Title",
                        enqueue
                            ? "Could not add playlist to the playback queue"
                            : "Could not play playlist"),
                    ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_playlistPlaybackCancellation, cancellation))
            {
                _playlistPlaybackCancellation = null;
            }

            cancellation.Dispose();
            _isPlaylistPlaybackBusy = false;
        }
    }

    private static IReadOnlyList<PlaybackQueueEntry> CreatePlayableEntries(
        IEnumerable<PlaylistMediaItem> items)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return items
            .OrderBy(item => item.SortOrder)
            .Where(item =>
                item.Kind is MediaLibraryItemKind.Audio or MediaLibraryItemKind.Video &&
                !string.IsNullOrWhiteSpace(item.Path) &&
                File.Exists(item.Path) &&
                paths.Add(item.Path))
            .Select(item => new PlaybackQueueEntry(
                item.MediaId,
                string.IsNullOrWhiteSpace(item.Title)
                    ? Path.GetFileNameWithoutExtension(item.Path)
                    : item.Title,
                item.Path,
                item.Kind,
                item.Artist,
                item.Album,
                item.ThumbnailPath,
                item.PlaybackPosition))
            .ToArray();
    }

    private void OpenPlaylistMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PlaylistViewModel playlist })
        {
            OpenPlaylist(playlist);
        }
    }

    private void PlaylistList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (GetPlaylistFromOriginalSource(e.OriginalSource) is { } playlist)
        {
            e.Handled = true;
            OpenPlaylist(playlist);
        }
    }

    private void PlaylistList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter &&
            PlaylistList.SelectedItem is PlaylistViewModel playlist)
        {
            e.Handled = true;
            OpenPlaylist(playlist);
        }
    }

    private void OpenPlaylist(PlaylistViewModel playlist)
    {
        Frame.Navigate(
            typeof(PlaylistDetailPage),
            playlist.Id,
            MotionHelper.AnimationsEnabled
                ? new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromRight
                }
                : new SuppressNavigationTransitionInfo());
    }

    private static PlaylistViewModel? GetPlaylistFromOriginalSource(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is Button)
            {
                return null;
            }

            if (current is FrameworkElement { Tag: PlaylistViewModel taggedPlaylist })
            {
                return taggedPlaylist;
            }

            if (current is ListViewItem { Content: PlaylistViewModel playlist })
            {
                return playlist;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaylistViewModel playlist })
        {
            _ = RenamePlaylistAsync(playlist);
        }
    }

    private void RenameMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PlaylistViewModel playlist })
        {
            _ = RenamePlaylistAsync(playlist);
        }
    }

    private void EditDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaylistViewModel playlist })
        {
            _ = EditDescriptionAsync(playlist);
        }
    }

    private void EditDescriptionMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PlaylistViewModel playlist })
        {
            _ = EditDescriptionAsync(playlist);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PlaylistViewModel playlist })
        {
            _ = DeletePlaylistAsync(playlist);
        }
    }

    private void DeleteMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: PlaylistViewModel playlist })
        {
            _ = DeletePlaylistAsync(playlist);
        }
    }

    private async Task CreatePlaylistAsync()
    {
        var nameBox = new TextBox
        {
            PlaceholderText = GetResourceString("PlaylistsPage_Dialog_NamePlaceholder", "Playlist name")
        };
        var descriptionBox = new TextBox
        {
            PlaceholderText = GetResourceString("PlaylistsPage_Dialog_DescriptionPlaceholder", "Description (optional)"),
            AcceptsReturn = true,
            Height = 96,
            TextWrapping = TextWrapping.Wrap
        };
        var nameValidation = CreateNameValidationText();

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                nameBox,
                nameValidation,
                descriptionBox
            }
        };

        var dialog = new ContentDialog
        {
            Title = GetResourceString("PlaylistsPage_Dialog_CreateTitle", "Create playlist"),
            Content = content,
            PrimaryButtonText = GetResourceString("PlaylistsPage_Dialog_CreateButton", "Create"),
            CloseButtonText = GetResourceString("PlaylistsPage_Dialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = Content.XamlRoot
        };

        AttachNameValidation(nameBox, nameValidation, dialog);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
        {
            return;
        }

        try
        {
            using var scope = App.Services.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
            await playlistService.CreateAsync(nameBox.Text, descriptionBox.Text);
            await LoadPlaylistsAsync();
            FooterStatusBar.ShowTransient(string.Format(
                GetResourceString(
                    "PlaylistsPage_StatusCreated",
                    "Created \"{0}\""),
                nameBox.Text.Trim()));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(
                GetResourceString("PlaylistsPage_FailedToCreateDialog_Title", "Failed to create playlist"),
                ex.Message);
        }
    }

    private async Task RenamePlaylistAsync(PlaylistViewModel playlist)
    {
        var nameBox = new TextBox
        {
            Text = playlist.Name,
            PlaceholderText = GetResourceString("PlaylistsPage_Dialog_NamePlaceholder", "Playlist name")
        };
        var nameValidation = CreateNameValidationText();
        var content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                nameBox,
                nameValidation
            }
        };

        var dialog = new ContentDialog
        {
            Title = GetResourceString("PlaylistsPage_Dialog_RenameTitle", "Rename playlist"),
            Content = content,
            PrimaryButtonText = GetResourceString("PlaylistsPage_Dialog_OKButton", "OK"),
            CloseButtonText = GetResourceString("PlaylistsPage_Dialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        AttachNameValidation(nameBox, nameValidation, dialog);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
        {
            return;
        }

        await UpdatePlaylistAsync(playlist, nameBox.Text, playlist.Description);
    }

    private async Task EditDescriptionAsync(PlaylistViewModel playlist)
    {
        var descriptionBox = new TextBox
        {
            Text = playlist.Description ?? string.Empty,
            PlaceholderText = GetResourceString("PlaylistsPage_Dialog_EnterDescription", "Enter description..."),
            AcceptsReturn = true,
            Height = 120,
            TextWrapping = TextWrapping.Wrap
        };

        var dialog = new ContentDialog
        {
            Title = GetResourceString("PlaylistsPage_Dialog_EditDescriptionTitle", "Edit description"),
            Content = descriptionBox,
            PrimaryButtonText = GetResourceString("PlaylistsPage_Dialog_SaveButton", "Save"),
            CloseButtonText = GetResourceString("PlaylistsPage_Dialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        await UpdatePlaylistAsync(playlist, playlist.Name, descriptionBox.Text);
    }

    private async Task UpdatePlaylistAsync(PlaylistViewModel playlist, string name, string? description)
    {
        try
        {
            using var scope = App.Services.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
            await playlistService.UpdateAsync(playlist.Id, name, description);
            await LoadPlaylistsAsync();
            FooterStatusBar.ShowTransient(string.Format(
                GetResourceString(
                    "PlaylistsPage_StatusUpdated",
                    "Updated \"{0}\""),
                name.Trim()));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(
                GetResourceString("PlaylistsPage_FailedToUpdateDialog_Title", "Failed to update playlist"),
                ex.Message);
        }
    }

    private async Task DeletePlaylistAsync(PlaylistViewModel playlist)
    {
        var dialog = new ContentDialog
        {
            Title = GetResourceString("PlaylistsPage_Dialog_DeleteTitle", "Delete playlist"),
            Content = string.Format(
                GetResourceString("PlaylistsPage_Dialog_DeleteMessage", "Delete playlist \"{0}\"? This cannot be undone."),
                playlist.Name),
            PrimaryButtonText = GetResourceString("PlaylistsPage_Dialog_DeleteButton", "Delete"),
            CloseButtonText = GetResourceString("PlaylistsPage_Dialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            using var scope = App.Services.CreateScope();
            var playlistService = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
            await playlistService.DeleteAsync(playlist.Id);
            await LoadPlaylistsAsync();
            FooterStatusBar.ShowTransient(string.Format(
                GetResourceString(
                    "PlaylistsPage_StatusDeleted",
                    "Deleted \"{0}\""),
                playlist.Name));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(
                GetResourceString("PlaylistsPage_FailedToDeleteDialog_Title", "Failed to delete playlist"),
                ex.Message);
        }
    }

    private void RefreshPlaylistList()
    {
        if (!_isViewInitialized ||
            SearchBox is null ||
            SortButton is null ||
            FooterStatusBar is null)
        {
            return;
        }

        var keyword = SearchBox.Text?.Trim();
        var playlists = _allPlaylists.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            playlists = playlists.Where(playlist =>
                playlist.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                (playlist.Description?.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        playlists = (_sortField, _isSortDescending) switch
        {
            ("Modified", true) => playlists
                .OrderByDescending(playlist => playlist.DateLastSaved ?? playlist.DateCreated)
                .ThenBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase),
            ("Modified", false) => playlists
                .OrderBy(playlist => playlist.DateLastSaved ?? playlist.DateCreated)
                .ThenBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase),
            ("Created", true) => playlists
                .OrderByDescending(playlist => playlist.DateCreated)
                .ThenBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase),
            ("Created", false) => playlists
                .OrderBy(playlist => playlist.DateCreated)
                .ThenBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase),
            ("ItemCount", true) => playlists
                .OrderByDescending(playlist => playlist.ItemCount)
                .ThenBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase),
            ("ItemCount", false) => playlists
                .OrderBy(playlist => playlist.ItemCount)
                .ThenBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase),
            ("Name", true) => playlists.OrderByDescending(
                playlist => playlist.Name,
                StringComparer.CurrentCultureIgnoreCase),
            _ => playlists.OrderBy(
                playlist => playlist.Name,
                StringComparer.CurrentCultureIgnoreCase)
        };

        ReconcilePlaylists(playlists.ToList());

        UpdateStatus();
        OnPropertyChanged(nameof(IsEmpty));
        UpdatePageState();
    }

    private void UpdateStatus()
    {
        if (!_isViewInitialized || FooterStatusBar is null || PlaylistList is null)
        {
            return;
        }

        var selectedCount = PlaylistList.SelectedItems.Count;
        var summary = _allPlaylists.Count == 0
            ? GetResourceString("Common_PageStatus_Empty", "0 items")
            : selectedCount > 0
                ? string.Format(
                    GetResourceString(
                        "Common_PageStatus_SummaryWithSelection",
                        "{0} shown / {1} total · {2} selected"),
                    Playlists.Count,
                    _allPlaylists.Count,
                    selectedCount)
                : string.Format(
                    GetResourceString(
                        "Common_PageStatus_Summary",
                        "{0} shown / {1} total"),
                    Playlists.Count,
                    _allPlaylists.Count);
        FooterStatusBar.SetSummary(summary);
    }

    private void ReconcilePlaylists(IReadOnlyList<PlaylistViewModel> desiredPlaylists)
    {
        for (var targetIndex = 0; targetIndex < desiredPlaylists.Count; targetIndex++)
        {
            var desiredPlaylist = desiredPlaylists[targetIndex];
            if (targetIndex < Playlists.Count &&
                Playlists[targetIndex].Id == desiredPlaylist.Id)
            {
                if (!ReferenceEquals(Playlists[targetIndex], desiredPlaylist))
                {
                    Playlists[targetIndex] = desiredPlaylist;
                }

                continue;
            }

            var currentIndex = -1;
            for (var index = targetIndex + 1; index < Playlists.Count; index++)
            {
                if (Playlists[index].Id == desiredPlaylist.Id)
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                Playlists.Move(currentIndex, targetIndex);
                Playlists[targetIndex] = desiredPlaylist;
            }
            else
            {
                Playlists.Insert(targetIndex, desiredPlaylist);
            }
        }

        while (Playlists.Count > desiredPlaylists.Count)
        {
            Playlists.RemoveAt(Playlists.Count - 1);
        }
    }

    private void UpdatePageState()
    {
        if (!_isViewInitialized ||
            PlaylistList is null ||
            SearchBox is null ||
            SortButton is null ||
            EmptyPanel is null ||
            NoResultsPanel is null ||
            StatePresenter is null ||
            LoadErrorInfoBar is null ||
            RefreshButton is null ||
            CreatePlaylistButton is null)
        {
            return;
        }

        var hasVisibleItems = Playlists.Count > 0;
        var hasAnyItems = _allPlaylists.Count > 0;
        var hasSearch = !string.IsNullOrWhiteSpace(SearchBox.Text);
        var isInitialLoading = _isLoading && !hasAnyItems;
        var isInitialError = !_isLoading && !hasAnyItems && !string.IsNullOrWhiteSpace(_loadErrorMessage);

        UIElement? bodySurface = hasVisibleItems
            ? PlaylistList
            : !_isLoading && !isInitialError && !hasAnyItems
                ? EmptyPanel
                : !_isLoading && hasAnyItems && !hasVisibleItems && hasSearch
                    ? NoResultsPanel
                    : null;
        SetBodySurface(bodySurface);
        StatePresenter.State = isInitialLoading
            ? PageViewState.InitialLoading
            : isInitialError
                ? PageViewState.Error
                : _isLoading && hasAnyItems
                    ? PageViewState.Refreshing
                    : PageViewState.Content;
        StatePresenter.Title = isInitialLoading
            ? GetResourceString("PlaylistsPage_LoadingState", "Loading playlists")
            : isInitialError
                ? GetResourceString(
                    "PlaylistsPage_FailedToLoadDialog_Title",
                    "Failed to load playlists")
                : string.Empty;
        StatePresenter.Description = isInitialError ? _loadErrorMessage ?? string.Empty : string.Empty;
        StatePresenter.RetryText = App.DataInitializationException is null
            ? _retryText
            : string.Empty;

        var showRefreshError = !_isLoading && hasAnyItems && !string.IsNullOrWhiteSpace(_loadErrorMessage);
        LoadErrorInfoBar.Title = GetResourceString(
            "PlaylistsPage_RefreshFailed",
            "Could not refresh playlists");
        LoadErrorInfoBar.Message = _loadErrorMessage ?? string.Empty;
        LoadErrorInfoBar.IsOpen = showRefreshError;

        RefreshButton.IsEnabled = !_isLoading;
        CreatePlaylistButton.IsEnabled = !_isLoading;
        SearchBox.IsEnabled = !isInitialLoading;
        SortButton.IsEnabled = !isInitialLoading;
    }

    private void SetBodySurface(UIElement? target)
    {
        if (ReferenceEquals(_visibleBodySurface, target))
        {
            return;
        }

        var previous = _visibleBodySurface;
        _visibleBodySurface = target;
        _bodyStateMotionCancellation?.Cancel();
        _bodyStateMotionCancellation?.Dispose();
        _bodyStateMotionCancellation = new CancellationTokenSource();

        if (!IsLoaded || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(
                PlaylistList,
                ReferenceEquals(target, PlaylistList));
            MotionHelper.SetVisibleInstant(
                EmptyPanel,
                ReferenceEquals(target, EmptyPanel));
            MotionHelper.SetVisibleInstant(
                NoResultsPanel,
                ReferenceEquals(target, NoResultsPanel));
            return;
        }

        _ = MotionHelper.CrossFadeAsync(
            previous,
            target,
            MotionPreset.Standard,
            MotionDirection.Down,
            _bodyStateMotionCancellation.Token);
    }

    private TextBlock CreateNameValidationText() => new()
    {
        Text = GetResourceString("PlaylistsPage_NameRequired", "Enter a playlist name."),
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KoukeiCriticalBrush"],
        FontSize = 12,
        Visibility = Visibility.Collapsed,
        TextWrapping = TextWrapping.Wrap
    };

    private static void AttachNameValidation(
        TextBox nameBox,
        TextBlock validationText,
        ContentDialog dialog)
    {
        nameBox.MaxLength = 128;

        void Validate()
        {
            var isValid = !string.IsNullOrWhiteSpace(nameBox.Text);
            dialog.IsPrimaryButtonEnabled = isValid;
            validationText.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
        }

        nameBox.TextChanged += (_, _) => Validate();
        Validate();
    }

    private string GetResourceString(string key, string fallback)
    {
        var value = _resourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private async Task ShowErrorDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = GetResourceString("PlaylistsPage_Dialog_OKButton", "OK"),
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class PlaylistViewModel(PlaylistSummary summary) : INotifyPropertyChanged
{
    private static readonly ResourceLoader ResourceLoader = new();
    private readonly ImageSource?[] _coverSources = summary.ThumbnailPaths
        .Select(PlaylistDetailItemViewModel.CreateThumbnailSource)
        .Where(source => source is not null)
        .Take(4)
        .ToArray();
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; } = summary.Id;

    public string Name { get; } = summary.Name;

    public string? Description { get; } = summary.Description;

    public string DescriptionText => string.IsNullOrWhiteSpace(Description)
        ? GetResourceString("PlaylistsPage_NoDescription", "No description")
        : Description;

    public int ItemCount { get; } = summary.ItemCount;

    public bool HasItems => ItemCount > 0;

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public ImageSource? Cover1Source => _coverSources.ElementAtOrDefault(0);

    public ImageSource? Cover2Source => _coverSources.ElementAtOrDefault(1);

    public ImageSource? Cover3Source => _coverSources.ElementAtOrDefault(2);

    public ImageSource? Cover4Source => _coverSources.ElementAtOrDefault(3);

    public Visibility PlaceholderVisibility => _coverSources.Length == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SingleCoverVisibility => _coverSources.Length == 1
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DoubleCoverVisibility => _coverSources.Length == 2
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility TripleCoverVisibility => _coverSources.Length == 3
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility QuadCoverVisibility => _coverSources.Length >= 4
        ? Visibility.Visible
        : Visibility.Collapsed;

    public DateTimeOffset DateCreated { get; } = summary.DateCreated;

    public DateTimeOffset? DateLastSaved { get; } = summary.DateLastSaved;

    public string ItemCountText => string.Format(
        ItemCount == 1
            ? GetResourceString("PlaylistsPage_ItemCountSingle", "{0} item")
            : GetResourceString("PlaylistsPage_ItemCountPlural", "{0} items"),
        ItemCount);

    public string DateText => (DateLastSaved ?? DateCreated).ToLocalTime().ToString("g");

    private static string GetResourceString(string key, string fallback)
    {
        var value = ResourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
