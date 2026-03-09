using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Koukei.UI.Helpers;

internal static class VideoThumbnailCache
{
    private const string CacheFolderName = "MediaThumbnails";
    private const string CacheFilePrefix = "ffmpeg-smart-";
    private const string LegacyCacheSearchPattern = "ffmpeg-smart-v*-*.png";
    private static int _legacyCacheCleanupStarted;

    public static string GetPath(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var lastModifiedTicks = TryGetLastModifiedTicks(sourcePath);
        var cacheKey = $"ffmpeg-smart|{sourcePath}|{lastModifiedTicks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))
            .ToLowerInvariant();
        return Path.Combine(
            DataLocationHelper.CacheLocation,
            CacheFolderName,
            $"{CacheFilePrefix}{hash}.png");
    }

    public static void EnsureDirectoryExists(string cachePath)
    {
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            ClearLegacyVersionedCacheOnce(directory);
        }
    }

    private static void ClearLegacyVersionedCacheOnce(string directory)
    {
        if (Interlocked.Exchange(ref _legacyCacheCleanupStarted, 1) != 0)
        {
            return;
        }

        try
        {
            foreach (var cacheFile in Directory.EnumerateFiles(
                directory,
                LegacyCacheSearchPattern,
                SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(cacheFile);
                }
                catch
                {
                    // Cache cleanup is best-effort and must not block thumbnail generation.
                }
            }
        }
        catch
        {
            // Cache cleanup is best-effort and must not block thumbnail generation.
        }
    }

    private static long TryGetLastModifiedTicks(string sourcePath)
    {
        try
        {
            return File.Exists(sourcePath)
                ? File.GetLastWriteTimeUtc(sourcePath).Ticks
                : 0L;
        }
        catch
        {
            return 0L;
        }
    }
}
