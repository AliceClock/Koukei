using Koukei.Bus.Services;
using Koukei.UI.Helpers;
using Koukei.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Koukei.UI.Pages;

/// <summary>
/// Settings page
/// </summary>
public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _languageChanged;
    public bool LanguageChanged
    {
        get => _languageChanged;
        set
        {
            if (_languageChanged == value) return;
            _languageChanged = value; OnPropertyChanged();
        }
    }

    private bool _isLoadingSettings; // Flag to track if settings are being loaded
    private readonly ResourceLoader _resourceLoader;
    private readonly HyperlinkButton _dataLocationHyperlink;
    private readonly TextBlock _dataLocationText;
    private readonly AsyncReentrancyGuard _dataMigrationGuard = new();
    private readonly AsyncReentrancyGuard _cacheCleanupGuard = new();

    public SettingsPage()
    {
        InitializeComponent();
        _resourceLoader = new ResourceLoader();

        _dataLocationText = new TextBlock
        {
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        _dataLocationHyperlink = new HyperlinkButton
        {
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = _dataLocationText
        };
        _dataLocationHyperlink.Click += DataLocationHyperlink_Click;
        DataLocationCard.Description = _dataLocationHyperlink;

        Loaded += SettingsPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
    }

    #region Settings Loading

    /// <summary>
    /// Loads all settings
    /// </summary>
    private void LoadSettings()
    {
        _isLoadingSettings = true; // Start loading settings

        LoadThemeSetting();
        LoadLanguageSetting();
        LoadDataLocationInfo();
        LoadAppInfo();

        _isLoadingSettings = false; // Loading complete
    }

    /// <summary>
    /// Loads theme settings from ThemeHelper into the ComboBox.
    /// </summary>
    private void LoadThemeSetting()
    {
        ThemeComboBox.SelectedIndex = ThemeHelper.RootTheme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark  => 2,
            _                  => 0  // Default / system
        };
    }

    /// <summary>
    /// Loads language settings
    /// </summary>
    private void LoadLanguageSetting()
    {
        var savedLanguage = LanguageHelper.CurrentLanguage;

        LanguageComboBox.SelectedIndex = savedLanguage switch
        {
            "en-US" => 0,
            "zh-CN" => 1,
            _ => 0
        };
        LanguageChanged = !string.Equals(
            savedLanguage,
            LanguageHelper.AppliedLanguage,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads app version info into the About card from the package manifest.
    /// </summary>
    private void LoadAppInfo()
    {
        var v = Windows.ApplicationModel.Package.Current.Id.Version;
        AppInfoCard.Description = $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
    }

    /// <summary>
    /// Populates the data location hyperlink with the current data directory path.
    /// </summary>
    private void LoadDataLocationInfo()
    {
        var path = DataLocationHelper.UserDataLocation;
        _dataLocationText.Text = path;
        ToolTipService.SetToolTip(_dataLocationHyperlink, path);
    }

    #endregion

    #region Theme Settings

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return; // Don't trigger during settings load

        if (ThemeComboBox.SelectedItem is not ComboBoxItem item)
            return;

        var elementTheme = (item.Tag as string ?? "Default") switch
        {
            "Light" => ElementTheme.Light,
            "Dark"  => ElementTheme.Dark,
            _       => ElementTheme.Default
        };

        ThemeHelper.RootTheme = elementTheme;

        var resolvedTheme = elementTheme == ElementTheme.Default ? ThemeHelper.ActualTheme : elementTheme;
        if (App.MainWindow is { } window)
            TitleBarHelper.ApplySystemThemeToCaptionButtons(window, resolvedTheme);
    }

    #endregion

    #region Language Settings

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings) return; // Don't trigger during settings load

        if (LanguageComboBox.SelectedItem is not ComboBoxItem item)
            return;

        var language = item.Tag as string ?? "en-US";

        LanguageHelper.CurrentLanguage = language;
        LanguageChanged = !string.Equals(
            language,
            LanguageHelper.AppliedLanguage,
            StringComparison.OrdinalIgnoreCase);
    }

    private void Click_LanguageRestart(object sender, RoutedEventArgs e)
    {
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
    }

    #endregion

    #region Data Location

    /// <summary>
    /// Opens Explorer at the current data directory when the path hyperlink is clicked.
    /// </summary>
    private void DataLocationHyperlink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DataLocationHelper.EnsureExists();
            Process.Start("explorer.exe", DataLocationHelper.UserDataLocation);
        }
        catch (Exception ex)
        {
            ShowNotification(DataLocationNotificationBar, InfoBarSeverity.Error,
                _resourceLoader.GetString("SettingsPage_FailedToOpenDialog_Title"), ex.Message);
        }
    }

    /// <summary>
    /// Opens a folder picker, then migrates all existing data files to the chosen directory.
    /// </summary>
    private void ChangeDataLocationButton_Click(object sender, RoutedEventArgs e) =>
        _ = ChangeDataLocationAsync();

    private async Task ChangeDataLocationAsync()
    {
        await _dataMigrationGuard.TryRunAsync(async () =>
        {
            SetDataMigrationBusy(true);
            try
            {
                var folderPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Desktop
                };
                folderPicker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder is null)
                {
                    return;
                }

                var newPath = folder.Path;
                var oldPath = DataLocationHelper.UserDataLocation;
                if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await Task.Run(() => MigrateDataFiles(oldPath, newPath));
                DataLocationHelper.UserDataLocation = newPath;
                _dataLocationText.Text = newPath;
                ToolTipService.SetToolTip(_dataLocationHyperlink, newPath);

                ShowNotification(DataLocationNotificationBar, InfoBarSeverity.Success,
                    _resourceLoader.GetString("SettingsPage_DataMigratedDialog_Title"),
                    string.Format(_resourceLoader.GetString("SettingsPage_DataMigratedDialog_Message"), newPath));
            }
            catch (Exception ex)
            {
                ShowNotification(DataLocationNotificationBar, InfoBarSeverity.Error,
                    _resourceLoader.GetString("SettingsPage_FailedToMigrateDialog_Title"), ex.Message);
            }
            finally
            {
                SetDataMigrationBusy(false);
            }
        });
    }

    private static void MigrateDataFiles(string oldPath, string newPath)
    {
        Directory.CreateDirectory(newPath);
        if (!Directory.Exists(oldPath))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(oldPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(oldPath, file);
            var destination = Path.Combine(newPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination, overwrite: true);
        }
    }

    private void SetDataMigrationBusy(bool isBusy)
    {
        ChangeDataLocationButton.IsEnabled = !isBusy;
        DataMigrationProgressRing.IsActive = isBusy;
        _ = isBusy
            ? MotionHelper.ShowAsync(
                DataMigrationProgressRing,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0)
            : MotionHelper.HideAsync(
                DataMigrationProgressRing,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0);
    }

    #endregion

    #region Data Cleanup

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        CacheNotificationBar.IsOpen = false;
        ClearCacheConfirmBar.IsOpen = true;
    }

    private async void ClearCacheConfirm_Click(object sender, RoutedEventArgs e)
    {
        ClearCacheConfirmBar.IsOpen = false;
        await _cacheCleanupGuard.TryRunAsync(async () =>
        {
            SetCacheCleanupBusy(true);
            try
            {
                var cacheLocation = DataLocationHelper.CacheLocation;
                using (var scope = App.Services.CreateScope())
                {
                    var library = scope.ServiceProvider.GetRequiredService<IMediaLibraryBus>();
                    await library.ClearThumbnailPathsUnderAsync(cacheLocation);
                }
                App.Services
                    .GetRequiredService<PlaybackCoordinator>()
                    .ClearQueueThumbnailsUnder(cacheLocation);
                var result = await Task.Run(ClearCacheFiles);
                if (result.Existed)
                {
                    ShowNotification(CacheNotificationBar, InfoBarSeverity.Success,
                        _resourceLoader.GetString("SettingsPage_ClearedDialog_Title"),
                        string.Format(_resourceLoader.GetString("SettingsPage_ClearedDialog_Message"), result.FileCount));
                }
                else
                {
                    ShowNotification(CacheNotificationBar, InfoBarSeverity.Informational,
                        _resourceLoader.GetString("SettingsPage_NoCacheDialog_Title"),
                        _resourceLoader.GetString("SettingsPage_NoCacheDialog_Message"));
                }
            }
            catch (Exception ex)
            {
                ShowNotification(CacheNotificationBar, InfoBarSeverity.Error,
                    _resourceLoader.GetString("SettingsPage_FailedToClearDialog_Title"), ex.Message);
            }
            finally
            {
                SetCacheCleanupBusy(false);
            }
        });
    }

    private static (bool Existed, int FileCount) ClearCacheFiles()
    {
        var cacheLocation = DataLocationHelper.CacheLocation;
        if (!Directory.Exists(cacheLocation))
        {
            return (false, 0);
        }

        var fileCount = Directory.GetFiles(cacheLocation, "*", SearchOption.AllDirectories).Length;
        Directory.Delete(cacheLocation, recursive: true);
        return (true, fileCount);
    }

    private void SetCacheCleanupBusy(bool isBusy)
    {
        ClearCacheButton.IsEnabled = !isBusy;
        ClearCacheConfirmActionButton.IsEnabled = !isBusy;
        CacheCleanupProgressRing.IsActive = isBusy;
        _ = isBusy
            ? MotionHelper.ShowAsync(
                CacheCleanupProgressRing,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0)
            : MotionHelper.HideAsync(
                CacheCleanupProgressRing,
                MotionPreset.Fast,
                MotionDirection.None,
                distance: 0);
    }

    #endregion

    #region Reset Settings

    private void ClearSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSettingsConfirmBar.IsOpen = true;
    }

    private void ClearSettingsConfirm_Click(object sender, RoutedEventArgs e)
    {
        ClearSettingsConfirmBar.IsOpen = false;

        ThemeHelper.RootTheme = ElementTheme.Default;
        LanguageHelper.CurrentLanguage = "en-US";
        DataLocationHelper.UserDataLocation = DataLocationHelper.DefaultUserDataLocation;

        if (App.MainWindow is { } window)
            TitleBarHelper.ApplySystemThemeToCaptionButtons(window, ThemeHelper.ActualTheme);

        _isLoadingSettings = true;
        LoadThemeSetting();
        LoadLanguageSetting();
        LoadDataLocationInfo();
        _isLoadingSettings = false;
    }

    #endregion

    #region Helper Methods

    private static void ShowNotification(InfoBar targetBar, InfoBarSeverity severity, string title, string message)
    {
        targetBar.Severity = severity;
        targetBar.Title = title;
        targetBar.Message = message;
        targetBar.IsOpen = true;
    }

    #endregion
}
