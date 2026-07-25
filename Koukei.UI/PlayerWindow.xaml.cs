using Koukei.Video;
using Koukei.Bus.Models;
using Koukei.Bus.Services;
using Koukei.UI.Controls;
using Koukei.UI.Helpers;
using Koukei.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Koukei.UI;

public sealed class PlayerQueueItem : INotifyPropertyChanged
{
    private string? _thumbnailPath;
    private ImageSource? _thumbnailSource;
    private bool _hasPresentedThumbnail;
    private bool _isCurrent;

    public PlayerQueueItem(
        Guid? mediaId,
        string title,
        string filePath,
        MediaLibraryItemKind kind = MediaLibraryItemKind.Video,
        string? thumbnailPath = null,
        string? stableKey = null,
        bool isCurrent = false)
    {
        MediaId = mediaId;
        FilePath = filePath;
        Title = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : title.Trim();
        Kind = kind;
        StableKey = stableKey ??
            $"{mediaId?.ToString("N") ?? string.Empty}:{(int)kind}:{filePath}";
        _thumbnailPath = thumbnailPath;
        _thumbnailSource = CreateThumbnailSource(thumbnailPath);
        _hasPresentedThumbnail = _thumbnailSource is not null;
        _isCurrent = isCurrent;
    }

    public Guid? MediaId { get; }
    public string Title { get; private set; }
    public string FilePath { get; set; }
    public MediaLibraryItemKind Kind { get; }
    public string KindGlyph => Kind == MediaLibraryItemKind.Audio ? "\uE8D6" : "\uE714";
    public string StableKey { get; }
    public bool IsCurrent => _isCurrent;
    public Visibility CurrentIndicatorVisibility =>
        _isCurrent ? Visibility.Visible : Visibility.Collapsed;
    public string TitleToolTipText => LongTextToolTip.CreateMediaText(Title, FilePath);

    public ImageSource? ThumbnailSource => _thumbnailSource;

    public double ThumbnailOpacity => _thumbnailSource is not null || _hasPresentedThumbnail
        ? 1
        : 0;

    public string? ThumbnailPath => _thumbnailPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateTitle(string title)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(FilePath)
            : title.Trim();
        if (string.Equals(Title, normalizedTitle, StringComparison.Ordinal))
        {
            return;
        }

        Title = normalizedTitle;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleToolTipText)));
    }

    internal void SetIsCurrent(bool isCurrent)
    {
        if (_isCurrent == isCurrent)
        {
            return;
        }

        _isCurrent = isCurrent;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(CurrentIndicatorVisibility)));
    }

    internal void SetThumbnailPath(string? thumbnailPath)
    {
        if (string.Equals(_thumbnailPath, thumbnailPath, StringComparison.OrdinalIgnoreCase) &&
            _thumbnailSource is not null)
        {
            return;
        }

        _thumbnailPath = thumbnailPath;
        _thumbnailSource = CreateThumbnailSource(thumbnailPath);
        if (_thumbnailSource is not null)
        {
            _hasPresentedThumbnail = true;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailSource)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailOpacity)));
    }

    internal void ReleaseThumbnailSource()
    {
        if (_thumbnailSource is null)
        {
            return;
        }

        _thumbnailSource = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailSource)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailOpacity)));
    }

    private static ImageSource? CreateThumbnailSource(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 192,
                UriSource = new Uri(Path.GetFullPath(thumbnailPath), UriKind.Absolute)
            };
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException)
        {
            return null;
        }
    }
}

public sealed partial class PlayerWindow : Window
{
    private const long MouseMoveIntervalMilliseconds = 16;
    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkF = 0x46;
    private const int VkQ = 0x51;
    private const string MiniExpand2MirroredGlyph = "\uEE47";
    private const string MiniContract2MirroredGlyph = "\uEE49";
    // Control-bar thresholds are based on the controls' actual 40/48 px targets,
    // group spacing, balanced side columns, and horizontal padding. They are
    // intentionally independent from the page-level responsive breakpoints.
    private const int WideControlBarMinWidth = 760;
    private const int PlaybackModeControlBarMinWidth = 840;
    private const int MediumControlBarMinWidth = 600;
    private const int CompactControlBarMinWidth = 480;
    private const int NarrowControlBarMinWidth = 360;
    private const int CompactControlBarHeightThreshold = 260;
    private const int MinimumPictureInPictureWidth = 360;
    private const int MinimumPictureInPictureHeight = 240;
    private const int MaximumPictureInPictureWidth = 720;
    private const int MaximumPictureInPictureHeight = 480;
    private const string D3D11CompositionVideoOutput = "d3d11-composition";
    private const string LegacyWidVideoOutput = "wid";
    private const string VideoOutputEnvironmentVariable = "KOUKEI_MPV_VIDEO_OUTPUT";
    private static readonly TimeSpan CompositionResizeThrottle = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan PlayerChromeAutoHideDelay = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan SeekPreviewDebounceDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan InitialVideoSizeProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForegroundActivationSettleDelay =
        TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan[] ForegroundActivationRetrySchedule =
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(75),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(450),
        TimeSpan.FromMilliseconds(900)
    ];
    private static readonly TimeSpan LoadingFeedbackDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan LoadingFeedbackMinimumVisibleDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan VideoVolumeDebounceDelay = TimeSpan.FromMilliseconds(50);
    private const double VideoVolumeConfirmationTolerance = 0.5;
    private const int MaximumSeekPreviewCacheEntries = 48;
    private static readonly double[] PlaybackSpeedSteps = [0.5, 0.75, 1, 1.25, 1.5, 2];
    private static readonly string[] ExternalAudioTrackExtensions =
        [".mp3", ".flac", ".aac", ".ogg", ".wav", ".m4a", ".opus", ".wma", ".mka", ".ac3"];
    private static IReadOnlyList<string> ExternalSubtitleTrackExtensions =>
        VideoSubtitleSidecar.SupportedExtensions;

    private static PlayerWindow? s_current;
    private static readonly SemaphoreSlim s_videoOperationGate = new(1, 1);
    private static readonly List<(string Title, string FilePath)> s_pendingVideoQueue = [];

    public static event EventHandler? VideoQueueChanged;

    private readonly IVideoPlaybackService _player;
    private readonly IVideoThumbnailService _thumbnailService;
    private readonly PlaybackCoordinator _playbackCoordinator;
    private readonly ResourceLoader _resourceLoader = new();
    private readonly TaskCompletionSource<bool> _closedCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<PlayerQueueItem> _videoQueueThumbnailQueue = new();
    private readonly HashSet<PlayerQueueItem> _queuedVideoQueueThumbnails = new();
    private readonly bool _usesD3D11Composition;
    private readonly object _compositionResizeLock = new();
    private readonly HashSet<FlyoutBase> _openPlayerFlyouts = new();
    private readonly Dictionary<int, string> _seekPreviewCache = [];
    private readonly Queue<int> _seekPreviewCacheOrder = new();
    private readonly UISettings _uiSettings = new();
    private bool _allowClose;
    private bool _isActivated;
    private bool _isAlwaysOnTop;
    private bool _isClosed;
    private bool _isClosing;
    private bool _isFullScreen;
    private bool _isPictureInPicture;
    private bool _isPlayerChromeVisible = true;
    private bool _isPauseOverlayVisible;
    private bool _isPaused;
    private bool _isPointerOverControlBar;
    private bool _isPointerOverTitleBarButtons;
    private bool _isPointerOverTitleBarDragRegion;
    private bool _isSeeking;
    private bool _isVideoVolumeAdjusting;
    private bool _isPlaybackUiReady;
    private bool _isSystemColorsSubscribed;
    private bool _isUpdatingPlaybackControls;
    private bool _isUpdatingVideoQueueSelection;
    private bool _isReplacingVideoQueue;
    private bool _isVideoMouseButtonDown;
    private bool _isVideoQueueThumbnailWorkerRunning;
    private bool _isWindowActive;
    private bool _shouldApplyInitialVideoSize;
    private bool _hasPendingCompositionResize;
    private bool _isCompositionResizeWorkerRunning;
    private Symbol _requestedPlayPauseSymbol = Symbol.Play;
    private Symbol _requestedVolumeSymbol = Symbol.Volume;
    private bool? _displayedShuffleEnabled;
    private AudioRepeatMode? _displayedRepeatMode;
    private VideoPlaybackState _playbackState = VideoPlaybackState.Empty;
    private CancellationTokenSource? _compositionResizeCancellation;
    private CancellationTokenSource? _playerChromeMotionCancellation;
    private CancellationTokenSource? _playerChromeAutoHideCancellation;
    private CancellationTokenSource? _seekPreviewCancellation;
    private CancellationTokenSource? _videoQueueThumbnailCancellation;
    private CancellationTokenSource? _loadingFeedbackCancellation;
    private CancellationTokenSource? _videoVolumeCancellation;
    private CancellationTokenSource? _foregroundActivationCancellation;
    private DateTimeOffset? _loadingFeedbackShownAt;
    private SizeInt32 _lastCompositionPixelSize;
    private SizeInt32 _pendingCompositionPixelSize;
    private SizeInt32? _preparedInitialVideoSize;
    private IntPtr _currentSwapChain;
    private string? _currentMediaPath;
    private string? _displayedTitleMediaPath;
    private string? _seekPreviewDisplayedPath;
    private string? _seekPreviewImageLoadingPath;
    private string? _seekPreviewCacheDirectory;
    private int _currentVideoPixelHeight;
    private int _currentVideoPixelWidth;
    private int _currentVideoQueueIndex = -1;
    private double _lastAudibleVolume = 100;
    private double? _pendingVideoVolume;
    private IntPtr _hwnd;
    private long _seekPreviewRequestId;
    private long _seekPreviewImageLoadRequestId;
    private long _videoQueueSelectionRequestId;
    private long _videoQueueThumbnailBatchId;
    private Task? _closeTask;
    private long _lastMouseMoveTicks;
    private long _foregroundActivationRequestId;
    private int _lastMouseMoveX = int.MinValue;
    private int _lastMouseMoveY = int.MinValue;

    public ObservableCollection<PlayerQueueItem> VideoQueue { get; } = [];
    public ObservableCollection<PlayerQueueItem> PlaybackQueue { get; } = [];
    private RectInt32? _fullScreenRestoreBounds;
    private RectInt32? _restoreBounds;
    private IntPtr _videoHwnd;
    private bool _isSeekPreviewPointerActive;
    private int _seekPreviewDisplayedBucket = -1;
    private int _seekPreviewImageLoadingBucket = -1;
    private int _seekPreviewRequestedBucket = -1;

    public PlayerWindow(IVideoPlaybackService player, IVideoThumbnailService thumbnailService)
    {
        InitializeComponent();

        _player = player;
        _thumbnailService = thumbnailService;
        _playbackCoordinator = App.Services.GetRequiredService<PlaybackCoordinator>();
        _playbackCoordinator.PlaybackQueueChanged += PlaybackCoordinator_PlaybackQueueChanged;
        _playbackCoordinator.AudioPlaybackOptionsChanged +=
            PlaybackCoordinator_PlaybackOptionsChanged;
        _player.WindowStateChanged += PlayerWindow_WindowStateChanged;
        _player.SwapChainChanged += PlayerWindow_SwapChainChanged;
        _player.PlaybackStateChanged += PlayerWindow_PlaybackStateChanged;
        _player.VideoSizeChanged += PlayerWindow_VideoSizeChanged;
        _player.ChaptersChanged += PlayerWindow_ChaptersChanged;
        _player.PlaybackClosed += PlayerWindow_PlaybackClosed;
        _usesD3D11Composition = UseD3D11CompositionVideoOutput();
        _hwnd = WindowNative.GetWindowHandle(this);
        RootPanel.KeyDown += PlayerWindow_RootPanelKeyDown;
        HookVideoVolumeSliderInput();

        if (_usesD3D11Composition)
        {
            HookCompositionInput();
        }
        else
        {
            _videoHwnd = NativeMpvHostWindow.Create(_hwnd, HandleVideoKeyDown, HandleVideoMouseInput);
            AudioFallbackPanel.Visibility = Visibility.Collapsed;
        }

        Title = GetPlayerResourceString("PlayerWindow_DefaultTitle", "Koukei Player");
        AppWindow.Title = Title;
        ApplyTransparentTitleBarPresenter();
        ApplyPlayerTheme(ThemeHelper.RootTheme);
        InitializePlayerAccessibility();
        ThemeHelper.ThemeChanged += PlayerWindow_ThemeChanged;
        RootPanel.ActualThemeChanged += PlayerWindow_RootPanelActualThemeChanged;
        SubscribeSystemAppearanceEvents();
        UpdateMaximizeButton();
        AppWindow.Resize(new SizeInt32(1120, 700));
        UpdateControlBarLayout(1120, 700);
        ResizePlayerSurface();
        AppWindow.Changed += PlayerWindow_AppWindowChanged;
        AppWindow.Closing += PlayerWindow_Closing;
        Activated += PlayerWindow_Activated;
        Closed += PlayerWindow_Closed;
        RefreshPlaybackQueue();
        UpdateVideoPlaybackModeControls();
        _isPlaybackUiReady = true;
    }

    private void PlayerWindow_ThemeChanged(object? sender, AppThemeChangedEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed)
            {
                ApplyPlayerTheme(args.RequestedTheme);
            }
        });
    }

    private void PlayerWindow_RootPanelActualThemeChanged(FrameworkElement sender, object args)
    {
        if (RootPanel.RequestedTheme == ElementTheme.Default)
        {
            ApplyPlayerPalette(ResolvePlayerTheme());
        }
    }

    private void PlayerWindow_SystemColorsChanged(UISettings sender, object args)
    {
        ReapplyPlayerPaletteFromSystem();
    }

    private void SubscribeSystemAppearanceEvents()
    {
        try
        {
            _uiSettings.ColorValuesChanged += PlayerWindow_SystemColorsChanged;
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
                _uiSettings.ColorValuesChanged -= PlayerWindow_SystemColorsChanged;
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

    private void ReapplyPlayerPaletteFromSystem()
    {
        if (_isClosed)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed)
            {
                ApplyPlayerPalette(ResolvePlayerTheme());
            }
        });
    }

    private void ApplyPlayerTheme(ElementTheme requestedTheme)
    {
        RootPanel.RequestedTheme = requestedTheme;
        ApplyPlayerPalette(ResolvePlayerTheme());
    }

    private ElementTheme ResolvePlayerTheme()
    {
        return RootPanel.ActualTheme is ElementTheme.Light or ElementTheme.Dark
            ? RootPanel.ActualTheme
            : ThemeHelper.ActualTheme;
    }

    private void ApplyPlayerPalette(ElementTheme resolvedTheme)
    {
        var palette = resolvedTheme == ElementTheme.Light
            ? PlayerThemePalette.Light
            : PlayerThemePalette.Dark;
        palette = palette.WithSystemAccent();

        SetRootBrushColor("PlayerWindowBackgroundBrush", palette.AudioBackground);
        SetRootBrushColor("PlayerAudioBackgroundBrush", palette.AudioBackground);
        SetRootBrushColor("PlayerControlForegroundBrush", palette.Foreground);
        SetRootBrushColor("PlayerControlMutedBrush", palette.MutedForeground);
        SetRootBrushColor("PlayerControlSubtleBrush", palette.SubtleBackground);
        SetRootBrushColor("PlayerPrimaryBackgroundBrush", palette.PrimaryBackground);
        SetRootBrushColor("PlayerPrimaryBorderBrush", palette.PrimaryBorder);
        SetRootBrushColor("PlayerPrimaryForegroundBrush", palette.PrimaryForeground);
        SetRootBrushColor("ButtonBackgroundPointerOver", palette.ButtonHoverBackground);
        SetRootBrushColor("ButtonBackgroundPressed", palette.ButtonPressedBackground);
        SetRootBrushColor("ButtonForegroundPointerOver", palette.Foreground);
        SetRootBrushColor("ButtonForegroundPressed", palette.Foreground);
        SetRootBrushColor("SliderTrackFill", palette.SliderTrack);
        SetRootBrushColor("SliderTrackFillPointerOver", palette.SliderTrackHover);
        SetRootBrushColor("SliderTrackValueFill", palette.Accent);
        SetRootBrushColor("SliderTrackValueFillPointerOver", palette.AccentHover);
        SetRootBrushColor("SliderThumbFill", palette.SliderThumb);
        SetRootBrushColor("SliderThumbFillPointerOver", palette.SliderThumb);
        SetRootBrushColor("SliderThumbBackground", palette.SliderThumb);
        SetRootBrushColor("SliderThumbBackgroundPointerOver", palette.SliderThumb);
        SetRootBrushColor("PlayerAccentBrush", palette.Accent);
        SetControlBarGradient(palette.ControlBarTop, palette.ControlBarBottom);

        SetBrushColor(
            PlayPauseButton.Resources,
            "ButtonBackgroundPointerOver",
            palette.PrimaryHoverBackground);
        SetBrushColor(
            PlayPauseButton.Resources,
            "ButtonBackgroundPressed",
            palette.PrimaryPressedBackground);
        SetBrushColor(
            PlayPauseButton.Resources,
            "ButtonForegroundPointerOver",
            palette.PrimaryForeground);
        SetBrushColor(
            PlayPauseButton.Resources,
            "ButtonForegroundPressed",
            palette.PrimaryForeground);

        PlaybackSeekBar.ApplyPalette(
            palette.SliderTrack,
            palette.Accent,
            palette.SliderThumb,
            palette.ChapterMarker,
            palette.AccentHover);
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, resolvedTheme);
    }

    private void SetRootBrushColor(string key, Color color)
    {
        SetBrushColor(RootPanel.Resources, key, color);
    }

    private static void SetBrushColor(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private void SetControlBarGradient(Color top, Color bottom)
    {
        if (RootPanel.Resources["PlayerControlBarBackgroundBrush"] is not LinearGradientBrush brush ||
            brush.GradientStops.Count < 2)
        {
            return;
        }

        brush.GradientStops[0].Color = top;
        brush.GradientStops[1].Color = bottom;
    }

    private void InitializePlayerAccessibility()
    {
        SetPlayerElementDescription(
            PlaybackSeekBar,
            GetPlayerResourceString("PlayerWindow_Seek", "Playback position"));
        SetPlayerElementDescription(
            PlayerMoreButton,
            GetPlayerResourceString("PlayerWindow_More", "More playback options"));
        UpdateVolumeButtonDescription();
        AutomationProperties.SetLiveSetting(VideoPlaylistCountText, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(AudioTitleText, AutomationLiveSetting.Polite);
        UpdateFullScreenAccessibility(false);
        UpdatePictureInPictureAccessibility(false);
    }

    private static void SetPlayerElementDescription(FrameworkElement element, string description)
    {
        AutomationProperties.SetName(element, description);
        ToolTipService.SetToolTip(element, description);
    }

    public static async Task ShowAsync(string title, string filePath)
    {
        await s_videoOperationGate.WaitAsync();
        try
        {
            if (s_current is { _isClosed: false, _isClosing: false } window)
            {
                var existingIndex = FindVideoQueueItemIndex(window.VideoQueue, filePath);
                if (existingIndex < 0)
                {
                    await window._player.AppendToPlaylistAsync(filePath);
                    if (!ReferenceEquals(s_current, window) || window._isClosed || window._isClosing)
                    {
                        return;
                    }

                    window.AppendVideoQueueItem(title, filePath);
                    existingIndex = window.VideoQueue.Count - 1;
                }

                var selectedItem = window.VideoQueue[existingIndex];
                var requestId = ++window._videoQueueSelectionRequestId;
                await window.PlayVideoQueueItemUnderGateAsync(selectedItem, requestId);
                window.RequestForegroundActivation();
                return;
            }

            if (s_pendingVideoQueue.Count == 0)
            {
                await ShowCoreAsync(title, filePath);
                return;
            }

            var pendingIndex = FindVideoQueueItemIndex(s_pendingVideoQueue, filePath);
            if (pendingIndex < 0)
            {
                s_pendingVideoQueue.Add((title, filePath));
                pendingIndex = s_pendingVideoQueue.Count - 1;
            }

            var pendingItems = s_pendingVideoQueue.ToList();
            await ShowPlaylistCoreAsync(pendingItems, pendingIndex);
            s_pendingVideoQueue.Clear();
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    private static async Task<PlayerWindow> ShowCoreAsync(string title, string filePath)
    {
        var player = App.Services.GetRequiredService<IVideoPlaybackService>();
        var thumbnailService = App.Services.GetRequiredService<IVideoThumbnailService>();
        PlayerWindow window;
        if (s_current is { _isClosed: false, _isClosing: false } currentWindow)
        {
            window = currentWindow;
        }
        else
        {
            var initialVideoSize = await TryGetInitialVideoSizeAsync(filePath);
            window = new PlayerWindow(player, thumbnailService);
            if (initialVideoSize is { } size)
            {
                window.PrepareInitialWindowForVideo(size);
            }
        }

        s_current = window;
        window.SetVideoQueue([(title, filePath)]);
        await window.PlayAsync(
            title,
            filePath,
            activationIntent: PlayerActivationIntent.UserInitiated);
        return window;
    }

    private static async Task<SizeInt32?> TryGetInitialVideoSizeAsync(string filePath)
    {
        var mediaInfoService = App.Services.GetService<IVideoMediaInfoService>();
        if (mediaInfoService is null)
        {
            return null;
        }

        using var cancellation = new CancellationTokenSource(InitialVideoSizeProbeTimeout);
        try
        {
            var mediaInfo = await mediaInfoService.GetMediaInfoAsync(filePath, cancellation.Token);
            if (mediaInfo.Video is not
                {
                    Width: > 0 and var width,
                    Height: > 0 and var height
                } video)
            {
                return null;
            }

            var normalizedRotation = ((video.Rotation ?? 0) % 360 + 360) % 360;
            return normalizedRotation is 90 or 270
                ? new SizeInt32(height, width)
                : new SizeInt32(width, height);
        }
        catch
        {
            // Playback can still determine the size after loading the video.
            return null;
        }
    }

    private static int FindVideoQueueItemIndex(
        IEnumerable<PlayerQueueItem> items,
        string filePath)
    {
        var index = 0;
        foreach (var item in items)
        {
            if (string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static int FindVideoQueueItemIndex(
        IEnumerable<(string Title, string FilePath)> items,
        string filePath)
    {
        var index = 0;
        foreach (var item in items)
        {
            if (string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    public static Task ShowPlaylistAsync(
        IReadOnlyList<(string Title, string FilePath)> items,
        int startIndex = 0,
        CancellationToken cancellationToken = default) =>
        ShowPlaylistAsync(
            items,
            startIndex,
            PlayerActivationIntent.UserInitiated,
            deferForegroundActivation: false,
            cancellationToken: cancellationToken);

    internal static Task ShowPlaylistAsync(
        IReadOnlyList<(string Title, string FilePath)> items,
        int startIndex,
        PlayerActivationIntent activationIntent,
        bool deferForegroundActivation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }
        if (startIndex < 0 || startIndex >= items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        var queueSnapshot = items.ToArray();
        return RunOnPlayerUiThreadAsync(() =>
            ShowPlaylistOnUiThreadAsync(
                queueSnapshot,
                startIndex,
                activationIntent,
                deferForegroundActivation,
                cancellationToken));
    }

    private static async Task ShowPlaylistOnUiThreadAsync(
        IReadOnlyList<(string Title, string FilePath)> items,
        int startIndex,
        PlayerActivationIntent activationIntent,
        bool deferForegroundActivation,
        CancellationToken cancellationToken)
    {
        await s_videoOperationGate.WaitAsync(cancellationToken);
        try
        {
            await ShowPlaylistCoreAsync(
                items,
                startIndex,
                activationIntent,
                deferForegroundActivation,
                cancellationToken);
            s_pendingVideoQueue.Clear();
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    private static async Task<PlayerWindow> ShowPlaylistCoreAsync(
        IReadOnlyList<(string Title, string FilePath)> items,
        int startIndex = 0,
        PlayerActivationIntent activationIntent = PlayerActivationIntent.UserInitiated,
        bool deferForegroundActivation = false,
        CancellationToken cancellationToken = default)
    {
        if (startIndex < 0 || startIndex >= items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        var selectedItem = items[startIndex];
        PlayerWindow window;
        if (s_current is { _isClosed: false, _isClosing: false } currentWindow)
        {
            window = currentWindow;
        }
        else
        {
            var initialVideoSize = await TryGetInitialVideoSizeAsync(selectedItem.FilePath);
            window = new PlayerWindow(
                App.Services.GetRequiredService<IVideoPlaybackService>(),
                App.Services.GetRequiredService<IVideoThumbnailService>());
            if (initialVideoSize is { } size)
            {
                window.PrepareInitialWindowForVideo(size);
            }
        }

        s_current = window;
        window._isReplacingVideoQueue = true;
        try
        {
            await window.PlayAsync(
                selectedItem.Title,
                selectedItem.FilePath,
                items.Select(item => item.FilePath).ToArray(),
                startIndex,
                activationIntent,
                deferForegroundActivation,
                cancellationToken);
            window.SetVideoQueue(items, startIndex);
        }
        finally
        {
            window._isReplacingVideoQueue = false;
        }
        return window;
    }

    public static async Task EnqueueAsync(string title, string filePath)
    {
        await s_videoOperationGate.WaitAsync();
        try
        {
            if (s_current is not { _isClosed: false, _isClosing: false } window)
            {
                s_pendingVideoQueue.Add((title, filePath));
                VideoQueueChanged?.Invoke(null, EventArgs.Empty);
                return;
            }

            await window._player.AppendToPlaylistAsync(filePath);
            if (!ReferenceEquals(s_current, window) || window._isClosed || window._isClosing)
            {
                return;
            }
            window.AppendVideoQueueItem(title, filePath);
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    public static IReadOnlyList<VideoQueueItem> GetCurrentQueue()
    {
        if (s_current is not { _isClosed: false, _isClosing: false } window)
        {
            return s_pendingVideoQueue
                .Select(item => new VideoQueueItem(item.Title, item.FilePath, false))
                .ToList();
        }

        var currentIndex = window._playbackState.PlaylistPosition >= 0 &&
            window._playbackState.PlaylistPosition < window.VideoQueue.Count
            ? (int)window._playbackState.PlaylistPosition
            : window._currentVideoQueueIndex;
        return window.VideoQueue
            .Select((item, index) => new VideoQueueItem(item.Title, item.FilePath, index == currentIndex))
            .ToList();
    }

    public static async Task PlayQueueItemAsync(int index)
    {
        await s_videoOperationGate.WaitAsync();
        try
        {
            if (s_current is { _isClosed: false, _isClosing: false } window)
            {
                if (index < 0 || index >= window.VideoQueue.Count)
                {
                    return;
                }

                var selectedItem = window.VideoQueue[index];
                var requestId = ++window._videoQueueSelectionRequestId;
                await window.PlayVideoQueueItemUnderGateAsync(selectedItem, requestId);
                window.RequestForegroundActivation();
                return;
            }

            if (index < 0 || index >= s_pendingVideoQueue.Count)
            {
                return;
            }

            var pendingItems = s_pendingVideoQueue.ToList();
            await ShowPlaylistCoreAsync(pendingItems, index);
            s_pendingVideoQueue.Clear();
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    public static async Task RemoveQueueItemAsync(int index)
    {
        await s_videoOperationGate.WaitAsync();
        try
        {
            if (s_current is { _isClosed: false, _isClosing: false } window)
            {
                await window.RemoveVideoQueueItemUnderGateAsync(index);
                return;
            }

            if (index < 0 || index >= s_pendingVideoQueue.Count)
            {
                return;
            }

            s_pendingVideoQueue.RemoveAt(index);
            VideoQueueChanged?.Invoke(null, EventArgs.Empty);
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    public static Task ClearQueueAsync() =>
        RunOnPlayerUiThreadAsync(ClearQueueOnUiThreadAsync);

    private static async Task ClearQueueOnUiThreadAsync()
    {
        await s_videoOperationGate.WaitAsync();
        try
        {
            s_pendingVideoQueue.Clear();
            if (s_current is not { _isClosed: false, _isClosing: false } window)
            {
                VideoQueueChanged?.Invoke(null, EventArgs.Empty);
                return;
            }

            ++window._videoQueueSelectionRequestId;
            window.VideoQueue.Clear();
            window.UpdateVideoPlaylistCount();
            window.RemoveVideoQueueItemButton.IsEnabled = false;
            window.ApplyVideoQueueSelectionOrClear(-1);
            VideoQueueChanged?.Invoke(null, EventArgs.Empty);
            await window.CloseWithAnimationCoreUnderGateAsync();
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    public static async Task MoveQueueItemAsync(int index, int targetIndex)
    {
        await s_videoOperationGate.WaitAsync();
        try
        {
            if (s_current is { _isClosed: false, _isClosing: false } window)
            {
                await window.MoveVideoQueueItemUnderGateAsync(index, targetIndex);
                return;
            }

            if (index < 0 ||
                index >= s_pendingVideoQueue.Count ||
                targetIndex < 0 ||
                targetIndex >= s_pendingVideoQueue.Count ||
                index == targetIndex)
            {
                return;
            }

            var item = s_pendingVideoQueue[index];
            s_pendingVideoQueue.RemoveAt(index);
            s_pendingVideoQueue.Insert(targetIndex, item);
            VideoQueueChanged?.Invoke(null, EventArgs.Empty);
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    public static Task CloseCurrentAsync()
    {
        return RunOnPlayerUiThreadAsync(CloseCurrentOnUiThreadAsync);
    }

    internal static Task RequestCurrentForegroundActivationAsync()
    {
        return RunOnPlayerUiThreadAsync(() =>
        {
            if (s_current is { _isClosed: false, _isClosing: false } window)
            {
                window.RequestForegroundActivation();
            }

            return Task.CompletedTask;
        });
    }

    private static Task CloseCurrentOnUiThreadAsync()
    {
        return s_current is { _isClosed: false } window
            ? window.CloseWithAnimationAsync()
            : Task.CompletedTask;
    }

    private static Task RunOnPlayerUiThreadAsync(Func<Task> action)
    {
        var dispatcher = s_current?.DispatcherQueue ?? App.MainWindow?.DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult(true);
                }
                catch (OperationCanceledException exception)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            completion.TrySetException(
                new InvalidOperationException("The player UI dispatcher is unavailable."));
        }

        return completion.Task;
    }

    private async Task PlayAsync(
        string title,
        string filePath,
        IReadOnlyList<string>? playlistFilePaths = null,
        int playlistStartIndex = 0,
        PlayerActivationIntent activationIntent = PlayerActivationIntent.UserInitiated,
        bool deferForegroundActivation = false,
        CancellationToken cancellationToken = default)
    {
        ApplyDisplayedMediaTitle(title, filePath);
        _isPaused = false;
        PlaybackSeekBar.CancelSeek();
        _isSeeking = false;
        ResetSeekPreviewSession();
        _currentMediaPath = filePath;
        if (_preparedInitialVideoSize is { } preparedSize)
        {
            _currentVideoPixelWidth = preparedSize.Width;
            _currentVideoPixelHeight = preparedSize.Height;
            _preparedInitialVideoSize = null;
            _shouldApplyInitialVideoSize = false;
        }
        else
        {
            _currentVideoPixelWidth = 0;
            _currentVideoPixelHeight = 0;
            _shouldApplyInitialVideoSize = true;
        }
        PlaybackSeekBar.SetChapters(Array.Empty<PlayerSeekBarChapter>());
        NotifyPlayerInteraction();
        ApplyPlaybackState(_playbackState with
        {
            IsPaused = false,
            Position = 0,
            Duration = 0,
            IsSeekable = false,
            PlaylistPosition = -1,
            PlaylistCount = 0
        });

        var isFirstActivation = !_isActivated;
        if (isFirstActivation)
        {
            NativeWindowEffects.SetOpacity(_hwnd, 0);
        }

        if (isFirstActivation &&
            activationIntent == PlayerActivationIntent.BackgroundContinuation)
        {
            AppWindow.Show(false);
            _isActivated = true;
        }

        if (activationIntent == PlayerActivationIntent.BackgroundContinuation)
        {
            CancelForegroundActivationRequest(stopTaskbarFlash: true);
        }

        var loadingFeedback = BeginLoadingFeedback();

        try
        {
            if (isFirstActivation)
            {
                if (MotionHelper.AnimationsEnabled)
                {
                    await NativeWindowEffects.FadeAsync(_hwnd, 0, 255, TimeSpan.FromMilliseconds(180));
                }
                else
                {
                    NativeWindowEffects.SetOpacity(_hwnd, 255);
                }
            }

            NativeWindowEffects.ClearOpacity(_hwnd);
            ResizePlayerSurface();

            if (_usesD3D11Composition)
            {
                if (playlistFilePaths is null)
                {
                    await PlayWithD3D11CompositionAsync(filePath);
                }
                else
                {
                    var compositionSize = GetCompositionPixelSize();
                    await _player.PlayPlaylistWithD3D11CompositionAsync(
                        playlistFilePaths,
                        playlistStartIndex,
                        compositionSize.Width,
                        compositionSize.Height,
                        cancellationToken);
                    ScheduleCompositionSizeUpdate();
                }
            }
            else if (playlistFilePaths is not null)
            {
                await _player.PlayPlaylistAsync(
                    playlistFilePaths,
                    playlistStartIndex,
                    _videoHwnd,
                    cancellationToken);
            }
            else
            {
                await _player.PlayAsync(filePath, _videoHwnd);
            }

            await RefreshPlaybackStateAsync();
        }
        catch
        {
            _allowClose = true;
            Close();
            throw;
        }
        finally
        {
            await EndLoadingFeedbackAsync(loadingFeedback);
        }

        if (activationIntent == PlayerActivationIntent.UserInitiated &&
            !deferForegroundActivation)
        {
            RequestForegroundActivation();
        }
    }

    private CancellationTokenSource BeginLoadingFeedback()
    {
        CancelLoadingFeedback(setFinalVisualState: false);
        _loadingFeedbackShownAt = null;
        var cancellation = new CancellationTokenSource();
        _loadingFeedbackCancellation = cancellation;
        _ = ShowLoadingFeedbackAfterDelayAsync(cancellation);
        return cancellation;
    }

    private async Task ShowLoadingFeedbackAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(LoadingFeedbackDelay, cancellation.Token);
            if (!ReferenceEquals(_loadingFeedbackCancellation, cancellation) ||
                cancellation.IsCancellationRequested ||
                _isClosed ||
                _isClosing)
            {
                return;
            }

            _loadingFeedbackShownAt = DateTimeOffset.UtcNow;
            LoadingRing.IsActive = true;
            await MotionHelper.ShowAsync(
                LoadingRing,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0,
                cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task EndLoadingFeedbackAsync(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_loadingFeedbackCancellation, cancellation))
        {
            return;
        }

        cancellation.Cancel();
        if (_loadingFeedbackShownAt is { } shownAt &&
            LoadingRing.Visibility == Visibility.Visible)
        {
            var elapsed = DateTimeOffset.UtcNow - shownAt;
            var remaining = LoadingFeedbackMinimumVisibleDuration - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }
        }

        if (!ReferenceEquals(_loadingFeedbackCancellation, cancellation))
        {
            return;
        }

        await MotionHelper.HideAsync(
            LoadingRing,
            MotionPreset.Fast,
            MotionDirection.None,
            distance: 0);
        if (!ReferenceEquals(_loadingFeedbackCancellation, cancellation))
        {
            return;
        }

        LoadingRing.IsActive = false;
        _loadingFeedbackShownAt = null;
        _loadingFeedbackCancellation = null;
        cancellation.Dispose();
    }

    private void CancelLoadingFeedback(bool setFinalVisualState = true)
    {
        var cancellation = _loadingFeedbackCancellation;
        _loadingFeedbackCancellation = null;
        _loadingFeedbackShownAt = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        if (!setFinalVisualState)
        {
            return;
        }

        LoadingRing.IsActive = false;
        MotionHelper.CancelMotion(LoadingRing);
        MotionHelper.SetVisibleInstant(LoadingRing, isVisible: false);
    }

    private void RequestForegroundActivation()
    {
        if (_isClosed || _isClosing)
        {
            return;
        }

        CancelForegroundActivationRequest(stopTaskbarFlash: true);

        var cancellation = new CancellationTokenSource();
        _foregroundActivationCancellation = cancellation;
        var requestId = Interlocked.Increment(ref _foregroundActivationRequestId);
        _ = RetryForegroundActivationAsync(requestId, cancellation);
    }

    private async Task RetryForegroundActivationAsync(
        long requestId,
        CancellationTokenSource cancellation)
    {
        var stopwatch = Stopwatch.StartNew();
        var windowShowRequested = false;
        try
        {
            // Let a closing picker finish restoring its owner before the first
            // foreground attempt, then keep observing through the retry schedule.
            foreach (var retryAt in ForegroundActivationRetrySchedule)
            {
                var delay = retryAt - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellation.Token);
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (_isClosed ||
                    _isClosing ||
                    requestId != Interlocked.Read(ref _foregroundActivationRequestId))
                {
                    return;
                }

                if (retryAt < ForegroundActivationSettleDelay)
                {
                    if (NativeWindowEffects.IsForegroundWindow(_hwnd))
                    {
                        NativeWindowEffects.StopTaskbarFlash(_hwnd);
                        FocusPlayerSurface();
                    }
                    continue;
                }

                if (!windowShowRequested)
                {
                    AppWindow.Show(false);
                    _isActivated = true;
                    windowShowRequested = true;
                }

                if (NativeWindowEffects.IsForegroundWindow(_hwnd))
                {
                    NativeWindowEffects.StopTaskbarFlash(_hwnd);
                    FocusPlayerSurface();
                    continue;
                }

                if (!NativeWindowEffects.CanRequestForegroundActivation())
                {
                    continue;
                }

                if (NativeWindowEffects.BringToFront(_hwnd))
                {
                    NativeWindowEffects.StopTaskbarFlash(_hwnd);
                    FocusPlayerSurface();
                }
            }

            if (_isClosed ||
                _isClosing ||
                requestId != Interlocked.Read(ref _foregroundActivationRequestId))
            {
                return;
            }

            if (NativeWindowEffects.IsForegroundWindow(_hwnd))
            {
                NativeWindowEffects.StopTaskbarFlash(_hwnd);
                FocusPlayerSurface();
            }
            else
            {
                NativeWindowEffects.FlashTaskbar(_hwnd);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_foregroundActivationCancellation, cancellation))
            {
                _foregroundActivationCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool CancelForegroundActivationRequest(bool stopTaskbarFlash)
    {
        var cancellation = _foregroundActivationCancellation;
        if (cancellation is not null)
        {
            _foregroundActivationCancellation = null;
            Interlocked.Increment(ref _foregroundActivationRequestId);
            cancellation.Cancel();
        }

        if (stopTaskbarFlash)
        {
            NativeWindowEffects.StopTaskbarFlash(_hwnd);
        }

        return cancellation is not null;
    }

    private async Task PlayWithD3D11CompositionAsync(string filePath)
    {
        // Keep the composition swapchain attached while replacing media. mpv commonly
        // reuses the same swapchain address, so detaching here could leave the panel
        // unbound when no SwapChainChanged event is raised for the next video.
        AudioFallbackPanel.Visibility = Visibility.Visible;

        var compositionSize = GetCompositionPixelSize();
        await _player.PlayWithD3D11CompositionAsync(filePath, compositionSize.Width, compositionSize.Height);
        ScheduleCompositionSizeUpdate();
    }

    private void HookCompositionInput()
    {
        RootPanel.Loaded += PlayerWindow_RootPanelLoaded;
        RootPanel.PointerMoved += PlayerWindow_RootPanelPointerMoved;
        RootPanel.PointerExited += PlayerWindow_RootPanelPointerExited;
        RootPanel.PointerPressed += PlayerWindow_RootPanelPointerPressed;
        RootPanel.PointerReleased += PlayerWindow_RootPanelPointerReleased;
        RootPanel.PointerWheelChanged += PlayerWindow_RootPanelPointerWheelChanged;
        RootPanel.RightTapped += PlayerWindow_RootPanelRightTapped;
        VideoPanel.SizeChanged += PlayerWindow_VideoPanelSizeChanged;
        VideoPanel.CompositionScaleChanged += PlayerWindow_VideoPanelCompositionScaleChanged;
    }

    private void FocusPlayerSurface()
    {
        if (_usesD3D11Composition)
        {
            if (!RootPanel.Focus(FocusState.Programmatic))
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isClosed && _isWindowActive)
                    {
                        _ = RootPanel.Focus(FocusState.Programmatic);
                    }
                });
            }

            return;
        }

        if (_videoHwnd != IntPtr.Zero)
        {
            NativeMpvHostWindow.Focus(_videoHwnd);
        }
    }

    private void ResizePlayerSurface()
    {
        if (_usesD3D11Composition)
        {
            ScheduleCompositionSizeUpdate();
            return;
        }

        if (_videoHwnd != IntPtr.Zero)
        {
            NativeMpvHostWindow.ResizeToParent(_hwnd, _videoHwnd);
        }
    }

    private void PlayerWindow_RootPanelLoaded(object sender, RoutedEventArgs args)
    {
        NotifyPlayerInteraction();
        FocusPlayerSurface();
        ScheduleCompositionSizeUpdate();
    }

    private void PlayerWindow_RootPanelKeyDown(object sender, KeyRoutedEventArgs args)
    {
        NotifyPlayerInteraction();
        if (args.Key == Windows.System.VirtualKey.Escape && _isFullScreen)
        {
            _ = SetFullScreenAsync(false);
            args.Handled = true;
            return;
        }

        if (!_usesD3D11Composition)
        {
            return;
        }

        if (args.Key == Windows.System.VirtualKey.Tab)
        {
            return;
        }

        if (IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        args.Handled = HandleVideoKeyDown((int)args.Key);
    }

    private void PlayerWindow_RootPanelPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_usesD3D11Composition)
        {
            return;
        }

        NotifyPlayerInteraction();
        if (IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        var input = CreateCompositionMouseInput(args, NativeMpvMouseInputKind.Move);
        SendMpvMouseMove(input.X, input.Y, force: _isVideoMouseButtonDown);
        args.Handled = true;
    }

    private void PlayerWindow_RootPanelPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!_usesD3D11Composition)
        {
            return;
        }

        SchedulePlayerChromeAutoHide();
        if (IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        _ = SendMpvKeyPressAsync("MOUSE_LEAVE");
        args.Handled = true;
    }

    private void PlayerWindow_RootPanelPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!_usesD3D11Composition)
        {
            return;
        }

        NotifyPlayerInteraction();
        if (IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        FocusPlayerSurface();
        RootPanel.CapturePointer(args.Pointer);

        var point = args.GetCurrentPoint(VideoPanel);
        var kind = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => NativeMpvMouseInputKind.LeftDown,
            PointerUpdateKind.RightButtonPressed => NativeMpvMouseInputKind.RightDown,
            PointerUpdateKind.MiddleButtonPressed => NativeMpvMouseInputKind.MiddleDown,
            _ => NativeMpvMouseInputKind.Move
        };

        if (kind is NativeMpvMouseInputKind.LeftDown or NativeMpvMouseInputKind.RightDown or NativeMpvMouseInputKind.MiddleDown)
        {
            _isVideoMouseButtonDown = true;
            var input = CreateCompositionMouseInput(args, kind);
            _ = SendMpvMouseButtonAsync(input, GetMouseButtonKeyName(kind), isPressed: true);
        }

        args.Handled = true;
    }

    private void PlayerWindow_RootPanelPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_usesD3D11Composition)
        {
            return;
        }

        NotifyPlayerInteraction();
        if (IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        RootPanel.ReleasePointerCapture(args.Pointer);

        var point = args.GetCurrentPoint(VideoPanel);
        var kind = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonReleased => NativeMpvMouseInputKind.LeftUp,
            PointerUpdateKind.RightButtonReleased => NativeMpvMouseInputKind.RightUp,
            PointerUpdateKind.MiddleButtonReleased => NativeMpvMouseInputKind.MiddleUp,
            _ => NativeMpvMouseInputKind.Move
        };

        if (kind is NativeMpvMouseInputKind.LeftUp or NativeMpvMouseInputKind.RightUp or NativeMpvMouseInputKind.MiddleUp)
        {
            _isVideoMouseButtonDown = false;
            var input = CreateCompositionMouseInput(args, kind);
            _ = SendMpvMouseButtonAsync(input, GetMouseButtonKeyName(kind), isPressed: false);
        }

        args.Handled = true;
    }

    private void PlayerWindow_RootPanelPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (!_usesD3D11Composition)
        {
            return;
        }

        NotifyPlayerInteraction();
        if (IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        var input = CreateCompositionMouseInput(args, NativeMpvMouseInputKind.VerticalWheel);
        ShowVolumeFlyoutFromWheelInput();
        _ = SendMpvMouseKeyPressAsync(input, input.WheelDelta > 0 ? "WHEEL_UP" : "WHEEL_DOWN");
        args.Handled = true;
    }

    private void PlayerWindow_RootPanelRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (!_usesD3D11Composition)
        {
            return;
        }

        NotifyPlayerInteraction();
        if (IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        PlayerContextMenu.ShowAt(OverlayRoot, args.GetPosition(OverlayRoot));
        args.Handled = true;
    }

    private void PlayerWindow_VideoPanelSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_usesD3D11Composition && !_isClosed && !_isClosing)
        {
            ScheduleCompositionSizeUpdate();
        }
    }

    private void PlayerWindow_VideoPanelCompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_usesD3D11Composition && !_isClosed && !_isClosing)
        {
            if (_currentSwapChain != IntPtr.Zero)
            {
                SetVideoPanelSwapChain(_currentSwapChain);
            }

            ScheduleCompositionSizeUpdate();
        }
    }

    private void PlayerWindow_SwapChainChanged(object? sender, VideoSwapChainChangedEventArgs args)
    {
        if (_isClosed || _isClosing || !_usesD3D11Composition)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || _isClosing || !_usesD3D11Composition)
            {
                return;
            }

            _currentSwapChain = args.SwapChain;
            SetVideoPanelSwapChain(args.SwapChain);

            if (args.SwapChain != IntPtr.Zero)
            {
                AudioFallbackPanel.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void SetVideoPanelSwapChain(IntPtr swapChain)
    {
        SwapChainPanelHost.SetSwapChain(
            VideoPanel,
            swapChain,
            GetVideoCompositionScaleX(),
            GetVideoCompositionScaleY());
    }

    private NativeMpvMouseInput CreateCompositionMouseInput(
        PointerRoutedEventArgs args,
        NativeMpvMouseInputKind kind)
    {
        var point = args.GetCurrentPoint(VideoPanel);
        var x = Math.Max(0, (int)Math.Round(point.Position.X * GetVideoCompositionScaleX()));
        var y = Math.Max(0, (int)Math.Round(point.Position.Y * GetVideoCompositionScaleY()));

        return new NativeMpvMouseInput(kind, x, y, point.Properties.MouseWheelDelta);
    }

    private SizeInt32 GetCompositionPixelSize()
    {
        var pixelWidth = VideoPanel.ActualWidth > 0
            ? (int)Math.Round(VideoPanel.ActualWidth * GetVideoCompositionScaleX())
            : AppWindow.Size.Width;
        var pixelHeight = VideoPanel.ActualHeight > 0
            ? (int)Math.Round(VideoPanel.ActualHeight * GetVideoCompositionScaleY())
            : AppWindow.Size.Height;

        return new SizeInt32(Math.Max(1, pixelWidth), Math.Max(1, pixelHeight));
    }

    private double GetVideoCompositionScaleX()
    {
        return VideoPanel.CompositionScaleX is > 0 and var scale
            ? scale
            : RootPanel.XamlRoot?.RasterizationScale is > 0 and var fallbackScale
                ? fallbackScale
                : 1;
    }

    private double GetVideoCompositionScaleY()
    {
        return VideoPanel.CompositionScaleY is > 0 and var scale
            ? scale
            : RootPanel.XamlRoot?.RasterizationScale is > 0 and var fallbackScale
                ? fallbackScale
                : 1;
    }

    private void ScheduleCompositionSizeUpdate()
    {
        if (!_usesD3D11Composition || _isClosed || _isClosing)
        {
            return;
        }

        var size = GetCompositionPixelSize();
        CancellationToken cancellationToken = default;
        var shouldStartWorker = false;

        lock (_compositionResizeLock)
        {
            _pendingCompositionPixelSize = size;
            _hasPendingCompositionResize = true;

            if (!_isCompositionResizeWorkerRunning)
            {
                if (_compositionResizeCancellation is null || _compositionResizeCancellation.IsCancellationRequested)
                {
                    _compositionResizeCancellation?.Dispose();
                    _compositionResizeCancellation = new CancellationTokenSource();
                }

                cancellationToken = _compositionResizeCancellation.Token;
                _isCompositionResizeWorkerRunning = true;
                shouldStartWorker = true;
            }
        }

        if (shouldStartWorker)
        {
            _ = ProcessCompositionSizeUpdatesAsync(cancellationToken);
        }
    }

    private async Task ProcessCompositionSizeUpdatesAsync(CancellationToken cancellationToken)
    {
        var completedNormally = false;

        try
        {
            while (true)
            {
                SizeInt32 size;
                lock (_compositionResizeLock)
                {
                    if (!_hasPendingCompositionResize)
                    {
                        _isCompositionResizeWorkerRunning = false;
                        completedNormally = true;
                        return;
                    }

                    size = _pendingCompositionPixelSize;
                    _hasPendingCompositionResize = false;
                }

                if (_isClosed || _isClosing || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!IsSamePixelSize(size, _lastCompositionPixelSize))
                {
                    await _player.SetD3D11CompositionSizeAsync(size.Width, size.Height, cancellationToken);
                    _lastCompositionPixelSize = size;
                }

                await Task.Delay(CompositionResizeThrottle, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            if (!completedNormally)
            {
                ResetCompositionResizeWorker(cancellationToken);
            }
        }
    }

    private void ResetCompositionResizeWorker(CancellationToken cancellationToken)
    {
        CancellationToken restartToken = default;
        var shouldRestartWorker = false;

        lock (_compositionResizeLock)
        {
            _isCompositionResizeWorkerRunning = false;

            if (_hasPendingCompositionResize &&
                !_isClosed &&
                !_isClosing &&
                !cancellationToken.IsCancellationRequested &&
                _compositionResizeCancellation is { IsCancellationRequested: false } cancellation)
            {
                restartToken = cancellation.Token;
                _isCompositionResizeWorkerRunning = true;
                shouldRestartWorker = true;
            }
        }

        if (shouldRestartWorker)
        {
            _ = ProcessCompositionSizeUpdatesAsync(restartToken);
        }
    }

    private static bool IsSamePixelSize(SizeInt32 left, SizeInt32 right)
    {
        return left.Width == right.Width && left.Height == right.Height;
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs args)
    {
        await TogglePauseAsync();
    }

    private async void PlayPauseMenuItem_Click(object sender, RoutedEventArgs args)
    {
        await TogglePauseAsync();
    }

    private async void FullScreenButton_Click(object sender, RoutedEventArgs args)
    {
        await SetFullScreenAsync(!_isFullScreen);
    }

    private async void FullScreenMenuItem_Click(object sender, RoutedEventArgs args)
    {
        await SetFullScreenAsync(!_isFullScreen);
    }

    private void PlaylistButton_Click(object sender, RoutedEventArgs args)
    {
        ShowVideoQueueFlyout(PlaylistButton);
    }

    private void MorePlaylistItem_Click(object sender, RoutedEventArgs args)
    {
        _ = DispatcherQueue.TryEnqueue(() => ShowVideoQueueFlyout(PlayerMoreButton));
    }

    private void ShowVideoQueueFlyout(FrameworkElement anchor)
    {
        NotifyPlayerInteraction();
        RefreshPlaybackQueue();
        UpdateVideoPlaylistCount();
        VideoPlaylistFlyout.ShowAt(anchor);
    }

    private void PlaybackCoordinator_PlaybackQueueChanged(object? sender, EventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed && !_isClosing)
            {
                RefreshPlaybackQueue();
                ApplyPlaybackState(_playbackState);
            }
        });
    }

    private void PlaybackCoordinator_PlaybackOptionsChanged(object? sender, EventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed && !_isClosing)
            {
                UpdateVideoPlaybackModeControls();
            }
        });
    }

    private void RefreshPlaybackQueue()
    {
        var queue = _playbackCoordinator.PlaybackQueue;
        var selectedStableKey =
            (VideoPlaylistList.SelectedItem as PlayerQueueItem)?.StableKey;
        SynchronizeVideoQueueTitles(queue);
        var currentIndex = -1;
        ResetVideoQueueThumbnailLoading();
        _isUpdatingVideoQueueSelection = true;
        try
        {
            var existingItemsByKey = PlaybackQueue.ToDictionary(
                item => item.StableKey,
                StringComparer.OrdinalIgnoreCase);
            var desiredItems = new List<PlayerQueueItem>(queue.Count);
            var stableKeyOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < queue.Count; index++)
            {
                var item = queue[index];
                var stableKeyRoot = item.MediaId is { } mediaId
                    ? mediaId.ToString("N")
                    : $"{(int)item.Kind}:{item.FilePath}";
                stableKeyOccurrences.TryGetValue(stableKeyRoot, out var occurrence);
                stableKeyOccurrences[stableKeyRoot] = occurrence + 1;
                var stableKey = $"{stableKeyRoot}\u001F{occurrence}";
                if (existingItemsByKey.Remove(stableKey, out var existingItem))
                {
                    existingItem.FilePath = item.FilePath;
                    existingItem.UpdateTitle(item.Title);
                    existingItem.SetThumbnailPath(item.ThumbnailPath);
                    existingItem.SetIsCurrent(item.IsCurrent);
                    desiredItems.Add(existingItem);
                }
                else
                {
                    desiredItems.Add(new PlayerQueueItem(
                        item.MediaId,
                        item.Title,
                        item.FilePath,
                        item.Kind,
                        item.ThumbnailPath,
                        stableKey,
                        item.IsCurrent));
                }

                if (item.IsCurrent)
                {
                    currentIndex = index;
                }
            }
            foreach (var removedItem in existingItemsByKey.Values)
            {
                removedItem.ReleaseThumbnailSource();
            }

            ReconcilePlaybackQueueItems(desiredItems);

            VideoPlaylistList.SelectedItem = string.IsNullOrWhiteSpace(selectedStableKey)
                ? null
                : PlaybackQueue.FirstOrDefault(item =>
                    string.Equals(
                        item.StableKey,
                        selectedStableKey,
                        StringComparison.OrdinalIgnoreCase));
            RemoveVideoQueueItemButton.IsEnabled =
                VideoPlaylistList.SelectedIndex >= 0;
        }
        finally
        {
            _isUpdatingVideoQueueSelection = false;
        }

        UpdateVideoPlaylistCount();
        UpdateVideoPlaybackModeControls();
        if (currentIndex >= 0 && _openPlayerFlyouts.Contains(VideoPlaylistFlyout))
        {
            ScrollVideoQueueToCurrentItem(MotionIntent.PlaybackFollow);
        }
    }

    private void ReconcilePlaybackQueueItems(IReadOnlyList<PlayerQueueItem> desiredItems)
    {
        for (var targetIndex = 0; targetIndex < desiredItems.Count; targetIndex++)
        {
            var desiredItem = desiredItems[targetIndex];
            if (targetIndex < PlaybackQueue.Count &&
                ReferenceEquals(PlaybackQueue[targetIndex], desiredItem))
            {
                continue;
            }

            var currentIndex = PlaybackQueue.IndexOf(desiredItem);
            if (currentIndex >= 0)
            {
                PlaybackQueue.Move(currentIndex, targetIndex);
            }
            else
            {
                PlaybackQueue.Insert(targetIndex, desiredItem);
            }
        }

        while (PlaybackQueue.Count > desiredItems.Count)
        {
            PlaybackQueue.RemoveAt(PlaybackQueue.Count - 1);
        }
    }

    private void SynchronizeVideoQueueTitles(IReadOnlyList<PlaybackQueueItem> queue)
    {
        var videoTitlesByPath = queue
            .Where(item => item.Kind == MediaLibraryItemKind.Video)
            .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Title,
                StringComparer.OrdinalIgnoreCase);
        foreach (var item in VideoQueue)
        {
            if (videoTitlesByPath.TryGetValue(item.FilePath, out var title))
            {
                item.UpdateTitle(title);
            }
        }

        var currentItem = queue.FirstOrDefault(
            item => item.IsCurrent && item.Kind == MediaLibraryItemKind.Video);
        if (currentItem is not null &&
            string.Equals(currentItem.FilePath, _currentMediaPath, StringComparison.OrdinalIgnoreCase))
        {
            ApplyDisplayedMediaTitle(currentItem.Title, currentItem.FilePath);
        }
    }

    private void UpdateVideoPlaylistCount()
    {
        SetLiveRegionText(
            VideoPlaylistCountText,
            string.Format(
            GetPlayerResourceString("PlaybackQueueSidebar_CountFormat", "{0} items"),
            PlaybackQueue.Count));
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

    private void ApplyDisplayedMediaTitle(string? title, string filePath)
    {
        var mediaChanged = !string.Equals(
            _displayedTitleMediaPath,
            filePath,
            StringComparison.OrdinalIgnoreCase);
        _displayedTitleMediaPath = filePath;
        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : title.Trim();
        Title = string.IsNullOrWhiteSpace(resolvedTitle)
            ? GetPlayerResourceString("PlayerWindow_DefaultTitle", "Koukei Player")
            : resolvedTitle;
        AppWindow.Title = Title;
        TitleText.Text = Title;
        TitleBarTitleText.Text = Title;
        SetLiveRegionText(AudioTitleText, Title);
        var titleToolTipText = LongTextToolTip.CreateMediaText(Title, filePath);
        LongTextToolTip.SetText(TitleBarDragRegion, titleToolTipText);
        LongTextToolTip.SetText(AudioTitleText, titleToolTipText);
        if (mediaChanged && _isActivated)
        {
            _ = MotionHelper.ShowAsync(
                TitleBarTitleText,
                MotionPreset.Standard,
                MotionDirection.Down,
                distance: 8);
            _ = MotionHelper.ShowAsync(
                AudioTitleText,
                MotionPreset.Standard,
                MotionDirection.Down,
                distance: 8);
        }
    }

    private void SetVideoQueue(
        IReadOnlyList<(string Title, string FilePath)> items,
        int selectedIndex = 0)
    {
        if (items.Count > 0 && (selectedIndex < 0 || selectedIndex >= items.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        }

        ++_videoQueueSelectionRequestId;
        ResetVideoQueueThumbnailLoading();
        _isUpdatingVideoQueueSelection = true;
        try
        {
            VideoQueue.Clear();
            foreach (var item in items)
            {
                VideoQueue.Add(new PlayerQueueItem(null, item.Title, item.FilePath));
            }
            _currentVideoQueueIndex = VideoQueue.Count > 0 ? selectedIndex : -1;
            UpdateVideoPlaylistCount();
        }
        finally
        {
            _isUpdatingVideoQueueSelection = false;
        }

        VideoQueueChanged?.Invoke(null, EventArgs.Empty);
    }

    private void AppendVideoQueueItem(string title, string filePath)
    {
        EnsureVideoQueueThumbnailLoadingSession();
        var queueItem = new PlayerQueueItem(null, title, filePath);
        VideoQueue.Add(queueItem);
        UpdateVideoPlaylistCount();
        VideoQueueChanged?.Invoke(null, EventArgs.Empty);
    }

    private void VideoPlaylistList_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.Item is PlayerQueueItem recycledItem)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (VideoPlaylistList.ContainerFromItem(recycledItem) is null)
                    {
                        recycledItem.ReleaseThumbnailSource();
                    }
                });
            }

            return;
        }

        if (args.Item is not PlayerQueueItem
            {
                Kind: MediaLibraryItemKind.Video,
                ThumbnailSource: null
            } item ||
            _isClosed ||
            _isClosing)
        {
            return;
        }

        QueueVideoQueueThumbnail(item);
    }

    private void QueueVideoQueueThumbnail(PlayerQueueItem item)
    {
        EnsureVideoQueueThumbnailLoadingSession();
        if (!_queuedVideoQueueThumbnails.Add(item))
        {
            return;
        }

        _videoQueueThumbnailQueue.Enqueue(item);
        StartVideoQueueThumbnailWorker();
    }

    private void StartVideoQueueThumbnailWorker()
    {
        var cancellation = _videoQueueThumbnailCancellation;
        if (_isVideoQueueThumbnailWorkerRunning ||
            cancellation is null ||
            cancellation.IsCancellationRequested ||
            _videoQueueThumbnailQueue.Count == 0 ||
            _isClosed ||
            _isClosing)
        {
            return;
        }

        _isVideoQueueThumbnailWorkerRunning = true;
        _ = ProcessVideoQueueThumbnailsAsync(
            _videoQueueThumbnailBatchId,
            cancellation.Token);
    }

    private async Task ProcessVideoQueueThumbnailsAsync(
        long batchId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Container realization can occur while a queue mutation still owns the
            // playback gate. Defer even cache hits so thumbnail work never extends it.
            await Task.Yield();
            while (!cancellationToken.IsCancellationRequested &&
                batchId == _videoQueueThumbnailBatchId &&
                _videoQueueThumbnailQueue.TryDequeue(out var item))
            {
                try
                {
                    if (item.ThumbnailSource is not null ||
                        !PlaybackQueue.Contains(item) ||
                        VideoPlaylistList.ContainerFromItem(item) is null)
                    {
                        continue;
                    }

                    await LoadVideoQueueThumbnailAsync(item, batchId, cancellationToken);
                }
                finally
                {
                    _queuedVideoQueueThumbnails.Remove(item);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _isVideoQueueThumbnailWorkerRunning = false;
            StartVideoQueueThumbnailWorker();
        }
    }

    private async Task LoadVideoQueueThumbnailAsync(
        PlayerQueueItem item,
        long batchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnailPath = await MediaThumbnailResolver.ResolveOrCreateAsync(
                new MediaLibraryItem
                {
                    Id = item.MediaId ?? Guid.Empty,
                    Path = item.FilePath,
                    Kind = MediaLibraryItemKind.Video,
                    ThumbnailPath = item.ThumbnailPath
                },
                cancellationToken);
            if (item.MediaId is { } mediaId &&
                !string.Equals(
                    item.ThumbnailPath,
                    thumbnailPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                using var scope = App.Services.CreateScope();
                var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
                await library.SetThumbnailAsync(mediaId, thumbnailPath, cancellationToken);
                _playbackCoordinator.UpdateQueueItemThumbnail(
                    mediaId,
                    item.FilePath,
                    thumbnailPath);
            }

            if (cancellationToken.IsCancellationRequested ||
                batchId != _videoQueueThumbnailBatchId ||
                _isClosed ||
                _isClosing ||
                !ReferenceEquals(s_current, this) ||
                !PlaybackQueue.Contains(item) ||
                VideoPlaylistList.ContainerFromItem(item) is null)
            {
                return;
            }

            item.SetThumbnailPath(thumbnailPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Queue thumbnails are best-effort and must never interrupt playback.
        }
    }

    private void EnsureVideoQueueThumbnailLoadingSession()
    {
        if (_videoQueueThumbnailCancellation is not null)
        {
            return;
        }

        ++_videoQueueThumbnailBatchId;
        _videoQueueThumbnailCancellation = new CancellationTokenSource();
    }

    private void ResetVideoQueueThumbnailLoading()
    {
        CancelVideoQueueThumbnailLoading();
        EnsureVideoQueueThumbnailLoadingSession();
    }

    private void CancelVideoQueueThumbnailLoading()
    {
        ++_videoQueueThumbnailBatchId;
        var cancellation = _videoQueueThumbnailCancellation;
        _videoQueueThumbnailCancellation = null;
        _videoQueueThumbnailQueue.Clear();
        _queuedVideoQueueThumbnails.Clear();
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void ReleaseVideoQueueThumbnails()
    {
        foreach (var item in PlaybackQueue)
        {
            item.ReleaseThumbnailSource();
        }
    }

    private void VideoPlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isUpdatingVideoQueueSelection)
        {
            return;
        }

        RemoveVideoQueueItemButton.IsEnabled =
            VideoPlaylistList.SelectedIndex >= 0 &&
            VideoPlaylistList.SelectedIndex < PlaybackQueue.Count;
    }

    private async void VideoPlaylistList_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs args)
    {
        if (GetVideoPlaylistItemFromOriginalSource(args.OriginalSource) is not { } item)
        {
            return;
        }

        args.Handled = true;
        await PlaySelectedPlaybackQueueItemAsync(item);
    }

    private async void VideoPlaylistList_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Enter ||
            VideoPlaylistList.SelectedItem is not PlayerQueueItem item)
        {
            return;
        }

        args.Handled = true;
        await PlaySelectedPlaybackQueueItemAsync(item);
    }

    private PlayerQueueItem? GetVideoPlaylistItemFromOriginalSource(object originalSource)
    {
        for (var current = originalSource as DependencyObject;
             current is not null && !ReferenceEquals(current, VideoPlaylistList);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListViewItem { Content: PlayerQueueItem item })
            {
                return item;
            }
        }

        return null;
    }

    private async Task PlaySelectedPlaybackQueueItemAsync(PlayerQueueItem item)
    {
        var index = PlaybackQueue.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        await _playbackCoordinator.PlayQueueItemAsync(index);
    }

    private async Task PlayVideoQueueItemUnderGateAsync(PlayerQueueItem selectedItem, long requestId)
    {
        if (requestId != _videoQueueSelectionRequestId ||
            !ReferenceEquals(s_current, this) ||
            _isClosed ||
            _isClosing)
        {
            return;
        }

        var index = VideoQueue.IndexOf(selectedItem);
        if (index < 0)
        {
            return;
        }

        if (_playbackState.PlaylistPosition == index)
        {
            ApplyVideoQueueSelection(index);
            return;
        }

        if (await TryExecutePlaybackCommandAsync(() => _player.PlayPlaylistItemAsync(index)))
        {
            if (requestId == _videoQueueSelectionRequestId)
            {
                ApplyVideoQueueSelection(index);
            }
        }
        else if (requestId == _videoQueueSelectionRequestId)
        {
            var confirmedState = await _player.GetPlaybackStateAsync();
            ApplyVideoQueueSelectionOrClear(confirmedState.PlaylistPosition);
        }
    }

    private async void RemoveVideoQueueItemButton_Click(object sender, RoutedEventArgs args)
    {
        if (VideoPlaylistList.SelectedIndex >= 0)
        {
            await _playbackCoordinator.RemoveQueueItemAsync(VideoPlaylistList.SelectedIndex);
        }
    }

    private async Task RemoveVideoQueueItemUnderGateAsync(int index)
    {
        if (!ReferenceEquals(s_current, this) ||
            _isClosed ||
            _isClosing ||
            index < 0 ||
            index >= VideoQueue.Count)
        {
            return;
        }

        var selectedItem = VideoQueue[index];
        ++_videoQueueSelectionRequestId;
        index = VideoQueue.IndexOf(selectedItem);
        if (index < 0)
        {
            return;
        }

        if (VideoQueue.Count == 1)
        {
            VideoQueue.Clear();
            UpdateVideoPlaylistCount();
            RemoveVideoQueueItemButton.IsEnabled = false;
            ApplyVideoQueueSelectionOrClear(-1);
            VideoQueueChanged?.Invoke(null, EventArgs.Empty);
            await CloseWithAnimationCoreUnderGateAsync();
            return;
        }

        var stateBeforeRemoval = await _player.GetPlaybackStateAsync();
        // mpv can auto-start only a following item when the current row is
        // removed. If the actual current row is final, hand off explicitly.
        var movedToPrevious = stateBeforeRemoval.PlaylistPosition == index &&
            index == VideoQueue.Count - 1;
        if (movedToPrevious &&
            !await TryExecutePlaybackCommandAsync(() => _player.PlayPlaylistItemAsync(index - 1)))
        {
            ApplyVideoQueueSelectionOrClear((await _player.GetPlaybackStateAsync()).PlaylistPosition);
            return;
        }

        if (!await TryExecutePlaybackCommandAsync(() => _player.RemovePlaylistItemAsync(index)))
        {
            ApplyVideoQueueSelectionOrClear((await _player.GetPlaybackStateAsync()).PlaylistPosition);
            return;
        }

        VideoQueue.RemoveAt(index);
        UpdateVideoPlaylistCount();
        ApplyVideoQueueSelectionOrClear((await _player.GetPlaybackStateAsync()).PlaylistPosition);
        VideoQueueChanged?.Invoke(null, EventArgs.Empty);
    }

    private async Task MoveVideoQueueItemUnderGateAsync(int index, int targetIndex)
    {
        if (!ReferenceEquals(s_current, this) ||
            _isClosed ||
            _isClosing ||
            index < 0 ||
            index >= VideoQueue.Count ||
            targetIndex < 0 ||
            targetIndex >= VideoQueue.Count ||
            index == targetIndex)
        {
            return;
        }

        ++_videoQueueSelectionRequestId;
        if (!await TryExecutePlaybackCommandAsync(
                () => _player.MovePlaylistItemAsync(index, targetIndex)))
        {
            ApplyVideoQueueSelectionOrClear(
                (await _player.GetPlaybackStateAsync()).PlaylistPosition);
            return;
        }

        _isUpdatingVideoQueueSelection = true;
        try
        {
            VideoQueue.Move(index, targetIndex);
        }
        finally
        {
            _isUpdatingVideoQueueSelection = false;
        }

        ApplyVideoQueueSelectionOrClear(
            (await _player.GetPlaybackStateAsync()).PlaylistPosition);
        VideoQueueChanged?.Invoke(null, EventArgs.Empty);
    }

    private void ApplyVideoQueueSelectionOrClear(long position)
    {
        if (position >= 0 && position < VideoQueue.Count)
        {
            ApplyVideoQueueSelection((int)position);
            return;
        }

        var selectionChanged = _currentVideoQueueIndex >= 0;
        _currentVideoQueueIndex = -1;

        if (selectionChanged)
        {
            VideoQueueChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private void ApplyVideoQueueSelection(int index)
    {
        if (index < 0 || index >= VideoQueue.Count)
        {
            return;
        }

        var selectionChanged = _currentVideoQueueIndex != index;
        _isUpdatingVideoQueueSelection = true;
        try
        {
            _currentVideoQueueIndex = index;
            var item = VideoQueue[index];
            if (!string.Equals(_currentMediaPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                PlaybackSeekBar.CancelSeek();
                _isSeeking = false;
                ResetSeekPreviewSession();
                _currentMediaPath = item.FilePath;
                _currentVideoPixelWidth = 0;
                _currentVideoPixelHeight = 0;
                _shouldApplyInitialVideoSize = true;
                PlaybackSeekBar.SetChapters(Array.Empty<PlayerSeekBarChapter>());
            }

            var queueTitle = _playbackCoordinator.PlaybackQueue.FirstOrDefault(
                queueItem => queueItem.Kind == MediaLibraryItemKind.Video &&
                    string.Equals(queueItem.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase))?.Title;
            item.UpdateTitle(queueTitle ?? item.Title);
            ApplyDisplayedMediaTitle(item.Title, item.FilePath);
        }
        finally
        {
            _isUpdatingVideoQueueSelection = false;
        }

        if (selectionChanged)
        {
            if (_openPlayerFlyouts.Contains(VideoPlaylistFlyout))
            {
                ScrollVideoQueueToCurrentItem(MotionIntent.PlaybackFollow);
            }
            VideoQueueChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private void ScrollVideoQueueToCurrentItem(MotionIntent intent)
    {
        if (_isClosed || _isClosing)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            var currentItem = PlaybackQueue.FirstOrDefault(item => item.IsCurrent);
            if (_isClosed ||
                _isClosing ||
                !_openPlayerFlyouts.Contains(VideoPlaylistFlyout) ||
                currentItem is null)
            {
                return;
            }

            MotionHelper.BringIntoView(
                VideoPlaylistList,
                currentItem,
                intent,
                ScrollIntoViewAlignment.Leading,
                verticalAlignmentRatio: 0);
        });
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecutePlaybackCommandAsync(() => _playbackCoordinator.PlayPreviousAsync());
    }

    private async void NextButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecutePlaybackCommandAsync(() => _playbackCoordinator.PlayNextAsync());
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs args)
    {
        NotifyPlayerInteraction();
        _playbackCoordinator.IsShuffleEnabled = !_playbackCoordinator.IsShuffleEnabled;
        UpdateVideoPlaybackModeControls();
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs args)
    {
        NotifyPlayerInteraction();
        _playbackCoordinator.RepeatMode = _playbackCoordinator.RepeatMode switch
        {
            AudioRepeatMode.Off => AudioRepeatMode.All,
            AudioRepeatMode.All => AudioRepeatMode.One,
            _ => AudioRepeatMode.Off
        };
        UpdateVideoPlaybackModeControls();
    }

    private void UpdateVideoPlaybackModeControls()
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
            var animate = _displayedShuffleEnabled.HasValue;
            _displayedShuffleEnabled = isShuffleEnabled;
            UIElement outgoing = isShuffleEnabled
                ? ShuffleInactiveIcon
                : ShuffleActiveIcon;
            UIElement incoming = isShuffleEnabled
                ? ShuffleActiveIcon
                : ShuffleInactiveIcon;
            if (animate)
            {
                _ = MotionHelper.CrossFadeAsync(
                    outgoing,
                    incoming,
                    MotionPreset.Fast,
                    MotionDirection.None);
            }
            else
            {
                MotionHelper.SetVisibleInstant(outgoing, isVisible: false);
                MotionHelper.SetVisibleInstant(incoming, isVisible: true);
            }
        }

        var repeatGlyph = repeatMode == AudioRepeatMode.One ? "\uE8ED" : "\uE8EE";
        if (_displayedRepeatMode != repeatMode)
        {
            var animate = _displayedRepeatMode.HasValue;
            _displayedRepeatMode = repeatMode;
            if (!string.Equals(RepeatActiveGlyph.Glyph, repeatGlyph, StringComparison.Ordinal))
            {
                if (animate)
                {
                    _ = MotionHelper.SwapContentAsync(
                        RepeatActiveGlyph,
                        () => RepeatActiveGlyph.Glyph = repeatGlyph,
                        MotionPreset.Fast);
                }
                else
                {
                    RepeatActiveGlyph.Glyph = repeatGlyph;
                }
            }

            UIElement outgoing = isRepeatEnabled
                ? RepeatInactiveIcon
                : RepeatActiveIcon;
            UIElement incoming = isRepeatEnabled
                ? RepeatActiveIcon
                : RepeatInactiveIcon;
            if (animate)
            {
                _ = MotionHelper.CrossFadeAsync(
                    outgoing,
                    incoming,
                    MotionPreset.Fast,
                    MotionDirection.None);
            }
            else
            {
                MotionHelper.SetVisibleInstant(outgoing, isVisible: false);
                MotionHelper.SetVisibleInstant(incoming, isVisible: true);
            }
        }

        MoreRepeatIcon.Glyph = repeatGlyph;
        var hasQueue = PlaybackQueue.Count > 0;
        ShuffleButton.IsEnabled = hasQueue;
        RepeatButton.IsEnabled = hasQueue;
        MoreShuffleItem.IsEnabled = hasQueue;
        MoreRepeatItem.IsEnabled = hasQueue;

        var shuffleName = GetPlayerResourceString(
            "PlayerWindow_Shuffle",
            "Shuffle");
        var shuffleAction = GetPlayerResourceString(
            isShuffleEnabled
                ? "PlayerWindow_ShuffleDisable"
                : "PlayerWindow_ShuffleEnable",
            isShuffleEnabled ? "Turn shuffle off" : "Turn shuffle on");
        AutomationProperties.SetName(ShuffleButton, shuffleName);
        ToolTipService.SetToolTip(ShuffleButton, shuffleAction);
        AutomationProperties.SetItemStatus(
            ShuffleButton,
            GetPlayerResourceString(
                isShuffleEnabled
                    ? "PlayerWindow_StateOn"
                    : "PlayerWindow_StateOff",
                isShuffleEnabled ? "On" : "Off"));
        MoreShuffleItem.Text = shuffleAction;
        AutomationProperties.SetName(MoreShuffleItem, shuffleAction);
        AutomationProperties.SetItemStatus(
            MoreShuffleItem,
            AutomationProperties.GetItemStatus(ShuffleButton));

        var repeatName = GetPlayerResourceString(
            "PlayerWindow_Repeat",
            "Repeat");
        var repeatAction = GetPlayerResourceString(
            repeatMode switch
            {
                AudioRepeatMode.All => "PlayerWindow_RepeatOneEnable",
                AudioRepeatMode.One => "PlayerWindow_RepeatDisable",
                _ => "PlayerWindow_RepeatEnable"
            },
            repeatMode switch
            {
                AudioRepeatMode.All => "Switch to repeat one",
                AudioRepeatMode.One => "Turn repeat off",
                _ => "Turn repeat all on"
            });
        AutomationProperties.SetName(RepeatButton, repeatName);
        ToolTipService.SetToolTip(RepeatButton, repeatAction);
        AutomationProperties.SetItemStatus(
            RepeatButton,
            GetPlayerResourceString(
                repeatMode switch
                {
                    AudioRepeatMode.All => "PlayerWindow_RepeatStateAll",
                    AudioRepeatMode.One => "PlayerWindow_RepeatStateOne",
                    _ => "PlayerWindow_StateOff"
                },
                repeatMode switch
                {
                    AudioRepeatMode.All => "Repeat all",
                    AudioRepeatMode.One => "Repeat one",
                    _ => "Off"
                }));
        MoreRepeatItem.Text = repeatAction;
        AutomationProperties.SetName(MoreRepeatItem, repeatAction);
        AutomationProperties.SetItemStatus(
            MoreRepeatItem,
            AutomationProperties.GetItemStatus(RepeatButton));
    }

    private async void RewindButton_Click(object sender, RoutedEventArgs args)
    {
        if (!CanSeekCurrentMedia())
        {
            return;
        }

        var seekSeconds = GetAdaptiveSeekSeconds(_playbackState.Duration);
        await ExecutePlaybackCommandAsync(() => _player.SeekRelativeAsync(-seekSeconds));
    }

    private async void ForwardButton_Click(object sender, RoutedEventArgs args)
    {
        if (!CanSeekCurrentMedia())
        {
            return;
        }

        var seekSeconds = GetAdaptiveSeekSeconds(_playbackState.Duration);
        await ExecutePlaybackCommandAsync(() => _player.SeekRelativeAsync(seekSeconds));
    }

    private void MuteButton_Click(object sender, RoutedEventArgs args)
    {
        ShowVolumeFlyout();
    }

    private void ShowVolumeFlyout()
    {
        NotifyPlayerInteraction();
        if (!VolumeFlyout.IsOpen)
        {
            VolumeFlyout.ShowAt(
                MuteButton,
                new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.Top,
                    ShowMode = FlyoutShowMode.Transient
                });
        }
    }

    private void ShowVolumeFlyoutFromWheelInput()
    {
        if (_isClosed || _isClosing)
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            ShowVolumeFlyout();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed && !_isClosing)
            {
                ShowVolumeFlyout();
            }
        });
    }

    private void PlayerFlyout_Opened(object? sender, object args)
    {
        if (sender is FlyoutBase flyout)
        {
            _openPlayerFlyouts.Add(flyout);
            if (ReferenceEquals(flyout, VideoPlaylistFlyout))
            {
                ScrollVideoQueueToCurrentItem(MotionIntent.InitialPosition);
            }
            else if (ReferenceEquals(flyout, VolumeFlyout))
            {
                UpdateVolumeButtonDescription();
            }
        }

        _playerChromeAutoHideCancellation?.Cancel();
        SetPlayerChromeVisible(true);
    }

    private void PlayerFlyout_Closed(object? sender, object args)
    {
        if (sender is FlyoutBase flyout)
        {
            _openPlayerFlyouts.Remove(flyout);
            if (ReferenceEquals(flyout, VolumeFlyout))
            {
                UpdateVolumeButtonDescription();
            }
        }

        if (!HasOpenPlayerFlyout)
        {
            NotifyPlayerInteraction();
        }
    }

    private MenuFlyout CreatePlayerMenuFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Opened += PlayerFlyout_Opened;
        flyout.Closed += PlayerFlyout_Closed;
        return flyout;
    }

    private bool HasOpenPlayerFlyout => _openPlayerFlyouts.Count > 0;

    private void SpeedButton_Click(object sender, RoutedEventArgs args)
    {
        ShowSpeedMenu(SpeedButton);
    }

    private void MoreSpeedItem_Click(object sender, RoutedEventArgs args)
    {
        _ = DispatcherQueue.TryEnqueue(() => ShowSpeedMenu(PlayerMoreButton));
    }

    private void ShowSpeedMenu(FrameworkElement anchor)
    {
        NotifyPlayerInteraction();

        var flyout = CreatePlayerMenuFlyout();
        foreach (var speed in PlaybackSpeedSteps)
        {
            var selectedSpeed = speed;
            var item = new ToggleMenuFlyoutItem
            {
                Text = $"{selectedSpeed:0.##}x",
                IsChecked = Math.Abs(_playbackState.Speed - selectedSpeed) < 0.01
            };
            item.Click += async (_, _) =>
                await ExecutePlaybackCommandAsync(() => _player.SetSpeedAsync(selectedSpeed));
            flyout.Items.Add(item);
        }

        flyout.ShowAt(anchor);
    }

    private async void AudioTrackButton_Click(object sender, RoutedEventArgs args)
    {
        await ShowTrackSelectionMenuAsync(AudioTrackButton, VideoTrackType.Audio);
    }

    private async void MoreAudioTrackItem_Click(object sender, RoutedEventArgs args)
    {
        await Task.Yield();
        await ShowTrackSelectionMenuAsync(PlayerMoreButton, VideoTrackType.Audio);
    }

    private async void SubtitleTrackButton_Click(object sender, RoutedEventArgs args)
    {
        await ShowTrackSelectionMenuAsync(SubtitleTrackButton, VideoTrackType.Subtitle);
    }

    private async void MoreSubtitleTrackItem_Click(object sender, RoutedEventArgs args)
    {
        await Task.Yield();
        await ShowTrackSelectionMenuAsync(PlayerMoreButton, VideoTrackType.Subtitle);
    }

    private async Task ShowTrackSelectionMenuAsync(Button anchor, VideoTrackType trackType)
    {
        NotifyPlayerInteraction();
        anchor.IsEnabled = false;

        IReadOnlyList<VideoTrackInfo> allTracks;
        try
        {
            allTracks = await _player.GetTracksAsync();
        }
        catch
        {
            allTracks = Array.Empty<VideoTrackInfo>();
        }
        finally
        {
            if (!_isClosed && !_isClosing)
            {
                anchor.IsEnabled = true;
            }
        }

        if (_isClosed || _isClosing)
        {
            return;
        }

        var tracks = allTracks.Where(track => track.Type == trackType).ToList();
        var selectedTrack = tracks.FirstOrDefault(track => track.IsSelected);
        var selectedName = selectedTrack is null
            ? GetTrackMenuText(
                trackType == VideoTrackType.Audio ? "PlayerTrack_NotSelected" : "PlayerTrack_SubtitlesOff",
                trackType == VideoTrackType.Audio ? "Not selected" : "Subtitles off")
            : FormatTrackName(selectedTrack, tracks.IndexOf(selectedTrack) + 1);

        var flyout = CreatePlayerMenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem
        {
            Text = string.Format(
                GetTrackMenuText(
                    trackType == VideoTrackType.Audio
                        ? "PlayerTrack_CurrentAudioFormat"
                        : "PlayerTrack_CurrentSubtitleFormat",
                    trackType == VideoTrackType.Audio ? "Current audio track: {0}" : "Current subtitles: {0}"),
                selectedName),
            IsEnabled = false
        });
        flyout.Items.Add(new MenuFlyoutSeparator());

        if (trackType == VideoTrackType.Subtitle)
        {
            var disableSubtitlesItem = new ToggleMenuFlyoutItem
            {
                Text = GetTrackMenuText("PlayerTrack_DisableSubtitles", "Disable subtitles"),
                IsChecked = selectedTrack is null
            };
            disableSubtitlesItem.Click += async (_, _) =>
                await ExecutePlaybackCommandAsync(() => _player.SelectSubtitleTrackAsync(null));
            flyout.Items.Add(disableSubtitlesItem);
        }

        if (tracks.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = GetTrackMenuText(
                    trackType == VideoTrackType.Audio ? "PlayerTrack_NoAudioTracks" : "PlayerTrack_NoSubtitleTracks",
                    trackType == VideoTrackType.Audio ? "No audio tracks available" : "No subtitles available"),
                IsEnabled = false
            });
        }
        else
        {
            for (var index = 0; index < tracks.Count; index++)
            {
                var track = tracks[index];
                var item = new ToggleMenuFlyoutItem
                {
                    Text = FormatTrackName(track, index + 1),
                    IsChecked = track.IsSelected
                };

                if (trackType == VideoTrackType.Audio)
                {
                    item.Click += async (_, _) =>
                        await ExecutePlaybackCommandAsync(() => _player.SelectAudioTrackAsync(track.Id));
                }
                else
                {
                    item.Click += async (_, _) =>
                        await ExecutePlaybackCommandAsync(() => _player.SelectSubtitleTrackAsync(track.Id));
                }

                flyout.Items.Add(item);
            }
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var addTrackItem = new MenuFlyoutItem
        {
            Text = GetTrackMenuText(
                trackType == VideoTrackType.Audio
                    ? "PlayerTrack_AddAudioTrack"
                    : "PlayerTrack_AddSubtitleTrack",
                trackType == VideoTrackType.Audio ? "Add audio track..." : "Add subtitles...")
        };
        addTrackItem.Click += async (_, _) => await PickAndAddExternalTrackAsync(trackType);
        flyout.Items.Add(addTrackItem);

        flyout.ShowAt(anchor);
    }

    private async Task PickAndAddExternalTrackAsync(VideoTrackType trackType)
    {
        NotifyPlayerInteraction();

        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = trackType == VideoTrackType.Audio
                    ? PickerLocationId.MusicLibrary
                    : PickerLocationId.DocumentsLibrary
            };
            var extensions = trackType == VideoTrackType.Audio
                ? ExternalAudioTrackExtensions
                : ExternalSubtitleTrackExtensions;
            foreach (var extension in extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null || _isClosed || _isClosing)
            {
                return;
            }

            if (trackType == VideoTrackType.Audio)
            {
                await ExecutePlaybackCommandAsync(() => _player.AddAudioTrackAsync(file.Path));
            }
            else
            {
                await ExecutePlaybackCommandAsync(() => _player.AddSubtitleTrackAsync(file.Path));
            }
        }
        catch
        {
        }
        finally
        {
            if (!_isClosed && !_isClosing)
            {
                FocusPlayerSurface();
            }
        }
    }

    private string FormatTrackName(VideoTrackInfo track, int ordinal)
    {
        var fallbackFormat = track.Type == VideoTrackType.Audio
            ? GetTrackMenuText("PlayerTrack_AudioFallbackFormat", "Audio track {0}")
            : GetTrackMenuText("PlayerTrack_SubtitleFallbackFormat", "Subtitle {0}");
        var title = string.IsNullOrWhiteSpace(track.Title)
            ? string.Format(fallbackFormat, ordinal)
            : track.Title.Trim();
        var details = new[] { track.Language, track.Codec }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var detailText = string.Join(" · ", details);

        return string.IsNullOrEmpty(detailText) ? title : $"{title}  ({detailText})";
    }

    private string GetTrackMenuText(string key, string fallback)
    {
        var value = _resourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private async void ScreenshotButton_Click(object sender, RoutedEventArgs args)
    {
        ScreenshotButton.IsEnabled = false;
        MoreScreenshotItem.IsEnabled = false;
        try
        {
            await ExecutePlaybackCommandAsync(() => _player.ScreenshotAsync());
        }
        finally
        {
            if (!_isClosed && !_isClosing)
            {
                ScreenshotButton.IsEnabled = true;
                MoreScreenshotItem.IsEnabled = true;
            }
        }
    }

    private async void MediaInfoButton_Click(object sender, RoutedEventArgs args)
    {
        NotifyPlayerInteraction();
        await ExecutePlaybackCommandAsync(() => _player.ToggleStatisticsAsync());
    }

    private string GetPlayerResourceString(string key, string fallback)
    {
        var value = _resourceLoader.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private async void PictureInPictureButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecutePlaybackCommandAsync(() => _player.SetAlwaysOnTopAsync(!_isAlwaysOnTop));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs args)
    {
        _ = CloseWithAnimationAsync();
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs args)
    {
        _ = CloseWithAnimationAsync();
    }

    private async Task TogglePauseAsync()
    {
        await ExecutePlaybackCommandAsync(() => _player.SetPausedAsync(!_playbackState.IsPaused));
    }

    private async Task ToggleMuteAsync()
    {
        var isEffectivelyMuted = _playbackState.IsMuted || _playbackState.Volume <= 0;
        if (!isEffectivelyMuted)
        {
            await ExecutePlaybackCommandAsync(() => _player.SetMutedAsync(true));
            return;
        }

        if (_playbackState.Volume <= 0)
        {
            await ExecutePlaybackCommandAsync(
                () => _player.SetVolumeAsync(Math.Clamp(_lastAudibleVolume, 1, 100)));
        }

        if (_playbackState.IsMuted)
        {
            await ExecutePlaybackCommandAsync(() => _player.SetMutedAsync(false));
        }
    }

    private async Task ExecutePlaybackCommandAsync(Func<Task> command)
    {
        _ = await TryExecutePlaybackCommandAsync(command);
    }

    private async Task<bool> TryExecutePlaybackCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (!_isClosed && !_isClosing)
            {
                try
                {
                    var focusedElement = FocusManager.GetFocusedElement(RootPanel.XamlRoot);
                    if (focusedElement is null || ReferenceEquals(focusedElement, RootPanel))
                    {
                        FocusPlayerSurface();
                    }
                }
                catch
                {
                }
            }
        }
    }

    private void PlaybackSeekBar_SeekStarted(object? sender, EventArgs args)
    {
        if (!CanSeekCurrentMedia())
        {
            PlaybackSeekBar.CancelSeek();
            return;
        }

        NotifyPlayerInteraction();
        _isSeeking = true;
    }

    private void PlaybackSeekBar_SeekCanceled(object? sender, EventArgs args)
    {
        _isSeeking = false;
    }

    private async void PlaybackSeekBar_SeekCompleted(
        object? sender,
        PlayerSeekBarSeekCompletedEventArgs args)
    {
        await CommitSeekBarValueAsync(args.Value);
    }

    private async Task CommitSeekBarValueAsync(double value)
    {
        if (!_isSeeking)
        {
            return;
        }

        _isSeeking = false;
        if (!CanSeekCurrentMedia() || !double.IsFinite(value))
        {
            return;
        }

        NotifyPlayerInteraction();
        await ExecutePlaybackCommandAsync(() => _player.SeekAbsoluteAsync(value));
    }

    private void PlaybackSeekBar_ValueChanged(
        object? sender,
        PlayerSeekBarValueChangedEventArgs args)
    {
        if (_isUpdatingPlaybackControls || !_isSeeking)
        {
            return;
        }

        CurrentTimeText.Text = FormatTime(args.NewValue);
    }

    private void PlaybackSeekBar_PreviewRequested(
        object? sender,
        PlayerSeekBarPreviewRequestedEventArgs args)
    {
        if (_isClosed ||
            _isClosing ||
            string.IsNullOrWhiteSpace(_currentMediaPath) ||
            _currentVideoPixelWidth <= 0 ||
            _currentVideoPixelHeight <= 0 ||
            _playbackState.Duration <= 0)
        {
            HideSeekPreview();
            return;
        }

        _isSeekPreviewPointerActive = true;
        SeekPreviewTimeText.Text = FormatTime(args.Value);
        PositionSeekPreview(args.HorizontalPosition);
        SeekPreviewPopup.Visibility = Visibility.Visible;

        var bucket = GetSeekPreviewBucket(args.Value, _playbackState.Duration);
        if (_seekPreviewRequestedBucket != bucket)
        {
            CancelSeekPreviewImageLoad();
        }

        if (_seekPreviewCache.TryGetValue(bucket, out var cachedPath) && File.Exists(cachedPath))
        {
            CancelSeekPreviewRequest();
            _seekPreviewRequestedBucket = bucket;
            ShowSeekPreviewImage(cachedPath, bucket);
            return;
        }

        if (_seekPreviewRequestedBucket == bucket &&
            _seekPreviewCancellation is { IsCancellationRequested: false })
        {
            return;
        }

        _seekPreviewRequestedBucket = bucket;
        UpdateSeekPreviewLoadingVisuals();
        ScheduleSeekPreviewGeneration(_currentMediaPath, bucket);
    }

    private void PlaybackSeekBar_PreviewDismissed(object? sender, EventArgs args)
    {
        HideSeekPreview();
    }

    private void PositionSeekPreview(double horizontalPosition)
    {
        if (SeekPreviewLayer.ActualWidth <= 0)
        {
            return;
        }

        var seekBarOrigin = PlaybackSeekBar.TransformToVisual(SeekPreviewLayer).TransformPoint(
            new Windows.Foundation.Point(horizontalPosition, 0));
        const double edgeMargin = 8;
        const double verticalGap = 8;
        var popupWidth = SeekPreviewPopup.Width;
        var popupHeight = SeekPreviewPopup.Height;
        var maximumLeft = Math.Max(edgeMargin, SeekPreviewLayer.ActualWidth - popupWidth - edgeMargin);
        var left = Math.Clamp(seekBarOrigin.X - popupWidth / 2, edgeMargin, maximumLeft);
        var top = Math.Max(edgeMargin, seekBarOrigin.Y - popupHeight - verticalGap);

        Canvas.SetLeft(SeekPreviewPopup, left);
        Canvas.SetTop(SeekPreviewPopup, top);
    }

    private static int GetSeekPreviewBucket(double position, double duration)
    {
        var interval = duration switch
        {
            <= 10 * 60 => 2,
            <= 60 * 60 => 5,
            _ => 10
        };
        var clampedPosition = Math.Clamp(position, 0, duration);
        var snappedPosition = Math.Round(clampedPosition / interval) * interval;
        return (int)Math.Clamp(snappedPosition, 0, Math.Ceiling(duration));
    }

    private void ScheduleSeekPreviewGeneration(string mediaPath, int bucket)
    {
        CancelSeekPreviewRequest();

        var cacheDirectory = _seekPreviewCacheDirectory ??= Path.Combine(
            Path.GetTempPath(),
            "Koukei",
            "SeekPreviews",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDirectory);

        var outputPath = Path.Combine(cacheDirectory, $"{bucket:D8}.jpg");
        var cancellation = new CancellationTokenSource();
        _seekPreviewCancellation = cancellation;
        var requestId = ++_seekPreviewRequestId;
        _ = GenerateSeekPreviewAsync(
            mediaPath,
            outputPath,
            bucket,
            requestId,
            cancellation.Token);
    }

    private async Task GenerateSeekPreviewAsync(
        string mediaPath,
        string outputPath,
        int bucket,
        long requestId,
        CancellationToken cancellationToken)
    {
        string? thumbnailPath = null;
        try
        {
            await Task.Delay(SeekPreviewDebounceDelay, cancellationToken).ConfigureAwait(false);
            thumbnailPath = File.Exists(outputPath)
                ? outputPath
                : await _thumbnailService.CreateVideoThumbnailAtAsync(
                    mediaPath,
                    outputPath,
                    TimeSpan.FromSeconds(bucket),
                    cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            thumbnailPath = null;
        }

        _ = DispatcherQueue.TryEnqueue(() => CompleteSeekPreviewRequest(
            mediaPath,
            bucket,
            requestId,
            thumbnailPath));
    }

    private void CompleteSeekPreviewRequest(
        string mediaPath,
        int bucket,
        long requestId,
        string? thumbnailPath)
    {
        if (requestId != _seekPreviewRequestId)
        {
            return;
        }

        _seekPreviewCancellation?.Dispose();
        _seekPreviewCancellation = null;

        if (_isClosed ||
            !_isSeekPreviewPointerActive ||
            bucket != _seekPreviewRequestedBucket ||
            !string.Equals(mediaPath, _currentMediaPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
        {
            UpdateSeekPreviewLoadingVisuals(isRequestComplete: true);
            return;
        }

        CacheSeekPreview(bucket, thumbnailPath);
        ShowSeekPreviewImage(thumbnailPath, bucket);
    }

    private void CacheSeekPreview(int bucket, string thumbnailPath)
    {
        if (_seekPreviewCache.ContainsKey(bucket))
        {
            _seekPreviewCache[bucket] = thumbnailPath;
            return;
        }

        _seekPreviewCache[bucket] = thumbnailPath;
        _seekPreviewCacheOrder.Enqueue(bucket);
        while (_seekPreviewCache.Count > MaximumSeekPreviewCacheEntries &&
               _seekPreviewCacheOrder.TryDequeue(out var expiredBucket))
        {
            _seekPreviewCache.Remove(expiredBucket);
        }
    }

    private void ShowSeekPreviewImage(string thumbnailPath, int bucket)
    {
        var fullPath = Path.GetFullPath(thumbnailPath);
        if (_seekPreviewDisplayedBucket == bucket &&
            string.Equals(_seekPreviewDisplayedPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
            SeekPreviewImage.Source is not null)
        {
            if (_seekPreviewImageLoadingBucket >= 0)
            {
                CancelSeekPreviewImageLoad();
            }

            SeekPreviewPlaceholderIcon.Visibility = Visibility.Collapsed;
            SetSeekPreviewLoading(false);
            return;
        }

        if (_seekPreviewImageLoadingBucket == bucket &&
            string.Equals(_seekPreviewImageLoadingPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var requestId = ++_seekPreviewImageLoadRequestId;
        _seekPreviewImageLoadingBucket = bucket;
        _seekPreviewImageLoadingPath = fullPath;
        UpdateSeekPreviewLoadingVisuals();
        _ = LoadSeekPreviewImageAsync(fullPath, bucket, requestId);
    }

    private async Task LoadSeekPreviewImageAsync(
        string thumbnailPath,
        int bucket,
        long requestId)
    {
        BitmapImage? bitmap = null;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(thumbnailPath);
            using var stream = await file.OpenReadAsync();
            bitmap = new BitmapImage
            {
                DecodePixelWidth = 320
            };
            await bitmap.SetSourceAsync(stream);
        }
        catch
        {
            // A failed preview must not replace the last successfully displayed frame.
        }

        if (requestId != _seekPreviewImageLoadRequestId ||
            bucket != _seekPreviewRequestedBucket ||
            !_isSeekPreviewPointerActive ||
            _isClosed ||
            _isClosing)
        {
            return;
        }

        _seekPreviewImageLoadingBucket = -1;
        _seekPreviewImageLoadingPath = null;
        if (bitmap is null)
        {
            UpdateSeekPreviewLoadingVisuals(isRequestComplete: true);
            return;
        }

        // The bitmap is fully prepared before replacing Source, so the previous
        // thumbnail remains visible until the next one can be shown atomically.
        SeekPreviewImage.Source = bitmap;
        _seekPreviewDisplayedBucket = bucket;
        _seekPreviewDisplayedPath = thumbnailPath;
        SeekPreviewPlaceholderIcon.Visibility = Visibility.Collapsed;
        SetSeekPreviewLoading(false);
    }

    private void UpdateSeekPreviewLoadingVisuals(bool isRequestComplete = false)
    {
        var hasDisplayedImage = SeekPreviewImage.Source is not null;
        SeekPreviewPlaceholderIcon.Visibility = hasDisplayedImage
            ? Visibility.Collapsed
            : Visibility.Visible;
        SetSeekPreviewLoading(!isRequestComplete && !hasDisplayedImage);
    }

    private void SetSeekPreviewLoading(bool isLoading)
    {
        SeekPreviewLoadingIndicator.IsActive = isLoading;
        SeekPreviewLoadingIndicator.Visibility = isLoading
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HideSeekPreview()
    {
        _isSeekPreviewPointerActive = false;
        CancelSeekPreviewRequest();
        CancelSeekPreviewImageLoad();
        _seekPreviewRequestedBucket = -1;
        _seekPreviewDisplayedBucket = -1;
        _seekPreviewDisplayedPath = null;
        SeekPreviewPopup.Visibility = Visibility.Collapsed;
        SeekPreviewImage.Source = null;
        SeekPreviewPlaceholderIcon.Visibility = Visibility.Visible;
        SetSeekPreviewLoading(false);
    }

    private void CancelSeekPreviewImageLoad()
    {
        ++_seekPreviewImageLoadRequestId;
        _seekPreviewImageLoadingBucket = -1;
        _seekPreviewImageLoadingPath = null;
    }

    private void CancelSeekPreviewRequest()
    {
        _seekPreviewRequestId++;
        _seekPreviewCancellation?.Cancel();
        _seekPreviewCancellation?.Dispose();
        _seekPreviewCancellation = null;
    }

    private void ResetSeekPreviewSession()
    {
        HideSeekPreview();
        _seekPreviewCache.Clear();
        _seekPreviewCacheOrder.Clear();
        _ = ReleaseSeekPreviewResourcesSafelyAsync();

        var cacheDirectory = _seekPreviewCacheDirectory;
        _seekPreviewCacheDirectory = null;
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
            catch
            {
            }
        });
    }

    private async Task ReleaseSeekPreviewResourcesSafelyAsync()
    {
        try
        {
            await _thumbnailService.ReleaseSeekPreviewResourcesAsync().ConfigureAwait(false);
        }
        catch
        {
            // Preview cleanup is best-effort during media transitions and window close.
        }
    }

    private async void PlaybackSeekBar_ChapterInvoked(
        object? sender,
        PlayerSeekBarChapterInvokedEventArgs args)
    {
        if (!CanSeekCurrentMedia() || !double.IsFinite(args.Chapter.StartTime))
        {
            return;
        }

        NotifyPlayerInteraction();
        await ExecutePlaybackCommandAsync(() => _player.SeekAbsoluteAsync(args.Chapter.StartTime));
    }

    private void VolumeSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs args)
    {
        var targetVolume = Math.Clamp(args.NewValue, 0, 100);
        VolumeValueText.Text = $"{targetVolume:0}%";
        if (targetVolume > 0)
        {
            _lastAudibleVolume = Math.Clamp(targetVolume, 1, 100);
        }

        if (!_isPlaybackUiReady || _isUpdatingPlaybackControls)
        {
            return;
        }

        _pendingVideoVolume = targetVolume;
        _ = QueueVideoVolumeAsync(targetVolume);
    }

    private async Task QueueVideoVolumeAsync(
        double volume,
        bool commitImmediately = false)
    {
        var targetVolume = Math.Clamp(volume, 0, 100);
        _pendingVideoVolume = targetVolume;
        var cancellation = new CancellationTokenSource();
        var previousCancellation = _videoVolumeCancellation;
        _videoVolumeCancellation = cancellation;
        previousCancellation?.Cancel();

        try
        {
            if (!commitImmediately)
            {
                await Task.Delay(VideoVolumeDebounceDelay, cancellation.Token);
            }

            await _player.SetVolumeAsync(targetVolume, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            if (ReferenceEquals(_videoVolumeCancellation, cancellation))
            {
                _pendingVideoVolume = null;
            }
        }
        finally
        {
            if (ReferenceEquals(_videoVolumeCancellation, cancellation))
            {
                _videoVolumeCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void HookVideoVolumeSliderInput()
    {
        VolumeSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(VolumeSlider_PointerPressed),
            handledEventsToo: true);
        VolumeSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(VolumeSlider_PointerAdjustmentCompleted),
            handledEventsToo: true);
        VolumeSlider.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(VolumeSlider_PointerAdjustmentCompleted),
            handledEventsToo: true);
        VolumeSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(VolumeSlider_PointerAdjustmentCompleted),
            handledEventsToo: true);
    }

    private void VolumeSlider_PointerPressed(
        object sender,
        PointerRoutedEventArgs args)
    {
        _isVideoVolumeAdjusting = true;
    }

    private void VolumeSlider_PointerAdjustmentCompleted(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (!_isVideoVolumeAdjusting)
        {
            return;
        }

        _isVideoVolumeAdjusting = false;
        var targetVolume = Math.Clamp(VolumeSlider.Value, 0, 100);
        _pendingVideoVolume = targetVolume;
        _ = QueueVideoVolumeAsync(targetVolume, commitImmediately: true);
    }

    private void CancelPendingVideoVolume()
    {
        var cancellation = _videoVolumeCancellation;
        _videoVolumeCancellation = null;
        _pendingVideoVolume = null;
        _isVideoVolumeAdjusting = false;
        cancellation?.Cancel();
    }

    private void PlayerWindow_PlaybackStateChanged(object? sender, VideoPlaybackStateChangedEventArgs args)
    {
        if (_isClosed || _isClosing)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isClosed && !_isClosing)
            {
                ApplyPlaybackState(args.State);
            }
        });
    }

    private async Task RefreshPlaybackStateAsync()
    {
        try
        {
            ApplyPlaybackState(await _player.GetPlaybackStateAsync());
        }
        catch
        {
        }
    }

    private void PlayerWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_isWindowActive)
        {
            var isForegroundRequestPending =
                _foregroundActivationCancellation is not null;
            NativeWindowEffects.StopTaskbarFlash(_hwnd);
            NotifyPlayerInteraction();
            if (isForegroundRequestPending ||
                args.WindowActivationState == WindowActivationState.CodeActivated)
            {
                FocusPlayerSurface();
            }
        }
        else
        {
            _isPointerOverControlBar = false;
            _isPointerOverTitleBarButtons = false;
            _isPointerOverTitleBarDragRegion = false;
            HidePlayerChrome(cancelAutoHide: true);
        }
    }

    private void ControlBar_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _isPointerOverControlBar = true;
        _playerChromeAutoHideCancellation?.Cancel();
        SetPlayerChromeVisible(true);
    }

    private void RootPanel_GotFocus(object sender, RoutedEventArgs args)
    {
        if (!IsControlBarInputSource(args.OriginalSource))
        {
            return;
        }

        _playerChromeAutoHideCancellation?.Cancel();
        SetPlayerChromeVisible(true);
    }

    private void RootPanel_LostFocus(object sender, RoutedEventArgs args)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!HasPlayerChromeKeyboardFocus())
            {
                SchedulePlayerChromeAutoHide();
            }
        });
    }

    private void ControlBar_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        _isPointerOverControlBar = false;
        SchedulePlayerChromeAutoHide();
    }

    private void TitleBar_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        SetTitleBarPointerState(sender, isPointerOver: true);
        _playerChromeAutoHideCancellation?.Cancel();
        SetPlayerChromeVisible(true);
    }

    private void TitleBar_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        SetTitleBarPointerState(sender, isPointerOver: false);
        SchedulePlayerChromeAutoHide();
    }

    private void SetTitleBarPointerState(object sender, bool isPointerOver)
    {
        if (ReferenceEquals(sender, TitleBarButtons))
        {
            _isPointerOverTitleBarButtons = isPointerOver;
        }
        else if (ReferenceEquals(sender, TitleBarDragRegion))
        {
            _isPointerOverTitleBarDragRegion = isPointerOver;
        }
    }

    private bool IsPointerOverTitleBar =>
        _isPointerOverTitleBarButtons || _isPointerOverTitleBarDragRegion;

    private void NotifyPlayerInteraction()
    {
        if (_isClosed || _isClosing || !_isWindowActive)
        {
            return;
        }

        SetPlayerChromeVisible(true);
        SchedulePlayerChromeAutoHide();
    }

    private void SchedulePlayerChromeAutoHide()
    {
        if (_isClosed || _isClosing || !_isWindowActive)
        {
            return;
        }

        var previous = _playerChromeAutoHideCancellation;
        previous?.Cancel();
        previous?.Dispose();
        _playerChromeAutoHideCancellation = null;

        if (_isPointerOverControlBar ||
            IsPointerOverTitleBar ||
            HasOpenPlayerFlyout ||
            HasPlayerChromeKeyboardFocus())
        {
            return;
        }

        var current = new CancellationTokenSource();
        _playerChromeAutoHideCancellation = current;
        _ = HidePlayerChromeAfterDelayAsync(current.Token);
    }

    private async Task HidePlayerChromeAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PlayerChromeAutoHideDelay, cancellationToken);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosed &&
                    !_isClosing &&
                    _isWindowActive &&
                    !_isSeeking &&
                    !_isPointerOverControlBar &&
                    !IsPointerOverTitleBar &&
                    !HasOpenPlayerFlyout &&
                    !HasPlayerChromeKeyboardFocus() &&
                    !cancellationToken.IsCancellationRequested)
                {
                    HidePlayerChrome(cancelAutoHide: false);
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HidePlayerChrome(bool cancelAutoHide)
    {
        if (cancelAutoHide)
        {
            _playerChromeAutoHideCancellation?.Cancel();
        }

        SetPlayerChromeVisible(false);
    }

    private void SetPlayerChromeVisible(bool isVisible)
    {
        if (_isPlayerChromeVisible == isVisible)
        {
            return;
        }

        _isPlayerChromeVisible = isVisible;
        var targetOpacity = isVisible ? 1 : 0;
        CancelPlayerChromeMotion();

        if (isVisible)
        {
            ControlBar.IsHitTestVisible = true;
            TitleBarDragRegion.IsHitTestVisible = true;
            ApplyTitleBarButtonVisibility(isVisible: true);
        }
        else
        {
            ControlBar.IsHitTestVisible = false;
            TitleBarDragRegion.IsHitTestVisible = false;
            ApplyTitleBarButtonVisibility(isVisible: false);
        }

        if (!MotionHelper.AnimationsEnabled)
        {
            SetPlayerChromeOpacityInstant(targetOpacity);
            return;
        }

        var motionCancellation = new CancellationTokenSource();
        _playerChromeMotionCancellation = motionCancellation;
        _ = AnimatePlayerChromeAsync(isVisible, motionCancellation);
    }

    private async Task AnimatePlayerChromeAsync(
        bool isVisible,
        CancellationTokenSource motionCancellation)
    {
        try
        {
            var preset = isVisible ? MotionPreset.Standard : MotionPreset.Fast;
            var controlBarMotion = isVisible
                ? MotionHelper.ShowAsync(
                    ControlBar,
                    preset,
                    MotionDirection.Down,
                    distance: 8,
                    cancellationToken: motionCancellation.Token)
                : MotionHelper.HideAsync(
                    ControlBar,
                    preset,
                    MotionDirection.Down,
                    distance: 8,
                    collapse: false,
                    cancellationToken: motionCancellation.Token);
            var titleBarMotion = isVisible
                ? MotionHelper.ShowAsync(
                    TitleBarDragRegion,
                    preset,
                    MotionDirection.Up,
                    distance: 8,
                    cancellationToken: motionCancellation.Token)
                : MotionHelper.HideAsync(
                    TitleBarDragRegion,
                    preset,
                    MotionDirection.Up,
                    distance: 8,
                    collapse: false,
                    cancellationToken: motionCancellation.Token);
            var titleButtonsMotion = isVisible
                ? MotionHelper.ShowAsync(
                    TitleBarButtons,
                    preset,
                    MotionDirection.Up,
                    distance: 8,
                    cancellationToken: motionCancellation.Token)
                : MotionHelper.HideAsync(
                    TitleBarButtons,
                    preset,
                    MotionDirection.Up,
                    distance: 8,
                    collapse: false,
                    cancellationToken: motionCancellation.Token);
            await Task.WhenAll(controlBarMotion, titleBarMotion, titleButtonsMotion);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_playerChromeMotionCancellation, motionCancellation))
            {
                _playerChromeMotionCancellation = null;
                motionCancellation.Dispose();
            }
        }
    }

    private void CancelPlayerChromeMotion()
    {
        _playerChromeMotionCancellation?.Cancel();
        _playerChromeMotionCancellation?.Dispose();
        _playerChromeMotionCancellation = null;
    }

    private void SetPlayerChromeOpacityInstant(double opacity)
    {
        MotionHelper.SetInstant(ControlBar, opacity, Vector3.Zero);
        MotionHelper.SetInstant(TitleBarDragRegion, opacity, Vector3.Zero);
        MotionHelper.SetInstant(TitleBarButtons, opacity, Vector3.Zero);
    }

    private void ApplyPlaybackState(VideoPlaybackState state)
    {
        _playbackState = state;
        _isPaused = state.IsPaused;
        UpdatePauseOverlay(state.IsPaused);

        _isUpdatingPlaybackControls = true;
        try
        {
            var duration = double.IsFinite(state.Duration) && state.Duration > 0 ? state.Duration : 0;
            var position = double.IsFinite(state.Position) ? Math.Clamp(state.Position, 0, Math.Max(1, duration)) : 0;
            var canSeek = state.IsSeekable && duration > 0 && !_isClosing;

            PlaybackSeekBar.Maximum = Math.Max(1, duration);
            PlaybackSeekBar.IsEnabled = canSeek;
            if (!_isSeeking)
            {
                PlaybackSeekBar.Value = position;
            }

            var engineVolume = Math.Clamp(state.Volume, 0, 100);
            var displayedVolume = engineVolume;
            var pendingVolume = _pendingVideoVolume;
            var hasPendingVolume = pendingVolume.HasValue;
            if (pendingVolume is double requestedVolume)
            {
                if (Math.Abs(engineVolume - requestedVolume) <=
                    VideoVolumeConfirmationTolerance)
                {
                    _pendingVideoVolume = null;
                    hasPendingVolume = false;
                }
                else
                {
                    displayedVolume = requestedVolume;
                }
            }

            if (_isVideoVolumeAdjusting && !hasPendingVolume)
            {
                displayedVolume = VolumeSlider.Value;
            }

            VolumeSlider.Value = displayedVolume;
            VolumeValueText.Text = $"{displayedVolume:0}%";
            if (!hasPendingVolume && !_isVideoVolumeAdjusting && engineVolume > 0)
            {
                _lastAudibleVolume = Math.Clamp(engineVolume, 1, 100);
            }
            CurrentTimeText.Text = FormatTime(_isSeeking ? PlaybackSeekBar.Value : position);
            DurationText.Text = FormatTime(duration);
            SpeedText.Text = $"{state.Speed:0.0}x";
            SetPlayPauseSymbol(state.IsPaused ? Symbol.Play : Symbol.Pause);
            SetVolumeSymbol(state.IsMuted || state.Volume <= 0 ? Symbol.Mute : Symbol.Volume);
            var playPauseDescription = state.IsPaused
                ? GetPlayerResourceString("PlayerWindow_Play", "Play")
                : GetPlayerResourceString("PlayerWindow_Pause", "Pause");
            SetPlayerElementDescription(PlayPauseButton, playPauseDescription);
            AutomationProperties.SetItemStatus(
                PlayPauseButton,
                state.IsPaused
                    ? GetPlayerResourceString("PlayerWindow_StatePaused", "Paused")
                    : GetPlayerResourceString("PlayerWindow_StatePlaying", "Playing"));
            UpdateVolumeButtonDescription();
            UpdateSeekButtonToolTips(duration);
            RewindButton.IsEnabled = canSeek;
            ForwardButton.IsEnabled = canSeek;
            PreviousButton.IsEnabled = _playbackCoordinator.CanPlayPrevious;
            NextButton.IsEnabled = _playbackCoordinator.CanPlayNext;
            MoreRewindItem.IsEnabled = RewindButton.IsEnabled;
            MoreForwardItem.IsEnabled = ForwardButton.IsEnabled;
            MorePreviousItem.IsEnabled = PreviousButton.IsEnabled;
            MoreNextItem.IsEnabled = NextButton.IsEnabled;
            MoreSpeedItem.Text = string.Format(
                GetPlayerResourceString("PlayerWindow_SpeedFormat", "Speed ({0:0.0}x)"),
                state.Speed);
            if (!_isReplacingVideoQueue)
            {
                ApplyVideoQueueSelectionOrClear(state.PlaylistPosition);
            }
        }
        finally
        {
            _isUpdatingPlaybackControls = false;
        }
    }

    private void UpdateVolumeButtonDescription()
    {
        SetPlayerElementDescription(
            MuteButton,
            GetPlayerResourceString(
                "PlayerWindow_OpenVolume",
                "Open volume controls"));
        AutomationProperties.SetItemStatus(MuteButton, string.Empty);
    }

    private void UpdatePauseOverlay(bool isPaused)
    {
        var shouldShow = isPaused &&
            !_isClosed &&
            !_isClosing &&
            !string.IsNullOrWhiteSpace(_currentMediaPath);
        if (_isPauseOverlayVisible == shouldShow)
        {
            return;
        }

        _isPauseOverlayVisible = shouldShow;
        if (shouldShow)
        {
            _ = MotionHelper.ShowAsync(
                PauseOverlayVisual,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0);
            return;
        }

        _ = MotionHelper.HideAsync(
            PauseOverlayVisual,
            MotionPreset.Fast,
            MotionDirection.None,
            distance: 0);
    }

    private void SetPlayPauseSymbol(Symbol symbol)
    {
        if (_requestedPlayPauseSymbol == symbol)
        {
            return;
        }

        _requestedPlayPauseSymbol = symbol;
        _ = MotionHelper.SwapContentAsync(
            PlayPauseIcon,
            () =>
            {
                if (_requestedPlayPauseSymbol == symbol)
                {
                    PlayPauseIcon.Symbol = symbol;
                }
            },
            MotionPreset.Fast);
    }

    private void SetVolumeSymbol(Symbol symbol)
    {
        if (_requestedVolumeSymbol == symbol)
        {
            return;
        }

        _requestedVolumeSymbol = symbol;
        _ = MotionHelper.SwapContentAsync(
            VolumeIcon,
            () =>
            {
                if (_requestedVolumeSymbol == symbol)
                {
                    VolumeIcon.Symbol = symbol;
                }
            },
            MotionPreset.Fast);
    }

    private bool CanSeekCurrentMedia()
    {
        return !_isClosed &&
            !_isClosing &&
            _playbackState.IsSeekable &&
            double.IsFinite(_playbackState.Duration) &&
            _playbackState.Duration > 0;
    }

    private static double GetAdaptiveSeekSeconds(double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0)
        {
            return 10;
        }

        return duration switch
        {
            <= 10 * 60 => 5,
            <= 30 * 60 => 10,
            <= 60 * 60 => 15,
            <= 2 * 60 * 60 => 30,
            _ => 60
        };
    }

    private void UpdateSeekButtonToolTips(double duration)
    {
        var seekSeconds = GetAdaptiveSeekSeconds(duration);
        var rewindDescription = string.Format(
            GetPlayerResourceString("PlayerWindow_RewindFormat", "Rewind {0:0} seconds"),
            seekSeconds);
        var forwardDescription = string.Format(
            GetPlayerResourceString("PlayerWindow_ForwardFormat", "Forward {0:0} seconds"),
            seekSeconds);
        SetPlayerElementDescription(RewindButton, rewindDescription);
        SetPlayerElementDescription(ForwardButton, forwardDescription);
        MoreRewindItem.Text = rewindDescription;
        MoreForwardItem.Text = forwardDescription;
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return "0:00";
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    private bool IsControlBarInputSource(object? source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ControlBar) ||
                ReferenceEquals(current, TitleBarButtons) ||
                ReferenceEquals(current, TitleBarDragRegion))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasPlayerChromeKeyboardFocus()
    {
        return RootPanel.XamlRoot is not null &&
            IsControlBarInputSource(FocusManager.GetFocusedElement(RootPanel.XamlRoot));
    }

    private static string GetMouseButtonKeyName(NativeMpvMouseInputKind kind)
    {
        return kind switch
        {
            NativeMpvMouseInputKind.LeftDown or NativeMpvMouseInputKind.LeftUp => "MBTN_LEFT",
            NativeMpvMouseInputKind.RightDown or NativeMpvMouseInputKind.RightUp => "MBTN_RIGHT",
            NativeMpvMouseInputKind.MiddleDown or NativeMpvMouseInputKind.MiddleUp => "MBTN_MID",
            _ => "MBTN_LEFT"
        };
    }

    private static bool UseD3D11CompositionVideoOutput()
    {
        var configuredOutput = Environment.GetEnvironmentVariable(VideoOutputEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configuredOutput) ||
            string.Equals(configuredOutput, D3D11CompositionVideoOutput, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(configuredOutput, LegacyWidVideoOutput, StringComparison.OrdinalIgnoreCase);
    }

    private bool HandleVideoKeyDown(int virtualKey)
    {
        if (_isClosed)
        {
            return true;
        }

        NotifyPlayerInteraction();
        if (virtualKey == VkTab)
        {
            FocusPlayerControlFromNative(reverse: IsKeyDown(VkShift));
            return true;
        }

        var hasModifier = HasMpvModifierKeyDown();
        if (!hasModifier && virtualKey == VkQ)
        {
            _ = CloseWithAnimationAsync();
            return true;
        }

        if (!hasModifier && virtualKey == VkF)
        {
            _ = SetFullScreenAsync(!_isFullScreen);
            return true;
        }

        if (!hasModifier && virtualKey == VkEscape && _isFullScreen)
        {
            _ = SetFullScreenAsync(false);
            return true;
        }

        var keyName = GetMpvKeyName(virtualKey);
        if (keyName is null)
        {
            return false;
        }

        _ = SendMpvKeyPressAsync(ApplyMpvKeyModifiers(virtualKey, keyName));
        return true;
    }

    private void FocusPlayerControlFromNative(bool reverse)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || _isClosing)
            {
                return;
            }

            SetPlayerChromeVisible(true);
            var focusTarget = reverse ? PlayerCloseButton : PlayPauseButton;
            focusTarget.Focus(FocusState.Keyboard);
        });
    }

    private void HandleVideoMouseInput(NativeMpvMouseInput input)
    {
        if (_isClosed)
        {
            return;
        }

        NotifyPlayerInteraction();
        switch (input.Kind)
        {
            case NativeMpvMouseInputKind.Move:
                SendMpvMouseMove(input.X, input.Y, force: _isVideoMouseButtonDown);
                break;
            case NativeMpvMouseInputKind.Leave:
                _ = SendMpvKeyPressAsync("MOUSE_LEAVE");
                break;
            case NativeMpvMouseInputKind.LeftDown:
                _isVideoMouseButtonDown = true;
                _ = SendMpvMouseButtonAsync(input, "MBTN_LEFT", isPressed: true);
                break;
            case NativeMpvMouseInputKind.LeftUp:
                _isVideoMouseButtonDown = false;
                _ = SendMpvMouseButtonAsync(input, "MBTN_LEFT", isPressed: false);
                break;
            case NativeMpvMouseInputKind.LeftDoubleClick:
                _ = SendMpvMouseKeyPressAsync(input, "MBTN_LEFT_DBL");
                break;
            case NativeMpvMouseInputKind.RightDown:
                _isVideoMouseButtonDown = true;
                _ = SendMpvMouseButtonAsync(input, "MBTN_RIGHT", isPressed: true);
                break;
            case NativeMpvMouseInputKind.RightUp:
                _isVideoMouseButtonDown = false;
                _ = SendMpvMouseButtonAsync(input, "MBTN_RIGHT", isPressed: false);
                break;
            case NativeMpvMouseInputKind.RightDoubleClick:
                _ = SendMpvMouseKeyPressAsync(input, "MBTN_RIGHT_DBL");
                break;
            case NativeMpvMouseInputKind.MiddleDown:
                _isVideoMouseButtonDown = true;
                _ = SendMpvMouseButtonAsync(input, "MBTN_MID", isPressed: true);
                break;
            case NativeMpvMouseInputKind.MiddleUp:
                _isVideoMouseButtonDown = false;
                _ = SendMpvMouseButtonAsync(input, "MBTN_MID", isPressed: false);
                break;
            case NativeMpvMouseInputKind.MiddleDoubleClick:
                _ = SendMpvMouseKeyPressAsync(input, "MBTN_MID_DBL");
                break;
            case NativeMpvMouseInputKind.VerticalWheel:
                ShowVolumeFlyoutFromWheelInput();
                _ = SendMpvMouseKeyPressAsync(input, input.WheelDelta > 0 ? "WHEEL_UP" : "WHEEL_DOWN");
                break;
            case NativeMpvMouseInputKind.HorizontalWheel:
                _ = SendMpvMouseKeyPressAsync(input, input.WheelDelta > 0 ? "WHEEL_RIGHT" : "WHEEL_LEFT");
                break;
        }
    }

    private async Task SetFullScreenAsync(bool isFullScreen)
    {
        try
        {
            await _player.SetFullscreenAsync(isFullScreen);
            ApplyFullScreen(isFullScreen);
        }
        catch
        {
            ApplyFullScreen(isFullScreen);
        }
    }

    private async Task SendMpvKeyPressAsync(string keyName)
    {
        try
        {
            await _player.SendKeyPressAsync(keyName);
        }
        catch
        {
        }
    }

    private void SendMpvMouseMove(int x, int y, bool force)
    {
        var now = Environment.TickCount64;
        if (!force &&
            _lastMouseMoveX == x &&
            _lastMouseMoveY == y)
        {
            return;
        }

        if (!force &&
            now - _lastMouseMoveTicks < MouseMoveIntervalMilliseconds)
        {
            return;
        }

        _lastMouseMoveX = x;
        _lastMouseMoveY = y;
        _lastMouseMoveTicks = now;
        _ = SendMpvMouseMoveAsync(x, y);
    }

    private async Task SendMpvMouseMoveAsync(int x, int y)
    {
        try
        {
            await _player.SendMouseMoveAsync(x, y);
        }
        catch
        {
        }
    }

    private async Task SendMpvMouseButtonAsync(NativeMpvMouseInput input, string keyName, bool isPressed)
    {
        try
        {
            await _player.SendMouseButtonAsync(input.X, input.Y, keyName, isPressed);
        }
        catch
        {
        }
    }

    private async Task SendMpvMouseKeyPressAsync(NativeMpvMouseInput input, string keyName)
    {
        try
        {
            await _player.SendMouseKeyPressAsync(input.X, input.Y, keyName);
        }
        catch
        {
        }
    }

    private static string? GetMpvKeyName(int virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return char.ToLowerInvariant((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return ((char)('0' + virtualKey - 0x60)).ToString();
        }

        return virtualKey switch
        {
            0x08 => "BS",
            0x09 => "TAB",
            0x0D => "ENTER",
            VkEscape => "ESC",
            VkShift or VkControl or VkMenu => null,
            0x20 => "SPACE",
            0x21 => "PGUP",
            0x22 => "PGDWN",
            0x23 => "END",
            0x24 => "HOME",
            0x25 => "LEFT",
            0x26 => "UP",
            0x27 => "RIGHT",
            0x28 => "DOWN",
            0x2D => "INS",
            0x2E => "DEL",
            0x6A => "*",
            0x6B => "+",
            0x6C => ",",
            0x6D => "-",
            0x6E => ".",
            0x6F => "/",
            0xBA => ";",
            0xBB => "+",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => null
        };
    }

    private static string ApplyMpvKeyModifiers(int virtualKey, string keyName)
    {
        var modifiedKeyName = keyName;
        if (virtualKey != VkShift && IsKeyDown(VkShift))
        {
            modifiedKeyName = $"Shift+{modifiedKeyName}";
        }

        if (virtualKey != VkMenu && IsKeyDown(VkMenu))
        {
            modifiedKeyName = $"Alt+{modifiedKeyName}";
        }

        if (virtualKey != VkControl && IsKeyDown(VkControl))
        {
            modifiedKeyName = $"Ctrl+{modifiedKeyName}";
        }

        return modifiedKeyName;
    }

    private void PlayerWindow_WindowStateChanged(object? sender, VideoWindowStateChangedEventArgs args)
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

            if (args.IsFullscreen is { } isFullScreen)
            {
                ApplyFullScreen(isFullScreen);
            }

            if (args.IsAlwaysOnTop is { } isAlwaysOnTop)
            {
                ApplyAlwaysOnTop(isAlwaysOnTop);
            }
        });
    }

    private void PlayerWindow_VideoSizeChanged(object? sender, VideoSizeChangedEventArgs args)
    {
        if (_isClosed ||
            !string.Equals(args.FilePath, _currentMediaPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed ||
                !string.Equals(args.FilePath, _currentMediaPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentVideoPixelWidth = Math.Max(0, args.PixelWidth);
            _currentVideoPixelHeight = Math.Max(0, args.PixelHeight);
            if (!_shouldApplyInitialVideoSize)
            {
                return;
            }

            _shouldApplyInitialVideoSize = false;
            if (_usesD3D11Composition && _currentSwapChain != IntPtr.Zero)
            {
                AudioFallbackPanel.Visibility = Visibility.Collapsed;
            }

            ResizeWindowForVideo(args.PixelWidth, args.PixelHeight);
        });
    }

    private void PlayerWindow_ChaptersChanged(object? sender, VideoChaptersChangedEventArgs args)
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

            var chapters = args.Chapters
                .Select(chapter => new PlayerSeekBarChapter(
                    chapter.StartTime,
                    string.IsNullOrWhiteSpace(chapter.Title)
                        ? string.Format(
                            GetPlayerResourceString("PlayerChapter_DefaultTitle", "Chapter {0}"),
                            chapter.Index + 1)
                        : chapter.Title.Trim()))
                .ToArray();
            PlaybackSeekBar.SetChapters(chapters);
        });
    }

    private void PrepareInitialWindowForVideo(SizeInt32 videoSize)
    {
        if (ResizeWindowForVideo(videoSize.Width, videoSize.Height))
        {
            _preparedInitialVideoSize = videoSize;
        }
    }

    private bool ResizeWindowForVideo(int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || _isFullScreen || _isPictureInPicture)
        {
            return false;
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return false;
        }

        var fitScale = Math.Min(
            1d,
            Math.Min(
                workArea.Width / (double)videoWidth,
                workArea.Height / (double)videoHeight));
        var windowWidth = Math.Max(1, (int)Math.Round(videoWidth * fitScale));
        var windowHeight = Math.Max(1, (int)Math.Round(videoHeight * fitScale));
        var x = workArea.X + (workArea.Width - windowWidth) / 2;
        var y = workArea.Y + (workArea.Height - windowHeight) / 2;

        AppWindow.MoveAndResize(new RectInt32(x, y, windowWidth, windowHeight));
        ResizePlayerSurface();
        if (NativeWindowEffects.IsForegroundWindow(_hwnd))
        {
            NativeWindowEffects.BringToFront(_hwnd);
            FocusPlayerSurface();
        }

        return true;
    }

    private void PlayerWindow_PlaybackClosed(object? sender, EventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(CloseAfterPlaybackClosed);
    }

    private void CloseAfterPlaybackClosed()
    {
        if (_isClosed || _isClosing)
        {
            return;
        }

        _ = CloseWithAnimationAsync();
    }

    private void ApplyFullScreen(bool isFullScreen)
    {
        if (_isFullScreen == isFullScreen &&
            AppWindow.Presenter.Kind == (isFullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped))
        {
            if (!isFullScreen)
            {
                ApplyTransparentTitleBarPresenter();
            }

            FullScreenIcon.Symbol = isFullScreen ? Symbol.BackToWindow : Symbol.FullScreen;
            UpdateFullScreenAccessibility(isFullScreen);
            return;
        }

        if (isFullScreen && _isPictureInPicture)
        {
            ExitPictureInPictureMode();
        }

        _isFullScreen = isFullScreen;
        if (isFullScreen)
        {
            _fullScreenRestoreBounds = new RectInt32(
                AppWindow.Position.X,
                AppWindow.Position.Y,
                AppWindow.Size.Width,
                AppWindow.Size.Height);
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            ApplyTransparentTitleBarPresenter();
            if (_fullScreenRestoreBounds is { } restoreBounds)
            {
                AppWindow.MoveAndResize(restoreBounds);
                _fullScreenRestoreBounds = null;
            }
        }

        ResizePlayerSurface();
        FullScreenIcon.Symbol = isFullScreen ? Symbol.BackToWindow : Symbol.FullScreen;
        UpdateFullScreenAccessibility(isFullScreen);
        FocusPlayerSurface();
    }

    private void ApplyAlwaysOnTop(bool isAlwaysOnTop)
    {
        if (_isAlwaysOnTop == isAlwaysOnTop)
        {
            return;
        }

        _isAlwaysOnTop = isAlwaysOnTop;
        NativeWindowEffects.SetTopMost(_hwnd, isAlwaysOnTop);
        if (isAlwaysOnTop)
        {
            EnterPictureInPictureMode();
            NativeWindowEffects.BringToFront(_hwnd);
        }
        else
        {
            ExitPictureInPictureMode();
        }

        PictureInPictureIcon.Glyph = isAlwaysOnTop
            ? MiniExpand2MirroredGlyph
            : MiniContract2MirroredGlyph;
        UpdatePictureInPictureAccessibility(isAlwaysOnTop);
    }

    private void UpdateFullScreenAccessibility(bool isFullScreen)
    {
        var description = GetPlayerResourceString(
            isFullScreen ? "PlayerWindow_ExitFullScreen" : "PlayerWindow_EnterFullScreen",
            isFullScreen ? "Exit fullscreen" : "Enter fullscreen");
        SetPlayerElementDescription(FullScreenButton, description);
        MoreFullScreenItem.Text = description;
        AutomationProperties.SetItemStatus(
            FullScreenButton,
            GetPlayerResourceString(
                isFullScreen ? "AudioPlayer_StateOn" : "AudioPlayer_StateOff",
                isFullScreen ? "On" : "Off"));
    }

    private void UpdatePictureInPictureAccessibility(bool isAlwaysOnTop)
    {
        var description = GetPlayerResourceString(
            isAlwaysOnTop
                ? "PlayerWindow_ExitPictureInPicture"
                : "PlayerWindow_EnterPictureInPicture",
            isAlwaysOnTop ? "Exit picture in picture" : "Enter picture in picture");
        SetPlayerElementDescription(PictureInPictureButton, description);
        MorePictureInPictureItem.Text = description;
        AutomationProperties.SetItemStatus(
            PictureInPictureButton,
            GetPlayerResourceString(
                isAlwaysOnTop ? "AudioPlayer_StateOn" : "AudioPlayer_StateOff",
                isAlwaysOnTop ? "On" : "Off"));
    }

    private void EnterPictureInPictureMode()
    {
        if (_isPictureInPicture)
        {
            return;
        }

        _restoreBounds = new RectInt32(
            AppWindow.Position.X,
            AppWindow.Position.Y,
            AppWindow.Size.Width,
            AppWindow.Size.Height);
        _isPictureInPicture = true;

        if (_isFullScreen)
        {
            _isFullScreen = false;
            ApplyTransparentTitleBarPresenter();
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.MoveAndResize(GetPictureInPictureBounds(workArea));
        ResizePlayerSurface();
        FocusPlayerSurface();
    }

    private RectInt32 GetPictureInPictureBounds(RectInt32 workArea)
    {
        var rasterScale = RootPanel.XamlRoot?.RasterizationScale is > 0 and var scale
            ? scale
            : 1d;
        var margin = Math.Max(1, (int)Math.Round(16 * rasterScale));
        var availableWidth = Math.Max(1, workArea.Width - margin * 2);
        var availableHeight = Math.Max(1, workArea.Height - margin * 2);

        var minimumWidth = Math.Min(
            availableWidth,
            Math.Max(1, (int)Math.Round(MinimumPictureInPictureWidth * rasterScale)));
        var minimumHeight = Math.Min(
            availableHeight,
            Math.Max(1, (int)Math.Round(MinimumPictureInPictureHeight * rasterScale)));
        var maximumWidth = Math.Min(
            availableWidth,
            Math.Max(minimumWidth, (int)Math.Round(MaximumPictureInPictureWidth * rasterScale)));
        var maximumHeight = Math.Min(
            availableHeight,
            Math.Max(minimumHeight, (int)Math.Round(MaximumPictureInPictureHeight * rasterScale)));
        var preferredWidth = Math.Clamp(
            (int)Math.Round(workArea.Width * 0.32),
            minimumWidth,
            maximumWidth);
        var preferredHeight = Math.Clamp(
            (int)Math.Round(workArea.Height * 0.38),
            minimumHeight,
            maximumHeight);

        var videoAspect = _currentVideoPixelWidth > 0 && _currentVideoPixelHeight > 0
            ? _currentVideoPixelWidth / (double)_currentVideoPixelHeight
            : _restoreBounds is { Height: > 0 } restoreBounds
                ? restoreBounds.Width / (double)restoreBounds.Height
                : 16d / 9d;
        var minimumUsableAspect = minimumWidth / (double)preferredHeight;
        var maximumUsableAspect = preferredWidth / (double)minimumHeight;
        var windowAspect = Math.Clamp(videoAspect, minimumUsableAspect, maximumUsableAspect);

        var width = preferredWidth;
        var height = Math.Max(1, (int)Math.Round(width / windowAspect));
        if (height > preferredHeight)
        {
            height = preferredHeight;
            width = Math.Max(1, (int)Math.Round(height * windowAspect));
        }

        var x = workArea.X + workArea.Width - width - margin;
        var y = workArea.Y + workArea.Height - height - margin;
        return new RectInt32(x, y, width, height);
    }

    private void ExitPictureInPictureMode()
    {
        if (!_isPictureInPicture)
        {
            return;
        }

        _isPictureInPicture = false;
        if (_restoreBounds is { } restoreBounds)
        {
            ApplyTransparentTitleBarPresenter();
            AppWindow.MoveAndResize(restoreBounds);
            _restoreBounds = null;
        }

        ResizePlayerSurface();
        FocusPlayerSurface();
    }

    private void ApplyTransparentTitleBarPresenter()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            ApplyTransparentTitleBar();
            UpdateMaximizeButton();
            return;
        }

        var overlappedPresenter = OverlappedPresenter.Create();
        overlappedPresenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(overlappedPresenter);
        ApplyTransparentTitleBar();
        UpdateMaximizeButton();
    }

    private void ApplyTransparentTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        ApplyTitleBarButtonVisibility(_isPlayerChromeVisible);
    }

    private void ApplyTitleBarButtonVisibility(bool isVisible)
    {
        TitleBarButtons.IsHitTestVisible = isVisible;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs args)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs args)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }

        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        var isMaximized = AppWindow.Presenter is OverlappedPresenter
        {
            State: OverlappedPresenterState.Maximized
        };
        MaximizeIcon.Glyph = isMaximized ? "\uE923" : "\uE922";
        SetPlayerElementDescription(
            MaximizeButton,
            GetPlayerResourceString(
                isMaximized ? "PlayerWindow_Restore" : "PlayerWindow_Maximize",
                isMaximized ? "Restore" : "Maximize"));
    }

    private void PlayerWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        _ = CloseWithAnimationAsync();
    }

    private void PlayerWindow_AppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            ResizePlayerSurface();
            if (RootPanel.ActualWidth > 0 && RootPanel.ActualHeight > 0)
            {
                UpdateControlBarLayout(RootPanel.ActualWidth, RootPanel.ActualHeight);
            }
        }

        if (args.DidPresenterChange || args.DidSizeChange)
        {
            UpdateMaximizeButton();
        }
    }

    private void RootPanel_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateControlBarLayout(args.NewSize.Width, args.NewSize.Height);
    }

    private void UpdateControlBarLayout(double windowWidth, double windowHeight)
    {
        var isWide = windowWidth >= WideControlBarMinWidth;
        var showPlaybackModes = windowWidth >= PlaybackModeControlBarMinWidth;
        var isMediumOrWider = windowWidth >= MediumControlBarMinWidth;
        var isCompactOrWider = windowWidth >= CompactControlBarMinWidth;
        var isNarrowOrWider = windowWidth >= NarrowControlBarMinWidth;
        var useCompactHeight = windowHeight < CompactControlBarHeightThreshold;

        PlaylistButton.Visibility = isMediumOrWider ? Visibility.Visible : Visibility.Collapsed;
        AudioTrackButton.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        SubtitleTrackButton.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        MediaInfoButton.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        ScreenshotButton.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;

        SpeedButton.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        PictureInPictureButton.Visibility = isMediumOrWider ? Visibility.Visible : Visibility.Collapsed;
        ShuffleButton.Visibility = showPlaybackModes ? Visibility.Visible : Visibility.Collapsed;
        RepeatButton.Visibility = showPlaybackModes ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.Visibility = isMediumOrWider ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = isMediumOrWider ? Visibility.Visible : Visibility.Collapsed;
        FullScreenButton.Visibility = isCompactOrWider ? Visibility.Visible : Visibility.Collapsed;

        var timeVisibility = isCompactOrWider ? Visibility.Visible : Visibility.Collapsed;
        CurrentTimeText.Visibility = timeVisibility;
        DurationText.Visibility = timeVisibility;
        RewindButton.Visibility = isNarrowOrWider ? Visibility.Visible : Visibility.Collapsed;
        ForwardButton.Visibility = isNarrowOrWider ? Visibility.Visible : Visibility.Collapsed;
        CenterPlaybackControls.Spacing = isNarrowOrWider ? 10 : 4;

        MorePlaylistItem.Visibility = PlaylistButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreAudioTrackItem.Visibility = AudioTrackButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreSubtitleTrackItem.Visibility = SubtitleTrackButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreMediaInfoItem.Visibility = MediaInfoButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreShuffleItem.Visibility = ShuffleButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreRepeatItem.Visibility = RepeatButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MorePreviousItem.Visibility = PreviousButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreRewindItem.Visibility = RewindButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreForwardItem.Visibility = ForwardButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreNextItem.Visibility = NextButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreSpeedItem.Visibility = SpeedButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreScreenshotItem.Visibility = ScreenshotButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MorePictureInPictureItem.Visibility = PictureInPictureButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreFullScreenItem.Visibility = FullScreenButton.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;

        var hasMediaGroup = !isWide || !isMediumOrWider;
        var hasTransportGroup = !showPlaybackModes || !isMediumOrWider || !isNarrowOrWider;
        var hasActionGroup = !isWide || !isMediumOrWider || !isCompactOrWider;
        MoreMediaSeparator.Visibility = hasMediaGroup && (hasTransportGroup || hasActionGroup)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoreTransportSeparator.Visibility = hasTransportGroup && hasActionGroup
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlayerMoreButton.Visibility = hasMediaGroup || hasTransportGroup || hasActionGroup
            ? Visibility.Visible
            : Visibility.Collapsed;

        var horizontalPadding = isMediumOrWider ? 22 : isNarrowOrWider ? 12 : 8;
        ControlBar.Height = useCompactHeight ? 92 : 112;
        ControlBar.Padding = new Thickness(
            horizontalPadding,
            useCompactHeight ? 4 : 7,
            horizontalPadding,
            useCompactHeight ? 4 : 12);
        SeekRow.Height = new GridLength(useCompactHeight ? 30 : 34);
        ControlsRow.Height = new GridLength(useCompactHeight ? 50 : 59);
    }

    private Task CloseWithAnimationAsync()
    {
        if (_closeTask is not null)
        {
            return _closeTask;
        }

        if (_isClosed || _isClosing)
        {
            return Task.CompletedTask;
        }

        _closeTask = CloseWithAnimationSerializedAsync();
        return _closeTask;
    }

    private async Task CloseWithAnimationSerializedAsync()
    {
        await s_videoOperationGate.WaitAsync();
        try
        {
            await CloseWithAnimationCoreUnderGateAsync();
        }
        finally
        {
            s_videoOperationGate.Release();
        }
    }

    private async Task CloseWithAnimationCoreUnderGateAsync()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosing = true;
        UpdatePauseOverlay(isPaused: false);
        CancelLoadingFeedback();
        CancelVideoQueueThumbnailLoading();
        PlaybackSeekBar.CancelSeek();
        _isSeeking = false;
        CancelForegroundActivationRequest(stopTaskbarFlash: true);
        ResetSeekPreviewSession();
        _playerChromeAutoHideCancellation?.Cancel();
        _playerChromeAutoHideCancellation?.Dispose();
        _playerChromeAutoHideCancellation = null;

        lock (_compositionResizeLock)
        {
            _hasPendingCompositionResize = false;
            _compositionResizeCancellation?.Cancel();
        }

        // Stop accepting native-pointer events before the mpv context is destroyed.
        _player.SwapChainChanged -= PlayerWindow_SwapChainChanged;
        if (_usesD3D11Composition && _currentSwapChain != IntPtr.Zero)
        {
            SwapChainPanelHost.SetSwapChain(VideoPanel, IntPtr.Zero);
            _currentSwapChain = IntPtr.Zero;
        }

        try
        {
            if (MotionHelper.AnimationsEnabled)
            {
                await NativeWindowEffects.FadeAsync(_hwnd, 255, 0, TimeSpan.FromMilliseconds(120));
            }
            await _player.CloseAsync();
        }
        finally
        {
            _allowClose = true;
            Close();
            await _closedCompletion.Task;
        }
    }

    private void PlayerWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        CancelForegroundActivationRequest(stopTaskbarFlash: true);
        CancelLoadingFeedback();
        CancelVideoQueueThumbnailLoading();
        CancelPendingVideoVolume();
        // Continuations are asynchronous and return to the UI dispatcher, so the
        // close gate is released only after this event handler has unwound.
        _closedCompletion.TrySetResult(true);
        ResetSeekPreviewSession();
        _playerChromeAutoHideCancellation?.Cancel();
        _playerChromeAutoHideCancellation?.Dispose();
        _playerChromeAutoHideCancellation = null;
        _openPlayerFlyouts.Clear();
        CancelPlayerChromeMotion();
        lock (_compositionResizeLock)
        {
            _hasPendingCompositionResize = false;
            _isCompositionResizeWorkerRunning = false;
            _compositionResizeCancellation?.Cancel();
            _compositionResizeCancellation?.Dispose();
            _compositionResizeCancellation = null;
        }

        Activated -= PlayerWindow_Activated;
        AppWindow.Changed -= PlayerWindow_AppWindowChanged;
        AppWindow.Closing -= PlayerWindow_Closing;
        Closed -= PlayerWindow_Closed;
        ThemeHelper.ThemeChanged -= PlayerWindow_ThemeChanged;
        RootPanel.ActualThemeChanged -= PlayerWindow_RootPanelActualThemeChanged;
        UnsubscribeSystemAppearanceEvents();
        RootPanel.KeyDown -= PlayerWindow_RootPanelKeyDown;
        _player.WindowStateChanged -= PlayerWindow_WindowStateChanged;
        _player.SwapChainChanged -= PlayerWindow_SwapChainChanged;
        _player.PlaybackStateChanged -= PlayerWindow_PlaybackStateChanged;
        _player.VideoSizeChanged -= PlayerWindow_VideoSizeChanged;
        _player.ChaptersChanged -= PlayerWindow_ChaptersChanged;
        _player.PlaybackClosed -= PlayerWindow_PlaybackClosed;
        _playbackCoordinator.PlaybackQueueChanged -= PlaybackCoordinator_PlaybackQueueChanged;
        _playbackCoordinator.AudioPlaybackOptionsChanged -=
            PlaybackCoordinator_PlaybackOptionsChanged;

        if (_usesD3D11Composition)
        {
            SwapChainPanelHost.SetSwapChain(VideoPanel, IntPtr.Zero);
            _currentSwapChain = IntPtr.Zero;
        }

        if (_videoHwnd != IntPtr.Zero)
        {
            NativeMpvHostWindow.Destroy(_videoHwnd);
            _videoHwnd = IntPtr.Zero;
        }

        if (ReferenceEquals(s_current, this))
        {
            s_pendingVideoQueue.Clear();
            s_pendingVideoQueue.AddRange(
                VideoQueue.Select(item => (item.Title, item.FilePath)));
            ReleaseVideoQueueThumbnails();
            s_current = null;
            VideoQueueChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static bool HasMpvModifierKeyDown()
    {
        return IsKeyDown(VkShift) || IsKeyDown(VkControl) || IsKeyDown(VkMenu);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
