using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Koukei.UI.Helpers;

/// <summary>
/// Provides helpers for synchronizing the window caption button colors with the active theme.
/// </summary>
internal static class TitleBarHelper
{
    /// <summary>
    /// Sets AppWindowTitleBar caption button colors to match <paramref name="currentTheme"/>.
    /// Pass the resolved (non-Default) theme: use <see cref="ThemeHelper.ActualTheme"/> when
    /// <see cref="ThemeHelper.RootTheme"/> is <see cref="ElementTheme.Default"/>.
    /// </summary>
    public static void ApplySystemThemeToCaptionButtons(Window window, ElementTheme currentTheme)
    {
        if (window.AppWindow == null) return;
        var foregroundColor = currentTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        window.AppWindow.TitleBar.ButtonForegroundColor = foregroundColor;
        window.AppWindow.TitleBar.ButtonHoverForegroundColor = foregroundColor;

        var backgroundHoverColor = currentTheme == ElementTheme.Dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        window.AppWindow.TitleBar.ButtonHoverBackgroundColor = backgroundHoverColor;
    }
}
