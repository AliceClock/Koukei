using System.Reflection;

namespace Koukei.UI.Tests.Infrastructure;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string UiProjectDirectory => Path.Combine(Root, "Koukei.UI");

    public static string GetUiExecutablePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("KOUKEI_UI_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var fullOverridePath = Path.GetFullPath(overridePath.Trim());
            if (!File.Exists(fullOverridePath))
            {
                throw new FileNotFoundException(
                    "KOUKEI_UI_EXE does not point to an existing executable.",
                    fullOverridePath);
            }

            return fullOverridePath;
        }

        var configuration = typeof(RepositoryPaths).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?
            .Configuration ?? "Debug";
        var expected = Path.Combine(
            UiProjectDirectory,
            "bin",
            "x64",
            configuration,
            "net8.0-windows10.0.19041.0",
            "win-x64",
            "Koukei.UI.exe");

        if (!File.Exists(expected))
        {
            throw new FileNotFoundException(
                "The UI executable was not built. Build the UI test project with Platform=x64.",
                expected);
        }

        return expected;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Koukei.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate Koukei.slnx above '{AppContext.BaseDirectory}'.");
    }
}
