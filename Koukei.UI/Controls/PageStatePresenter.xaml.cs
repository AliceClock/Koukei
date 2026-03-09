using System;
using System.Windows.Input;
using Koukei.UI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Threading;
using System.Threading.Tasks;

namespace Koukei.UI.Controls;

public sealed class PageStatePresenter : UserControl
{
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(object), typeof(PageStatePresenter),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PageViewState), typeof(PageStatePresenter),
        new PropertyMetadata(PageViewState.Content, OnStatePropertyChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageStatePresenter),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PageStatePresenter),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty RetryTextProperty = DependencyProperty.Register(
        nameof(RetryText), typeof(string), typeof(PageStatePresenter),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register(
        nameof(RetryCommand), typeof(ICommand), typeof(PageStatePresenter),
        new PropertyMetadata(null, OnRetryCommandChanged));

    private readonly ContentPresenter _bodyPresenter;
    private readonly ProgressBar _refreshProgressBar;
    private readonly Grid _stateLayer;
    private readonly ProgressRing _loadingRing;
    private readonly FontIcon _stateIcon;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _descriptionBlock;
    private readonly Button _retryButton;
    private EventHandler? _retryRequested;
    private ICommand? _subscribedRetryCommand;
    private bool _restoreFocusAfterRetry;
    private bool _isVisualStateInitialized;
    private PageViewState _displayedState = PageViewState.Content;
    private CancellationTokenSource? _stateMotionCancellation;

    public PageStatePresenter()
    {
        _bodyPresenter = new ContentPresenter
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        _refreshProgressBar = new ProgressBar
        {
            Height = 3,
            VerticalAlignment = VerticalAlignment.Top,
            IsIndeterminate = true
        };

        _loadingRing = new ProgressRing
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _stateIcon = new FontIcon
        {
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _titleBlock = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _descriptionBlock = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        ApplyStyle(_titleBlock, "KoukeiSectionTitleTextStyle");
        ApplyStyle(_descriptionBlock, "KoukeiMetadataTextStyle");
        AutomationProperties.SetLiveSetting(_titleBlock, AutomationLiveSetting.Polite);

        _retryButton = new Button
        {
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyStyle(_retryButton, "KoukeiStateActionButtonStyle");
        _retryButton.Click += RetryButton_Click;

        var statePanel = new StackPanel
        {
            MaxWidth = 520,
            Padding = new Thickness(24),
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        statePanel.Children.Add(_loadingRing);
        statePanel.Children.Add(_stateIcon);
        statePanel.Children.Add(_titleBlock);
        statePanel.Children.Add(_descriptionBlock);
        statePanel.Children.Add(_retryButton);

        // The page surface already owns its theme-aware background. Keeping this overlay
        // transparent avoids pinning a brush from the theme active at construction time.
        _stateLayer = new Grid();
        _stateLayer.Children.Add(statePanel);

        var root = new Grid();
        root.Children.Add(_bodyPresenter);
        root.Children.Add(_refreshProgressBar);
        root.Children.Add(_stateLayer);
        Content = root;
        Loaded += PageStatePresenter_Loaded;
        Unloaded += PageStatePresenter_Unloaded;
        UpdateVisuals(animateStateChange: false);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public PageViewState State
    {
        get => (PageViewState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string RetryText
    {
        get => (string)GetValue(RetryTextProperty);
        set => SetValue(RetryTextProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public event EventHandler? RetryRequested
    {
        add
        {
            _retryRequested += value;
            UpdateVisuals(animateStateChange: false);
        }
        remove
        {
            _retryRequested -= value;
            UpdateVisuals(animateStateChange: false);
        }
    }

    private static void ApplyStyle(FrameworkElement element, string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Style style)
        {
            element.Style = style;
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is PageStatePresenter presenter)
        {
            presenter.UpdateVisuals(animateStateChange: false);
        }
    }

    private static void OnStatePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PageStatePresenter presenter)
        {
            return;
        }

        var state = (PageViewState)args.NewValue;
        if (state == PageViewState.InitialLoading)
        {
            presenter.ScheduleRetryFocus(state);
        }

        presenter.UpdateVisuals(animateStateChange: true);
        if (state != PageViewState.InitialLoading)
        {
            presenter.ScheduleRetryFocus(state);
        }
    }

    private static void OnRetryCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PageStatePresenter presenter)
        {
            return;
        }

        presenter.UnsubscribeRetryCommand();
        presenter.SubscribeRetryCommand();
        presenter.UpdateVisuals(animateStateChange: false);
    }

    private void PageStatePresenter_Loaded(object sender, RoutedEventArgs args)
    {
        SubscribeRetryCommand();
        UpdateVisuals(animateStateChange: false);
    }

    private void PageStatePresenter_Unloaded(object sender, RoutedEventArgs args)
    {
        UnsubscribeRetryCommand();
        CancelStateMotion();
        _restoreFocusAfterRetry = false;
        IsTabStop = false;
    }

    private void SubscribeRetryCommand()
    {
        if (!IsLoaded || RetryCommand is null || ReferenceEquals(_subscribedRetryCommand, RetryCommand))
        {
            return;
        }

        _subscribedRetryCommand = RetryCommand;
        _subscribedRetryCommand.CanExecuteChanged += RetryCommand_CanExecuteChanged;
    }

    private void UnsubscribeRetryCommand()
    {
        if (_subscribedRetryCommand is null)
        {
            return;
        }

        _subscribedRetryCommand.CanExecuteChanged -= RetryCommand_CanExecuteChanged;
        _subscribedRetryCommand = null;
    }

    private void RetryCommand_CanExecuteChanged(object? sender, EventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            UpdateVisuals(animateStateChange: false);
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() => UpdateVisuals(animateStateChange: false));
    }

    private void UpdateVisuals(bool animateStateChange)
    {
        _bodyPresenter.Content = Body;
        SetLiveRegionText(_titleBlock, Title);
        _descriptionBlock.Text = Description;
        _retryButton.Content = RetryText;
        AutomationProperties.SetName(
            this,
            !string.IsNullOrWhiteSpace(Title) ? Title : Description);

        var isLoading = State == PageViewState.InitialLoading;
        _loadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        _loadingRing.IsActive = isLoading;
        _stateIcon.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        _titleBlock.Visibility = string.IsNullOrWhiteSpace(Title) ? Visibility.Collapsed : Visibility.Visible;
        _descriptionBlock.Visibility = string.IsNullOrWhiteSpace(Description) ? Visibility.Collapsed : Visibility.Visible;
        var hasRetryAction = RetryCommand is not null || _retryRequested is not null;
        var canRetry = _retryRequested is not null || RetryCommand?.CanExecute(null) == true;
        _retryButton.Visibility = State == PageViewState.Error
            && !string.IsNullOrWhiteSpace(RetryText)
            && hasRetryAction
            ? Visibility.Visible
            : Visibility.Collapsed;
        _retryButton.IsEnabled = canRetry;

        _stateIcon.Glyph = State switch
        {
            PageViewState.Empty => "\uE90C",
            PageViewState.NoResults => "\uE721",
            PageViewState.Error => "\uE783",
            _ => "\uE946"
        };

        if (!_isVisualStateInitialized || !IsLoaded)
        {
            CancelStateMotion();
            ApplyStateVisibilityInstant(State);
            _displayedState = State;
            _isVisualStateInitialized = true;
            return;
        }

        if (!animateStateChange || _displayedState == State)
        {
            return;
        }

        var previousState = _displayedState;
        _displayedState = State;
        CancelStateMotion();
        _stateMotionCancellation = new CancellationTokenSource();
        _ = AnimateStateChangeAsync(previousState, State, _stateMotionCancellation.Token);
    }

    private async Task AnimateStateChangeAsync(
        PageViewState previousState,
        PageViewState nextState,
        CancellationToken cancellationToken)
    {
        var previousShowsBody = ShowsBody(previousState);
        var nextShowsBody = ShowsBody(nextState);
        var tasks = new System.Collections.Generic.List<Task>(2);

        if (previousShowsBody != nextShowsBody)
        {
            tasks.Add(MotionHelper.CrossFadeAsync(
                previousShowsBody ? _bodyPresenter : _stateLayer,
                nextShowsBody ? _bodyPresenter : _stateLayer,
                MotionPreset.Standard,
                MotionDirection.Down,
                cancellationToken));
        }
        else if (!nextShowsBody)
        {
            // The state details have already been updated so live-region announcements are
            // immediate. Re-enter only the state surface to avoid an intermediate blank page.
            tasks.Add(MotionHelper.ShowAsync(
                _stateLayer,
                MotionPreset.Standard,
                MotionDirection.Down,
                distance: 8,
                cancellationToken: cancellationToken));
        }
        else
        {
            MotionHelper.SetVisibleInstant(_bodyPresenter, isVisible: true);
            MotionHelper.SetVisibleInstant(_stateLayer, isVisible: false);
        }

        if (nextState == PageViewState.Refreshing)
        {
            tasks.Add(MotionHelper.ShowAsync(
                _refreshProgressBar,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0,
                cancellationToken: cancellationToken));
        }
        else if (previousState == PageViewState.Refreshing ||
                 _refreshProgressBar.Visibility == Visibility.Visible)
        {
            tasks.Add(MotionHelper.HideAsync(
                _refreshProgressBar,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0,
                cancellationToken: cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyStateVisibilityInstant(PageViewState state)
    {
        var showBody = ShowsBody(state);
        MotionHelper.SetVisibleInstant(_bodyPresenter, showBody);
        MotionHelper.SetVisibleInstant(_stateLayer, !showBody);
        MotionHelper.SetVisibleInstant(
            _refreshProgressBar,
            state == PageViewState.Refreshing,
            isHitTestVisible: false);
    }

    private static bool ShowsBody(PageViewState state) =>
        state is PageViewState.Content or PageViewState.Refreshing;

    private void CancelStateMotion()
    {
        if (_stateMotionCancellation is null)
        {
            return;
        }

        _stateMotionCancellation.Cancel();
        _stateMotionCancellation.Dispose();
        _stateMotionCancellation = null;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _restoreFocusAfterRetry = _retryButton.FocusState != FocusState.Unfocused ||
            ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), _retryButton);

        if (RetryCommand?.CanExecute(null) == true)
        {
            RetryCommand.Execute(null);
        }

        _retryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleRetryFocus(PageViewState state)
    {
        if (!_restoreFocusAfterRetry)
        {
            return;
        }

        if (state == PageViewState.InitialLoading)
        {
            // Move focus before the Retry button is removed from the tree's active focus path.
            // Subsequent completion only restores into content if the user leaves it here.
            IsTabStop = true;
            Focus(FocusState.Programmatic);
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_restoreFocusAfterRetry || State != state || !IsLoaded || XamlRoot is null)
            {
                _restoreFocusAfterRetry = false;
                IsTabStop = false;
                return;
            }

            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (focusedElement is not null && !IsDescendantOf(focusedElement, this))
            {
                _restoreFocusAfterRetry = false;
                IsTabStop = false;
                return;
            }

            Control? focusTarget = state == PageViewState.Error && _retryButton.IsEnabled
                ? _retryButton
                : FocusManager.FindFirstFocusableElement(_bodyPresenter) as Control;

            if (focusTarget?.Focus(FocusState.Programmatic) == true)
            {
                IsTabStop = false;
            }
            else
            {
                IsTabStop = true;
                Focus(FocusState.Programmatic);
            }

            _restoreFocusAfterRetry = false;
        });
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

    private static void SetLiveRegionText(TextBlock element, string value)
    {
        if (string.Equals(element.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        element.Text = value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var peer = FrameworkElementAutomationPeer.FromElement(element) ??
            FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
