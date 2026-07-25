using Koukei.Core.Tests.Infrastructure;
using Koukei.Video;

namespace Koukei.Core.Tests;

public sealed class VideoSubtitleSidecarTests
{
    [Fact]
    public void FindMatch_prefers_exact_name_and_extension_priority()
    {
        using var temp = new TempDirectory();
        var mediaPath = temp.GetPath("movie.mkv");
        var exactAss = temp.GetPath("movie.ass");
        var exactSrt = temp.GetPath("movie.srt");
        File.WriteAllText(exactAss, "ass");
        File.WriteAllText(exactSrt, "srt");
        File.WriteAllText(temp.GetPath("movie.en.srt"), "language");

        var match = VideoSubtitleSidecar.FindMatch(mediaPath);

        Assert.Equal(System.IO.Path.GetFullPath(exactSrt), match);
    }

    [Fact]
    public void FindMatch_uses_language_sidecar_when_exact_name_is_absent()
    {
        using var temp = new TempDirectory();
        var mediaPath = temp.GetPath("movie.mkv");
        var expected = temp.GetPath("movie.zh-CN.ass");
        File.WriteAllText(temp.GetPath("movie.en.vtt"), "vtt");
        File.WriteAllText(expected, "ass");

        var match = VideoSubtitleSidecar.FindMatch(mediaPath);

        Assert.Equal(System.IO.Path.GetFullPath(expected), match);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("missing/movie.mkv")]
    public void FindMatch_returns_null_for_invalid_or_missing_locations(string mediaPath)
    {
        Assert.Null(VideoSubtitleSidecar.FindMatch(mediaPath));
    }
}
