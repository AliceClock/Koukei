using System.Text;
using System.Text.RegularExpressions;

namespace Koukei.Audio;

public enum AudioLyricsSource
{
    None,
    Sidecar,
    Embedded
}

public sealed record AudioLyricLine(TimeSpan? Timestamp, string Text);

public sealed record AudioLyricsDocument(
    IReadOnlyList<AudioLyricLine> Lines,
    bool IsSynchronized,
    AudioLyricsSource Source)
{
    public static AudioLyricsDocument Empty { get; } = new(
        Array.Empty<AudioLyricLine>(),
        IsSynchronized: false,
        AudioLyricsSource.None);
}

public static partial class AudioLyricsLoader
{
    private const int MaximumLyricsBytes = 2 * 1024 * 1024;
    private const int MaximumLyricsCharacters = 2 * 1024 * 1024;
    private const int MaximumLyricsLineCount = 20_000;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    static AudioLyricsLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<AudioLyricsDocument> LoadAsync(
        string filePath,
        string? embeddedLyrics,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var sidecarPath = Path.ChangeExtension(filePath, ".lrc");
        if (File.Exists(sidecarPath) && new FileInfo(sidecarPath).Length <= MaximumLyricsBytes)
        {
            var bytes = await File.ReadAllBytesAsync(sidecarPath, cancellationToken)
                .ConfigureAwait(false);
            var sidecar = bytes.Length <= MaximumLyricsBytes
                ? Parse(DecodeLyrics(bytes), AudioLyricsSource.Sidecar)
                : AudioLyricsDocument.Empty;
            if (sidecar.Lines.Count > 0)
            {
                return sidecar;
            }
        }

        return string.IsNullOrWhiteSpace(embeddedLyrics)
            ? AudioLyricsDocument.Empty
            : Parse(NormalizeEscapedLineBreaks(embeddedLyrics), AudioLyricsSource.Embedded);
    }

    public static AudioLyricsDocument Parse(
        string? content,
        AudioLyricsSource source = AudioLyricsSource.Embedded)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaximumLyricsCharacters)
        {
            return AudioLyricsDocument.Empty;
        }

        var parsedLines = new List<(TimeSpan Timestamp, string Text, int Order)>();
        var plainLines = new List<AudioLyricLine>();
        var offset = TimeSpan.Zero;
        var order = 0;
        var lines = NormalizeEscapedLineBreaks(content)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        for (var lineIndex = 0;
             lineIndex < lines.Length && lineIndex < MaximumLyricsLineCount;
             lineIndex++)
        {
            var rawLine = lines[lineIndex];
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var offsetMatch = OffsetRegex().Match(line);
            if (offsetMatch.Success &&
                int.TryParse(offsetMatch.Groups["milliseconds"].Value, out var offsetMilliseconds))
            {
                offset = TimeSpan.FromMilliseconds(offsetMilliseconds);
                continue;
            }

            var timestampMatches = TimestampRegex().Matches(line);
            if (timestampMatches.Count == 0)
            {
                if (!MetadataTagRegex().IsMatch(line))
                {
                    plainLines.Add(new AudioLyricLine(null, RemoveInlineTimestamps(line)));
                }
                continue;
            }

            var text = RemoveInlineTimestamps(TimestampRegex().Replace(line, string.Empty)).Trim();
            if (text.Length == 0)
            {
                text = "♪";
            }

            foreach (Match timestampMatch in timestampMatches)
            {
                if (parsedLines.Count >= MaximumLyricsLineCount)
                {
                    break;
                }

                if (TryParseTimestamp(timestampMatch, out var timestamp))
                {
                    parsedLines.Add((timestamp, text, order++));
                }
            }
        }

        if (parsedLines.Count == 0)
        {
            return plainLines.Count == 0
                ? AudioLyricsDocument.Empty
                : new AudioLyricsDocument(plainLines, IsSynchronized: false, source);
        }

        var synchronizedLines = parsedLines
            .Select(line => (
                Timestamp: line.Timestamp + offset < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : line.Timestamp + offset,
                line.Text,
                line.Order))
            .OrderBy(static line => line.Timestamp)
            .ThenBy(static line => line.Order)
            .GroupBy(static line => line.Timestamp)
            .Select(static group => new AudioLyricLine(
                group.Key,
                string.Join(
                    Environment.NewLine,
                    group.Select(static line => line.Text).Distinct(StringComparer.Ordinal))))
            .ToArray();

        return new AudioLyricsDocument(synchronizedLines, IsSynchronized: true, source);
    }

    private static bool TryParseTimestamp(Match match, out TimeSpan timestamp)
    {
        timestamp = default;
        if (!int.TryParse(match.Groups["minutes"].Value, out var minutes) ||
            !double.TryParse(
                match.Groups["seconds"].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds))
        {
            return false;
        }

        var hours = match.Groups["hours"].Success &&
                    int.TryParse(match.Groups["hours"].Value, out var parsedHours)
            ? parsedHours
            : 0;
        timestamp = TimeSpan.FromHours(hours) +
                    TimeSpan.FromMinutes(minutes) +
                    TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string DecodeLyrics(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return Encoding.UTF8.GetString(bytes.AsSpan(Encoding.UTF8.Preamble.Length));
        }
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
        {
            return Encoding.Unicode.GetString(bytes.AsSpan(Encoding.Unicode.Preamble.Length));
        }
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return Encoding.BigEndianUnicode.GetString(
                bytes.AsSpan(Encoding.BigEndianUnicode.Preamble.Length));
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(54936).GetString(bytes);
        }
    }

    private static string NormalizeEscapedLineBreaks(string content)
    {
        return !content.Contains('\n') && content.Contains("\\n", StringComparison.Ordinal)
            ? content.Replace("\\r\\n", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal)
            : content;
    }

    private static string RemoveInlineTimestamps(string text) =>
        InlineTimestampRegex().Replace(text, string.Empty).Trim();

    [GeneratedRegex(
        @"\[(?:(?<hours>\d{1,2}):)?(?<minutes>\d{1,3}):(?<seconds>\d{1,2}(?:[\.,]\d{1,3})?)\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(
        @"^\[offset:(?<milliseconds>[+-]?\d+)\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffsetRegex();

    [GeneratedRegex(
        @"^\[(?:ar|ti|al|by|length|re|ve):.*\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataTagRegex();

    [GeneratedRegex(
        @"<(?:(?:\d{1,2}):)?\d{1,3}:\d{1,2}(?:[\.,]\d{1,3})?>",
        RegexOptions.CultureInvariant)]
    private static partial Regex InlineTimestampRegex();
}
