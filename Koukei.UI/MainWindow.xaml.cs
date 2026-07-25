using Koukei.Audio;
using Koukei.Bus.Models;
using Koukei.Bus.Services;
using Koukei.UI.Controls;
using Koukei.UI.Helpers;
using Koukei.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
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
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Koukei.UI.Pages;
using Microsoft.UI.Dispatching;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using VirtualKey = Windows.System.VirtualKey;

namespace Koukei.UI;

public sealed class HorizontalResizeHandle : ContentControl
{
    public HorizontalResizeHandle()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }
}

public sealed partial class MainWindow : Window
{
    private readonly record struct PlaybackQueueFocusSnapshot(string? FilePath, int Index);

    private enum AudioShortcutCommand
    {
        TogglePlayback,
        SeekBackward,
        SeekForward,
        VolumeUp,
        VolumeDown,
        ToggleMute,
        Previous,
        Next,
        ToggleShuffle,
        CycleRepeat
    }

    private const double PlaybackQueueDefaultWidth = 340;
    private const double PlaybackQueueMinimumWidth = 260;
    private const double PlaybackQueueMaximumWidth = 640;
    private const double PlaybackQueueMinimumContentWidth = 420;
    private const double PlaybackQueueResizeHandleWidth = 8;
    private const double PlaybackQueueOuterMargin = 8;
    private const double PlaybackQueueOuterRightMargin = 4;
    private const string PlaybackQueueWidthSettingKey = "PlaybackQueueSidebarWidth";
    private static readonly TimeSpan AudioSeekDebounceDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan AudioVolumeDebounceDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan AudioSeekConfirmationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AudioSeekStaleStateGuardDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan AudioProgressUpdateInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PlaybackLoadingFeedbackDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan PlaybackLoadingMinimumVisibleDuration = TimeSpan.FromMilliseconds(180);
    private const double AudioSeekConfirmationToleranceSeconds = 0.75;
    private const double AudioVolumeConfirmationTolerance = 0.5;
    private const double AudioShortcutVolumeStep = 5;
    private const int VirtualKeyMediaNextTrack = 0xB0;
    private const int VirtualKeyMediaPreviousTrack = 0xB1;
    private const int VirtualKeyVolumeMute = 0xAD;
    private const int VirtualKeyVolumeDown = 0xAE;
    private const int VirtualKeyVolumeUp = 0xAF;
    private const int VirtualKeyMediaPlayPause = 0xB3;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly PlaybackCoordinator _playbackCoordinator;
    private readonly ResourceLoader _resourceLoader = new();
    private readonly UISettings _uiSettings = new();
    private readonly object _audioStateDispatchLock = new();
    private readonly DispatcherQueueTimer _audioProgressTimer;
    private AudioPlaybackState _audioPlaybackState = AudioPlaybackState.Empty;
    private AudioPlaybackState? _pendingAudioPlaybackState;
    private CancellationTokenSource? _audioSeekCancellation;
    private CancellationTokenSource? _audioVolumeCancellation;
    private CancellationTokenSource? _audioPlayerBarMotionCancellation;
    private CancellationTokenSource? _audioExpandedMotionCancellation;
    private CancellationTokenSource? _playbackQueueMotionCancellation;
    private CancellationTokenSource? _audioLyricsLoadCancellation;
    private CancellationTokenSource? _audioLyricsStateMotionCancellation;
    private CancellationTokenSource? _audioLoadingFeedbackCancellation;
    private CancellationTokenSource? _audioMetadataMotionCancellation;
    private DateTimeOffset? _audioLoadingFeedbackShownAt;
    private double? _pendingAudioSeekPosition;
    private double? _confirmedAudioSeekPosition;
    private double? _pendingAudioVolume;
    private DateTimeOffset _audioSeekStaleStateGuardUntil;
    private bool _isAudioExpanded;
    private bool _isClosed;
    private bool _isAudioPlayerBarVisible;
    private bool _isAudioPlayerReady;
    private bool _isAudioSeekDragging;
    private bool _isAudioVolumeAdjusting;
    private bool _isAudioVolumeSliderInputHooked;
    private bool _isAudioStateDispatchPending;
    private bool _isUpdatingAudioControls;
    private bool _isAudioLoadingFeedbackRequested;
    private bool _isAudioLoadingFeedbackEnding;
    private bool _isSystemColorsSubscribed;
    private bool _isPlaybackQueueSidebarOpen;
    private bool _isPlaybackQueueSidebarClosing;
    private bool _isPlaybackQueueSidebarBusy;
    private bool _playbackQueueShowsItems;
    private int _trackedPlaybackQueueCurrentIndex = -1;
    private string? _trackedPlaybackQueueCurrentPath;
    private int _playbackQueueDragOriginalIndex = -1;
    private double _playbackQueueSidebarWidth = PlaybackQueueDefaultWidth;
    private uint? _playbackQueueResizePointerId;
    private double _playbackQueueResizeStartX;
    private double _playbackQueueResizeStartWidth;
    private double _lastAudibleAudioVolume = 100;
    private long _audioMetadataVersion;
    private long _audioArtworkMotionVersion;
    private string? _audioArtworkPath;
    private string? _displayedAudioMediaPath;
    private long _audioExpandedFocusRestoreVersion;
    private long _audioPlaybackStateTimestamp;
    private long _displayedAudioDurationSecond = -1;
    private long _displayedAudioPositionSecond = -1;
    private long _pendingAudioPlaybackStateTimestamp;
    private long _audioSeekRequestVersion;
    private int _activeAudioLyricIndex = -1;
    private string _requestedAudioPlayPauseGlyph = "\uE768";
    private string _requestedAudioMuteGlyph = "\uE767";
    private bool _displayedShuffleEnabled;
    private AudioRepeatMode _displayedRepeatMode = AudioRepeatMode.Off;
    private UIElement? _audioLyricsVisibleSurface;
    private bool _hasSynchronizedAudioLyrics;
    private PlaybackQueueSidebarItem? _playbackQueueDraggedItem;
    private Control? _playbackQueueFocusRestoreElement;
    private Control? _audioExpandedFocusRestoreElement;

    public ObservableCollection<AudioLyricLineViewModel> AudioLyricLines { get; } = [];

    public ObservableCollection<PlaybackQueueSidebarItem> PlaybackQueueSidebarItems { get; } = [];

    public DispatcherQueue DispatchQueue;

    public NavigationView NavigationView => NavigationViewControl;

    public Action? NavigationViewLoaded;

    private OverlappedPresenter? WindowPresenter { get; }

    private OverlappedPresenterState CurrentWindowState { get; set; }

    // Flag to track if navigation was triggered by NavigationView to avoid circular updates
    private bool _isNavigatingFromNavView = false;

    // Playlist library instance
    //private readonly PlaylistLibrary _playlistLibrary;

    public MainWindow()
    {
        this.InitializeComponent();
        MainRoot.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(MainRoot_KeyDown),
            handledEventsToo: true);
        MotionHelper.SetInstant(AudioPlayerBar, 0, new Vector3(0, 12, 0));
        MotionHelper.SetInstant(AudioExpandedLayer, 0, new Vector3(0, 16, 0));
        MotionHelper.SetVisibleInstant(ExpandedLyricsList, isVisible: false);
        MotionHelper.SetVisibleInstant(ExpandedLyricsLoadingPanel, isVisible: false);
        MotionHelper.SetVisibleInstant(ExpandedLyricsEmptyPanel, isVisible: true);
        _audioLyricsVisibleSurface = ExpandedLyricsEmptyPanel;
        MotionHelper.SetVisibleInstant(PlaybackQueueSidebarList, isVisible: false);
        MotionHelper.SetVisibleInstant(PlaybackQueueSidebarEmptyPanel, isVisible: true);
        _audioProgressTimer = DispatcherQueue.CreateTimer();
        _audioProgressTimer.Interval = AudioProgressUpdateInterval;
        _audioProgressTimer.IsRepeating = true;
        _audioProgressTimer.Tick += AudioProgressTimer_Tick;
        _audioPlaybackService = App.Services.GetRequiredService<IAudioPlaybackService>();
        _playbackCoordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
        _audioPlaybackService.StateChanged += AudioPlaybackService_StateChanged;
        _audioPlaybackService.MediaChanged += AudioPlaybackService_MediaChanged;
        _playbackCoordinator.PlaybackQueueChanged += PlaybackCoordinator_PlaybackQueueChanged;
        _playbackCoordinator.AudioPlaybackOptionsChanged += PlaybackCoordinator_AudioPlaybackOptionsChanged;
        Closed += MainWindow_Closed;
        MainRoot.ActualThemeChanged += MainRoot_ActualThemeChanged;
        SubscribeSystemAppearanceEvents();
        ApplyAudioSeekBarPalette();
        InitializeAudioPlayerAccessibility();
        RestorePlaybackQueueSidebarWidth();
        _isAudioPlayerReady = true;
        RefreshPlaybackQueue();
        SetWindowProperties();
        DispatchQueue = DispatcherQueue.GetForCurrentThread();

        // Initialize playlist library
        //_playlistLibrary = new PlaylistLibrary();

        RootFrame.Navigated += OnRootFrameNavigated;
        NavigationViewControl.SelectedItem = Home;

        if (AppWindow.Presenter is not OverlappedPresenter windowPresenter)
        {
            return;
        }
        WindowPresenter = windowPresenter;
        CurrentWindowState = WindowPresenter.State;
        AdjustNavigationViewMargin(force: true);
        AppWindow.Changed += MainWindow_AppWindowChanged;
    }

    private void MainWindow_AppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        AdjustNavigationViewMargin();
    }

    private void AdjustNavigationViewMargin(bool? force = null)
    {
        if (WindowPresenter is null ||
            (WindowPresenter.State == CurrentWindowState && force is not true))
        {
            return;
        }

        NavigationView.Margin = WindowPresenter.State == OverlappedPresenterState.Maximized
            ? new Thickness(0, -1, 0, 0)
            : new Thickness(0, -2, 0, 0);
        CurrentWindowState = WindowPresenter.State;
    }

    private void SetWindowProperties()
    {
        this.Title = "Koukei";
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(TitleBar);
    }

    private void OnPaneDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        TitleBar.IsPaneToggleButtonVisible = sender.PaneDisplayMode != NavigationViewPaneDisplayMode.Top;
    }

    public Frame GetRootFrame()
    {
        RootFrame.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
        RootFrame.NavigationFailed += OnNavigationFailed;
        return RootFrame;
    }

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }

    public void Navigate(Type pageType, object? targetPageArguments = null, NavigationTransitionInfo? navigationTransitionInfo = null)
    {
        if (RootFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        var transition = !MotionHelper.AnimationsEnabled
            ? new SuppressNavigationTransitionInfo()
            : navigationTransitionInfo ?? new EntranceNavigationTransitionInfo();
        RootFrame.Navigate(pageType, targetPageArguments, transition);
    }

    //#region Playlist Navigation Management

    ///// <summary>
    ///// Refreshes playlist navigation sub-items
    ///// </summary>
    //public void RefreshPlaylistNavItems(IEnumerable<(Guid Id, string Name)> playlists)
    //{
    //    Playlists.MenuItems.Clear();
    //    foreach (var (id, name) in playlists)
    //    {
    //        var item = new NavigationViewItem
    //        {
    //            Content = name,
    //            Tag = id,
    //            Icon = new FontIcon { Glyph = "\uE90B" }
    //        };
    //        Playlists.MenuItems.Add(item);
    //    }
    //}

    ///// <summary>
    ///// Adds a single playlist navigation item
    ///// </summary>
    //public void AddPlaylistNavItem(Guid id, string name)
    //{
    //    var item = new NavigationViewItem
    //    {
    //        Content = name,
    //        Tag = id,
    //        Icon = new FontIcon { Glyph = "\uE90B" }
    //    };
    //    Playlists.MenuItems.Add(item);
    //}

    ///// <summary>
    ///// Removes a single playlist navigation item
    ///// </summary>
    //public void RemovePlaylistNavItem(Guid id)
    //{
    //    var item = Playlists.MenuItems
    //        .OfType<NavigationViewItem>()
    //        .FirstOrDefault(i => i.Tag is Guid tag && tag == id);
    //    if (item != null)
    //    {
    //        Playlists.MenuItems.Remove(item);
    //    }
    //}

    ///// <summary>
    ///// Updates the name of a playlist navigation item
    ///// </summary>
    //public void UpdatePlaylistNavItemName(Guid id, string newName)
    //{
    //    var item = Playlists.MenuItems
    //        .OfType<NavigationViewItem>()
    //        .FirstOrDefault(i => i.Tag is Guid tag && tag == id);
    //    if (item != null)
    //    {
    //        item.Content = newName;
    //    }
    //}

    ///// <summary>
    ///// Selects the specified playlist navigation item
    ///// </summary>
    //public void SelectPlaylistNavItem(Guid id)
    //{
    //    var item = Playlists.MenuItems
    //        .OfType<NavigationViewItem>()
    //        .FirstOrDefault(i => i.Tag is Guid tag && tag == id);
    //    if (item == null) return;
    //    Playlists.IsExpanded = true;
    //    _isNavigatingFromNavView = true;
    //    NavigationViewControl.SelectedItem = item;
    //}

    //#endregion

    public void EnsureNavigationSelection(string id)
    {
        foreach (var rawGroup in this.NavigationView.MenuItems)
        {
            if (rawGroup is not NavigationViewItem group)
            {
                return;
            }
            foreach (var rawItem in group.MenuItems)
            {
                if (rawItem is not NavigationViewItem item)
                {
                    return;
                }
                if ((string)item.Tag == id)
                {
                    group.IsExpanded = true;
                    NavigationView.SelectedItem = item;
                    item.IsSelected = true;
                    return;
                }
                else if (item.MenuItems.Count > 0)
                {
                    foreach (var rawInnerItem in item.MenuItems)
                    {
                        if (rawInnerItem is not NavigationViewItem innerItem)
                        {
                            return;
                        }
                        if ((string)innerItem.Tag != id)
                        {
                            return;
                        }
                        group.IsExpanded = true;
                        item.IsExpanded = true;
                        NavigationView.SelectedItem = innerItem;
                        innerItem.IsSelected = true;
                        return;
                    }
                }
            }
        }
    }

    private void OnNavigationViewControlLoaded(object sender, RoutedEventArgs e)
    {
        // Delay necessary to ensure NavigationView visual state can match navigation
        Task.Delay(500).ContinueWith(_ => this.NavigationViewLoaded?.Invoke(), TaskScheduler.FromCurrentSynchronizationContext());

        var navigationView = sender as NavigationView;
        if (navigationView?.SettingsItem is DependencyObject settingsItem)
        {
            AutomationProperties.SetAutomationId(settingsItem, "NavSettings");
        }
        navigationView?.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, OnIsPaneOpenChanged);

        // Load playlists after NavigationView is loaded
        //_ = LoadPlaylistsAsync();
    }

    /// <summary>
    /// Loads playlists and displays them in NavigationView
    /// </summary>
    //private async Task LoadPlaylistsAsync()
    //{
    //    try
    //    {
    //        var playlists = await _playlistLibrary.GetAllPlaylistsAsync(loadItems: false);
    //        var playlistItems = playlists.Select(p => (p.Id, p.Name)).ToList();

    //        // Update NavigationView on UI thread
    //        DispatcherQueue.TryEnqueue(() =>
    //        {
    //            RefreshPlaylistNavItems(playlistItems);
    //        });
    //    }
    //    catch (Exception ex)
    //    {
    //        System.Diagnostics.Debug.WriteLine($"Failed to load playlists: {ex.Message}");
    //    }
    //}

    private static void OnIsPaneOpenChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is not NavigationView navigationView)
        {
            return;
        }

        var announcementText = navigationView.IsPaneOpen ? "Navigation Pane Opened" : "Navigation Pane Closed";
    }

    private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (RootFrame.CurrentSourcePageType == typeof(SettingsPage))
            {
                return;
            }
            _isNavigatingFromNavView = true;
            Navigate(typeof(SettingsPage));
        }
        else
        {
            var selectedItem = args.SelectedItemContainer;
            if (selectedItem == Home)
            {
                if (RootFrame.CurrentSourcePageType == typeof(HomePage))
                {
                    return;
                }
                _isNavigatingFromNavView = true;
                Navigate(typeof(HomePage));
            }
            else if (selectedItem == VideoLibrary)
            {
                if (RootFrame.CurrentSourcePageType == typeof(VideoLibraryPage))
                {
                    return;
                }
                _isNavigatingFromNavView = true;
                Navigate(typeof(VideoLibraryPage));
            }
            else if (selectedItem == AudioLibrary)
            {
                if (RootFrame.CurrentSourcePageType == typeof(AudioLibraryPage))
                {
                    return;
                }
                _isNavigatingFromNavView = true;
                Navigate(typeof(AudioLibraryPage));
            }
            else if (selectedItem == Playlists)
            {
                if (RootFrame.CurrentSourcePageType == typeof(PlaylistsPage))
                {
                    return;
                }
                _isNavigatingFromNavView = true;
                Navigate(typeof(PlaylistsPage));
            }
            //else if (selectedItem?.Tag is Guid playlistId)
            //{
            //    // Playlist sub-item clicked
            //    _isNavigatingFromNavView = true;
            //    Navigate(typeof(PlaylistDetailPage), playlistId);
            //}
        }
    }

    private void OnRootFrameNavigated(object sender, NavigationEventArgs e)
    {
        // Update back button visibility
        TitleBar.IsBackButtonVisible = RootFrame.CanGoBack;

        // If navigation was not triggered by NavigationView (e.g., back navigation), 
        // need to sync NavigationView's selected item
        if (!_isNavigatingFromNavView)
        {
            var pageType = e.SourcePageType;

            if (pageType == typeof(HomePage))
            {
                NavigationViewControl.SelectedItem = Home;
            }
            else if (pageType == typeof(VideoLibraryPage))
            {
                MediaLibrary.IsExpanded = true;
                NavigationViewControl.SelectedItem = VideoLibrary;
            }
            else if (pageType == typeof(AudioLibraryPage))
            {
                MediaLibrary.IsExpanded = true;
                NavigationViewControl.SelectedItem = AudioLibrary;
            }
            else if (pageType == typeof(PlaylistsPage))
            {
                NavigationViewControl.SelectedItem = Playlists;
            }
            else if (pageType == typeof(PlaylistDetailPage))
            {
                NavigationViewControl.SelectedItem = Playlists;
            }
            //else if (pageType == typeof(PlaylistDetailPage))
            //{
            //    // Select the corresponding playlist sub-item
            //    if (e.Parameter is Guid playlistId)
            //    {
            //        var subItem = Playlists.MenuItems
            //            .OfType<NavigationViewItem>()
            //            .FirstOrDefault(i => i.Tag is Guid tag && tag == playlistId);
            //        if (subItem != null)
            //        {
            //            Playlists.IsExpanded = true;
            //            NavigationViewControl.SelectedItem = subItem;
            //        }
            //        else
            //        {
            //            NavigationViewControl.SelectedItem = Playlists;
            //        }
            //    }
            //    else
            //    {
            //        NavigationViewControl.SelectedItem = Playlists;
            //    }
            //}
            else if (pageType == typeof(SettingsPage))
            {
                NavigationViewControl.SelectedItem = NavigationViewControl.SettingsItem;
            }
        }

        // Reset flag
        _isNavigatingFromNavView = false;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavigationViewControl.IsPaneOpen = !NavigationViewControl.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (this.RootFrame.CanGoBack)
        {
            NavigationTransitionInfo transition = MotionHelper.AnimationsEnabled
                ? new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromLeft
                }
                : new SuppressNavigationTransitionInfo();
            this.RootFrame.GoBack(transition);
        }
    }

    private void MainRoot_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyAudioSeekBarPalette();
    }

    private void MainWindow_SystemColorsChanged(UISettings sender, object args)
    {
        ReapplyAudioPaletteFromSystem();
    }

    private void SubscribeSystemAppearanceEvents()
    {
        try
        {
            _uiSettings.ColorValuesChanged += MainWindow_SystemColorsChanged;
            _isSystemColorsSubscribed = true;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Debug.WriteLine($"UISettings.ColorValuesChanged is unavailable: 0x{ex.HResult:X8}");
        }
    }

    private void UnsubscribeSystemAppearanceEvents()
    {
        if (_isSystemColorsSubscribed)
        {
            try
            {
                _uiSettings.ColorValuesChanged -= MainWindow_SystemColorsChanged;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Debug.WriteLine($"Failed to remove UISettings.ColorValuesChanged: 0x{ex.HResult:X8}");
            }
            finally
            {
                _isSystemColorsSubscribed = false;
            }
        }
    }

    private void ReapplyAudioPaletteFromSystem()
    {
        if (_isClosed)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed)
            {
                ApplyAudioSeekBarPalette();
            }
        });
    }

    private void ApplyAudioSeekBarPalette()
    {
        var resolvedTheme = MainRoot.ActualTheme is ElementTheme.Light or ElementTheme.Dark
            ? MainRoot.ActualTheme
            : ThemeHelper.ActualTheme;
        var palette = resolvedTheme == ElementTheme.Light
            ? PlayerThemePalette.Light
            : PlayerThemePalette.Dark;
        palette = palette.WithSystemAccent();

        CompactAudioSeekBar.ApplyPalette(
            palette.SliderTrack,
            palette.Accent,
            palette.SliderThumb,
            palette.ChapterMarker,
            palette.AccentHover);
    }

    private void InitializeAudioPlayerAccessibility()
    {
        SetButtonDescription(
            CompactPlayPauseButton,
            "AudioPlayer_Play",
            "Play",
            "AudioPlayer_ShortcutPlayPause",
            "Space / Ctrl+P");
        SetButtonDescription(
            CompactShuffleButton,
            "AudioPlayer_Shuffle",
            "Shuffle",
            "AudioPlayer_ShortcutShuffle",
            "S / Ctrl+Shift+S");
        SetButtonDescription(
            CompactPreviousButton,
            "AudioPlayer_Previous",
            "Previous",
            "AudioPlayer_ShortcutPrevious",
            "Page Up / Ctrl+Left");
        SetButtonDescription(
            CompactNextButton,
            "AudioPlayer_Next",
            "Next",
            "AudioPlayer_ShortcutNext",
            "Page Down / Ctrl+Right");
        SetButtonDescription(
            CompactRepeatButton,
            "AudioPlayer_Repeat",
            "Repeat",
            "AudioPlayer_ShortcutRepeat",
            "R / Ctrl+Shift+R");
        UpdateAudioVolumeButtonDescription();
        SetButtonDescription(CompactMoreButton, "AudioPlayer_More", "More playback options");
        SetButtonDescription(AudioNowPlayingButton, "AudioPlayer_Expand", "Open player details");
        SetButtonDescription(AudioCloseButton, "AudioPlayer_Close", "Close audio player");
        SetButtonDescription(
            AudioCollapseButton,
            "AudioPlayer_Collapse",
            "Close player details",
            "AudioPlayer_ShortcutCloseDetails",
            "Esc");
        AutomationProperties.SetName(
            CompactAudioSeekBar,
            GetAudioPlayerString("AudioPlayer_Seek", "Playback position"));
        ToolTipService.SetToolTip(
            CompactAudioSeekBar,
            FormatAudioShortcutToolTip(
                GetAudioPlayerString("AudioPlayer_Seek", "Playback position"),
                GetAudioPlayerString(
                    "AudioPlayer_ShortcutSeek",
                    "Left / Right")));
    }

    private void SetButtonDescription(
        FrameworkElement element,
        string resourceKey,
        string fallback,
        string? shortcutResourceKey = null,
        string? shortcutFallback = null)
    {
        var value = GetAudioPlayerString(resourceKey, fallback);
        var toolTip = shortcutResourceKey is null
            ? value
            : FormatAudioShortcutToolTip(
                value,
                GetAudioPlayerString(shortcutResourceKey, shortcutFallback ?? string.Empty));
        ToolTipService.SetToolTip(element, toolTip);
        AutomationProperties.SetName(element, value);
    }

    private static string FormatAudioShortcutToolTip(string action, string shortcut)
    {
        return string.IsNullOrWhiteSpace(shortcut)
            ? action
            : $"{action}  ({shortcut})";
    }

    private string GetAudioPlayerString(string resourceKey, string fallback)
    {
        var value = _resourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private void AudioPlaybackService_StateChanged(object? sender, AudioPlaybackStateChangedEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        var shouldDispatch = false;
        lock (_audioStateDispatchLock)
        {
            _pendingAudioPlaybackState = args.State;
            _pendingAudioPlaybackStateTimestamp = Stopwatch.GetTimestamp();
            if (!_isAudioStateDispatchPending)
            {
                _isAudioStateDispatchPending = true;
                shouldDispatch = true;
            }
        }

        if (shouldDispatch && !DispatcherQueue.TryEnqueue(ApplyPendingAudioPlaybackState))
        {
            lock (_audioStateDispatchLock)
            {
                _isAudioStateDispatchPending = false;
            }
        }
    }

    private void ApplyPendingAudioPlaybackState()
    {
        AudioPlaybackState? state;
        long stateTimestamp;
        lock (_audioStateDispatchLock)
        {
            state = _pendingAudioPlaybackState;
            stateTimestamp = _pendingAudioPlaybackStateTimestamp;
            _pendingAudioPlaybackState = null;
            _isAudioStateDispatchPending = false;
        }

        if (!_isClosed && state is not null)
        {
            ApplyAudioPlaybackState(state, stateTimestamp);
        }
    }

    private void AudioPlaybackService_MediaChanged(object? sender, AudioMediaChangedEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed)
            {
                return;
            }

            CancelPendingAudioSeek();
            _ = ApplyAudioMetadataAsync(args.Metadata);
        });
    }

    private void PlaybackCoordinator_PlaybackQueueChanged(object? sender, EventArgs args)
    {
        if (!_isClosed)
        {
            _ = DispatcherQueue.TryEnqueue(RefreshPlaybackQueue);
        }
    }

    private void PlaybackCoordinator_AudioPlaybackOptionsChanged(object? sender, EventArgs args)
    {
        if (!_isClosed)
        {
            _ = DispatcherQueue.TryEnqueue(UpdateAudioPlaybackModeControls);
        }
    }

    private void RefreshPlaybackQueue()
    {
        if (_isClosed)
        {
            return;
        }

        UpdateAudioPlaybackModeControls();
        RefreshCurrentAudioTitleFromQueue();
        if (_isPlaybackQueueSidebarOpen)
        {
            RefreshPlaybackQueueSidebar();
        }
    }

    private void RefreshCurrentAudioTitleFromQueue()
    {
        var currentItem = _playbackCoordinator.PlaybackQueue.FirstOrDefault(
            item => item.IsCurrent && item.Kind == MediaLibraryItemKind.Audio);
        if (currentItem is not null)
        {
            ApplyAudioTitle(currentItem.Title, currentItem.FilePath);
            if (currentItem.MediaId is not null)
            {
                ApplyAudioArtistAndAlbum(
                    string.IsNullOrWhiteSpace(currentItem.Artist)
                        ? GetAudioPlayerString("AudioPlayer_UnknownArtist", "Unknown artist")
                        : currentItem.Artist,
                    string.IsNullOrWhiteSpace(currentItem.Album)
                        ? GetAudioPlayerString("AudioPlayer_UnknownAlbum", "Unknown album")
                        : currentItem.Album);

                var artworkAlreadyMatches = string.Equals(
                        _audioArtworkPath,
                        currentItem.ThumbnailPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    CompactArtworkImage.Source is not null;
                if (!artworkAlreadyMatches)
                {
                    ClearAudioArtwork();
                    _ = ApplyAudioArtworkFromPathAsync(
                        currentItem.ThumbnailPath,
                        _audioMetadataVersion);
                }
            }
        }
    }

    private void UpdateAudioPlaybackModeControls()
    {
        if (_isClosed)
        {
            return;
        }

        var isShuffleEnabled = _playbackCoordinator.IsShuffleEnabled;
        var repeatMode = _playbackCoordinator.RepeatMode;
        var isRepeatEnabled = repeatMode != AudioRepeatMode.Off;

        if (_displayedShuffleEnabled != isShuffleEnabled)
        {
            _displayedShuffleEnabled = isShuffleEnabled;
            _ = MotionHelper.CrossFadeAsync(
                isShuffleEnabled ? CompactShuffleInactiveIcon : CompactShuffleActiveIcon,
                isShuffleEnabled ? CompactShuffleActiveIcon : CompactShuffleInactiveIcon,
                MotionPreset.Fast,
                MotionDirection.None);
        }

        var repeatGlyph = repeatMode == AudioRepeatMode.One ? "\uE8ED" : "\uE8EE";
        if (_displayedRepeatMode != repeatMode)
        {
            _displayedRepeatMode = repeatMode;
            if (!string.Equals(CompactRepeatActiveGlyph.Glyph, repeatGlyph, StringComparison.Ordinal))
            {
                _ = MotionHelper.SwapContentAsync(
                    CompactRepeatActiveGlyph,
                    () => CompactRepeatActiveGlyph.Glyph = repeatGlyph,
                    MotionPreset.Fast);
            }

            _ = MotionHelper.CrossFadeAsync(
                isRepeatEnabled ? CompactRepeatInactiveIcon : CompactRepeatActiveIcon,
                isRepeatEnabled ? CompactRepeatActiveIcon : CompactRepeatInactiveIcon,
                MotionPreset.Fast,
                MotionDirection.None);
        }

        CompactMoreRepeatIcon.Glyph = repeatMode == AudioRepeatMode.One
            ? "\uE8ED"
            : "\uE8EE";
        CompactShuffleButton.IsEnabled = _playbackCoordinator.PlaybackQueue.Count > 0;
        CompactRepeatButton.IsEnabled = _playbackCoordinator.PlaybackQueue.Count > 0;
        CompactPreviousButton.IsEnabled = _playbackCoordinator.CanPlayPrevious;
        CompactNextButton.IsEnabled = _playbackCoordinator.CanPlayNext;
        CompactMoreShuffleItem.IsEnabled = CompactShuffleButton.IsEnabled;
        CompactMoreRepeatItem.IsEnabled = CompactRepeatButton.IsEnabled;
        CompactMorePreviousItem.IsEnabled = CompactPreviousButton.IsEnabled;
        CompactMoreNextItem.IsEnabled = CompactNextButton.IsEnabled;

        AutomationProperties.SetName(
            CompactShuffleButton,
            GetAudioPlayerString("AudioPlayer_Shuffle", "Shuffle"));
        var shuffleAction = GetAudioPlayerString(
            isShuffleEnabled ? "AudioPlayer_ShuffleDisable" : "AudioPlayer_ShuffleEnable",
            isShuffleEnabled ? "Turn shuffle off" : "Turn shuffle on");
        ToolTipService.SetToolTip(
            CompactShuffleButton,
            FormatAudioShortcutToolTip(
                shuffleAction,
                GetAudioPlayerString(
                    "AudioPlayer_ShortcutShuffle",
                    "S / Ctrl+Shift+S")));
        CompactMoreShuffleItem.Text = shuffleAction;
        AutomationProperties.SetItemStatus(
            CompactShuffleButton,
            GetAudioPlayerString(
                isShuffleEnabled ? "AudioPlayer_StateOn" : "AudioPlayer_StateOff",
                isShuffleEnabled ? "On" : "Off"));

        AutomationProperties.SetName(
            CompactRepeatButton,
            GetAudioPlayerString("AudioPlayer_Repeat", "Repeat"));
        var repeatAction = GetAudioPlayerString(
            repeatMode switch
            {
                AudioRepeatMode.All => "AudioPlayer_RepeatOneEnable",
                AudioRepeatMode.One => "AudioPlayer_RepeatDisable",
                _ => "AudioPlayer_RepeatEnable"
            },
            repeatMode switch
            {
                AudioRepeatMode.All => "Switch to repeat one",
                AudioRepeatMode.One => "Turn repeat off",
                _ => "Turn repeat all on"
            });
        ToolTipService.SetToolTip(
            CompactRepeatButton,
            FormatAudioShortcutToolTip(
                repeatAction,
                GetAudioPlayerString(
                    "AudioPlayer_ShortcutRepeat",
                    "R / Ctrl+Shift+R")));
        CompactMoreRepeatItem.Text = repeatAction;
        AutomationProperties.SetItemStatus(
            CompactRepeatButton,
            GetAudioPlayerString(
                repeatMode switch
                {
                    AudioRepeatMode.All => "AudioPlayer_RepeatStateAll",
                    AudioRepeatMode.One => "AudioPlayer_RepeatStateOne",
                    _ => "AudioPlayer_StateOff"
                },
                repeatMode switch
                {
                    AudioRepeatMode.All => "Repeat all",
                    AudioRepeatMode.One => "Repeat one",
                    _ => "Off"
                }));
    }

    private void ApplyAudioPlaybackState(AudioPlaybackState state, long stateTimestamp)
    {
        if (_isClosed)
        {
            return;
        }

        _audioPlaybackState = state;
        _audioPlaybackStateTimestamp = stateTimestamp;
        UpdateAudioLoadingFeedback(state.Status == AudioPlaybackStatus.Loading);
        var hasMedia = state.Status != AudioPlaybackStatus.None;
        if (!hasMedia)
        {
            CancelPendingAudioVolume();
            StopAudioProgressTimer();
            _displayedAudioDurationSecond = -1;
            _displayedAudioPositionSecond = -1;
            _audioMetadataVersion++;
            CancelMotion(ref _audioMetadataMotionCancellation);
            _displayedAudioMediaPath = null;
            CancelAudioLyricsLoad();
            ResetAudioLyrics();
            ClearAudioArtwork();
            SetAudioExpanded(false, animate: false);
            SetAudioPlayerBarVisible(false, animate: true);
            return;
        }

        UpdateAudioProgressTimerState();
        SetAudioPlayerBarVisible(true, animate: true);

        _isUpdatingAudioControls = true;
        try
        {
            var duration = Math.Max(0, state.Duration);
            var position = GetProjectedAudioPosition(state, stateTimestamp);
            var maximum = Math.Max(1, duration);
            CompactAudioSeekBar.Maximum = maximum;
            CompactAudioSeekBar.IsEnabled = state.IsSeekable && duration > 0;

            if (!_isAudioSeekDragging)
            {
                var displayedPosition = position;
                if (_pendingAudioSeekPosition is double pendingPosition)
                {
                    if (Math.Abs(position - pendingPosition) <= AudioSeekConfirmationToleranceSeconds)
                    {
                        _pendingAudioSeekPosition = null;
                        _confirmedAudioSeekPosition = pendingPosition;
                        _audioSeekStaleStateGuardUntil =
                            DateTimeOffset.UtcNow + AudioSeekStaleStateGuardDelay;
                    }
                    else
                    {
                        displayedPosition = pendingPosition;
                    }
                }
                else if (_confirmedAudioSeekPosition is double confirmedPosition)
                {
                    if (DateTimeOffset.UtcNow < _audioSeekStaleStateGuardUntil &&
                        Math.Abs(position - confirmedPosition) > AudioSeekConfirmationToleranceSeconds)
                    {
                        displayedPosition = confirmedPosition;
                    }
                    else
                    {
                        _confirmedAudioSeekPosition = null;
                    }
                }

                UpdateAudioSeekVisual(displayedPosition);
            }

            var durationSecond = (long)Math.Floor(duration);
            if (_displayedAudioDurationSecond != durationSecond)
            {
                _displayedAudioDurationSecond = durationSecond;
                CompactDurationText.Text = FormatMediaTime(duration);
            }

            var engineVolume = Math.Clamp(state.Volume, 0, 100);
            var displayedVolume = engineVolume;
            var pendingVolume = _pendingAudioVolume;
            var hasPendingVolume = pendingVolume.HasValue;
            if (pendingVolume is double requestedVolume)
            {
                if (Math.Abs(engineVolume - requestedVolume) <=
                    AudioVolumeConfirmationTolerance)
                {
                    _pendingAudioVolume = null;
                    hasPendingVolume = false;
                }
                else
                {
                    displayedVolume = requestedVolume;
                }
            }

            if (_isAudioVolumeAdjusting && !hasPendingVolume &&
                CompactVolumeSlider is not null)
            {
                displayedVolume = CompactVolumeSlider.Value;
            }

            if (CompactVolumeSlider is not null)
            {
                CompactVolumeSlider.Value = displayedVolume;
            }

            if (CompactVolumeValueText is not null)
            {
                CompactVolumeValueText.Text = $"{displayedVolume:0}%";
            }
            if (!hasPendingVolume && !_isAudioVolumeAdjusting && engineVolume > 0)
            {
                _lastAudibleAudioVolume = Math.Clamp(engineVolume, 1, 100);
            }
            var isPlaying = state.Status == AudioPlaybackStatus.Playing;
            var playGlyph = isPlaying ? "\uE769" : "\uE768";
            SetAudioPlayPauseGlyph(playGlyph);
            var playDescription = isPlaying
                ? GetAudioPlayerString("AudioPlayer_Pause", "Pause")
                : GetAudioPlayerString("AudioPlayer_Play", "Play");
            SetDynamicButtonDescription(
                CompactPlayPauseButton,
                playDescription,
                GetAudioPlayerString(
                    "AudioPlayer_ShortcutPlayPause",
                    "Space / Ctrl+P"));

            var muteGlyph = state.IsMuted || state.Volume <= 0 ? "\uE74F" : "\uE767";
            SetAudioMuteGlyph(muteGlyph);
            UpdateAudioVolumeButtonDescription();

            var controlsEnabled = state.Status is AudioPlaybackStatus.Playing or
                AudioPlaybackStatus.Paused or AudioPlaybackStatus.Stopped;
            CompactPlayPauseButton.IsEnabled = controlsEnabled;
            CompactMuteButton.IsEnabled = controlsEnabled;
            if (CompactVolumeSlider is not null)
            {
                CompactVolumeSlider.IsEnabled = controlsEnabled;
            }

            SetLiveRegionText(ExpandedAudioStatusText, state.ErrorMessage ?? string.Empty);
            ExpandedAudioStatusText.Visibility = string.IsNullOrWhiteSpace(state.ErrorMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        finally
        {
            _isUpdatingAudioControls = false;
        }
    }

    private void SetAudioPlayPauseGlyph(string glyph)
    {
        if (string.Equals(_requestedAudioPlayPauseGlyph, glyph, StringComparison.Ordinal))
        {
            return;
        }

        _requestedAudioPlayPauseGlyph = glyph;
        _ = MotionHelper.SwapContentAsync(
            CompactPlayPauseIcon,
            () =>
            {
                if (string.Equals(_requestedAudioPlayPauseGlyph, glyph, StringComparison.Ordinal))
                {
                    CompactPlayPauseIcon.Glyph = glyph;
                }
            },
            MotionPreset.Fast);
    }

    private void SetAudioMuteGlyph(string glyph)
    {
        if (string.Equals(_requestedAudioMuteGlyph, glyph, StringComparison.Ordinal))
        {
            return;
        }

        _requestedAudioMuteGlyph = glyph;
        _ = MotionHelper.SwapContentAsync(
            CompactMuteIcon,
            () =>
            {
                if (string.Equals(_requestedAudioMuteGlyph, glyph, StringComparison.Ordinal))
                {
                    CompactMuteIcon.Glyph = glyph;
                }
            },
            MotionPreset.Fast);
    }

    private void UpdateAudioLoadingFeedback(bool isLoading)
    {
        _isAudioLoadingFeedbackRequested = isLoading;
        if (isLoading)
        {
            if (_audioLoadingFeedbackCancellation is not null &&
                !_isAudioLoadingFeedbackEnding)
            {
                return;
            }

            if (_audioLoadingFeedbackCancellation is { } previousCancellation)
            {
                _audioLoadingFeedbackCancellation = null;
                previousCancellation.Cancel();
                previousCancellation.Dispose();
            }

            _isAudioLoadingFeedbackEnding = false;
            var cancellation = new CancellationTokenSource();
            _audioLoadingFeedbackCancellation = cancellation;
            if (CompactPlaybackLoadingRing.Visibility == Visibility.Visible)
            {
                _audioLoadingFeedbackShownAt ??= DateTimeOffset.UtcNow;
                CompactPlaybackLoadingRing.IsActive = true;
            }
            else
            {
                _audioLoadingFeedbackShownAt = null;
                _ = ShowAudioLoadingFeedbackAfterDelayAsync(cancellation);
            }

            return;
        }

        if (!_isAudioLoadingFeedbackEnding &&
            _audioLoadingFeedbackCancellation is { } activeCancellation)
        {
            _isAudioLoadingFeedbackEnding = true;
            _ = EndAudioLoadingFeedbackAsync(activeCancellation);
        }
    }

    private async Task ShowAudioLoadingFeedbackAfterDelayAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(PlaybackLoadingFeedbackDelay, cancellation.Token);
            if (!ReferenceEquals(_audioLoadingFeedbackCancellation, cancellation) ||
                cancellation.IsCancellationRequested ||
                _isClosed)
            {
                return;
            }

            _audioLoadingFeedbackShownAt = DateTimeOffset.UtcNow;
            CompactPlaybackLoadingRing.IsActive = true;
            await MotionHelper.CrossFadeAsync(
                CompactPlayPauseIcon,
                CompactPlaybackLoadingRing,
                MotionPreset.Fast,
                MotionDirection.None,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task EndAudioLoadingFeedbackAsync(
        CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_audioLoadingFeedbackCancellation, cancellation))
        {
            return;
        }

        cancellation.Cancel();
        var loadingWasShown =
            _audioLoadingFeedbackShownAt is not null &&
            CompactPlaybackLoadingRing.Visibility == Visibility.Visible;
        if (loadingWasShown && _audioLoadingFeedbackShownAt is { } shownAt)
        {
            var elapsed = DateTimeOffset.UtcNow - shownAt;
            var remaining = PlaybackLoadingMinimumVisibleDuration - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }
        }

        if (!ReferenceEquals(_audioLoadingFeedbackCancellation, cancellation))
        {
            return;
        }

        if (_isAudioLoadingFeedbackRequested)
        {
            _isAudioLoadingFeedbackEnding = false;
            return;
        }

        if (loadingWasShown)
        {
            CompactPlayPauseIcon.Glyph = _requestedAudioPlayPauseGlyph;
            await MotionHelper.CrossFadeAsync(
                CompactPlaybackLoadingRing,
                CompactPlayPauseIcon,
                MotionPreset.Fast,
                MotionDirection.None);
        }

        if (!ReferenceEquals(_audioLoadingFeedbackCancellation, cancellation))
        {
            return;
        }

        CompactPlaybackLoadingRing.IsActive = false;
        MotionHelper.SetVisibleInstant(CompactPlaybackLoadingRing, isVisible: false);
        _audioLoadingFeedbackShownAt = null;
        _audioLoadingFeedbackCancellation = null;
        _isAudioLoadingFeedbackEnding = false;
        cancellation.Dispose();
    }

    private void AudioProgressTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isClosed ||
            _audioPlaybackState.Status != AudioPlaybackStatus.Playing ||
            _isAudioSeekDragging)
        {
            return;
        }

        if (_pendingAudioSeekPosition is double pendingPosition)
        {
            UpdateAudioSeekVisual(pendingPosition);
            return;
        }

        var position = GetProjectedAudioPosition(
            _audioPlaybackState,
            _audioPlaybackStateTimestamp);
        if (_confirmedAudioSeekPosition is double confirmedPosition &&
            DateTimeOffset.UtcNow < _audioSeekStaleStateGuardUntil)
        {
            position = Math.Max(position, confirmedPosition);
        }

        UpdateAudioSeekVisual(position);
    }

    private static double GetProjectedAudioPosition(AudioPlaybackState state, long stateTimestamp)
    {
        var duration = Math.Max(0, state.Duration);
        var position = double.IsFinite(state.Position) ? Math.Max(0, state.Position) : 0;
        if (state.Status == AudioPlaybackStatus.Playing && stateTimestamp != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(stateTimestamp).TotalSeconds;
            position += elapsed * Math.Clamp(state.Speed, 0.25, 4);
        }

        return duration > 0
            ? Math.Clamp(position, 0, duration)
            : position;
    }

    private void UpdateAudioProgressTimerState()
    {
        if (!_isClosed && _audioPlaybackState.Status == AudioPlaybackStatus.Playing)
        {
            if (!_audioProgressTimer.IsRunning)
            {
                _audioProgressTimer.Start();
            }
            return;
        }

        StopAudioProgressTimer();
    }

    private void StopAudioProgressTimer()
    {
        if (_audioProgressTimer.IsRunning)
        {
            _audioProgressTimer.Stop();
        }
    }

    private static void SetDynamicButtonDescription(
        FrameworkElement element,
        string value,
        string? shortcut = null)
    {
        ToolTipService.SetToolTip(
            element,
            shortcut is null
                ? value
                : FormatAudioShortcutToolTip(value, shortcut));
        AutomationProperties.SetName(element, value);
    }

    private void UpdateAudioVolumeButtonDescription()
    {
        SetDynamicButtonDescription(
            CompactMuteButton,
            GetAudioPlayerString(
                "AudioPlayer_OpenVolume",
                "Open volume controls"),
            GetAudioPlayerString(
                "AudioPlayer_ShortcutVolume",
                "Up / Down · M"));
        AutomationProperties.SetItemStatus(CompactMuteButton, string.Empty);
    }

    private static void SetLiveRegionText(TextBlock element, string value)
    {
        if (string.Equals(element.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        element.Text = value;
        var peer = FrameworkElementAutomationPeer.FromElement(element) ??
            FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private async Task ApplyAudioMetadataAsync(AudioMediaMetadata metadata)
    {
        if (_isClosed)
        {
            return;
        }

        var version = ++_audioMetadataVersion;
        var hadPreviousMetadata = !string.IsNullOrWhiteSpace(_displayedAudioMediaPath);
        var mediaChanged = !string.Equals(
            _displayedAudioMediaPath,
            metadata.FilePath,
            StringComparison.OrdinalIgnoreCase);
        _displayedAudioMediaPath = metadata.FilePath;
        var currentQueueItem = _playbackCoordinator.PlaybackQueue.FirstOrDefault(
            item => item.IsCurrent &&
                item.Kind == MediaLibraryItemKind.Audio &&
                string.Equals(item.FilePath, metadata.FilePath, StringComparison.OrdinalIgnoreCase));
        _ = LoadAudioLyricsAsync(metadata, version, currentQueueItem?.LinkedFilePath);
        var unknownArtist = GetAudioPlayerString("AudioPlayer_UnknownArtist", "Unknown artist");
        var artistSource = currentQueueItem?.MediaId is not null
            ? currentQueueItem.Artist
            : metadata.Artist;
        var albumSource = currentQueueItem?.MediaId is not null
            ? currentQueueItem.Album
            : metadata.Album;
        var artist = string.IsNullOrWhiteSpace(artistSource) ? unknownArtist : artistSource;
        var album = string.IsNullOrWhiteSpace(albumSource)
            ? GetAudioPlayerString("AudioPlayer_UnknownAlbum", "Unknown album")
            : albumSource;
        ApplyAudioMetadataText(
            currentQueueItem?.Title ?? metadata.Title,
            metadata.FilePath,
            artist,
            album,
            animate: mediaChanged,
            hadPreviousMetadata: hadPreviousMetadata);

        ClearAudioArtwork();

        if (currentQueueItem?.MediaId is { } mediaId)
        {
            var thumbnailPath = currentQueueItem.ThumbnailPath;
            if (string.IsNullOrWhiteSpace(thumbnailPath) ||
                !File.Exists(thumbnailPath))
            {
                thumbnailPath = await MediaThumbnailResolver.TryCreateAudioFromPlaybackMetadataAsync(
                    metadata.FilePath,
                    metadata);
                if (_isClosed || version != _audioMetadataVersion)
                {
                    return;
                }

                if (!string.Equals(
                        currentQueueItem.ThumbnailPath,
                        thumbnailPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var scope = App.Services.CreateScope();
                        var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
                        await library.SetThumbnailAsync(mediaId, thumbnailPath);
                        _playbackCoordinator.UpdateQueueItemThumbnail(
                            mediaId,
                            metadata.FilePath,
                            thumbnailPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"Failed to persist audio artwork for '{metadata.FilePath}': {ex.Message}");
                    }
                }
            }

            await ApplyAudioArtworkFromPathAsync(thumbnailPath, version);
            return;
        }

        if (metadata.AlbumArt is not { Length: > 0 })
        {
            return;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(metadata.AlbumArt.AsBuffer());
            stream.Seek(0);
            var source = new BitmapImage
            {
                DecodePixelWidth = 440
            };
            await source.SetSourceAsync(stream);
            if (_isClosed || version != _audioMetadataVersion)
            {
                return;
            }

            ApplyAudioArtworkSource(source, artworkPath: null);
        }
        catch
        {
        }
    }

    private async Task ApplyAudioArtworkFromPathAsync(string? thumbnailPath, long version)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath) ||
            !File.Exists(thumbnailPath))
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(thumbnailPath);
            using var stream = await file.OpenReadAsync();
            var source = new BitmapImage
            {
                DecodePixelWidth = 440
            };
            await source.SetSourceAsync(stream);
            if (_isClosed || version != _audioMetadataVersion)
            {
                return;
            }

            ApplyAudioArtworkSource(source, thumbnailPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Failed to load persisted audio artwork '{thumbnailPath}': {ex.Message}");
        }
    }

    private void ApplyAudioArtistAndAlbum(string artist, string album)
    {
        CompactAudioArtistText.Text = artist;
        ExpandedAudioArtistText.Text = artist;
        LongTextToolTip.SetText(CompactAudioArtistText, artist);
        LongTextToolTip.SetText(ExpandedAudioArtistText, artist);
        ExpandedAudioAlbumText.Text = album;
        LongTextToolTip.SetText(ExpandedAudioAlbumText, album);
    }

    private void ApplyAudioMetadataText(
        string? title,
        string filePath,
        string artist,
        string album,
        bool animate,
        bool hadPreviousMetadata)
    {
        CancelMotion(ref _audioMetadataMotionCancellation);
        if (!animate || !MotionHelper.AnimationsEnabled)
        {
            ApplyAudioTitle(title, filePath);
            ApplyAudioArtistAndAlbum(artist, album);
            MotionHelper.SetVisibleInstant(
                CompactAudioMetadataPanel,
                isVisible: true);
            MotionHelper.SetVisibleInstant(
                ExpandedAudioMetadataPanel,
                isVisible: true);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _audioMetadataMotionCancellation = cancellation;
        _ = AnimateAudioMetadataTextAsync(
            title,
            filePath,
            artist,
            album,
            hadPreviousMetadata,
            cancellation);
    }

    private async Task AnimateAudioMetadataTextAsync(
        string? title,
        string filePath,
        string artist,
        string album,
        bool hadPreviousMetadata,
        CancellationTokenSource cancellation)
    {
        try
        {
            if (hadPreviousMetadata)
            {
                await Task.WhenAll(
                    MotionHelper.HideAsync(
                        CompactAudioMetadataPanel,
                        MotionPreset.Fast,
                        MotionDirection.Up,
                        distance: 8,
                        collapse: false,
                        cancellationToken: cancellation.Token),
                    MotionHelper.HideAsync(
                        ExpandedAudioMetadataPanel,
                        MotionPreset.Fast,
                        MotionDirection.Up,
                        distance: 8,
                        collapse: false,
                        cancellationToken: cancellation.Token));
            }

            if (!ReferenceEquals(_audioMetadataMotionCancellation, cancellation) ||
                cancellation.IsCancellationRequested)
            {
                return;
            }

            ApplyAudioTitle(title, filePath);
            ApplyAudioArtistAndAlbum(artist, album);
            await Task.WhenAll(
                MotionHelper.ShowAsync(
                    CompactAudioMetadataPanel,
                    MotionPreset.Standard,
                    MotionDirection.Down,
                    distance: 8,
                    cancellationToken: cancellation.Token),
                MotionHelper.ShowAsync(
                    ExpandedAudioMetadataPanel,
                    MotionPreset.Standard,
                    MotionDirection.Down,
                    distance: 8,
                    cancellationToken: cancellation.Token));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_audioMetadataMotionCancellation, cancellation))
            {
                _audioMetadataMotionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void ApplyAudioTitle(string? title, string filePath)
    {
        var displayTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : title.Trim();
        CompactAudioTitleText.Text = displayTitle;
        ExpandedAudioTitleText.Text = displayTitle;
        var titleToolTipText = LongTextToolTip.CreateMediaText(displayTitle, filePath);
        LongTextToolTip.SetText(CompactAudioTitleText, titleToolTipText);
        LongTextToolTip.SetText(ExpandedAudioTitleText, titleToolTipText);
    }

    private void ClearAudioArtwork()
    {
        var motionVersion = ++_audioArtworkMotionVersion;
        _audioArtworkPath = null;
        if (_isClosed ||
            !MotionHelper.AnimationsEnabled ||
            (CompactArtworkImage.Source is null && ExpandedArtworkImage.Source is null))
        {
            MotionHelper.SetVisibleInstant(CompactArtworkImage, isVisible: false);
            MotionHelper.SetVisibleInstant(ExpandedArtworkImage, isVisible: false);
            MotionHelper.SetVisibleInstant(CompactArtworkFallback, isVisible: true);
            MotionHelper.SetVisibleInstant(ExpandedArtworkFallback, isVisible: true);
            CompactArtworkImage.Source = null;
            ExpandedArtworkImage.Source = null;
            return;
        }

        _ = ClearAudioArtworkAsync(motionVersion);
    }

    private async Task ClearAudioArtworkAsync(long motionVersion)
    {
        await Task.WhenAll(
            MotionHelper.CrossFadeAsync(
                CompactArtworkImage,
                CompactArtworkFallback,
                MotionPreset.Fast,
                MotionDirection.None),
            MotionHelper.CrossFadeAsync(
                ExpandedArtworkImage,
                ExpandedArtworkFallback,
                MotionPreset.Fast,
                MotionDirection.None));
        if (motionVersion != _audioArtworkMotionVersion)
        {
            return;
        }

        CompactArtworkImage.Source = null;
        ExpandedArtworkImage.Source = null;
    }

    private void ApplyAudioArtworkSource(BitmapImage source, string? artworkPath)
    {
        ArgumentNullException.ThrowIfNull(source);

        _audioArtworkMotionVersion++;
        _audioArtworkPath = artworkPath;
        CompactArtworkImage.Source = source;
        ExpandedArtworkImage.Source = source;
        _ = Task.WhenAll(
            MotionHelper.CrossFadeAsync(
                CompactArtworkFallback,
                CompactArtworkImage,
                MotionPreset.Standard,
                MotionDirection.None),
            MotionHelper.CrossFadeAsync(
                ExpandedArtworkFallback,
                ExpandedArtworkImage,
                MotionPreset.Standard,
                MotionDirection.None));
    }

    private async Task LoadAudioLyricsAsync(
        AudioMediaMetadata metadata,
        long metadataVersion,
        string? linkedLyricsFilePath)
    {
        if (_isClosed)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        var previousCancellation = _audioLyricsLoadCancellation;
        _audioLyricsLoadCancellation = cancellation;
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        ResetAudioLyrics(showLoading: true);
        try
        {
            var document = await AudioLyricsLoader.LoadAsync(
                metadata.FilePath,
                metadata.Lyrics,
                linkedLyricsFilePath,
                cancellation.Token);
            if (_isClosed ||
                cancellation.IsCancellationRequested ||
                metadataVersion != _audioMetadataVersion ||
                !ReferenceEquals(_audioLyricsLoadCancellation, cancellation))
            {
                return;
            }

            _hasSynchronizedAudioLyrics = document.IsSynchronized;
            foreach (var line in document.Lines)
            {
                AudioLyricLines.Add(new AudioLyricLineViewModel(line));
            }

            if (AudioLyricLines.Count == 0)
            {
                SetAudioLyricsSurface(ExpandedLyricsEmptyPanel, animate: true);
                return;
            }

            SetAudioLyricsSurface(ExpandedLyricsList, animate: true);
            ExpandedLyricsSourceText.Text = document.Source switch
            {
                AudioLyricsSource.Sidecar => GetAudioPlayerString("AudioPlayer_LyricsSourceSidecar", "LRC"),
                AudioLyricsSource.Embedded => GetAudioPlayerString("AudioPlayer_LyricsSourceEmbedded", "Embedded"),
                _ => string.Empty
            };
            ExpandedLyricsSourceText.Visibility = string.IsNullOrWhiteSpace(ExpandedLyricsSourceText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateActiveAudioLyric(GetProjectedAudioPosition(
                _audioPlaybackState,
                _audioPlaybackStateTimestamp));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            if (metadataVersion == _audioMetadataVersion &&
                ReferenceEquals(_audioLyricsLoadCancellation, cancellation))
            {
                ResetAudioLyrics();
            }
        }
        finally
        {
            if (ReferenceEquals(_audioLyricsLoadCancellation, cancellation))
            {
                _audioLyricsLoadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ResetAudioLyrics(bool showLoading = false)
    {
        AudioLyricLines.Clear();
        _activeAudioLyricIndex = -1;
        _hasSynchronizedAudioLyrics = false;
        ExpandedLyricsSourceText.Text = string.Empty;
        ExpandedLyricsSourceText.Visibility = Visibility.Collapsed;
        SetAudioLyricsSurface(
            showLoading ? ExpandedLyricsLoadingPanel : ExpandedLyricsEmptyPanel,
            animate: true);
    }

    private void SetAudioLyricsSurface(UIElement target, bool animate)
    {
        if (ReferenceEquals(_audioLyricsVisibleSurface, target))
        {
            return;
        }

        var previous = _audioLyricsVisibleSurface;
        _audioLyricsVisibleSurface = target;
        CancelMotion(ref _audioLyricsStateMotionCancellation);
        if (!animate || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(
                ExpandedLyricsList,
                ReferenceEquals(target, ExpandedLyricsList));
            MotionHelper.SetVisibleInstant(
                ExpandedLyricsLoadingPanel,
                ReferenceEquals(target, ExpandedLyricsLoadingPanel));
            MotionHelper.SetVisibleInstant(
                ExpandedLyricsEmptyPanel,
                ReferenceEquals(target, ExpandedLyricsEmptyPanel));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _audioLyricsStateMotionCancellation = cancellation;
        _ = AnimateAudioLyricsSurfaceAsync(previous, target, cancellation);
    }

    private async Task AnimateAudioLyricsSurfaceAsync(
        UIElement? previous,
        UIElement target,
        CancellationTokenSource cancellation)
    {
        try
        {
            await MotionHelper.CrossFadeAsync(
                previous,
                target,
                MotionPreset.Standard,
                MotionDirection.Down,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_audioLyricsStateMotionCancellation, cancellation))
            {
                _audioLyricsStateMotionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void CancelAudioLyricsLoad()
    {
        var cancellation = _audioLyricsLoadCancellation;
        _audioLyricsLoadCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void UpdateActiveAudioLyric(double positionSeconds)
    {
        if (!_hasSynchronizedAudioLyrics || AudioLyricLines.Count == 0)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Max(0, positionSeconds));
        var lower = 0;
        var upper = AudioLyricLines.Count - 1;
        var activeIndex = -1;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            if (AudioLyricLines[middle].Timestamp is { } timestamp && timestamp <= position)
            {
                activeIndex = middle;
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        if (activeIndex == _activeAudioLyricIndex)
        {
            return;
        }

        if (_activeAudioLyricIndex >= 0 && _activeAudioLyricIndex < AudioLyricLines.Count)
        {
            AudioLyricLines[_activeAudioLyricIndex].IsActive = false;
        }

        _activeAudioLyricIndex = activeIndex;
        if (activeIndex < 0)
        {
            return;
        }

        var activeLine = AudioLyricLines[activeIndex];
        activeLine.IsActive = true;
        if (_isAudioExpanded)
        {
            ScrollActiveAudioLyricIntoView(activeLine, MotionIntent.PlaybackFollow);
        }
    }

    private void ScrollActiveAudioLyricIntoView(
        AudioLyricLineViewModel? activeLine,
        MotionIntent intent)
    {
        activeLine ??= _activeAudioLyricIndex >= 0 &&
                       _activeAudioLyricIndex < AudioLyricLines.Count
            ? AudioLyricLines[_activeAudioLyricIndex]
            : null;
        if (activeLine is null)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            MotionHelper.BringIntoView(
                ExpandedLyricsList,
                activeLine,
                intent,
                ScrollIntoViewAlignment.Default,
                verticalAlignmentRatio: 0.5);
        });
    }

    private async void AudioPlayPauseButton_Click(object sender, RoutedEventArgs args)
    {
        await RunAudioCommandAsync(ToggleAudioPlaybackAsync);
    }

    private async Task ToggleAudioPlaybackAsync()
    {
        if (_audioPlaybackState.Status == AudioPlaybackStatus.Stopped &&
            _audioPlaybackState.Duration > 0 &&
            _audioPlaybackState.Position >= _audioPlaybackState.Duration - 0.1)
        {
            await _playbackCoordinator.ReplayCurrentAsync();
            return;
        }

        await _audioPlaybackService.SetPausedAsync(
            _audioPlaybackState.Status == AudioPlaybackStatus.Playing);
    }

    private async void AudioMuteButton_Click(object sender, RoutedEventArgs args)
    {
        await RunAudioCommandAsync(ToggleAudioMuteAsync);
    }

    private async Task ToggleAudioMuteAsync()
    {
        var isEffectivelyMuted =
            _audioPlaybackState.IsMuted || _audioPlaybackState.Volume <= 0;
        if (!isEffectivelyMuted)
        {
            await _audioPlaybackService.SetMutedAsync(true);
            return;
        }

        CancelPendingAudioVolume();
        if (_audioPlaybackState.Volume <= 0)
        {
            await _audioPlaybackService.SetVolumeAsync(
                Math.Clamp(_lastAudibleAudioVolume, 1, 100));
        }

        if (_audioPlaybackState.IsMuted)
        {
            await _audioPlaybackService.SetMutedAsync(false);
        }
    }

    private async void AudioPreviousButton_Click(object sender, RoutedEventArgs args)
    {
        await RunAudioCommandAsync(() => _playbackCoordinator.PlayPreviousAsync());
    }

    private async void AudioNextButton_Click(object sender, RoutedEventArgs args)
    {
        await RunAudioCommandAsync(() => _playbackCoordinator.PlayNextAsync());
    }

    private void AudioShuffleButton_Click(object sender, RoutedEventArgs args)
    {
        _playbackCoordinator.IsShuffleEnabled = !_playbackCoordinator.IsShuffleEnabled;
        UpdateAudioPlaybackModeControls();
    }

    private void AudioRepeatButton_Click(object sender, RoutedEventArgs args)
    {
        _playbackCoordinator.RepeatMode = _playbackCoordinator.RepeatMode switch
        {
            AudioRepeatMode.Off => AudioRepeatMode.All,
            AudioRepeatMode.All => AudioRepeatMode.One,
            _ => AudioRepeatMode.Off
        };
        UpdateAudioPlaybackModeControls();
    }

    public void TogglePlaybackQueueSidebar(MediaLibraryItemKind kind)
    {
        if (_isPlaybackQueueSidebarOpen)
        {
            SetPlaybackQueueSidebarVisible(false);
            return;
        }

        RefreshPlaybackQueueSidebar();
        SetPlaybackQueueSidebarVisible(true);
    }

    private void PlaybackQueueNavigationItem_Tapped(
        object sender,
        TappedRoutedEventArgs args)
    {
        ToggleDefaultPlaybackQueueSidebar();
        args.Handled = true;
    }

    private void PlaybackQueueNavigationItem_KeyDown(
        object sender,
        KeyRoutedEventArgs args)
    {
        if (args.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space))
        {
            return;
        }

        ToggleDefaultPlaybackQueueSidebar();
        args.Handled = true;
    }

    private void ToggleDefaultPlaybackQueueSidebar()
    {
        TogglePlaybackQueueSidebar(MediaLibraryItemKind.Audio);
    }

    private void SetPlaybackQueueSidebarVisible(bool isVisible)
    {
        var wasVisible = _isPlaybackQueueSidebarOpen;
        if (isVisible == wasVisible && !_isPlaybackQueueSidebarClosing)
        {
            return;
        }

        if (isVisible && !wasVisible)
        {
            _playbackQueueFocusRestoreElement =
                FocusManager.GetFocusedElement(MainRoot.XamlRoot) as Control ??
                PlaybackQueueNavigationItem;
        }

        _playbackQueueMotionCancellation?.Cancel();
        _playbackQueueMotionCancellation?.Dispose();
        _playbackQueueMotionCancellation = new CancellationTokenSource();

        _isPlaybackQueueSidebarOpen = isVisible;
        _isPlaybackQueueSidebarClosing = !isVisible && wasVisible;
        var availableWidth = MainContentGrid.ActualWidth > 0
            ? MainContentGrid.ActualWidth
            : MainRoot.ActualWidth;
        var isOverlay = ResponsiveLayout.Resolve(availableWidth) is
            UiBreakpoint.Compact or UiBreakpoint.Medium;

        if (isVisible)
        {
            _isPlaybackQueueSidebarClosing = false;
            PlaybackQueueSidebar.Visibility = Visibility.Visible;
            UpdatePlaybackQueueLayout(availableWidth);
            _ = AnimatePlaybackQueueSidebarAsync(
                isVisible: true,
                isOverlay,
                _playbackQueueMotionCancellation.Token);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                var focusTarget = PlaybackQueueSidebarList.Visibility == Visibility.Visible
                    ? (Control)PlaybackQueueSidebarList
                    : PlaybackQueueSidebarCloseButton;
                focusTarget.Focus(FocusState.Programmatic);
            });
            ScrollPlaybackQueueSidebarToCurrentItem(MotionIntent.InitialPosition);
        }
        else if (wasVisible)
        {
            RestorePlaybackQueueFocus();
            PlaybackQueueResizeHandle.Visibility = Visibility.Collapsed;
            _ = AnimatePlaybackQueueSidebarAsync(
                isVisible: false,
                isOverlay,
                _playbackQueueMotionCancellation.Token);
        }
    }

    private async Task AnimatePlaybackQueueSidebarAsync(
        bool isVisible,
        bool isOverlay,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isVisible)
            {
                var tasks = new List<Task>
                {
                    MotionHelper.ShowAsync(
                        PlaybackQueueSidebar,
                        MotionPreset.Panel,
                        MotionDirection.Right,
                        distance: isOverlay ? 16 : 12,
                        cancellationToken: cancellationToken)
                };

                if (isOverlay)
                {
                    tasks.Add(MotionHelper.ShowAsync(
                        PlaybackQueueOverlayScrim,
                        MotionPreset.Standard,
                        MotionDirection.None,
                        distance: 0,
                        cancellationToken: cancellationToken));
                }
                else
                {
                    MotionHelper.SetVisibleInstant(
                        PlaybackQueueOverlayScrim,
                        isVisible: false,
                        isHitTestVisible: false);
                }

                await Task.WhenAll(tasks);
                return;
            }

            var closingTasks = new List<Task>
            {
                MotionHelper.HideAsync(
                    PlaybackQueueSidebar,
                    MotionPreset.Panel,
                    MotionDirection.Right,
                    distance: isOverlay ? 16 : 12,
                    cancellationToken: cancellationToken)
            };
            if (PlaybackQueueOverlayScrim.Visibility == Visibility.Visible)
            {
                closingTasks.Add(MotionHelper.HideAsync(
                    PlaybackQueueOverlayScrim,
                    MotionPreset.Standard,
                    MotionDirection.None,
                    distance: 0,
                    cancellationToken: cancellationToken));
            }

            await Task.WhenAll(closingTasks);
            if (cancellationToken.IsCancellationRequested || _isPlaybackQueueSidebarOpen)
            {
                return;
            }

            _isPlaybackQueueSidebarClosing = false;
            UpdatePlaybackQueueLayout(
                MainContentGrid.ActualWidth > 0
                    ? MainContentGrid.ActualWidth
                    : MainRoot.ActualWidth);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _playbackQueueMotionCancellation?.Dispose();
                _playbackQueueMotionCancellation = null;
            }
        }
    }

    private void UpdatePlaybackQueueLayout(double availableWidth)
    {
        var isOverlay = ResponsiveLayout.Resolve(availableWidth) is
            UiBreakpoint.Compact or UiBreakpoint.Medium;
        var isLayoutOpen = _isPlaybackQueueSidebarOpen || _isPlaybackQueueSidebarClosing;

        if (isOverlay)
        {
            Grid.SetColumn(PlaybackQueueSidebar, 0);
            Grid.SetColumnSpan(PlaybackQueueSidebar, 3);
            PlaybackQueueSidebar.HorizontalAlignment = HorizontalAlignment.Right;
            PlaybackQueueSidebar.Margin = new Thickness(
                PlaybackQueueOuterMargin,
                0,
                PlaybackQueueOuterRightMargin,
                0);
            var maximumOverlayWidth = Math.Max(
                0,
                availableWidth -
                PlaybackQueueOuterMargin -
                PlaybackQueueOuterRightMargin);
            PlaybackQueueSidebar.Width = Math.Min(
                maximumOverlayWidth,
                Math.Max(
                    PlaybackQueueMinimumWidth,
                    Math.Min(420, availableWidth * 0.82)));
            PlaybackQueueSidebarColumn.Width = new GridLength(0);
            PlaybackQueueResizeColumn.Width = new GridLength(0);
            PlaybackQueueResizeHandle.Visibility = Visibility.Collapsed;
            PlaybackQueueOverlayScrim.Visibility = isLayoutOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        Grid.SetColumn(PlaybackQueueSidebar, 2);
        Grid.SetColumnSpan(PlaybackQueueSidebar, 1);
        PlaybackQueueSidebar.HorizontalAlignment = HorizontalAlignment.Stretch;
        PlaybackQueueSidebar.Margin = new Thickness(
            0,
            0,
            PlaybackQueueOuterRightMargin,
            0);
        PlaybackQueueSidebar.Width = double.NaN;
        PlaybackQueueOverlayScrim.Visibility = Visibility.Collapsed;
        PlaybackQueueResizeHandle.Visibility = _isPlaybackQueueSidebarOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaybackQueueResizeColumn.Width = isLayoutOpen
            ? new GridLength(PlaybackQueueResizeHandleWidth)
            : new GridLength(0);
        PlaybackQueueSidebarColumn.Width = isLayoutOpen
            ? new GridLength(ClampPlaybackQueueSidebarWidth(
                _playbackQueueSidebarWidth,
                availableWidth))
            : new GridLength(0);
    }

    private void RestorePlaybackQueueFocus()
    {
        var focusTarget = _playbackQueueFocusRestoreElement;
        _playbackQueueFocusRestoreElement = null;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!TryFocusControl(focusTarget))
            {
                FocusMainSurface();
            }
        });
    }

    private void PlaybackQueueOverlayScrim_Tapped(
        object sender,
        TappedRoutedEventArgs args)
    {
        SetPlaybackQueueSidebarVisible(false);
        args.Handled = true;
    }

    private static double ClampPlaybackQueueSidebarWidth(
        double requestedWidth,
        double windowWidth)
    {
        var maximumWidth = Math.Min(
            PlaybackQueueMaximumWidth,
            Math.Max(
                PlaybackQueueMinimumWidth,
                windowWidth - PlaybackQueueMinimumContentWidth - PlaybackQueueResizeHandleWidth));
        return Math.Clamp(requestedWidth, PlaybackQueueMinimumWidth, maximumWidth);
    }

    private void PlaybackQueueResizeHandle_PointerPressed(
        object sender,
        PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(MainRoot);
        if (!point.Properties.IsLeftButtonPressed ||
            sender is not UIElement resizeHandle)
        {
            return;
        }

        _playbackQueueResizePointerId = args.Pointer.PointerId;
        _playbackQueueResizeStartX = point.Position.X;
        _playbackQueueResizeStartWidth = PlaybackQueueSidebarColumn.Width.Value;
        resizeHandle.CapturePointer(args.Pointer);
        PlaybackQueueResizeIndicator.Width = 2;
        args.Handled = true;
    }

    private void PlaybackQueueResizeHandle_KeyDown(
        object sender,
        KeyRoutedEventArgs args)
    {
        var widthDelta = args.Key switch
        {
            Windows.System.VirtualKey.Left => 24,
            Windows.System.VirtualKey.Right => -24,
            _ => 0
        };
        if (widthDelta == 0 ||
            ResponsiveLayout.Resolve(MainContentGrid.ActualWidth) is
                UiBreakpoint.Compact or UiBreakpoint.Medium)
        {
            return;
        }

        _playbackQueueSidebarWidth = ClampPlaybackQueueSidebarWidth(
            _playbackQueueSidebarWidth + widthDelta,
            MainContentGrid.ActualWidth);
        PlaybackQueueSidebarColumn.Width = new GridLength(_playbackQueueSidebarWidth);
        SavePlaybackQueueSidebarWidth();
        args.Handled = true;
    }

    private void PlaybackQueueResizeHandle_PointerMoved(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (_playbackQueueResizePointerId != args.Pointer.PointerId)
        {
            return;
        }

        var point = args.GetCurrentPoint(MainRoot);
        var requestedWidth = _playbackQueueResizeStartWidth +
            _playbackQueueResizeStartX -
            point.Position.X;
        _playbackQueueSidebarWidth = ClampPlaybackQueueSidebarWidth(
            requestedWidth,
            MainRoot.ActualWidth);
        PlaybackQueueSidebarColumn.Width =
            new GridLength(_playbackQueueSidebarWidth);
        args.Handled = true;
    }

    private void PlaybackQueueResizeHandle_PointerReleased(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (sender is UIElement resizeHandle)
        {
            resizeHandle.ReleasePointerCapture(args.Pointer);
        }
        CompletePlaybackQueueResize();
        args.Handled = true;
    }

    private void PlaybackQueueResizeHandle_PointerCanceled(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (sender is UIElement resizeHandle)
        {
            resizeHandle.ReleasePointerCapture(args.Pointer);
        }
        CompletePlaybackQueueResize();
    }

    private void PlaybackQueueResizeHandle_PointerCaptureLost(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (_playbackQueueResizePointerId == args.Pointer.PointerId)
        {
            CompletePlaybackQueueResize();
        }
    }

    private void CompletePlaybackQueueResize()
    {
        if (_playbackQueueResizePointerId is null)
        {
            return;
        }

        _playbackQueueResizePointerId = null;
        PlaybackQueueResizeIndicator.Width = 1;
        SavePlaybackQueueSidebarWidth();
    }

    private void RestorePlaybackQueueSidebarWidth()
    {
        try
        {
            if (Windows.Storage.ApplicationData.Current.LocalSettings.Values[
                    PlaybackQueueWidthSettingKey] is double savedWidth)
            {
                _playbackQueueSidebarWidth = savedWidth;
            }
        }
        catch
        {
            _playbackQueueSidebarWidth = PlaybackQueueDefaultWidth;
        }
    }

    private void SavePlaybackQueueSidebarWidth()
    {
        try
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[
                PlaybackQueueWidthSettingKey] = _playbackQueueSidebarWidth;
        }
        catch
        {
            // Sidebar sizing remains usable even when persistence is unavailable.
        }
    }

    private void RefreshPlaybackQueueSidebar()
    {
        var focusSnapshot = CapturePlaybackQueueItemFocus();
        var selectedStableKey =
            (PlaybackQueueSidebarList.SelectedItem as PlaybackQueueSidebarItem)?.StableKey;
        var currentIndex = -1;
        string? currentPath = null;

        var queue = _playbackCoordinator.PlaybackQueue;
        var desiredItems = new List<PlaybackQueueSidebarItem>(queue.Count);
        var stableKeyOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < queue.Count; index++)
        {
            var item = queue[index];
            var stableKeyRoot = item.MediaId is { } mediaId
                ? mediaId.ToString("N")
                : $"{(int)item.Kind}:{item.FilePath}";
            stableKeyOccurrences.TryGetValue(stableKeyRoot, out var occurrence);
            stableKeyOccurrences[stableKeyRoot] = occurrence + 1;
            desiredItems.Add(new PlaybackQueueSidebarItem(
                $"{stableKeyRoot}\u001F{occurrence}",
                item.Title,
                item.FilePath,
                item.IsCurrent,
                item.Kind,
                canRemove: true));
            if (item.IsCurrent)
            {
                currentIndex = index;
                currentPath = item.FilePath;
            }
        }
        ReconcilePlaybackQueueSidebarItems(desiredItems);
        PlaybackQueueSidebarList.SelectedItem = string.IsNullOrWhiteSpace(selectedStableKey)
            ? null
            : PlaybackQueueSidebarItems.FirstOrDefault(item =>
                string.Equals(
                    item.StableKey,
                    selectedStableKey,
                    StringComparison.OrdinalIgnoreCase));

        var currentItemChanged =
            _trackedPlaybackQueueCurrentIndex != currentIndex ||
            !string.Equals(
                _trackedPlaybackQueueCurrentPath,
                currentPath,
                StringComparison.OrdinalIgnoreCase);
        _trackedPlaybackQueueCurrentIndex = currentIndex;
        _trackedPlaybackQueueCurrentPath = currentPath;
        SetLiveRegionText(
            PlaybackQueueSidebarCountText,
            string.Format(
                GetAudioPlayerString("PlaybackQueueSidebar_CountFormat", "{0} items"),
                PlaybackQueueSidebarItems.Count));
        var hasItems = PlaybackQueueSidebarItems.Count > 0;
        UpdatePlaybackQueueBodyState(hasItems);
        PlaybackQueueSidebarSaveButton.IsEnabled = hasItems && !_isPlaybackQueueSidebarBusy;
        PlaybackQueueSidebarClearButton.IsEnabled = hasItems && !_isPlaybackQueueSidebarBusy;

        if (currentItemChanged)
        {
            ScrollPlaybackQueueSidebarToCurrentItem(MotionIntent.PlaybackFollow);
        }
        else
        {
            RestorePlaybackQueueItemFocus(focusSnapshot);
        }
    }

    private void ReconcilePlaybackQueueSidebarItems(
        IReadOnlyList<PlaybackQueueSidebarItem> desiredItems)
    {
        for (var targetIndex = 0; targetIndex < desiredItems.Count; targetIndex++)
        {
            var desiredItem = desiredItems[targetIndex];
            if (targetIndex < PlaybackQueueSidebarItems.Count &&
                string.Equals(
                    PlaybackQueueSidebarItems[targetIndex].StableKey,
                    desiredItem.StableKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                PlaybackQueueSidebarItems[targetIndex].ApplySnapshot(desiredItem);
                continue;
            }

            var currentIndex = -1;
            for (var index = targetIndex + 1;
                 index < PlaybackQueueSidebarItems.Count;
                 index++)
            {
                if (string.Equals(
                        PlaybackQueueSidebarItems[index].StableKey,
                        desiredItem.StableKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex >= 0)
            {
                var existingItem = PlaybackQueueSidebarItems[currentIndex];
                PlaybackQueueSidebarItems.Move(currentIndex, targetIndex);
                existingItem.ApplySnapshot(desiredItem);
            }
            else
            {
                PlaybackQueueSidebarItems.Insert(targetIndex, desiredItem);
            }
        }

        while (PlaybackQueueSidebarItems.Count > desiredItems.Count)
        {
            PlaybackQueueSidebarItems.RemoveAt(PlaybackQueueSidebarItems.Count - 1);
        }
    }

    private void UpdatePlaybackQueueBodyState(bool hasItems)
    {
        if (_playbackQueueShowsItems == hasItems)
        {
            return;
        }

        _playbackQueueShowsItems = hasItems;
        if (!_isPlaybackQueueSidebarOpen || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(PlaybackQueueSidebarList, hasItems);
            MotionHelper.SetVisibleInstant(PlaybackQueueSidebarEmptyPanel, !hasItems);
            return;
        }

        _ = MotionHelper.CrossFadeAsync(
            hasItems ? PlaybackQueueSidebarEmptyPanel : PlaybackQueueSidebarList,
            hasItems ? PlaybackQueueSidebarList : PlaybackQueueSidebarEmptyPanel,
            MotionPreset.Standard,
            MotionDirection.Down);
    }

    private PlaybackQueueFocusSnapshot? CapturePlaybackQueueItemFocus()
    {
        if (!_isPlaybackQueueSidebarOpen ||
            FocusManager.GetFocusedElement(MainRoot.XamlRoot) is not DependencyObject focusedElement ||
            !IsDescendantOf(focusedElement, PlaybackQueueSidebarList))
        {
            return null;
        }

        DependencyObject? current = focusedElement;
        while (current is not null && !ReferenceEquals(current, PlaybackQueueSidebarList))
        {
            var item = current switch
            {
                ListViewItem { Content: PlaybackQueueSidebarItem containerItem } => containerItem,
                FrameworkElement { Tag: PlaybackQueueSidebarItem taggedItem } => taggedItem,
                FrameworkElement { DataContext: PlaybackQueueSidebarItem dataItem } => dataItem,
                _ => null
            };
            if (item is not null)
            {
                var index = PlaybackQueueSidebarItems.IndexOf(item);
                return index >= 0
                    ? new PlaybackQueueFocusSnapshot(item.FilePath, index)
                    : null;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void RestorePlaybackQueueItemFocus(PlaybackQueueFocusSnapshot? snapshot)
    {
        if (snapshot is not { } requestedFocus)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || !_isPlaybackQueueSidebarOpen)
            {
                return;
            }

            var item = !string.IsNullOrWhiteSpace(requestedFocus.FilePath)
                ? PlaybackQueueSidebarItems.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.FilePath,
                        requestedFocus.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (item is null && PlaybackQueueSidebarItems.Count > 0)
            {
                item = PlaybackQueueSidebarItems[
                    Math.Clamp(requestedFocus.Index, 0, PlaybackQueueSidebarItems.Count - 1)];
            }

            if (item is null)
            {
                PlaybackQueueSidebarCloseButton.Focus(FocusState.Programmatic);
                return;
            }

            MotionHelper.BringIntoView(
                PlaybackQueueSidebarList,
                item,
                MotionIntent.FocusRestore,
                ScrollIntoViewAlignment.Default);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (PlaybackQueueSidebarList.ContainerFromItem(item) is not Control container ||
                    !container.Focus(FocusState.Programmatic))
                {
                    PlaybackQueueSidebarList.Focus(FocusState.Programmatic);
                }
            });
        });
    }

    private void ScrollPlaybackQueueSidebarToCurrentItem(MotionIntent intent)
    {
        if (_isClosed ||
            !_isPlaybackQueueSidebarOpen ||
            PlaybackQueueSidebarList.Visibility != Visibility.Visible)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed ||
                !_isPlaybackQueueSidebarOpen ||
                PlaybackQueueSidebarList.Visibility != Visibility.Visible)
            {
                return;
            }

            var currentItem = PlaybackQueueSidebarItems.FirstOrDefault(item => item.IsCurrent);
            if (currentItem is not null)
            {
                MotionHelper.BringIntoView(
                    PlaybackQueueSidebarList,
                    currentItem,
                    intent,
                    ScrollIntoViewAlignment.Leading,
                    verticalAlignmentRatio: 0);
            }
        });
    }

    private void PlaybackQueueSidebarCloseButton_Click(object sender, RoutedEventArgs args)
    {
        SetPlaybackQueueSidebarVisible(false);
    }

    private async void PlaybackQueueSidebarPlayButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: PlaybackQueueSidebarItem item })
        {
            await PlayPlaybackQueueSidebarItemAsync(item);
        }
    }

    private async void PlaybackQueueSidebarList_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs args)
    {
        if (GetPlaybackQueueSidebarItemFromOriginalSource(args.OriginalSource) is not { } item)
        {
            return;
        }

        args.Handled = true;
        await PlayPlaybackQueueSidebarItemAsync(item);
    }

    private PlaybackQueueSidebarItem? GetPlaybackQueueSidebarItemFromOriginalSource(
        object originalSource)
    {
        var current = originalSource as DependencyObject;
        PlaybackQueueSidebarItem? item = null;
        while (current is not null && !ReferenceEquals(current, PlaybackQueueSidebarList))
        {
            if (current is Button)
            {
                return null;
            }

            item ??= current switch
            {
                ListViewItem { Content: PlaybackQueueSidebarItem containerItem } => containerItem,
                FrameworkElement { Tag: PlaybackQueueSidebarItem taggedItem } => taggedItem,
                FrameworkElement { DataContext: PlaybackQueueSidebarItem dataItem } => dataItem,
                _ => null
            };
            current = VisualTreeHelper.GetParent(current);
        }

        return item;
    }

    private async Task PlayPlaybackQueueSidebarItemAsync(PlaybackQueueSidebarItem item)
    {
        if (_isPlaybackQueueSidebarBusy)
        {
            return;
        }

        var index = PlaybackQueueSidebarItems.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        _isPlaybackQueueSidebarBusy = true;
        try
        {
            await _playbackCoordinator.PlayQueueItemAsync(index);

            RefreshPlaybackQueueSidebar();
        }
        catch (Exception ex)
        {
            SetLiveRegionText(PlaybackQueueSidebarCountText, ex.Message);
        }
        finally
        {
            _isPlaybackQueueSidebarBusy = false;
        }
    }

    private async void PlaybackQueueSidebarRemoveButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: PlaybackQueueSidebarItem item } ||
            _isPlaybackQueueSidebarBusy)
        {
            return;
        }

        var index = PlaybackQueueSidebarItems.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        _isPlaybackQueueSidebarBusy = true;
        try
        {
            await _playbackCoordinator.RemoveQueueItemAsync(index);

            RefreshPlaybackQueueSidebar();
        }
        catch (Exception ex)
        {
            SetLiveRegionText(PlaybackQueueSidebarCountText, ex.Message);
        }
        finally
        {
            _isPlaybackQueueSidebarBusy = false;
        }
    }

    private async void PlaybackQueueSidebarClearButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPlaybackQueueSidebarBusy || PlaybackQueueSidebarItems.Count == 0)
        {
            return;
        }

        _isPlaybackQueueSidebarBusy = true;
        PlaybackQueueSidebarClearButton.IsEnabled = false;
        try
        {
            await _playbackCoordinator.ClearQueueAsync();

            RefreshPlaybackQueueSidebar();
        }
        catch (Exception ex)
        {
            SetLiveRegionText(PlaybackQueueSidebarCountText, ex.Message);
        }
        finally
        {
            _isPlaybackQueueSidebarBusy = false;
            PlaybackQueueSidebarClearButton.IsEnabled = PlaybackQueueSidebarItems.Count > 0;
        }
    }

    private async void PlaybackQueueSidebarSaveButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPlaybackQueueSidebarBusy || App.Services is null)
        {
            return;
        }

        var mediaIds = _playbackCoordinator.PlaybackQueue
            .Where(item => item.MediaId is not null)
            .Select(item => item.MediaId!.Value)
            .Distinct()
            .ToArray();
        if (mediaIds.Length == 0)
        {
            SetLiveRegionText(
                PlaybackQueueSidebarCountText,
                GetAudioPlayerString(
                    "PlaybackQueueSidebar_NoLibraryItems",
                    "The queue has no media-library items to save."));
            return;
        }

        _isPlaybackQueueSidebarBusy = true;
        PlaybackQueueSidebarSaveButton.IsEnabled = false;
        PlaybackQueueSidebarClearButton.IsEnabled = false;
        try
        {
            using var scope = App.Services.CreateScope();
            var playlistBus = scope.ServiceProvider.GetRequiredService<IPlaylistBus>();
            var playlist = await PlaylistPickerHelper.ChooseOrCreateAsync(
                MainRoot.XamlRoot,
                playlistBus,
                GetAudioPlayerString);
            if (playlist is null)
            {
                return;
            }

            var result = await playlistBus.AddItemsAsync(playlist.Id, mediaIds);
            SetLiveRegionText(
                PlaybackQueueSidebarCountText,
                string.Format(
                    GetAudioPlayerString(
                        "PlaybackQueueSidebar_SavedToPlaylist",
                        "Added {0} items to \"{1}\"; {2} already present"),
                    result.AddedCount,
                    playlist.Name,
                    result.DuplicateCount));
        }
        catch (Exception ex)
        {
            SetLiveRegionText(
                PlaybackQueueSidebarCountText,
                string.Format(
                    GetAudioPlayerString(
                        "PlaybackQueueSidebar_SaveFailed",
                        "Could not save the queue: {0}"),
                    ex.Message));
        }
        finally
        {
            _isPlaybackQueueSidebarBusy = false;
            var hasItems = PlaybackQueueSidebarItems.Count > 0;
            PlaybackQueueSidebarSaveButton.IsEnabled = hasItems;
            PlaybackQueueSidebarClearButton.IsEnabled = hasItems;
        }
    }

    private void PlaybackQueueSidebarList_DragItemsStarting(
        object sender,
        DragItemsStartingEventArgs args)
    {
        ResetPlaybackQueueDrag();
        if (_isPlaybackQueueSidebarBusy ||
            args.Items.Count != 1 ||
            args.Items[0] is not PlaybackQueueSidebarItem item)
        {
            args.Cancel = true;
            return;
        }

        var index = PlaybackQueueSidebarItems.IndexOf(item);
        if (index < 0)
        {
            args.Cancel = true;
            return;
        }

        _playbackQueueDraggedItem = item;
        _playbackQueueDragOriginalIndex = index;
    }

    private async void PlaybackQueueSidebarList_DragItemsCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        var item = _playbackQueueDraggedItem;
        var originalIndex = _playbackQueueDragOriginalIndex;
        ResetPlaybackQueueDrag();

        if (item is null ||
            originalIndex < 0 ||
            _isPlaybackQueueSidebarBusy)
        {
            RefreshPlaybackQueueSidebar();
            return;
        }

        var targetIndex = PlaybackQueueSidebarItems.IndexOf(item);
        if (targetIndex < 0 || targetIndex == originalIndex)
        {
            return;
        }

        _isPlaybackQueueSidebarBusy = true;
        try
        {
            await _playbackCoordinator.MoveQueueItemAsync(
                originalIndex,
                targetIndex);

            RefreshPlaybackQueueSidebar();
        }
        catch (Exception ex)
        {
            SetLiveRegionText(PlaybackQueueSidebarCountText, ex.Message);
        }
        finally
        {
            _isPlaybackQueueSidebarBusy = false;
        }
    }

    private void ResetPlaybackQueueDrag()
    {
        _playbackQueueDraggedItem = null;
        _playbackQueueDragOriginalIndex = -1;
    }

    private void CompactMuteButton_Click(object sender, RoutedEventArgs args)
    {
        ShowCompactVolumeFlyout();
    }

    private void ShowCompactVolumeFlyout()
    {
        if (!CompactVolumeFlyout.IsOpen)
        {
            CompactVolumeFlyout.ShowAt(
                CompactMuteButton,
                new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.Top,
                    ShowMode = FlyoutShowMode.Transient
                });
        }
    }

    private void CompactVolumeFlyout_Opened(object? sender, object args)
    {
        UpdateAudioVolumeButtonDescription();
    }

    private void CompactVolumeFlyout_Closed(object? sender, object args)
    {
        UpdateAudioVolumeButtonDescription();
    }

    private async void AudioCloseButton_Click(object sender, RoutedEventArgs args)
    {
        CancelPendingAudioSeek();
        SetAudioExpanded(false, animate: false);
        await RunAudioCommandAsync(() => _playbackCoordinator.CloseAudioPlaybackAsync());
    }

    private void SetAudioPlayerBarVisible(bool isVisible, bool animate)
    {
        if (_isAudioPlayerBarVisible == isVisible &&
            (_audioPlayerBarMotionCancellation is not null ||
             AudioPlayerBar.Visibility == (isVisible ? Visibility.Visible : Visibility.Collapsed)))
        {
            return;
        }

        _isAudioPlayerBarVisible = isVisible;
        CancelMotion(ref _audioPlayerBarMotionCancellation);

        if (!isVisible)
        {
            MoveFocusOutsideAudioSurfaces();
        }

        if (!animate || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(AudioPlayerBar, isVisible);
            return;
        }

        if (!isVisible && AudioPlayerBar.Visibility != Visibility.Visible)
        {
            MotionHelper.SetVisibleInstant(AudioPlayerBar, isVisible: false);
            return;
        }

        var motionCancellation = new CancellationTokenSource();
        _audioPlayerBarMotionCancellation = motionCancellation;
        _ = AnimateAudioPlayerBarAsync(isVisible, motionCancellation);
    }

    private async Task AnimateAudioPlayerBarAsync(
        bool isVisible,
        CancellationTokenSource motionCancellation)
    {
        try
        {
            if (isVisible)
            {
                await MotionHelper.ShowAsync(
                    AudioPlayerBar,
                    MotionPreset.Standard,
                    MotionDirection.Down,
                    distance: 12,
                    cancellationToken: motionCancellation.Token);
            }
            else
            {
                await MotionHelper.HideAsync(
                    AudioPlayerBar,
                    MotionPreset.Fast,
                    MotionDirection.Down,
                    distance: 12,
                    cancellationToken: motionCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_audioPlayerBarMotionCancellation, motionCancellation))
            {
                _audioPlayerBarMotionCancellation = null;
                motionCancellation.Dispose();
            }
        }
    }

    private void AudioArtworkButton_Click(object sender, RoutedEventArgs args)
    {
        SetAudioExpanded(!_isAudioExpanded, animate: true);
    }

    private void AudioCollapseButton_Click(object sender, RoutedEventArgs args)
    {
        SetAudioExpanded(false, animate: true);
    }

    private async void ExpandedLyricsList_ItemClick(object sender, ItemClickEventArgs args)
    {
        if (!_audioPlaybackState.IsSeekable ||
            args.ClickedItem is not AudioLyricLineViewModel { Timestamp: { } timestamp })
        {
            return;
        }

        await QueueAudioSeekAsync(timestamp.TotalSeconds);
    }

    private void AudioExpandedBackdrop_Tapped(object sender, TappedRoutedEventArgs args)
    {
        SetAudioExpanded(false, animate: true);
    }

    private void AudioExpandedPanel_Tapped(object sender, TappedRoutedEventArgs args)
    {
        args.Handled = true;
    }

    private void SetAudioExpanded(bool isExpanded, bool animate)
    {
        if (_isAudioExpanded == isExpanded &&
            AudioExpandedLayer.Visibility == (isExpanded ? Visibility.Visible : Visibility.Collapsed))
        {
            return;
        }

        if (isExpanded && !_isAudioExpanded)
        {
            _audioExpandedFocusRestoreVersion++;
            _audioExpandedFocusRestoreElement =
                FocusManager.GetFocusedElement(MainRoot.XamlRoot) as Control ??
                AudioNowPlayingButton;
        }

        if (isExpanded)
        {
            UpdateAudioExpandedLayout(
                MainContentGrid.ActualWidth > 0 ? MainContentGrid.ActualWidth : MainRoot.ActualWidth,
                MainContentGrid.ActualHeight);
        }

        _isAudioExpanded = isExpanded;
        CancelMotion(ref _audioExpandedMotionCancellation);

        if (!animate || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(AudioExpandedLayer, isExpanded);
            if (isExpanded)
            {
                ScrollActiveAudioLyricIntoView(
                    activeLine: null,
                    intent: MotionIntent.InitialPosition);
                FocusAudioExpandedPanel();
            }
            else
            {
                RestoreAudioExpandedFocus();
            }
            return;
        }

        if (isExpanded)
        {
            ScrollActiveAudioLyricIntoView(
                activeLine: null,
                intent: MotionIntent.InitialPosition);
            FocusAudioExpandedPanel();
        }
        else if (AudioExpandedLayer.Visibility != Visibility.Visible)
        {
            MotionHelper.SetVisibleInstant(AudioExpandedLayer, isVisible: false);
            RestoreAudioExpandedFocus();
            return;
        }
        else
        {
            RestoreAudioExpandedFocus();
        }

        var motionCancellation = new CancellationTokenSource();
        _audioExpandedMotionCancellation = motionCancellation;
        _ = AnimateAudioExpandedAsync(isExpanded, motionCancellation);
    }

    private async Task AnimateAudioExpandedAsync(
        bool isExpanded,
        CancellationTokenSource motionCancellation)
    {
        try
        {
            if (isExpanded)
            {
                await MotionHelper.ShowAsync(
                    AudioExpandedLayer,
                    MotionPreset.Panel,
                    MotionDirection.Down,
                    distance: 16,
                    cancellationToken: motionCancellation.Token);
            }
            else
            {
                await MotionHelper.HideAsync(
                    AudioExpandedLayer,
                    MotionPreset.Panel,
                    MotionDirection.Down,
                    distance: 16,
                    cancellationToken: motionCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_audioExpandedMotionCancellation, motionCancellation))
            {
                _audioExpandedMotionCancellation = null;
                motionCancellation.Dispose();
            }
        }
    }

    private void FocusAudioExpandedPanel()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
            AudioCollapseButton.Focus(FocusState.Programmatic));
    }

    private void RestoreAudioExpandedFocus()
    {
        var focusTarget = _audioExpandedFocusRestoreElement;
        _audioExpandedFocusRestoreElement = null;
        var restoreVersion = ++_audioExpandedFocusRestoreVersion;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (restoreVersion != _audioExpandedFocusRestoreVersion ||
                !_isAudioPlayerBarVisible ||
                AudioPlayerBar.Visibility != Visibility.Visible)
            {
                return;
            }

            var currentFocus = FocusManager.GetFocusedElement(MainRoot.XamlRoot) as DependencyObject;
            if (currentFocus is not null && !IsDescendantOf(currentFocus, AudioExpandedLayer))
            {
                // The user already moved focus elsewhere while the panel was closing.
                return;
            }

            if (!TryFocusControl(focusTarget))
            {
                FocusMainSurface();
            }
        });
    }

    private void MoveFocusOutsideAudioSurfaces()
    {
        _audioExpandedFocusRestoreVersion++;
        _audioExpandedFocusRestoreElement = null;
        if (_isClosed ||
            FocusManager.GetFocusedElement(MainRoot.XamlRoot) is not DependencyObject focusedElement ||
            (!IsDescendantOf(focusedElement, AudioPlayerBar) &&
             !IsDescendantOf(focusedElement, AudioExpandedLayer)))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(FocusMainSurface);
    }

    private void FocusMainSurface()
    {
        var firstPageControl = FocusManager.FindFirstFocusableElement(RootFrame) as Control;
        if (TryFocusControl(firstPageControl))
        {
            return;
        }

        if (TryFocusControl(NavigationViewControl.SelectedItem as Control))
        {
            return;
        }

        _ = TryFocusControl(NavigationViewControl);
    }

    private static bool TryFocusControl(Control? control)
    {
        return control is
        {
            XamlRoot: not null,
            Visibility: Visibility.Visible,
            IsEnabled: true
        } && control.Focus(FocusState.Programmatic);
    }

    private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static void CancelMotion(ref CancellationTokenSource? motionCancellation)
    {
        motionCancellation?.Cancel();
        motionCancellation?.Dispose();
        motionCancellation = null;
    }

    private void AudioSeekBar_SeekStarted(object? sender, EventArgs args)
    {
        _isAudioSeekDragging = true;
    }

    private async void AudioSeekBar_SeekCompleted(
        object? sender,
        PlayerSeekBarSeekCompletedEventArgs args)
    {
        if (!_isAudioSeekDragging)
        {
            return;
        }

        _isAudioSeekDragging = false;
        await QueueAudioSeekAsync(args.Value);
    }

    private void AudioSeekBar_ValueChanged(
        object? sender,
        PlayerSeekBarValueChangedEventArgs args)
    {
        if (_isUpdatingAudioControls || !_isAudioSeekDragging)
        {
            return;
        }

        UpdateAudioSeekVisual(args.NewValue);
    }

    private async Task QueueAudioSeekAsync(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return;
        }

        var maximum = Math.Max(0, _audioPlaybackState.Duration);
        var target = maximum > 0
            ? Math.Clamp(seconds, 0, maximum)
            : Math.Max(0, seconds);
        var requestVersion = ++_audioSeekRequestVersion;
        _pendingAudioSeekPosition = target;
        _confirmedAudioSeekPosition = null;
        UpdateAudioSeekVisual(target);

        var cancellation = new CancellationTokenSource();
        var previousCancellation = _audioSeekCancellation;
        _audioSeekCancellation = cancellation;
        previousCancellation?.Cancel();

        try
        {
            await Task.Delay(AudioSeekDebounceDelay, cancellation.Token);
            await _audioPlaybackService.SeekAbsoluteAsync(target, cancellation.Token);
            await Task.Delay(AudioSeekConfirmationTimeout, cancellation.Token);

            if (requestVersion == _audioSeekRequestVersion &&
                _pendingAudioSeekPosition is not null)
            {
                _pendingAudioSeekPosition = null;
                _confirmedAudioSeekPosition = null;
                if (!_isAudioSeekDragging)
                {
                    UpdateAudioSeekVisual(GetProjectedAudioPosition(
                        _audioPlaybackState,
                        _audioPlaybackStateTimestamp));
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestVersion == _audioSeekRequestVersion)
            {
                _pendingAudioSeekPosition = null;
                _confirmedAudioSeekPosition = null;
                if (!_isAudioSeekDragging)
                {
                    UpdateAudioSeekVisual(GetProjectedAudioPosition(
                        _audioPlaybackState,
                        _audioPlaybackStateTimestamp));
                }
            }

            SetLiveRegionText(ExpandedAudioStatusText, ex.Message);
            ExpandedAudioStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (ReferenceEquals(_audioSeekCancellation, cancellation))
            {
                _audioSeekCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingAudioSeek()
    {
        _audioSeekRequestVersion++;
        _pendingAudioSeekPosition = null;
        _confirmedAudioSeekPosition = null;
        _audioSeekStaleStateGuardUntil = default;
        var cancellation = _audioSeekCancellation;
        _audioSeekCancellation = null;
        cancellation?.Cancel();
    }

    private void UpdateAudioSeekVisual(double seconds)
    {
        var maximum = Math.Max(CompactAudioSeekBar.Minimum, CompactAudioSeekBar.Maximum);
        var position = Math.Clamp(seconds, CompactAudioSeekBar.Minimum, maximum);
        var wasUpdating = _isUpdatingAudioControls;
        _isUpdatingAudioControls = true;
        try
        {
            CompactAudioSeekBar.Value = position;
            var positionSecond = (long)Math.Floor(position);
            if (_displayedAudioPositionSecond != positionSecond)
            {
                _displayedAudioPositionSecond = positionSecond;
                CompactCurrentTimeText.Text = FormatMediaTime(position);
            }
        }
        finally
        {
            _isUpdatingAudioControls = wasUpdating;
        }

        UpdateActiveAudioLyric(position);
    }

    private async void AudioVolumeSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs args)
    {
        var targetVolume = Math.Clamp(args.NewValue, 0, 100);
        if (CompactVolumeValueText is not null)
        {
            CompactVolumeValueText.Text = $"{targetVolume:0}%";
        }
        if (targetVolume > 0)
        {
            _lastAudibleAudioVolume = Math.Clamp(targetVolume, 1, 100);
        }

        if (!_isAudioPlayerReady || _isUpdatingAudioControls)
        {
            return;
        }

        _pendingAudioVolume = targetVolume;
        await QueueAudioVolumeAsync(targetVolume);
    }

    private async Task QueueAudioVolumeAsync(
        double volume,
        bool commitImmediately = false)
    {
        var targetVolume = Math.Clamp(volume, 0, 100);
        _pendingAudioVolume = targetVolume;
        var cancellation = new CancellationTokenSource();
        var previousCancellation = _audioVolumeCancellation;
        _audioVolumeCancellation = cancellation;
        previousCancellation?.Cancel();

        try
        {
            if (!commitImmediately)
            {
                await Task.Delay(AudioVolumeDebounceDelay, cancellation.Token);
            }

            await _audioPlaybackService.SetVolumeAsync(
                targetVolume,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_audioVolumeCancellation, cancellation))
            {
                _pendingAudioVolume = null;
            }

            SetLiveRegionText(ExpandedAudioStatusText, ex.Message);
            ExpandedAudioStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (ReferenceEquals(_audioVolumeCancellation, cancellation))
            {
                _audioVolumeCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingAudioVolume()
    {
        var cancellation = _audioVolumeCancellation;
        _audioVolumeCancellation = null;
        _pendingAudioVolume = null;
        _isAudioVolumeAdjusting = false;
        cancellation?.Cancel();
    }

    private void CompactVolumeFlyout_Loaded(object sender, RoutedEventArgs args)
    {
        _isUpdatingAudioControls = true;
        try
        {
            var displayedVolume = Math.Clamp(
                _pendingAudioVolume ?? _audioPlaybackState.Volume,
                0,
                100);
            CompactVolumeSlider.Value = displayedVolume;
            CompactVolumeValueText.Text = $"{displayedVolume:0}%";
        }
        finally
        {
            _isUpdatingAudioControls = false;
        }

        CompactVolumeSlider.ValueChanged -= AudioVolumeSlider_ValueChanged;
        CompactVolumeSlider.ValueChanged += AudioVolumeSlider_ValueChanged;
        HookAudioVolumeSliderInput();
    }

    private void HookAudioVolumeSliderInput()
    {
        if (_isAudioVolumeSliderInputHooked)
        {
            return;
        }

        CompactVolumeSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(AudioVolumeSlider_PointerPressed),
            handledEventsToo: true);
        CompactVolumeSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(AudioVolumeSlider_PointerAdjustmentCompleted),
            handledEventsToo: true);
        CompactVolumeSlider.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(AudioVolumeSlider_PointerAdjustmentCompleted),
            handledEventsToo: true);
        CompactVolumeSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(AudioVolumeSlider_PointerAdjustmentCompleted),
            handledEventsToo: true);
        _isAudioVolumeSliderInputHooked = true;
    }

    private void AudioVolumeSlider_PointerPressed(
        object sender,
        PointerRoutedEventArgs args)
    {
        _isAudioVolumeAdjusting = true;
    }

    private void AudioVolumeSlider_PointerAdjustmentCompleted(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (!_isAudioVolumeAdjusting)
        {
            return;
        }

        _isAudioVolumeAdjusting = false;
        var targetVolume = Math.Clamp(CompactVolumeSlider.Value, 0, 100);
        _pendingAudioVolume = targetVolume;
        _ = QueueAudioVolumeAsync(targetVolume, commitImmediately: true);
    }

    private async Task RunAudioCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            SetLiveRegionText(ExpandedAudioStatusText, ex.Message);
            ExpandedAudioStatusText.Visibility = Visibility.Visible;
        }
    }

    private static string FormatMediaTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private void MainRoot_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            if (args.Handled)
            {
                return;
            }

            if (_isAudioExpanded)
            {
                SetAudioExpanded(false, animate: true);
                args.Handled = true;
                return;
            }

            if (_isPlaybackQueueSidebarOpen)
            {
                SetPlaybackQueueSidebarVisible(false);
                args.Handled = true;
            }

            return;
        }

        if (TryHandleAudioShortcut(args))
        {
            args.Handled = true;
        }
    }

    private bool TryHandleAudioShortcut(KeyRoutedEventArgs args)
    {
        if (!_isAudioPlayerBarVisible ||
            _audioPlaybackState.Status is AudioPlaybackStatus.None or
                AudioPlaybackStatus.Loading or
                AudioPlaybackStatus.Failed ||
            !TryResolveAudioShortcut(args, out var command))
        {
            return false;
        }

        var isRepeatable = command is
            AudioShortcutCommand.SeekBackward or
            AudioShortcutCommand.SeekForward or
            AudioShortcutCommand.VolumeUp or
            AudioShortcutCommand.VolumeDown;
        if (args.KeyStatus.WasKeyDown && !isRepeatable)
        {
            return true;
        }

        return ExecuteAudioShortcut(command);
    }

    private bool TryResolveAudioShortcut(
        KeyRoutedEventArgs args,
        out AudioShortcutCommand command)
    {
        command = default;
        var virtualKey = (int)args.Key;
        switch (virtualKey)
        {
            case VirtualKeyMediaPlayPause:
                command = AudioShortcutCommand.TogglePlayback;
                return true;
            case VirtualKeyMediaPreviousTrack:
                command = AudioShortcutCommand.Previous;
                return true;
            case VirtualKeyMediaNextTrack:
                command = AudioShortcutCommand.Next;
                return true;
            case VirtualKeyVolumeMute:
                command = AudioShortcutCommand.ToggleMute;
                return true;
            case VirtualKeyVolumeUp:
                command = AudioShortcutCommand.VolumeUp;
                return true;
            case VirtualKeyVolumeDown:
                command = AudioShortcutCommand.VolumeDown;
                return true;
        }

        var isControlDown = IsVirtualKeyDown(VirtualKey.Control);
        var isShiftDown = IsVirtualKeyDown(VirtualKey.Shift);
        var isAltDown = IsVirtualKeyDown(VirtualKey.Menu);
        if (isAltDown || IsAudioShortcutTextInput(args.OriginalSource))
        {
            return false;
        }

        if (isControlDown)
        {
            command = args.Key switch
            {
                VirtualKey.P => AudioShortcutCommand.TogglePlayback,
                VirtualKey.Left => AudioShortcutCommand.Previous,
                VirtualKey.Right => AudioShortcutCommand.Next,
                VirtualKey.M => AudioShortcutCommand.ToggleMute,
                VirtualKey.S when isShiftDown => AudioShortcutCommand.ToggleShuffle,
                VirtualKey.R when isShiftDown => AudioShortcutCommand.CycleRepeat,
                _ => default
            };
            return args.Key is VirtualKey.P or
                VirtualKey.Left or
                VirtualKey.Right or
                VirtualKey.M ||
                (isShiftDown && args.Key is VirtualKey.S or VirtualKey.R);
        }

        if (isShiftDown || IsAudioShortcutInteractiveSource(args.OriginalSource))
        {
            return false;
        }

        command = args.Key switch
        {
            VirtualKey.Space => AudioShortcutCommand.TogglePlayback,
            VirtualKey.Left => AudioShortcutCommand.SeekBackward,
            VirtualKey.Right => AudioShortcutCommand.SeekForward,
            VirtualKey.Up => AudioShortcutCommand.VolumeUp,
            VirtualKey.Down => AudioShortcutCommand.VolumeDown,
            VirtualKey.PageUp => AudioShortcutCommand.Previous,
            VirtualKey.PageDown => AudioShortcutCommand.Next,
            VirtualKey.M => AudioShortcutCommand.ToggleMute,
            VirtualKey.S => AudioShortcutCommand.ToggleShuffle,
            VirtualKey.R => AudioShortcutCommand.CycleRepeat,
            _ => default
        };
        return args.Key is VirtualKey.Space or
            VirtualKey.Left or
            VirtualKey.Right or
            VirtualKey.Up or
            VirtualKey.Down or
            VirtualKey.PageUp or
            VirtualKey.PageDown or
            VirtualKey.M or
            VirtualKey.S or
            VirtualKey.R;
    }

    private bool ExecuteAudioShortcut(AudioShortcutCommand command)
    {
        switch (command)
        {
            case AudioShortcutCommand.TogglePlayback:
                _ = RunAudioCommandAsync(ToggleAudioPlaybackAsync);
                return true;

            case AudioShortcutCommand.SeekBackward:
            case AudioShortcutCommand.SeekForward:
                if (!_audioPlaybackState.IsSeekable ||
                    _audioPlaybackState.Duration <= 0)
                {
                    return false;
                }

                var seekStep = CompactAudioSeekBar.EffectiveSmallChange;
                var seekOffset = command == AudioShortcutCommand.SeekBackward
                    ? -seekStep
                    : seekStep;
                _ = QueueAudioSeekAsync(CompactAudioSeekBar.Value + seekOffset);
                return true;

            case AudioShortcutCommand.VolumeUp:
            case AudioShortcutCommand.VolumeDown:
                var volumeOffset = command == AudioShortcutCommand.VolumeUp
                    ? AudioShortcutVolumeStep
                    : -AudioShortcutVolumeStep;
                SetAudioVolumeFromShortcut(volumeOffset);
                return true;

            case AudioShortcutCommand.ToggleMute:
                _ = RunAudioCommandAsync(ToggleAudioMuteAsync);
                return true;

            case AudioShortcutCommand.Previous:
                if (!_playbackCoordinator.CanPlayPrevious)
                {
                    return false;
                }

                _ = RunAudioCommandAsync(() => _playbackCoordinator.PlayPreviousAsync());
                return true;

            case AudioShortcutCommand.Next:
                if (!_playbackCoordinator.CanPlayNext)
                {
                    return false;
                }

                _ = RunAudioCommandAsync(() => _playbackCoordinator.PlayNextAsync());
                return true;

            case AudioShortcutCommand.ToggleShuffle:
                if (_playbackCoordinator.PlaybackQueue.Count == 0)
                {
                    return false;
                }

                _playbackCoordinator.IsShuffleEnabled =
                    !_playbackCoordinator.IsShuffleEnabled;
                UpdateAudioPlaybackModeControls();
                return true;

            case AudioShortcutCommand.CycleRepeat:
                if (_playbackCoordinator.PlaybackQueue.Count == 0)
                {
                    return false;
                }

                _playbackCoordinator.RepeatMode = _playbackCoordinator.RepeatMode switch
                {
                    AudioRepeatMode.Off => AudioRepeatMode.All,
                    AudioRepeatMode.All => AudioRepeatMode.One,
                    _ => AudioRepeatMode.Off
                };
                UpdateAudioPlaybackModeControls();
                return true;

            default:
                return false;
        }
    }

    private void SetAudioVolumeFromShortcut(double offset)
    {
        var currentVolume = CompactVolumeSlider?.Value ??
            Math.Clamp(_audioPlaybackState.Volume, 0, 100);
        var targetVolume = Math.Clamp(
            currentVolume + offset,
            0,
            100);
        if (Math.Abs(targetVolume - currentVolume) < 0.01)
        {
            return;
        }

        var wasUpdating = _isUpdatingAudioControls;
        _isUpdatingAudioControls = true;
        try
        {
            if (CompactVolumeSlider is not null)
            {
                CompactVolumeSlider.Value = targetVolume;
            }

            if (CompactVolumeValueText is not null)
            {
                CompactVolumeValueText.Text = $"{targetVolume:0}%";
            }
        }
        finally
        {
            _isUpdatingAudioControls = wasUpdating;
        }

        _ = QueueAudioVolumeAsync(targetVolume);
    }

    private static bool IsVirtualKeyDown(VirtualKey key)
    {
        return (InputKeyboardSource.GetKeyStateForCurrentThread(key) &
                CoreVirtualKeyStates.Down) != 0;
    }

    private static bool IsAudioShortcutTextInput(object? source)
    {
        for (var current = source as DependencyObject;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox or
                RichEditBox or
                PasswordBox or
                AutoSuggestBox)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAudioShortcutInteractiveSource(object? source)
    {
        for (var current = source as DependencyObject;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase or
                RangeBase or
                Selector or
                SelectorItem or
                ToggleSwitch or
                PlayerSeekBar)
            {
                return true;
            }
        }

        return false;
    }

    private void MainRoot_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdatePlaybackQueueLayout(args.NewSize.Width);
        UpdateAudioPlayerResponsiveLayout(
            AudioPlayerBar.ActualWidth > 0
                ? AudioPlayerBar.ActualWidth
                : args.NewSize.Width);
        UpdateAudioExpandedLayout(args.NewSize.Width, MainContentGrid.ActualHeight);
    }

    private void MainContentGrid_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateAudioExpandedLayout(args.NewSize.Width, args.NewSize.Height);
    }

    private void AudioPlayerBar_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateAudioPlayerResponsiveLayout(args.NewSize.Width);
    }

    private void UpdateAudioPlayerResponsiveLayout(double availableWidth)
    {
        var showExtendedTransport = availableWidth >= ResponsiveLayout.MediumUpperBound;
        var showTrackNavigation = availableWidth >= ResponsiveLayout.CompactUpperBound;
        CompactShuffleButton.Visibility = showExtendedTransport ? Visibility.Visible : Visibility.Collapsed;
        CompactRepeatButton.Visibility = showExtendedTransport ? Visibility.Visible : Visibility.Collapsed;
        CompactPreviousButton.Visibility = showTrackNavigation ? Visibility.Visible : Visibility.Collapsed;
        CompactNextButton.Visibility = showTrackNavigation ? Visibility.Visible : Visibility.Collapsed;
        CompactLeftControlsColumn.Width = showExtendedTransport
            ? new GridLength(232)
            : showTrackNavigation ? new GridLength(144) : new GridLength(48);

        CompactMorePreviousItem.Visibility = showTrackNavigation
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompactMoreNextItem.Visibility = showTrackNavigation
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompactMoreShuffleItem.Visibility = showExtendedTransport
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompactMoreRepeatItem.Visibility = showExtendedTransport
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompactMoreTransportSeparator.Visibility = !showTrackNavigation && !showExtendedTransport
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactMoreButton.Visibility = showExtendedTransport
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompactRightControlsColumn.Width = new GridLength(
            CompactMoreButton.Visibility == Visibility.Visible ? 132 : 88);

        CompactTimePanel.Visibility = availableWidth >= 760
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactAudioArtistText.Visibility = availableWidth >= ResponsiveLayout.CompactUpperBound
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateAudioExpandedLayout(double availableWidth, double availableHeight)
    {
        var breakpoint = ResponsiveLayout.Resolve(availableWidth);
        AudioExpandedPanel.Padding = availableHeight < 520
            ? new Thickness(16)
            : ResponsiveLayout.GetPagePadding(breakpoint);

        var showExpandedArtwork = availableWidth >= 720 && availableHeight >= 520;
        var artworkSize = availableWidth >= ResponsiveLayout.ExpandedUpperBound && availableHeight >= 700
            ? 320
            : availableWidth >= ResponsiveLayout.MediumUpperBound && availableHeight >= 620
                ? 280
                : 220;
        ExpandedArtworkColumn.Width = showExpandedArtwork
            ? new GridLength(artworkSize)
            : new GridLength(0);
        ExpandedArtworkContainer.Visibility = showExpandedArtwork ? Visibility.Visible : Visibility.Collapsed;
        ExpandedArtworkContainer.Width = artworkSize;
        ExpandedArtworkContainer.Height = artworkSize;
        ExpandedArtworkFallback.Width = artworkSize;
        ExpandedArtworkFallback.Height = artworkSize;
        ExpandedArtworkImage.Width = artworkSize;
        ExpandedArtworkImage.Height = artworkSize;
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        lock (_audioStateDispatchLock)
        {
            _pendingAudioPlaybackState = null;
            _isAudioStateDispatchPending = false;
        }

        StopAudioProgressTimer();
        _audioProgressTimer.Tick -= AudioProgressTimer_Tick;
        CancelPendingAudioSeek();
        CancelPendingAudioVolume();
        CancelAudioLyricsLoad();
        CancelMotion(ref _audioPlayerBarMotionCancellation);
        CancelMotion(ref _audioExpandedMotionCancellation);
        CancelMotion(ref _audioMetadataMotionCancellation);
        CancelMotion(ref _audioLyricsStateMotionCancellation);
        CancelMotion(ref _playbackQueueMotionCancellation);
        CancelMotion(ref _audioLoadingFeedbackCancellation);
        _isAudioLoadingFeedbackRequested = false;
        _isAudioLoadingFeedbackEnding = false;
        CompactPlaybackLoadingRing.IsActive = false;
        _audioPlaybackService.StateChanged -= AudioPlaybackService_StateChanged;
        _audioPlaybackService.MediaChanged -= AudioPlaybackService_MediaChanged;
        _playbackCoordinator.PlaybackQueueChanged -= PlaybackCoordinator_PlaybackQueueChanged;
        _playbackCoordinator.AudioPlaybackOptionsChanged -= PlaybackCoordinator_AudioPlaybackOptionsChanged;
        MainRoot.ActualThemeChanged -= MainRoot_ActualThemeChanged;
        UnsubscribeSystemAppearanceEvents();
        AppWindow.Changed -= MainWindow_AppWindowChanged;
        RootFrame.Navigated -= OnRootFrameNavigated;
        RootFrame.NavigationFailed -= OnNavigationFailed;
        Closed -= MainWindow_Closed;
        ClearAudioArtwork();
        ResetAudioLyrics();
        PlaybackQueueSidebarItems.Clear();
        NavigationViewLoaded = null;
        RootFrame.BackStack.Clear();
        RootFrame.ForwardStack.Clear();
        RootFrame.Content = null;
        App.ReleaseMainWindow(this);
        try
        {
            await _playbackCoordinator.CloseAudioPlaybackAsync();
        }
        catch
        {
        }
        finally
        {
            StopAudioProgressTimer();
        }
    }
}

public sealed class PlaybackQueueSidebarItem(
    string stableKey,
    string title,
    string filePath,
    bool isCurrent,
    MediaLibraryItemKind kind,
    bool canRemove) : INotifyPropertyChanged
{
    private string _title = title;
    private string _filePath = filePath;
    private bool _isCurrent = isCurrent;
    private MediaLibraryItemKind _kind = kind;
    private bool _canRemove = canRemove;

    public string StableKey { get; } = stableKey;

    public string Title => _title;

    public string FilePath => _filePath;

    public string FileName => Path.GetFileName(_filePath);

    public string TitleToolTipText => LongTextToolTip.CreateMediaText(Title, FilePath);

    public bool IsCurrent => _isCurrent;

    public string KindGlyph => _kind == MediaLibraryItemKind.Audio
        ? "\uE8D6"
        : "\uE714";

    public Visibility CurrentIndicatorVisibility => IsCurrent
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RemoveVisibility => _canRemove
        ? Visibility.Visible
        : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void ApplySnapshot(PlaybackQueueSidebarItem snapshot)
    {
        if (!string.Equals(_title, snapshot.Title, StringComparison.Ordinal))
        {
            _title = snapshot.Title;
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(TitleToolTipText));
        }

        if (!string.Equals(_filePath, snapshot.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            _filePath = snapshot.FilePath;
            RaisePropertyChanged(nameof(FilePath));
            RaisePropertyChanged(nameof(FileName));
            RaisePropertyChanged(nameof(TitleToolTipText));
        }

        if (_isCurrent != snapshot.IsCurrent)
        {
            _isCurrent = snapshot.IsCurrent;
            RaisePropertyChanged(nameof(IsCurrent));
            RaisePropertyChanged(nameof(CurrentIndicatorVisibility));
        }

        if (_kind != snapshot._kind)
    {
            _kind = snapshot._kind;
            RaisePropertyChanged(nameof(KindGlyph));
        }

        if (_canRemove != snapshot._canRemove)
        {
            _canRemove = snapshot._canRemove;
            RaisePropertyChanged(nameof(RemoveVisibility));
        }
    }

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
