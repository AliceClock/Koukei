namespace Koukei.Video;

public static class VideoSubtitleSidecar
{
    private static readonly string[] Extensions =
        [".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup"];

    public static IReadOnlyList<string> SupportedExtensions => Extensions;

    public static string? FindMatch(string mediaFilePath)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
        {
            return null;
        }

        try
        {
            foreach (var extension in Extensions)
            {
                var candidate = Path.ChangeExtension(mediaFilePath, extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            var directoryPath = Path.GetDirectoryName(mediaFilePath);
            var mediaName = Path.GetFileNameWithoutExtension(mediaFilePath);
            if (string.IsNullOrWhiteSpace(directoryPath) ||
                string.IsNullOrWhiteSpace(mediaName) ||
                !Directory.Exists(directoryPath))
            {
                return null;
            }

            var languagePrefix = $"{mediaName}.";
            return Directory
                .EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Extensions.Contains(
                    Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase))
                .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(
                    languagePrefix,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Array.FindIndex(
                    Extensions,
                    extension => string.Equals(
                        extension,
                        Path.GetExtension(path),
                        StringComparison.OrdinalIgnoreCase)))
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(Path.GetFullPath)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            IOException or
            NotSupportedException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }
}
