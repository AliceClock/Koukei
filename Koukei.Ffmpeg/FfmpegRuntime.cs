using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Koukei.Native;

namespace Koukei.Ffmpeg;

internal static class FfmpegRuntime
{
    private const string SupportedRuntimeIdentifier = "win-x64";
    private static readonly string[] RequiredLibraryNames =
    [
        "avcodec-62.dll",
        "avdevice-62.dll",
        "avfilter-11.dll",
        "avformat-62.dll",
        "avutil-60.dll",
        "swresample-6.dll",
        "swscale-9.dll"
    ];
    private static readonly object InitializationLock = new();
    internal static SemaphoreSlim NativeOperationGate { get; } = new(1, 1);
    private static volatile bool _isInitialized;

    public static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        lock (InitializationLock)
        {
            if (_isInitialized)
            {
                return;
            }

            var location = NativeRuntimeLocator.Locate(
                "FFmpeg",
                typeof(FfmpegRuntime).Assembly.Location,
                SupportedRuntimeIdentifier,
                RequiredLibraryNames);

            DynamicallyLoadedBindings.LibrariesPath = location.DirectoryPath;
            DynamicallyLoadedBindings.Initialize();
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            _isInitialized = true;
        }
    }

}
