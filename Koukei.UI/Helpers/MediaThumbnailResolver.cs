using Koukei.Audio;
using Koukei.Bus.Models;
using Koukei.Video;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Koukei.UI.Helpers;

internal static class MediaThumbnailResolver
{
    private const string ThumbnailCacheFolderName = "MediaThumbnails";
    private const string AudioThumbnailCachePrefix = "ffmpeg-";

    public static bool IsCurrentAudioThumbnail(string sourcePath, string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            return false;
        }

        var expectedFolder = Path.Combine(
            ApplicationData.Current.LocalCacheFolder.Path,
            ThumbnailCacheFolderName);
        return string.Equals(
                Path.GetDirectoryName(thumbnailPath),
                expectedFolder,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Path.GetFileNameWithoutExtension(thumbnailPath),
                CreateAudioCacheFileStem(sourcePath),
                StringComparison.OrdinalIgnoreCase);
    }

    public static string GetAudioMissingMarkerPath(string sourcePath)
    {
        return Path.Combine(
            ApplicationData.Current.LocalCacheFolder.Path,
            ThumbnailCacheFolderName,
            $"{CreateAudioCacheFileStem(sourcePath)}.none");
    }

    public static async Task<string?> ResolveOrCreateAsync(
        MediaLibraryItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
        {
            return null;
        }

        if (item.Kind == MediaLibraryItemKind.Video)
        {
            var expectedPath = VideoThumbnailCache.GetPath(item.Path);
            if (string.Equals(item.ThumbnailPath, expectedPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(expectedPath))
            {
                return expectedPath;
            }

            return await TryCreateVideoAsync(item.Path, cancellationToken);
        }

        if (item.Kind == MediaLibraryItemKind.Audio)
        {
            if (IsCurrentAudioThumbnail(item.Path, item.ThumbnailPath) &&
                File.Exists(item.ThumbnailPath))
            {
                return item.ThumbnailPath;
            }

            return await TryCreateAudioAsync(item.Path, metadata: null, cancellationToken);
        }

        return !string.IsNullOrWhiteSpace(item.ThumbnailPath) &&
            File.Exists(item.ThumbnailPath)
                ? item.ThumbnailPath
                : null;
    }

    public static async Task<string?> TryCreateVideoAsync(
        string? sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !File.Exists(sourcePath) ||
            App.Services is null)
        {
            return null;
        }

        var thumbnailService = App.Services.GetService<IVideoThumbnailService>();
        if (thumbnailService is null)
        {
            return null;
        }

        var outputPath = VideoThumbnailCache.GetPath(sourcePath);
        VideoThumbnailCache.EnsureDirectoryExists(outputPath);
        try
        {
            return await thumbnailService.CreateVideoThumbnailAsync(
                sourcePath,
                outputPath,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to create video thumbnail for '{sourcePath}': {ex.Message}");
            return null;
        }
    }

    public static async Task<string?> TryCreateAudioAsync(
        string? sourcePath,
        AudioFileMetadata? metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !File.Exists(sourcePath) ||
            App.Services is null)
        {
            return null;
        }

        try
        {
            var missingMarkerPath = GetAudioMissingMarkerPath(sourcePath);
            if (metadata is null && File.Exists(missingMarkerPath))
            {
                return null;
            }

            metadata ??= await App.Services
                .GetRequiredService<IAudioMetadataService>()
                .GetMetadataAsync(sourcePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var cacheFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
                ThumbnailCacheFolderName,
                CreationCollisionOption.OpenIfExists);
            if (metadata.AlbumArt is not { Length: > 0 } albumArt)
            {
                await File.WriteAllBytesAsync(missingMarkerPath, [], cancellationToken);
                return null;
            }

            var outputPath = Path.Combine(
                cacheFolder.Path,
                $"{CreateAudioCacheFileStem(sourcePath)}{GetAlbumArtFileExtension(albumArt)}");
            await File.WriteAllBytesAsync(outputPath, albumArt, cancellationToken);
            if (File.Exists(missingMarkerPath))
            {
                File.Delete(missingMarkerPath);
            }

            return outputPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to create audio thumbnail for '{sourcePath}': {ex.Message}");
            return null;
        }
    }

    public static Task<string?> TryCreateAudioFromPlaybackMetadataAsync(
        string? sourcePath,
        AudioMediaMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return TryCreateAudioAsync(
            sourcePath,
            new AudioFileMetadata(
                metadata.FilePath,
                metadata.Title,
                metadata.Artist,
                metadata.Album,
                Duration: null,
                FormatName: null,
                CodecName: null,
                ChannelCount: null,
                SampleRate: null,
                BitsPerSample: null,
                BitRate: null,
                AlbumArt: metadata.AlbumArt,
                Lyrics: metadata.Lyrics),
            cancellationToken);
    }

    private static string CreateAudioCacheFileStem(string sourcePath)
    {
        var lastModifiedTicks = TryGetLastModified(sourcePath)?.UtcTicks ?? 0L;
        var cacheKey = $"ffmpeg|{sourcePath}|{lastModifiedTicks}";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLowerInvariant();
        return $"{AudioThumbnailCachePrefix}{hash}";
    }

    private static DateTimeOffset? TryGetLastModified(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static string GetAlbumArtFileExtension(ReadOnlySpan<byte> albumArt)
    {
        if (albumArt.Length >= 8 &&
            albumArt[0] == 0x89 &&
            albumArt[1] == 0x50 &&
            albumArt[2] == 0x4E &&
            albumArt[3] == 0x47)
        {
            return ".png";
        }

        if (albumArt.Length >= 3 &&
            albumArt[0] == 0xFF &&
            albumArt[1] == 0xD8 &&
            albumArt[2] == 0xFF)
        {
            return ".jpg";
        }

        if (albumArt.Length >= 6 &&
            (albumArt[..6].SequenceEqual("GIF87a"u8) ||
             albumArt[..6].SequenceEqual("GIF89a"u8)))
        {
            return ".gif";
        }

        if (albumArt.Length >= 2 &&
            albumArt[0] == 0x42 &&
            albumArt[1] == 0x4D)
        {
            return ".bmp";
        }

        if (albumArt.Length >= 12 &&
            albumArt[..4].SequenceEqual("RIFF"u8) &&
            albumArt.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return ".jpg";
    }
}
