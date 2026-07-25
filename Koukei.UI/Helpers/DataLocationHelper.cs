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
    private const string USER_DATA_LOCATION_ENVIRONMENT_VARIABLE = "KOUKEI_USER_DATA_HOME";

    /// <summary>
    /// The fallback path used when no custom location has been saved.
    /// Uses the package's LocalState folder, which is the correct-isolated
    /// storage location for WinUI 3 MSIX packaged apps.
    /// </summary>
    public static readonly string DefaultUserDataLocation =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Koukei");

    internal static bool HasEnvironmentOverride => GetEnvironmentLocation() is not null;

    public static string CacheLocation
    {
        get
        {
            var environmentLocation = GetEnvironmentLocation();
            if (environmentLocation is not null)
            {
                return Path.Combine(environmentLocation, "Cache");
            }

            return ApplicationData.Current.LocalCacheFolder.Path;
        }
    }

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
            var environmentLocation = GetEnvironmentLocation();
            if (environmentLocation is not null)
            {
                return environmentLocation;
            }

            var saved = ApplicationData.Current.LocalSettings.Values[USER_DATA_LOCATION_KEY] as string;
            return string.IsNullOrEmpty(saved) ? DefaultUserDataLocation : saved;
        }
        set
        {
            if (HasEnvironmentOverride)
            {
                throw new InvalidOperationException(
                    "The data location cannot be changed while KOUKEI_USER_DATA_HOME is set.");
            }

            ApplicationData.Current.LocalSettings.Values[USER_DATA_LOCATION_KEY] = value;
        }
    }

    /// <summary>
    /// Creates the current data directory if it does not already exist.
    /// </summary>
    public static void EnsureExists()
    {
        if (!Directory.Exists(UserDataLocation))
            Directory.CreateDirectory(UserDataLocation);
    }

    private static string? GetEnvironmentLocation()
    {
        var value = Environment.GetEnvironmentVariable(
            USER_DATA_LOCATION_ENVIRONMENT_VARIABLE);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Path.GetFullPath(value.Trim());
    }
}
