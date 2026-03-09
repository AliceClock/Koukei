using Koukei.Video;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Koukei.UI.Helpers;

internal static class MediaInfoFormatter
{
    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is not { } value || value <= TimeSpan.Zero)
        {
            return "--";
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
            : $"{value.Minutes}:{value.Seconds:D2}";
    }

    public static string FormatResolution(VideoStreamMetadata? video)
    {
        return FormatResolution(video?.Width, video?.Height);
    }

    public static string FormatResolution(int? width, int? height)
    {
        return width is > 0 && height is > 0
            ? $"{width}x{height}"
            : "--";
    }

    public static string FormatCodec(string? codec, string? description, string? profile = null)
    {
        var primary = !string.IsNullOrWhiteSpace(description)
            ? description.Trim()
            : !string.IsNullOrWhiteSpace(codec) ? codec.Trim() : "--";
        var details = new[] { codec, profile }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Where(value => !string.Equals(value, primary, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return details.Length == 0 ? primary : $"{primary} ({string.Join(", ", details)})";
    }

    public static string FormatFrameRate(double? frameRate)
    {
        return frameRate is > 0 ? $"{frameRate:0.###} fps" : "--";
    }

    public static string FormatBitRate(long? bitRate)
    {
        if (bitRate is not > 0)
        {
            return "--";
        }

        return bitRate >= 1_000_000
            ? $"{bitRate.Value / 1_000_000d:0.##} Mbps"
            : $"{bitRate.Value / 1_000d:0.##} Kbps";
    }

    public static string FormatChannelCount(int? channelCount)
    {
        return channelCount is > 0 ? channelCount.Value.ToString() : "--";
    }

    public static string FormatSampleRate(int? sampleRate)
    {
        return sampleRate is > 0 ? $"{sampleRate.Value / 1000d:0.###} kHz" : "--";
    }

    public static string FormatBitsPerSample(int? bitsPerSample)
    {
        return bitsPerSample is > 0 ? $"{bitsPerSample.Value}-bit" : "--";
    }

    public static string? FormatRotation(int? rotation)
    {
        return rotation.HasValue ? $"{rotation.Value}°" : null;
    }

    public static string FormatAudioDetails(AudioStreamMetadata? audio)
    {
        if (audio is null)
        {
            return "--";
        }

        var details = new List<string>();
        if (audio.ChannelCount is > 0)
        {
            details.Add($"{audio.ChannelCount} ch");
        }
        else if (!string.IsNullOrWhiteSpace(audio.ChannelLayout))
        {
            details.Add(audio.ChannelLayout);
        }

        if (audio.SampleRate is > 0)
        {
            details.Add($"{audio.SampleRate / 1000d:0.###} kHz");
        }

        if (audio.BitRate is > 0)
        {
            details.Add(FormatBitRate(audio.BitRate));
        }

        return details.Count == 0 ? "--" : string.Join(" / ", details);
    }

    public static string FormatAudioDetails(
        int? channelCount,
        int? sampleRate,
        int? bitsPerSample,
        long? bitRate)
    {
        var details = new List<string>();
        if (channelCount is > 0)
        {
            details.Add($"{channelCount} ch");
        }

        if (sampleRate is > 0)
        {
            details.Add($"{sampleRate / 1000d:0.###} kHz");
        }

        if (bitsPerSample is > 0)
        {
            details.Add($"{bitsPerSample}-bit");
        }

        if (bitRate is > 0)
        {
            details.Add(FormatBitRate(bitRate));
        }

        return details.Count == 0 ? "--" : string.Join(" / ", details);
    }

    public static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(0, bytes);
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
    }
}
