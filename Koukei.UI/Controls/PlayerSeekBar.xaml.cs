using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Windows.UI;

namespace Koukei.UI.Controls;

public sealed partial class PlayerSeekBar : UserControl
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(PlayerSeekBar),
        new PropertyMetadata(0d, OnMinimumPropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(PlayerSeekBar),
        new PropertyMetadata(1d, OnMaximumPropertyChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(PlayerSeekBar),
        new PropertyMetadata(0d, OnValuePropertyChanged));

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(double), typeof(PlayerSeekBar),
        new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty TrackVerticalOffsetProperty = DependencyProperty.Register(
        nameof(TrackVerticalOffset), typeof(double), typeof(PlayerSeekBar),
        new PropertyMetadata(0d, OnTrackVerticalOffsetPropertyChanged));

    private const double ThumbSize = 18;
    private const double TrackHeight = 4;
    private const double ChapterMarkerHeight = 10;
    private const double ChapterMarkerWidth = 2;
    private const double ChapterHitTargetSize = 20;
    private const int MaximumRenderedChapters = 500;
    private static readonly TimeSpan PreviewDismissDelay = TimeSpan.FromMilliseconds(80);
    private IReadOnlyList<PlayerSeekBarChapter> _chapters = Array.Empty<PlayerSeekBarChapter>();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _previewDismissTimer;
    private bool _isSeeking;
    private Color _chapterMarkerColor = Color.FromArgb(210, 255, 255, 255);
    private Color _chapterMarkerHoverColor = Color.FromArgb(255, 255, 130, 50);
    private double _maximum = 1;
    private double _minimum;
    private double _trackStart;
    private double _trackWidth;
    private double _value;

    public PlayerSeekBar()
    {
        InitializeComponent();

        Thumb.Width = ThumbSize;
        Thumb.Height = ThumbSize;
        IsEnabledChanged += PlayerSeekBar_IsEnabledChanged;
        _previewDismissTimer = DispatcherQueue.CreateTimer();
        _previewDismissTimer.Interval = PreviewDismissDelay;
        _previewDismissTimer.IsRepeating = false;
        _previewDismissTimer.Tick += PreviewDismissTimer_Tick;
    }

    public event EventHandler? SeekStarted;

    public event EventHandler<PlayerSeekBarSeekCompletedEventArgs>? SeekCompleted;

    public event EventHandler? SeekCanceled;

    public event EventHandler<PlayerSeekBarValueChangedEventArgs>? ValueChanged;

    public event EventHandler<PlayerSeekBarChapterInvokedEventArgs>? ChapterInvoked;

    public event EventHandler<PlayerSeekBarPreviewRequestedEventArgs>? PreviewRequested;

    public event EventHandler? PreviewDismissed;

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public double TrackVerticalOffset
    {
        get => (double)GetValue(TrackVerticalOffsetProperty);
        set => SetValue(TrackVerticalOffsetProperty, value);
    }

    public bool IsSeeking => _isSeeking;

    internal double EffectiveSmallChange =>
        double.IsFinite(SmallChange) && SmallChange > 0
            ? SmallChange
            : Math.Max(1, (_maximum - _minimum) / 100);

    public void ApplyPalette(
        Color trackColor,
        Color progressColor,
        Color thumbColor,
        Color chapterMarkerColor,
        Color chapterMarkerHoverColor)
    {
        TrackBackground.Background = new SolidColorBrush(trackColor);
        ProgressTrack.Background = new SolidColorBrush(progressColor);
        Thumb.Fill = new SolidColorBrush(thumbColor);
        _chapterMarkerColor = chapterMarkerColor;
        _chapterMarkerHoverColor = chapterMarkerHoverColor;
        UpdateChapterMarkers();
    }

    public void SetChapters(IEnumerable<PlayerSeekBarChapter>? chapters)
    {
        _chapters = chapters?
            .Where(static chapter => double.IsFinite(chapter.StartTime))
            .OrderBy(static chapter => chapter.StartTime)
            .Take(MaximumRenderedChapters)
            .ToArray() ?? Array.Empty<PlayerSeekBarChapter>();
        UpdateChapterMarkers();
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new PlayerSeekBarAutomationPeer(this);
    }

    internal void SetValueFromAutomation(double value)
    {
        if (!IsEnabled)
        {
            throw new ElementNotEnabledException();
        }

        if (!double.IsFinite(value) || value < _minimum || value > _maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        SeekStarted?.Invoke(this, EventArgs.Empty);
        SetPlaybackValue(value);
        SeekCompleted?.Invoke(this, new PlayerSeekBarSeekCompletedEventArgs(_value));
    }

    private static void OnMinimumPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var seekBar = (PlayerSeekBar)dependencyObject;
        var minimum = args.NewValue is double value && double.IsFinite(value) ? value : 0;
        if (args.NewValue is not double rawValue ||
            !double.IsFinite(rawValue) ||
            Math.Abs(rawValue - minimum) >= 0.001)
        {
            seekBar.SetValue(MinimumProperty, minimum);
            return;
        }

        var oldMinimum = seekBar._minimum;
        seekBar._minimum = minimum;
        if (seekBar._maximum <= minimum)
        {
            seekBar.Maximum = minimum + 1;
        }

        seekBar.SetPlaybackValue(seekBar._value);
        seekBar.UpdateChapterMarkers();
        seekBar.RaiseRangeAutomationPropertyChanged(
            RangeValuePatternIdentifiers.MinimumProperty,
            oldMinimum,
            minimum);
    }

    private static void OnMaximumPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var seekBar = (PlayerSeekBar)dependencyObject;
        var maximum = args.NewValue is double value && double.IsFinite(value) && value > seekBar._minimum
            ? value
            : seekBar._minimum + 1;
        if (args.NewValue is not double rawValue ||
            !double.IsFinite(rawValue) ||
            Math.Abs(rawValue - maximum) >= 0.001)
        {
            seekBar.SetValue(MaximumProperty, maximum);
            return;
        }

        var oldMaximum = seekBar._maximum;
        seekBar._maximum = maximum;
        seekBar.SetPlaybackValue(seekBar._value);
        seekBar.UpdateChapterMarkers();
        seekBar.RaiseRangeAutomationPropertyChanged(
            RangeValuePatternIdentifiers.MaximumProperty,
            oldMaximum,
            maximum);
    }

    private static void OnValuePropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var seekBar = (PlayerSeekBar)dependencyObject;
        var requestedValue = args.NewValue is double value ? value : seekBar._minimum;
        var normalized = double.IsFinite(requestedValue)
            ? Math.Clamp(requestedValue, seekBar._minimum, seekBar._maximum)
            : seekBar._minimum;
        if (!double.IsFinite(requestedValue) ||
            Math.Abs(requestedValue - normalized) >= 0.0001)
        {
            seekBar.SetValue(ValueProperty, normalized);
            return;
        }

        var oldValue = seekBar._value;
        seekBar._value = normalized;
        seekBar.UpdatePlaybackVisuals();
        if (Math.Abs(oldValue - normalized) < 0.0001)
        {
            return;
        }

        seekBar.ValueChanged?.Invoke(
            seekBar,
            new PlayerSeekBarValueChangedEventArgs(oldValue, normalized));
        seekBar.RaiseRangeAutomationPropertyChanged(
            RangeValuePatternIdentifiers.ValueProperty,
            oldValue,
            normalized);
    }

    private static void OnTrackVerticalOffsetPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var seekBar = (PlayerSeekBar)dependencyObject;
        var offset = args.NewValue is double value && double.IsFinite(value) ? value : 0;
        if (args.NewValue is not double rawValue ||
            !double.IsFinite(rawValue) ||
            Math.Abs(rawValue - offset) >= 0.001)
        {
            seekBar.SetValue(TrackVerticalOffsetProperty, offset);
            return;
        }

        seekBar.UpdateGeometry();
        seekBar.UpdateChapterMarkers();
    }

    private void SetPlaybackValue(double value)
    {
        var normalized = double.IsFinite(value)
            ? Math.Clamp(value, _minimum, _maximum)
            : _minimum;
        if (Math.Abs(_value - normalized) < 0.0001)
        {
            UpdatePlaybackVisuals();
            return;
        }

        SetValue(ValueProperty, normalized);
    }

    private void RaiseRangeAutomationPropertyChanged(
        AutomationProperty property,
        double oldValue,
        double newValue)
    {
        if (FrameworkElementAutomationPeer.FromElement(this) is PlayerSeekBarAutomationPeer peer)
        {
            peer.RaisePropertyChangedEvent(property, oldValue, newValue);
        }
    }

    private void InputSurface_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!IsEnabled || IsChapterMarkerSource(args.OriginalSource))
        {
            return;
        }

        var point = args.GetCurrentPoint(InputSurface);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        CancelPreviewDismiss();
        Focus(FocusState.Pointer);
        _isSeeking = true;
        SeekStarted?.Invoke(this, EventArgs.Empty);
        InputSurface.CapturePointer(args.Pointer);
        UpdateValueFromPointer(point.Position.X);
        args.Handled = true;
    }

    private void InputSurface_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        var pointerPosition = args.GetCurrentPoint(InputSurface).Position;
        var isWithinInputSurface = IsWithinInputSurface(pointerPosition);
        if (isWithinInputSurface || _isSeeking)
        {
            CancelPreviewDismiss();
        }
        else
        {
            SchedulePreviewDismiss();
            return;
        }

        var pointerX = pointerPosition.X;
        RaisePreviewRequested(pointerX);
        if (!_isSeeking)
        {
            return;
        }

        UpdateValueFromPointer(pointerX);
        args.Handled = true;
    }

    private void InputSurface_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        CancelPreviewDismiss();
        RaisePreviewRequested(args.GetCurrentPoint(InputSurface).Position.X);
    }

    private void InputSurface_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!_isSeeking)
        {
            SchedulePreviewDismiss();
        }
    }

    private void InputSurface_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_isSeeking)
        {
            return;
        }

        var pointerPosition = args.GetCurrentPoint(InputSurface).Position;
        UpdateValueFromPointer(pointerPosition.X);
        CompleteSeek();
        InputSurface.ReleasePointerCapture(args.Pointer);
        if (!IsWithinInputSurface(pointerPosition))
        {
            SchedulePreviewDismiss();
        }

        args.Handled = true;
    }

    private void InputSurface_PointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        CancelSeek();
        SchedulePreviewDismiss();
    }

    private void InputSurface_PointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        CancelSeek();
        if (IsWithinInputSurface(args.GetCurrentPoint(InputSurface).Position))
        {
            CancelPreviewDismiss();
        }
        else
        {
            SchedulePreviewDismiss();
        }
    }

    public void CancelSeek()
    {
        if (!_isSeeking)
        {
            return;
        }

        _isSeeking = false;
        SeekCanceled?.Invoke(this, EventArgs.Empty);
    }

    private void CompleteSeek()
    {
        if (!_isSeeking)
        {
            return;
        }

        _isSeeking = false;
        SeekCompleted?.Invoke(this, new PlayerSeekBarSeekCompletedEventArgs(_value));
    }

    private void UpdateValueFromPointer(double pointerX)
    {
        if (_trackWidth <= 0)
        {
            return;
        }

        var progress = Math.Clamp((pointerX - _trackStart) / _trackWidth, 0, 1);
        SetPlaybackValue(_minimum + progress * (_maximum - _minimum));
    }

    private void RaisePreviewRequested(double pointerX)
    {
        if (!IsEnabled || _trackWidth <= 0)
        {
            return;
        }

        var trackX = Math.Clamp(pointerX, _trackStart, _trackStart + _trackWidth);
        var progress = Math.Clamp((trackX - _trackStart) / _trackWidth, 0, 1);
        var value = _minimum + progress * (_maximum - _minimum);
        PreviewRequested?.Invoke(
            this,
            new PlayerSeekBarPreviewRequestedEventArgs(value, trackX));
    }

    private void PlayerSeekBar_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!IsEnabled)
        {
            return;
        }

        var step = EffectiveSmallChange;
        var newValue = args.Key switch
        {
            VirtualKey.Left => _value - step,
            VirtualKey.Right => _value + step,
            VirtualKey.Home => _minimum,
            VirtualKey.End => _maximum,
            _ => double.NaN
        };
        if (!double.IsFinite(newValue))
        {
            return;
        }

        SeekStarted?.Invoke(this, EventArgs.Empty);
        SetPlaybackValue(newValue);
        SeekCompleted?.Invoke(this, new PlayerSeekBarSeekCompletedEventArgs(_value));
        args.Handled = true;
    }

    private void InputSurface_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateGeometry();
        UpdateChapterMarkers();
    }

    private void PlayerSeekBar_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        Opacity = IsEnabled ? 1 : 0.55;
        if (!IsEnabled)
        {
            CancelPreviewDismiss();
            CancelSeek();
            PreviewDismissed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SchedulePreviewDismiss()
    {
        _previewDismissTimer.Stop();
        _previewDismissTimer.Start();
    }

    private void CancelPreviewDismiss()
    {
        _previewDismissTimer.Stop();
    }

    private void PreviewDismissTimer_Tick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        PreviewDismissed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsWithinInputSurface(Windows.Foundation.Point position)
    {
        return position.X >= 0 &&
            position.X <= InputSurface.ActualWidth &&
            position.Y >= 0 &&
            position.Y <= InputSurface.ActualHeight;
    }

    private void UpdatePlaybackVisuals()
    {
        if (_trackWidth <= 0)
        {
            return;
        }

        var range = _maximum - _minimum;
        var progress = range > 0 ? Math.Clamp((_value - _minimum) / range, 0, 1) : 0;
        var centerX = GetTrackX(progress);
        ProgressTrack.Width = Math.Max(0, centerX - _trackStart);
        Canvas.SetLeft(Thumb, SnapToPhysicalPixel(centerX - ThumbSize / 2));
    }

    private void UpdateChapterMarkers()
    {
        ChapterCanvas.Children.Clear();

        var duration = _maximum - _minimum;
        if (duration <= 0 || _trackWidth <= 0 || _chapters.Count == 0)
        {
            return;
        }

        var canvasHeight = Math.Max(ChapterHitTargetSize, ChapterCanvas.Height);
        var centerY = GetTrackCenterY(canvasHeight);
        foreach (var chapter in _chapters)
        {
            if (chapter.StartTime <= _minimum + 0.001 || chapter.StartTime >= _maximum)
            {
                continue;
            }

            var line = new Border
            {
                Width = ChapterMarkerWidth,
                Height = ChapterMarkerHeight,
                Background = new SolidColorBrush(_chapterMarkerColor),
                CornerRadius = new CornerRadius(1),
                IsHitTestVisible = false
            };
            var marker = new Grid
            {
                Width = ChapterHitTargetSize,
                Height = ChapterHitTargetSize,
                Background = new SolidColorBrush(Colors.Transparent),
                Tag = chapter
            };

            ToolTipService.SetToolTip(marker, $"{chapter.Title}  {FormatTime(chapter.StartTime)}");
            marker.PointerEntered += (_, _) =>
                line.Background = new SolidColorBrush(_chapterMarkerHoverColor);
            marker.PointerExited += (_, _) =>
                line.Background = new SolidColorBrush(_chapterMarkerColor);
            marker.Tapped += (_, args) =>
            {
                args.Handled = true;
                ChapterInvoked?.Invoke(this, new PlayerSeekBarChapterInvokedEventArgs(chapter));
            };

            var progress = (chapter.StartTime - _minimum) / duration;
            var centerX = GetTrackX(progress);
            Canvas.SetLeft(line, SnapToPhysicalPixel(centerX - ChapterMarkerWidth / 2));
            Canvas.SetTop(line, SnapToPhysicalPixel(centerY - line.Height / 2));
            Canvas.SetLeft(marker, SnapToPhysicalPixel(centerX - marker.Width / 2));
            Canvas.SetTop(marker, Math.Clamp(centerY - marker.Height / 2, 0, canvasHeight - marker.Height));
            ChapterCanvas.Children.Add(line);
            ChapterCanvas.Children.Add(marker);
        }
    }

    private void UpdateGeometry()
    {
        var width = Math.Max(0, InputSurface.ActualWidth);
        var height = Math.Max(0, InputSurface.ActualHeight);
        VisualCanvas.Width = width;
        VisualCanvas.Height = height;
        ChapterCanvas.Width = width;
        ChapterCanvas.Height = height;

        _trackStart = SnapToPhysicalPixel(Math.Min(ThumbSize / 2, width / 2));
        var trackEnd = SnapToPhysicalPixel(Math.Max(_trackStart, width - ThumbSize / 2));
        _trackWidth = Math.Max(0, trackEnd - _trackStart);
        var centerY = GetTrackCenterY(height);
        var trackTop = SnapToPhysicalPixel(centerY - TrackHeight / 2);

        TrackBackground.Width = _trackWidth;
        TrackBackground.Height = TrackHeight;
        Canvas.SetLeft(TrackBackground, _trackStart);
        Canvas.SetTop(TrackBackground, trackTop);

        ProgressTrack.Height = TrackHeight;
        Canvas.SetLeft(ProgressTrack, _trackStart);
        Canvas.SetTop(ProgressTrack, trackTop);
        Canvas.SetTop(Thumb, SnapToPhysicalPixel(centerY - ThumbSize / 2));
        UpdatePlaybackVisuals();
    }

    private double GetTrackCenterY(double height)
    {
        if (height <= 0)
        {
            return 0;
        }

        var minimumCenter = Math.Min(ThumbSize / 2, height / 2);
        var maximumCenter = Math.Max(minimumCenter, height - ThumbSize / 2);
        return Math.Clamp(height / 2 + TrackVerticalOffset, minimumCenter, maximumCenter);
    }

    private double GetTrackX(double progress)
    {
        return SnapToPhysicalPixel(_trackStart + Math.Clamp(progress, 0, 1) * _trackWidth);
    }

    private double SnapToPhysicalPixel(double value)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        return scale > 0
            ? Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale
            : value;
    }

    private bool IsChapterMarkerSource(object? source)
    {
        for (var current = source as DependencyObject;
             current is not null && !ReferenceEquals(current, this);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Tag: PlayerSeekBarChapter })
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}

internal sealed class PlayerSeekBarAutomationPeer(PlayerSeekBar owner)
    : FrameworkElementAutomationPeer(owner), IRangeValueProvider
{
    private PlayerSeekBar SeekBar => (PlayerSeekBar)Owner;

    public bool IsReadOnly => !SeekBar.IsEnabled;

    public double LargeChange => Math.Max(1, (Maximum - Minimum) / 10);

    public double SmallChange => SeekBar.EffectiveSmallChange;

    public double Maximum => SeekBar.Maximum;

    public double Minimum => SeekBar.Minimum;

    public double Value => SeekBar.Value;

    public void SetValue(double value)
    {
        SeekBar.SetValueFromAutomation(value);
    }

    protected override string GetClassNameCore() => nameof(PlayerSeekBar);

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.Slider;

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue
            ? this
            : base.GetPatternCore(patternInterface);
}

public sealed record PlayerSeekBarChapter(double StartTime, string Title);

public sealed class PlayerSeekBarSeekCompletedEventArgs(double value) : EventArgs
{
    public double Value { get; } = value;
}

public sealed class PlayerSeekBarValueChangedEventArgs(double oldValue, double newValue) : EventArgs
{
    public double OldValue { get; } = oldValue;

    public double NewValue { get; } = newValue;
}

public sealed class PlayerSeekBarChapterInvokedEventArgs(PlayerSeekBarChapter chapter) : EventArgs
{
    public PlayerSeekBarChapter Chapter { get; } = chapter;
}

public sealed class PlayerSeekBarPreviewRequestedEventArgs(
    double value,
    double horizontalPosition) : EventArgs
{
    public double Value { get; } = value;

    public double HorizontalPosition { get; } = horizontalPosition;
}
