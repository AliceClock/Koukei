using System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Koukei.UI.Helpers;

internal sealed record PlayerThemePalette(
    Color AudioBackground,
    Color ControlBarTop,
    Color ControlBarBottom,
    Color Foreground,
    Color MutedForeground,
    Color SubtleBackground,
    Color PrimaryBackground,
    Color PrimaryBorder,
    Color PrimaryForeground,
    Color ButtonHoverBackground,
    Color ButtonPressedBackground,
    Color SliderTrack,
    Color SliderTrackHover,
    Color SliderThumb,
    Color Accent,
    Color AccentHover,
    Color ChapterMarker,
    Color PrimaryHoverBackground,
    Color PrimaryPressedBackground)
{
    public PlayerThemePalette WithSystemAccent()
    {
        try
        {
            var settings = new UISettings();
            var accent = settings.GetColorValue(UIColorType.Accent);
            if (new AccessibilitySettings().HighContrast)
            {
                var background = settings.GetColorValue(UIColorType.Background);
                var foreground = settings.GetColorValue(UIColorType.Foreground);
                return this with
                {
                    AudioBackground = background,
                    ControlBarTop = background,
                    ControlBarBottom = background,
                    Foreground = foreground,
                    MutedForeground = foreground,
                    SubtleBackground = background,
                    PrimaryBackground = accent,
                    PrimaryBorder = foreground,
                    PrimaryForeground = background,
                    ButtonHoverBackground = accent,
                    ButtonPressedBackground = accent,
                    SliderTrack = foreground,
                    SliderTrackHover = foreground,
                    SliderThumb = foreground,
                    Accent = accent,
                    AccentHover = accent,
                    ChapterMarker = foreground,
                    PrimaryHoverBackground = accent,
                    PrimaryPressedBackground = accent
                };
            }

            return this with
            {
                Accent = accent,
                AccentHover = Blend(accent, Foreground, 0.18)
            };
        }
        catch
        {
            return this;
        }
    }

    private static Color Blend(Color source, Color target, double amount)
    {
        var inverse = 1 - amount;
        return Color.FromArgb(
            source.A,
            (byte)Math.Round(source.R * inverse + target.R * amount),
            (byte)Math.Round(source.G * inverse + target.G * amount),
            (byte)Math.Round(source.B * inverse + target.B * amount));
    }

    public static PlayerThemePalette Dark { get; } = new(
        AudioBackground: Color.FromArgb(255, 5, 5, 6),
        ControlBarTop: Color.FromArgb(211, 27, 28, 32),
        ControlBarBottom: Color.FromArgb(236, 17, 18, 22),
        Foreground: Color.FromArgb(255, 255, 255, 255),
        MutedForeground: Color.FromArgb(168, 255, 255, 255),
        SubtleBackground: Color.FromArgb(31, 255, 255, 255),
        PrimaryBackground: Color.FromArgb(245, 255, 255, 255),
        PrimaryBorder: Color.FromArgb(43, 255, 255, 255),
        PrimaryForeground: Color.FromArgb(255, 17, 18, 20),
        ButtonHoverBackground: Color.FromArgb(32, 255, 255, 255),
        ButtonPressedBackground: Color.FromArgb(52, 255, 255, 255),
        SliderTrack: Color.FromArgb(82, 255, 255, 255),
        SliderTrackHover: Color.FromArgb(107, 255, 255, 255),
        SliderThumb: Color.FromArgb(255, 255, 255, 255),
        Accent: Color.FromArgb(255, 255, 130, 50),
        AccentHover: Color.FromArgb(255, 255, 150, 90),
        ChapterMarker: Color.FromArgb(210, 255, 255, 255),
        PrimaryHoverBackground: Color.FromArgb(255, 255, 255, 255),
        PrimaryPressedBackground: Color.FromArgb(223, 255, 255, 255));

    public static PlayerThemePalette Light { get; } = new(
        AudioBackground: Color.FromArgb(255, 244, 244, 246),
        ControlBarTop: Color.FromArgb(210, 248, 248, 249),
        ControlBarBottom: Color.FromArgb(232, 238, 238, 240),
        Foreground: Color.FromArgb(255, 23, 23, 25),
        MutedForeground: Color.FromArgb(168, 23, 23, 25),
        SubtleBackground: Color.FromArgb(24, 0, 0, 0),
        PrimaryBackground: Color.FromArgb(245, 27, 27, 29),
        PrimaryBorder: Color.FromArgb(50, 0, 0, 0),
        PrimaryForeground: Color.FromArgb(255, 255, 255, 255),
        ButtonHoverBackground: Color.FromArgb(22, 0, 0, 0),
        ButtonPressedBackground: Color.FromArgb(38, 0, 0, 0),
        SliderTrack: Color.FromArgb(66, 0, 0, 0),
        SliderTrackHover: Color.FromArgb(92, 0, 0, 0),
        SliderThumb: Color.FromArgb(255, 27, 27, 29),
        Accent: Color.FromArgb(255, 238, 104, 30),
        AccentHover: Color.FromArgb(255, 255, 126, 54),
        ChapterMarker: Color.FromArgb(205, 23, 23, 25),
        PrimaryHoverBackground: Color.FromArgb(255, 17, 18, 20),
        PrimaryPressedBackground: Color.FromArgb(232, 17, 18, 20));
}
