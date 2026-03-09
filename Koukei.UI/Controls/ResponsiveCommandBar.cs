using Koukei.UI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Koukei.UI.Controls;

/// <summary>
/// CommandBar that derives its presentation from its own available width. Commands placed
/// in SecondaryCommands remain available in the overflow at every breakpoint.
/// </summary>
public sealed class ResponsiveCommandBar : CommandBar
{
    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(
        nameof(Breakpoint), typeof(UiBreakpoint), typeof(ResponsiveCommandBar),
        new PropertyMetadata(UiBreakpoint.Expanded));

    public ResponsiveCommandBar()
    {
        IsDynamicOverflowEnabled = true;
        DefaultLabelPosition = CommandBarDefaultLabelPosition.Right;
        SizeChanged += ResponsiveCommandBar_SizeChanged;
    }

    public UiBreakpoint Breakpoint
    {
        get => (UiBreakpoint)GetValue(BreakpointProperty);
        private set => SetValue(BreakpointProperty, value);
    }

    private void ResponsiveCommandBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var nextBreakpoint = ResponsiveLayout.Resolve(e.NewSize.Width);
        if (nextBreakpoint == Breakpoint)
        {
            return;
        }

        Breakpoint = nextBreakpoint;
        DefaultLabelPosition = nextBreakpoint switch
        {
            UiBreakpoint.Compact => CommandBarDefaultLabelPosition.Collapsed,
            _ => CommandBarDefaultLabelPosition.Right
        };
        IsOpen = false;
    }
}
