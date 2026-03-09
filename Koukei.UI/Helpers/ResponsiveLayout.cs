using Microsoft.UI.Xaml;

namespace Koukei.UI.Helpers;

public enum UiBreakpoint
{
    Compact,
    Medium,
    Expanded,
    Wide
}

public static class ResponsiveLayout
{
    public const double CompactUpperBound = 640;
    public const double MediumUpperBound = 960;
    public const double ExpandedUpperBound = 1280;

    public static UiBreakpoint Resolve(double availableWidth)
    {
        if (availableWidth < CompactUpperBound)
        {
            return UiBreakpoint.Compact;
        }

        if (availableWidth < MediumUpperBound)
        {
            return UiBreakpoint.Medium;
        }

        return availableWidth < ExpandedUpperBound
            ? UiBreakpoint.Expanded
            : UiBreakpoint.Wide;
    }

    public static Thickness GetPagePadding(UiBreakpoint breakpoint) => breakpoint switch
    {
        UiBreakpoint.Compact => new Thickness(16),
        UiBreakpoint.Medium => new Thickness(24),
        UiBreakpoint.Expanded => new Thickness(24),
        _ => new Thickness(40)
    };
}
