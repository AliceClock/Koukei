using System.Runtime.InteropServices;

namespace Koukei.Native;

internal sealed record NativeRuntimeLocation(
    string RuntimeIdentifier,
    string DirectoryPath);

internal static class NativeRuntimeLocator
{
    public static NativeRuntimeLocation Locate(
        string componentName,
        string assemblyLocation,
        string supportedRuntimeIdentifier,
        IReadOnlyCollection<string> requiredLibraryNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyLocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(supportedRuntimeIdentifier);
        ArgumentNullException.ThrowIfNull(requiredLibraryNames);
        if (requiredLibraryNames.Count == 0)
        {
            throw new ArgumentException(
                "At least one native library name is required.",
                nameof(requiredLibraryNames));
        }

        var runtimeIdentifier = GetCurrentRuntimeIdentifier();
        if (!string.Equals(
                runtimeIdentifier,
                supportedRuntimeIdentifier,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PlatformNotSupportedException(
                $"{componentName} supports '{supportedRuntimeIdentifier}', " +
                $"but the current runtime is '{runtimeIdentifier}'.");
        }

        var normalizedComponentName = componentName.Trim().ToLowerInvariant();
        var environmentVariableName =
            $"KOUKEI_{normalizedComponentName.ToUpperInvariant()}_HOME";
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
        var candidates = new List<string>();
        var configuredDirectory = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            candidates.Add(configuredDirectory);
        }

        var applicationBaseDirectory =
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        candidates.Add(Path.Combine(
            applicationBaseDirectory,
            normalizedComponentName,
            runtimeIdentifier));
        if (string.Equals(
                Path.GetFileName(applicationBaseDirectory),
                "AppX",
                StringComparison.OrdinalIgnoreCase))
        {
            var deploymentOutputDirectory =
                Directory.GetParent(applicationBaseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(deploymentOutputDirectory))
            {
                candidates.Add(Path.Combine(
                    deploymentOutputDirectory,
                    normalizedComponentName,
                    runtimeIdentifier));
            }
        }

        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            candidates.Add(Path.Combine(
                assemblyDirectory,
                normalizedComponentName,
                runtimeIdentifier));
        }

        var attemptedDirectories = new List<string>();
        foreach (var candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                attemptedDirectories.Add(candidate);
                continue;
            }

            if (attemptedDirectories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            attemptedDirectories.Add(fullPath);
            if (requiredLibraryNames.All(
                    libraryName => File.Exists(Path.Combine(fullPath, libraryName))))
            {
                return new NativeRuntimeLocation(runtimeIdentifier, fullPath);
            }
        }

        throw new DllNotFoundException(
            $"{componentName} native libraries for '{runtimeIdentifier}' were not found. " +
            $"Set {environmentVariableName} to the directory containing " +
            $"{string.Join(", ", requiredLibraryNames)}. " +
            $"Searched: {string.Join("; ", attemptedDirectories)}");
    }

    private static string GetCurrentRuntimeIdentifier()
    {
        var operatingSystem = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : throw new PlatformNotSupportedException(
                        "The current operating system is not supported.");
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"The current process architecture '{RuntimeInformation.ProcessArchitecture}' " +
                "is not supported.")
        };

        return $"{operatingSystem}-{architecture}";
    }
}
