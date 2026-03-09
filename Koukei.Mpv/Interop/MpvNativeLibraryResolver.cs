using Koukei.Native;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

internal static class MpvNativeLibraryResolver
{
    private const string LibraryName = "libmpv-2.dll";
    private const string SupportedRuntimeIdentifier = "win-x64";
    private static readonly object InitializationLock = new();
    private static volatile bool _isRegistered;
    private static string? _libraryPath;
    private static string? _configurationDirectory;

    public static void EnsureRegistered()
    {
        if (_isRegistered)
        {
            return;
        }

        lock (InitializationLock)
        {
            if (_isRegistered)
            {
                return;
            }

            var location = NativeRuntimeLocator.Locate(
                "MPV",
                typeof(MpvNativeLibraryResolver).Assembly.Location,
                SupportedRuntimeIdentifier,
                [LibraryName]);
            _configurationDirectory = location.DirectoryPath;
            _libraryPath = Path.Combine(location.DirectoryPath, LibraryName);
            NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, Resolve);
            _isRegistered = true;
        }
    }

    public static string? FindConfigurationDirectory()
    {
        EnsureRegistered();
        var directory = _configurationDirectory;
        return !string.IsNullOrWhiteSpace(directory) &&
               (Directory.Exists(Path.Combine(directory, "scripts")) ||
                Directory.Exists(Path.Combine(directory, "script-opts")) ||
                Directory.Exists(Path.Combine(directory, "fonts")))
            ? directory
            : null;
    }

    private static IntPtr Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        var libraryPath = _libraryPath;
        if (!string.IsNullOrWhiteSpace(libraryPath) &&
            NativeLibrary.TryLoad(libraryPath, assembly, searchPath, out var handle))
        {
            return handle;
        }

        throw new DllNotFoundException(
            $"MPV native library could not be loaded from '{libraryPath}'.");
    }
}
