using System;
using System.Threading;
using System.Threading.Tasks;
using Koukei.UI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Koukei.UI.Controls;

public sealed class PageStatusBar : ContentControl
{
    private static readonly TimeSpan DefaultTransientDuration = TimeSpan.FromSeconds(4);

    private readonly TextBlock _statusText;
    private CancellationTokenSource? _transientCancellation;
    private string _summaryText = string.Empty;
    private string? _overrideText;
    private string _requestedText = string.Empty;
    private long _displayVersion;
    private bool _isBusyOverride;
    private bool _isLoaded;

    public PageStatusBar()
    {
        _statusText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        if (Application.Current.Resources.TryGetValue(
                "KoukeiMetadataTextStyle",
                out var resource) &&
            resource is Style style)
        {
            _statusText.Style = style;
        }

        AutomationProperties.SetLiveSetting(_statusText, AutomationLiveSetting.Polite);
        Content = _statusText;

        Loaded += PageStatusBar_Loaded;
        Unloaded += PageStatusBar_Unloaded;
        SizeChanged += PageStatusBar_SizeChanged;
    }

    internal void SetSummary(string text)
    {
        _summaryText = Normalize(text);
        if (_overrideText is null)
        {
            SetDisplayedText(_summaryText);
        }
    }

    internal void ShowBusy(string text)
    {
        var animate = _overrideText is null || _transientCancellation is not null;
        CancelTransientRestore();
        _isBusyOverride = true;
        _overrideText = Normalize(text);
        SetDisplayedText(_overrideText, animate);
    }

    internal void ShowTransient(string text, TimeSpan? duration = null)
    {
        CancelTransientRestore();
        _isBusyOverride = false;
        _overrideText = Normalize(text);
        SetDisplayedText(_overrideText);

        var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;
        _transientCancellation = cancellation;
        _ = RestoreSummaryAfterDelayAsync(
            cancellation,
            cancellationToken,
            duration ?? DefaultTransientDuration);
    }

    internal void ClearOverride()
    {
        CancelTransientRestore();
        _isBusyOverride = false;
        _overrideText = null;
        SetDisplayedText(_summaryText);
    }

    private static string Normalize(string? text) => text?.Trim() ?? string.Empty;

    private void PageStatusBar_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateResponsivePadding(ActualWidth);
        SetDisplayedText(_overrideText ?? _summaryText);
    }

    private void PageStatusBar_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        if (!_isBusyOverride)
        {
            CancelTransientRestore();
            _overrideText = null;
        }

        _displayVersion++;
        var currentText = _overrideText ?? _summaryText;
        _requestedText = currentText;
        AutomationProperties.SetName(_statusText, currentText);
        MotionHelper.SetVisibleInstant(_statusText, isVisible: true);
        _statusText.Text = currentText;
    }

    private void PageStatusBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsivePadding(e.NewSize.Width);
    }

    private void UpdateResponsivePadding(double width)
    {
        var breakpoint = ResponsiveLayout.Resolve(width);
        var pagePadding = ResponsiveLayout.GetPagePadding(breakpoint);
        Padding = new Thickness(pagePadding.Left, 8, pagePadding.Right, 8);
    }

    private void CancelTransientRestore()
    {
        var cancellation = _transientCancellation;
        _transientCancellation = null;
        cancellation?.Cancel();
    }

    private async Task RestoreSummaryAfterDelayAsync(
        CancellationTokenSource cancellation,
        CancellationToken cancellationToken,
        TimeSpan duration)
    {
        try
        {
            await Task.Delay(duration, cancellationToken);
            if (!ReferenceEquals(_transientCancellation, cancellation))
            {
                return;
            }

            _transientCancellation = null;
            _isBusyOverride = false;
            _overrideText = null;
            SetDisplayedText(_summaryText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void SetDisplayedText(string text, bool animate = true)
    {
        if (string.Equals(_requestedText, text, StringComparison.Ordinal))
        {
            return;
        }

        _requestedText = text;
        var displayVersion = ++_displayVersion;
        AutomationProperties.SetName(_statusText, text);
        if (!animate || !_isLoaded || !MotionHelper.AnimationsEnabled)
        {
            MotionHelper.SetVisibleInstant(_statusText, isVisible: true);
            _statusText.Text = text;
            return;
        }

        _ = MotionHelper.SwapContentAsync(
            _statusText,
            () =>
            {
                if (displayVersion == _displayVersion)
                {
                    _statusText.Text = text;
                }
            },
            MotionPreset.Fast);
    }
}
