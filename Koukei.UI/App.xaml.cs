using Koukei.UI.Helpers;
using Koukei.Bus;
using Koukei.Audio;
using Koukei.Bus.Services;
using Koukei.Ffmpeg;
using Koukei.Video;
using Koukei.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Koukei.UI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private const string StartupDiagnosticsEnvironmentVariable =
            "KOUKEI_UI_STARTUP_DIAGNOSTICS";
        private Window? _window;

        /// <summary>
        /// Gets the main application window.
        /// </summary>
        public static Window? MainWindow { get; private set; }

        public static IServiceProvider Services { get; private set; } = null!;

        public static Exception? DataInitializationException { get; private set; }

        internal static void ReleaseMainWindow(Window window)
        {
            if (ReferenceEquals(MainWindow, window))
            {
                MainWindow = null;
            }

            if (Current is App app && ReferenceEquals(app._window, window))
            {
                app._window = null;
            }
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            WriteStartupDiagnostic("App constructor started.");
            UnhandledException += (_, e) =>
            {
#if DEBUG
                Debug.WriteLine("Unhandled XAML exception:");
                Debug.WriteLine(e.Exception);
#endif
                WriteStartupDiagnostic("Unhandled XAML exception.", e.Exception);
            };
            try
            {
                LanguageHelper.Initialize();
                WriteStartupDiagnostic("Language initialized.");
                InitializeComponent();
                WriteStartupDiagnostic("Application XAML initialized.");
            }
            catch (Exception ex)
            {
                WriteStartupDiagnostic("App constructor failed.", ex);
                throw;
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            WriteStartupDiagnostic("OnLaunched started.");
            try
            {
                DataLocationHelper.EnsureExists();
                WriteStartupDiagnostic("Data directory initialized.");
                Services = ConfigureServices();
                WriteStartupDiagnostic("Services configured.");

                try
                {
                    using var scope = Services.CreateScope();
                    var initializer = scope.ServiceProvider.GetRequiredService<IKoukeiDataGateway>();
                    await initializer.EnsureReadyAsync();
                    WriteStartupDiagnostic("Data store initialized.");
                }
                catch (Exception ex)
                {
                    DataInitializationException = ex;
                    WriteStartupDiagnostic("Data store initialization failed; continuing.", ex);
                }

                _window = new MainWindow();
                MainWindow = _window;
                WriteStartupDiagnostic("Main window created.");
                ThemeHelper.Initialize();
                var resolvedTheme = ThemeHelper.RootTheme == ElementTheme.Default
                    ? ThemeHelper.ActualTheme
                    : ThemeHelper.RootTheme;
                TitleBarHelper.ApplySystemThemeToCaptionButtons(_window, resolvedTheme);
                _window.Activate();
                WriteStartupDiagnostic("Main window activated.");
            }
            catch (Exception ex)
            {
                WriteStartupDiagnostic("OnLaunched failed.", ex);
                throw;
            }
        }

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddKoukeiBus(DataLocationHelper.DatabasePath);
            services.AddSingleton<IFfmpegMediaProbe, FfmpegMediaProbe>();
            services.AddSingleton<IFfmpegVideoThumbnailGenerator, FfmpegVideoThumbnailGenerator>();
            services.AddSingleton<IVideoPlaybackService, MpvVideoPlaybackService>();
            services.AddSingleton<IAudioMetadataService, FfmpegAudioMetadataService>();
            services.AddSingleton<IAudioPlaybackService, OutOfProcessAudioPlaybackService>();
            services.AddSingleton<PlaybackCoordinator>();
            services.AddSingleton<PlaybackQueueBuilder>();
            services.AddSingleton<IVideoThumbnailService, VideoThumbnailService>();
            services.AddSingleton<IVideoMediaInfoService, FfmpegVideoMediaInfoService>();
            return services.BuildServiceProvider();
        }

        private static void WriteStartupDiagnostic(string message, Exception? exception = null)
        {
            var path = Environment.GetEnvironmentVariable(
                StartupDiagnosticsEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var line = $"{DateTimeOffset.Now:O} {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never affect application startup.
            }
        }
    }
}
