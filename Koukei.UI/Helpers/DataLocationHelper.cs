using Koukei.Bus;
using System;
using System.IO;
using Windows.Storage;

namespace Koukei.UI.Helpers;

/// <summary>
/// Manages the application data directory path with LocalSettings persistence.
/// </summary>
internal static class DataLocationHelper
{
    private const string USER_DATA_LOCATION_KEY = "UserDataLocation";

    /// <summary>
    /// The fallback path used when no custom location has been saved.
    /// Uses the package's LocalState folder, which is the correct-isolated
    /// storage location for WinUI 3 MSIX packaged apps.
    /// </summary>
    public static readonly string DefaultUserDataLocation =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Koukei");

    public static readonly string CacheLocation =
        ApplicationData.Current.LocalCacheFolder.Path;

    public static string DatabasePath =>
        Path.Combine(UserDataLocation, KoukeiBusDefaults.DefaultDatabaseFileName);

    /// <summary>
    /// Gets or sets the current data directory path.
    /// The value is persisted to LocalSettings; reads fall back to <see cref="DefaultUserDataLocation"/>.
    /// </summary>
    public static string UserDataLocation
    {
        get
        {
            var saved = ApplicationData.Current.LocalSettings.Values[USER_DATA_LOCATION_KEY] as string;
            return string.IsNullOrEmpty(saved) ? DefaultUserDataLocation : saved;
        }
        set => ApplicationData.Current.LocalSettings.Values[USER_DATA_LOCATION_KEY] = value;
    }

    /// <summary>
    /// Creates the current data directory if it does not already exist.
    /// </summary>
    public static void EnsureExists()
    {
        if (!Directory.Exists(UserDataLocation))
            Directory.CreateDirectory(UserDataLocation);
    }
}
