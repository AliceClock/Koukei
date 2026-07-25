using Microsoft.UI.Xaml;
using System;
using Windows.Storage;

namespace Koukei.UI.Helpers;

/// <summary>
/// Provides functionality for switching and persisting the app theme.
/// </summary>
internal static class ThemeHelper
{
    private const string APP_THEME_KEY = "AppTheme";
    private static string _environmentTheme = "Default";

    public static event EventHandler<AppThemeChangedEventArgs>? ThemeChanged;

    /// <summary>
    /// Gets the actual resolved theme: the root element's RequestedTheme if it is not Default,
    /// otherwise the system ApplicationTheme.
    /// </summary>
    public static ElementTheme ActualTheme
    {
        get
        {
            if (App.MainWindow?.Content is FrameworkElement rootElement)
            {
                if (rootElement.RequestedTheme != ElementTheme.Default)
                {
                    return rootElement.RequestedTheme;
                }

                if (rootElement.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
                {
                    return rootElement.ActualTheme;
                }
            }

            return Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }
    }

    /// <summary>
    /// Gets or sets the RequestedTheme of the root element, persisting the value to LocalSettings.
    /// </summary>
    public static ElementTheme RootTheme
    {
        get
        {
            if (App.MainWindow?.Content is FrameworkElement rootElement)
                return rootElement.RequestedTheme;

            return ElementTheme.Default;
        }
        set
        {
            var previousTheme = RootTheme;

            if (App.MainWindow?.Content is FrameworkElement rootElement)
                rootElement.RequestedTheme = value;

            if (DataLocationHelper.HasEnvironmentOverride)
            {
                _environmentTheme = value.ToString();
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[APP_THEME_KEY] = value.ToString();
            }

            if (previousTheme != value)
            {
                ThemeChanged?.Invoke(
                    null,
                    new AppThemeChangedEventArgs(value, ActualTheme));
            }
        }
    }

    /// <summary>
    /// Reads the persisted theme from LocalSettings and applies it to the root element.
    /// Call this after the main window has been created.
    /// </summary>
    public static void Initialize()
    {
        var saved = DataLocationHelper.HasEnvironmentOverride
            ? _environmentTheme
            : ApplicationData.Current.LocalSettings.Values[APP_THEME_KEY] as string ?? "Default";
        RootTheme = saved switch
        {
            "Light" => ElementTheme.Light,
            "Dark"  => ElementTheme.Dark,
            _       => ElementTheme.Default
        };
    }
}

internal sealed class AppThemeChangedEventArgs(
    ElementTheme requestedTheme,
    ElementTheme actualTheme) : EventArgs
{
    public ElementTheme RequestedTheme { get; } = requestedTheme;

    public ElementTheme ActualTheme { get; } = actualTheme;
}
