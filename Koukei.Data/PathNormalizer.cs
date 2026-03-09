namespace Koukei.Data;

internal static class PathNormalizer
{
    public static string? NormalizeNullable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Normalize(path);
    }

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Trim();

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return uri.AbsoluteUri.TrimEnd('/').ToUpperInvariant();
        }

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch (ArgumentException)
        {
            normalized = normalized.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
        catch (NotSupportedException)
        {
            normalized = normalized.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        normalized = Path.TrimEndingDirectorySeparator(normalized);
        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }
}
