using Koukei.Audio;
using Koukei.Core.Tests.Infrastructure;

namespace Koukei.Core.Tests;

public sealed class AudioLyricsLoaderTests
{
    [Fact]
    public void Parse_sorts_groups_and_offsets_synchronized_lines()
    {
        const string lyrics = """
            [ar:Koukei]
            [offset:+250]
            [00:02.00][00:01.00]First
            [00:01.00]Second
            [00:03.50]<00:03.60>Third
            """;

        var document = AudioLyricsLoader.Parse(lyrics, AudioLyricsSource.Embedded);

        Assert.True(document.IsSynchronized);
        Assert.Equal(AudioLyricsSource.Embedded, document.Source);
        Assert.Collection(
            document.Lines,
            line =>
            {
                Assert.Equal(TimeSpan.FromSeconds(1.25), line.Timestamp);
                Assert.Equal($"First{Environment.NewLine}Second", line.Text);
            },
            line =>
            {
                Assert.Equal(TimeSpan.FromSeconds(2.25), line.Timestamp);
                Assert.Equal("First", line.Text);
            },
            line =>
            {
                Assert.Equal(TimeSpan.FromSeconds(3.75), line.Timestamp);
                Assert.Equal("Third", line.Text);
            });
    }

    [Fact]
    public void Parse_returns_plain_lyrics_when_timestamps_are_absent()
    {
        var document = AudioLyricsLoader.Parse("[ti:Title]\nFirst line\nSecond line");

        Assert.False(document.IsSynchronized);
        Assert.Equal(["First line", "Second line"], document.Lines.Select(line => line.Text));
        Assert.All(document.Lines, line => Assert.Null(line.Timestamp));
    }

    [Fact]
    public async Task Load_prefers_explicit_linked_sidecar_then_automatic_sidecar()
    {
        using var temp = new TempDirectory();
        var mediaPath = temp.GetPath("track.flac");
        var automaticPath = temp.GetPath("track.lrc");
        var linkedPath = temp.GetPath("custom.lrc");
        await File.WriteAllTextAsync(automaticPath, "[00:01.00]Automatic");
        await File.WriteAllTextAsync(linkedPath, "[00:02.00]Linked");

        var linked = await AudioLyricsLoader.LoadAsync(mediaPath, "Embedded", linkedPath);
        File.Delete(linkedPath);
        var automatic = await AudioLyricsLoader.LoadAsync(mediaPath, "Embedded", linkedPath);

        Assert.Equal("Linked", Assert.Single(linked.Lines).Text);
        Assert.Equal(AudioLyricsSource.Sidecar, linked.Source);
        Assert.Equal("Automatic", Assert.Single(automatic.Lines).Text);
        Assert.Equal(AudioLyricsSource.Sidecar, automatic.Source);
    }

    [Fact]
    public async Task Load_falls_back_to_embedded_lyrics_when_no_sidecar_exists()
    {
        using var temp = new TempDirectory();

        var document = await AudioLyricsLoader.LoadAsync(
            temp.GetPath("missing.mp3"),
            "First\\nSecond");

        Assert.Equal(AudioLyricsSource.Embedded, document.Source);
        Assert.Equal(["First", "Second"], document.Lines.Select(line => line.Text));
    }
}
