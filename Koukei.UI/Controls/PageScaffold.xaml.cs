using Koukei.UI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Koukei.UI.Controls;

public sealed class PageScaffold : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageScaffold),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(PageScaffold),
        new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty HeaderCommandsProperty = DependencyProperty.Register(
        nameof(HeaderCommands), typeof(object), typeof(PageScaffold),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(object), typeof(PageScaffold),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(PageScaffold),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ContentMaxWidthProperty = DependencyProperty.Register(
        nameof(ContentMaxWidth), typeof(double), typeof(PageScaffold),
        new PropertyMetadata(1440d, OnVisualPropertyChanged));

    public static readonly DependencyProperty PagePaddingProperty = DependencyProperty.Register(
        nameof(PagePadding), typeof(Thickness), typeof(PageScaffold),
        new PropertyMetadata(new Thickness(24), OnVisualPropertyChanged));

    public static readonly DependencyProperty UseResponsivePaddingProperty = DependencyProperty.Register(
        nameof(UseResponsivePadding), typeof(bool), typeof(PageScaffold), new PropertyMetadata(true));

    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(
        nameof(Breakpoint), typeof(UiBreakpoint), typeof(PageScaffold),
        new PropertyMetadata(UiBreakpoint.Expanded));

    private readonly Grid _contentGrid;
    private readonly Grid _headerGrid;
    private readonly StackPanel _titlePanel;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _subtitleBlock;
    private readonly ContentPresenter _headerCommandsPresenter;
    private readonly ContentPresenter _bodyPresenter;
    private readonly ContentPresenter _footerPresenter;

    public PageScaffold()
    {
        _titleBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
        _subtitleBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
        ApplyStyle(_titleBlock, "KoukeiPageTitleTextStyle");
        ApplyStyle(_subtitleBlock, "KoukeiMetadataTextStyle");

        _titlePanel = new StackPanel { Spacing = 4 };
        _titlePanel.Children.Add(_titleBlock);
        _titlePanel.Children.Add(_subtitleBlock);

        _headerCommandsPresenter = new ContentPresenter
        {
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        _headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerGrid.Children.Add(_titlePanel);
        Grid.SetColumn(_headerCommandsPresenter, 1);
        _headerGrid.Children.Add(_headerCommandsPresenter);

        _bodyPresenter = new ContentPresenter
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(_bodyPresenter, 1);

        _footerPresenter = new ContentPresenter
        {
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(_footerPresenter, 2);

        _contentGrid = new Grid
        {
            Width = double.NaN,
            MaxWidth = ContentMaxWidth,
            Padding = PagePadding,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _contentGrid.Children.Add(_headerGrid);
        _contentGrid.Children.Add(_bodyPresenter);
        _contentGrid.Children.Add(_footerPresenter);

        // Keep the scaffold transparent so the window's Mica/page surface remains visible and
        // theme/high-contrast changes do not leave a cached brush on this code-built control.
        var outerGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        outerGrid.Children.Add(_contentGrid);
        Content = outerGrid;

        SizeChanged += PageScaffold_SizeChanged;
        UpdateVisuals();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? HeaderCommands
    {
        get => GetValue(HeaderCommandsProperty);
        set => SetValue(HeaderCommandsProperty, value);
    }

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public double ContentMaxWidth
    {
        get => (double)GetValue(ContentMaxWidthProperty);
        set => SetValue(ContentMaxWidthProperty, value);
    }

    public Thickness PagePadding
    {
        get => (Thickness)GetValue(PagePaddingProperty);
        set => SetValue(PagePaddingProperty, value);
    }

    public bool UseResponsivePadding
    {
        get => (bool)GetValue(UseResponsivePaddingProperty);
        set => SetValue(UseResponsivePaddingProperty, value);
    }

    public UiBreakpoint Breakpoint
    {
        get => (UiBreakpoint)GetValue(BreakpointProperty);
        private set => SetValue(BreakpointProperty, value);
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
        if (dependencyObject is PageScaffold scaffold)
        {
            scaffold.UpdateVisuals();
        }
    }

    private void PageScaffold_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Breakpoint = ResponsiveLayout.Resolve(e.NewSize.Width);
        if (UseResponsivePadding)
        {
            PagePadding = ResponsiveLayout.GetPagePadding(Breakpoint);
        }

        var isCompact = Breakpoint == UiBreakpoint.Compact;
        _headerGrid.RowDefinitions.Clear();
        if (isCompact)
        {
            _headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetColumn(_headerCommandsPresenter, isCompact ? 0 : 1);
        Grid.SetRow(_headerCommandsPresenter, isCompact ? 1 : 0);
        _headerCommandsPresenter.Margin = isCompact
            ? new Thickness(0, 12, 0, 0)
            : new Thickness(16, 0, 0, 0);
    }

    private void UpdateVisuals()
    {
        _titleBlock.Text = Title;
        _subtitleBlock.Text = Subtitle;
        _headerCommandsPresenter.Content = HeaderCommands;
        _bodyPresenter.Content = Body;
        _footerPresenter.Content = Footer;
        _contentGrid.MaxWidth = ContentMaxWidth;
        _contentGrid.Padding = PagePadding;

        _titleBlock.Visibility = string.IsNullOrWhiteSpace(Title) ? Visibility.Collapsed : Visibility.Visible;
        _subtitleBlock.Visibility = string.IsNullOrWhiteSpace(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        _titlePanel.Visibility = _titleBlock.Visibility == Visibility.Collapsed && _subtitleBlock.Visibility == Visibility.Collapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        _headerGrid.Visibility = _titlePanel.Visibility == Visibility.Collapsed && HeaderCommands is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        _footerPresenter.Visibility = Footer is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
