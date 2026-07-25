using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using Koukei.UI.Tests.Infrastructure;
using Xunit.Sdk;

namespace Koukei.UI.Tests;

[Trait("Category", "UIAutomation")]
public sealed class NavigationSmokeTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void App_launches_and_navigates_through_primary_pages()
    {
        using var dataDirectory = new TempDirectory("data");
        var diagnosticsDirectory = Path.Combine(
            RepositoryPaths.Root,
            "TestResults",
            "ui-smoke");
        Directory.CreateDirectory(diagnosticsDirectory);
        var startupDiagnosticsPath = Path.Combine(diagnosticsDirectory, "startup.log");
        foreach (var fileName in new[] { "startup.log", "failure.png", "uia-tree.txt" })
        {
            File.Delete(Path.Combine(diagnosticsDirectory, fileName));
        }
        var executablePath = RepositoryPaths.GetUiExecutablePath();
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false
        };
        startInfo.Environment["KOUKEI_USER_DATA_HOME"] = dataDirectory.Path;
        startInfo.Environment["KOUKEI_UI_STARTUP_DIAGNOSTICS"] = startupDiagnosticsPath;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to launch '{executablePath}'.");
        using var application = Application.Attach(process.Id);
        using var automation = new UIA3Automation();
        Window? window = null;

        try
        {
            window = WaitFor(
                () => application.GetMainWindow(automation, TimeSpan.FromMilliseconds(100)),
                StartupTimeout,
                process,
                "the Koukei main window",
                startupDiagnosticsPath);

            Assert.Contains("Koukei", window.Title, StringComparison.OrdinalIgnoreCase);
            Assert.False(window.IsOffscreen, "The Koukei main window is not visible.");
            WaitForId(window, "PageHome", NavigationTimeout, process);
            WaitForId(window, "HomeOpenFiles", NavigationTimeout, process);

            SelectNavigationItem(window, "NavVideoLibrary", process);
            WaitForId(window, "PageVideoLibrary", NavigationTimeout, process);
            WaitForId(window, "LibrarySearchBox", NavigationTimeout, process);

            SelectNavigationItem(window, "NavAudioLibrary", process);
            WaitForId(window, "PageAudioLibrary", NavigationTimeout, process);
            WaitForId(window, "LibrarySearchBox", NavigationTimeout, process);

            SelectNavigationItem(window, "NavPlaylists", process);
            WaitForId(window, "PagePlaylists", NavigationTimeout, process);
            WaitForId(window, "CreatePlaylist", NavigationTimeout, process);

            SelectNavigationItem(window, "NavSettings", process);
            WaitForId(window, "PageSettings", NavigationTimeout, process);
            WaitForId(window, "ThemeSelector", NavigationTimeout, process);
        }
        catch
        {
            TryWriteFailureDiagnostics(window);
            throw;
        }
        finally
        {
            StopProcess(process);
        }
    }

    private static AutomationElement WaitForId(
        Window window,
        string automationId,
        TimeSpan timeout,
        Process process) => WaitFor(
            () => window.FindFirstDescendant(
                condition => condition.ByAutomationId(automationId)),
            timeout,
            process,
            $"AutomationId '{automationId}'");

    private static void SelectNavigationItem(Window window, string automationId, Process process)
    {
        var element = WaitForId(window, automationId, NavigationTimeout, process);
        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        throw new XunitException(
            $"Navigation element '{automationId}' supports neither SelectionItem nor Invoke.");
    }

    private static T WaitFor<T>(
        Func<T?> probe,
        TimeSpan timeout,
        Process process,
        string description,
        string? startupDiagnosticsPath = null)
        where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;

        while (stopwatch.Elapsed < timeout)
        {
            if (process.HasExited)
            {
                var startupDiagnostics = ReadStartupDiagnostics(startupDiagnosticsPath);
                throw new XunitException(
                    $"Koukei exited with code {process.ExitCode} while waiting for {description}." +
                    startupDiagnostics);
            }

            try
            {
                var result = probe();
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(100);
        }

        throw new XunitException(
            $"Timed out after {timeout.TotalSeconds:0} seconds waiting for {description}." +
            (lastError is null ? string.Empty : $" Last UI Automation error: {lastError.Message}"));
    }

    private static string ReadStartupDiagnostics(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return Environment.NewLine + "Startup diagnostics:" + Environment.NewLine +
                File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryWriteFailureDiagnostics(Window? window)
    {
        if (window is null)
        {
            return;
        }

        var outputDirectory = Path.Combine(
            RepositoryPaths.Root,
            "TestResults",
            "ui-smoke");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            Capture.Element(window).ToFile(Path.Combine(outputDirectory, "failure.png"));
        }
        catch
        {
            // Continue so the UI Automation tree can still be captured.
        }

        try
        {
            var tree = window.FindAllDescendants()
                .Select(element =>
                    $"{element.ControlType} | Name={element.Name} | AutomationId={element.AutomationId}")
                .ToArray();
            File.WriteAllLines(Path.Combine(outputDirectory, "uia-tree.txt"), tree);
        }
        catch
        {
            // Keep the original test failure when UI tree capture is unavailable.
        }
    }

    private static void StopProcess(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.CloseMainWindow();
        if (!process.WaitForExit(5_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
    }
}
