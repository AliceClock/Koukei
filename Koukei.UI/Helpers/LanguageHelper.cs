using Windows.Globalization;
using Windows.Storage;

namespace Koukei.UI.Helpers;

/// <summary>
/// Manages the application display language with LocalSettings persistence.
/// </summary>
internal static class LanguageHelper
{
    private const string APP_LANGUAGE_KEY = "AppLanguage";
    private static string _environmentLanguage = "en-US";

    /// <summary>
    /// Gets the language applied to resources for the lifetime of the current process.
    /// Persisting a different language does not change this value until restart.
    /// </summary>
    public static string AppliedLanguage { get; private set; } = "en-US";

    /// <summary>
    /// Gets or sets the current language tag (e.g. "en-US", "zh-CN").
    /// The value is persisted to LocalSettings.
    /// </summary>
    public static string CurrentLanguage
    {
        get
        {
            if (DataLocationHelper.HasEnvironmentOverride)
            {
                return _environmentLanguage;
            }

            return ApplicationData.Current.LocalSettings.Values[APP_LANGUAGE_KEY] as string ?? "en-US";
        }
        set
        {
            if (DataLocationHelper.HasEnvironmentOverride)
            {
                _environmentLanguage = value;
                return;
            }

            ApplicationData.Current.LocalSettings.Values[APP_LANGUAGE_KEY] = value;
        }
    }

    /// <summary>
    /// Reads the persisted language from LocalSettings and applies it as the
    /// <see cref="ApplicationLanguages.PrimaryLanguageOverride"/>.
    /// Call this before <see cref="Microsoft.UI.Xaml.Application.OnLaunched"/> runs.
    /// </summary>
    public static void Initialize()
    {
        var saved = CurrentLanguage;
        if (!string.IsNullOrEmpty(saved))
        {
            if (!DataLocationHelper.HasEnvironmentOverride)
            {
                ApplicationLanguages.PrimaryLanguageOverride = saved;
            }

            AppliedLanguage = saved;
        }
    }
}
