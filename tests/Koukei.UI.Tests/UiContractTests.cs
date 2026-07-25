using System.Xml.Linq;
using Koukei.UI.Tests.Infrastructure;

namespace Koukei.UI.Tests;

public sealed class UiContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void All_source_xaml_files_are_well_formed()
    {
        var files = GetSourceXamlFiles();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            Assert.NotNull(document.Root);
        }
    }

    [Fact]
    public void English_and_chinese_resources_have_identical_keys()
    {
        var english = LoadResourceKeys("en-US");
        var chinese = LoadResourceKeys("zh-CN");

        Assert.Equal(english, chinese);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void Resource_entries_have_unique_nonempty_keys_and_values(string language)
    {
        var entries = LoadResourceEntries(language);

        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key), $"{language} has an empty resource key.");
            Assert.False(
                string.IsNullOrWhiteSpace(entry.Value),
                $"{language} resource '{entry.Key}' has an empty value.");
        });

        var duplicateKeys = entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(duplicateKeys);
    }

    [Fact]
    public void Every_xaml_uid_resolves_in_both_languages()
    {
        var english = LoadResourceKeys("en-US");
        var chinese = LoadResourceKeys("zh-CN");
        var uids = GetSourceXamlFiles()
            .Select(XDocument.Load)
            .SelectMany(document => document.Root!.DescendantsAndSelf())
            .Select(element => (string?)element.Attribute(XamlNamespace + "Uid"))
            .Where(uid => !string.IsNullOrWhiteSpace(uid))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(uid => uid, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(uids);
        foreach (var uid in uids)
        {
            Assert.True(HasResourceForUid(english, uid), $"en-US is missing x:Uid '{uid}'.");
            Assert.True(HasResourceForUid(chinese, uid), $"zh-CN is missing x:Uid '{uid}'.");
        }
    }

    [Fact]
    public void Primary_navigation_and_pages_expose_stable_automation_ids()
    {
        var expectedByFile = new Dictionary<string, string[]>
        {
            ["MainWindow.xaml"] =
            [
                "AppShell",
                "PrimaryNavigation",
                "NavHome",
                "NavVideoLibrary",
                "NavAudioLibrary",
                "NavPlaylists"
            ],
            ["Pages/HomePage.xaml"] = ["PageHome", "HomeOpenFiles"],
            ["Pages/MediaLibraryPage.xaml"] = ["PageMediaLibrary", "LibrarySearchBox"],
            ["Pages/PlaylistsPage.xaml"] = ["PagePlaylists", "CreatePlaylist"],
            ["Pages/SettingsPage.xaml"] = ["PageSettings", "ThemeSelector"]
        };

        foreach (var (relativePath, expectedIds) in expectedByFile)
        {
            var document = XDocument.Load(Path.Combine(
                RepositoryPaths.UiProjectDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var actualIds = document
                .Descendants()
                .Attributes()
                .Where(attribute =>
                    attribute.Name.LocalName.Equals(
                        "AutomationProperties.AutomationId",
                        StringComparison.Ordinal))
                .Select(attribute => attribute.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var expectedId in expectedIds)
            {
                Assert.Contains(expectedId, actualIds);
            }
        }

        var mediaLibraryCode = File.ReadAllText(Path.Combine(
            RepositoryPaths.UiProjectDirectory,
            "Pages",
            "MediaLibraryPage.xaml.cs"));
        Assert.Contains("\"PageVideoLibrary\"", mediaLibraryCode, StringComparison.Ordinal);
        Assert.Contains("\"PageAudioLibrary\"", mediaLibraryCode, StringComparison.Ordinal);

        var mainWindowCode = File.ReadAllText(Path.Combine(
            RepositoryPaths.UiProjectDirectory,
            "MainWindow.xaml.cs"));
        Assert.Contains("\"NavSettings\"", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_automation_ids_are_nonempty_and_globally_unique()
    {
        var ids = GetSourceXamlFiles()
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants().Attributes())
            .Where(attribute => attribute.Name.LocalName.Equals(
                "AutomationProperties.AutomationId",
                StringComparison.Ordinal))
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));

        var duplicateIds = ids
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(duplicateIds);
    }

    private static string[] GetSourceXamlFiles() => Directory
        .EnumerateFiles(RepositoryPaths.UiProjectDirectory, "*.xaml", SearchOption.AllDirectories)
        .Where(path =>
            !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string[] LoadResourceKeys(string language)
    {
        var resourcePath = Path.Combine(
            RepositoryPaths.UiProjectDirectory,
            "Strings",
            language,
            "Resources.resw");
        return XDocument.Load(resourcePath)
            .Root!
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static (string Key, string Value)[] LoadResourceEntries(string language)
    {
        var resourcePath = Path.Combine(
            RepositoryPaths.UiProjectDirectory,
            "Strings",
            language,
            "Resources.resw");
        return XDocument.Load(resourcePath)
            .Root!
            .Elements("data")
            .Select(element => (
                Key: (string?)element.Attribute("name") ?? string.Empty,
                Value: element.Element("value")?.Value ?? string.Empty))
            .ToArray();
    }

    private static bool HasResourceForUid(IEnumerable<string> keys, string uid) =>
        keys.Any(key =>
            key.Equals(uid, StringComparison.Ordinal) ||
            key.StartsWith(uid + ".", StringComparison.Ordinal));
}
