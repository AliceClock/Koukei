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
#if DEBUG
            UnhandledException += (_, e) =>
            {
                Debug.WriteLine("Unhandled XAML exception:");
                Debug.WriteLine(e.Exception);
            };
#endif
            LanguageHelper.Initialize();
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            DataLocationHelper.EnsureExists();
            Services = ConfigureServices();

            try
            {
                using var scope = Services.CreateScope();
                var initializer = scope.ServiceProvider.GetRequiredService<IKoukeiDataGateway>();
                await initializer.EnsureReadyAsync();
            }
            catch (Exception ex)
            {
                DataInitializationException = ex;
            }

            _window = new MainWindow();
            MainWindow = _window;
            ThemeHelper.Initialize();
            var resolvedTheme = ThemeHelper.RootTheme == ElementTheme.Default
                ? ThemeHelper.ActualTheme
                : ThemeHelper.RootTheme;
            TitleBarHelper.ApplySystemThemeToCaptionButtons(_window, resolvedTheme);
            _window.Activate();
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
    }
}
