using FFmpeg.AutoGen.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Koukei.Ffmpeg;

public sealed class FfmpegMediaProbe : IFfmpegMediaProbe
{
    private const int MaximumStreamCount = 1024;
    private const int MaximumProbeAttachedPictureBytes = 32 * 1024 * 1024;
    private const int MaximumCacheEntryCount = 64;
    private const long MaximumCachePayloadBytes = MaximumProbeAttachedPictureBytes;
    private const int TextBufferSize = 1024;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheWriteLock = new();
    private long _cachePayloadBytes;
    private long _cacheSequence;

    public async Task<FfmpegMediaInfo> ProbeAsync(
        string filePath,
        CancellationToken cancellationToken = default,
        bool includeAttachedPictures = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(filePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The media file does not exist.", fullPath);
        }

        var cachePath = GetCachePath(fullPath, includeAttachedPictures);
        var cacheKey = new FileIdentity(file.Length, file.LastWriteTimeUtc);
        if (_cache.TryGetValue(cachePath, out var cached) && cached.Identity == cacheKey)
        {
            return cached.MediaInfo;
        }

        await FfmpegRuntime.NativeOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            file.Refresh();
            if (!file.Exists)
            {
                throw new FileNotFoundException("The media file does not exist.", fullPath);
            }

            cacheKey = new FileIdentity(file.Length, file.LastWriteTimeUtc);
            if (_cache.TryGetValue(cachePath, out cached) && cached.Identity == cacheKey)
            {
                return cached.MediaInfo;
            }

            var mediaInfo = await Task.Run(
                () => ProbeCore(
                    fullPath,
                    file.Length,
                    includeAttachedPictures,
                    cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
            StoreCacheEntry(cachePath, cacheKey, mediaInfo);
            return mediaInfo;
        }
        finally
        {
            FfmpegRuntime.NativeOperationGate.Release();
        }
    }

    private void StoreCacheEntry(
        string fullPath,
        FileIdentity identity,
        FfmpegMediaInfo mediaInfo)
    {
        lock (_cacheWriteLock)
        {
            if (_cache.TryGetValue(fullPath, out var previousEntry))
            {
                _cachePayloadBytes -= previousEntry.PayloadBytes;
            }

            var payloadBytes = GetCachePayloadBytes(mediaInfo);
            _cache[fullPath] = new CacheEntry(
                identity,
                mediaInfo,
                Interlocked.Increment(ref _cacheSequence),
                payloadBytes);
            _cachePayloadBytes += payloadBytes;
            TrimCacheCore();
        }
    }

    private void TrimCacheCore()
    {
        while (_cache.Count > MaximumCacheEntryCount ||
               _cachePayloadBytes > MaximumCachePayloadBytes)
        {
            var oldestEntry = _cache.ToArray().MinBy(static pair => pair.Value.Sequence);
            if (string.IsNullOrEmpty(oldestEntry.Key) ||
                !_cache.TryGetValue(oldestEntry.Key, out var currentEntry) ||
                currentEntry.Sequence != oldestEntry.Value.Sequence ||
                !_cache.TryRemove(oldestEntry.Key, out var removedEntry))
            {
                break;
            }

            _cachePayloadBytes = Math.Max(0, _cachePayloadBytes - removedEntry.PayloadBytes);
        }
    }

    private static long GetCachePayloadBytes(FfmpegMediaInfo mediaInfo)
    {
        long payloadBytes = 0;
        foreach (var stream in mediaInfo.Streams)
        {
            payloadBytes += stream.AttachedPicture?.LongLength ?? 0;
        }

        return payloadBytes;
    }

    private static unsafe FfmpegMediaInfo ProbeCore(
        string filePath,
        long fileSize,
        bool includeAttachedPictures,
        CancellationToken cancellationToken)
    {
        FfmpegRuntime.EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();

        var interruptState = new InterruptState(
            cancellationToken,
            Stopwatch.GetTimestamp() + (long)(ProbeTimeout.TotalSeconds * Stopwatch.Frequency));
        var interruptHandle = GCHandle.Alloc(interruptState);
        AVFormatContext* formatContext = null;

        try
        {
            formatContext = ffmpeg.avformat_alloc_context();
            if (formatContext == null)
            {
                throw new OutOfMemoryException("FFmpeg could not allocate a format context.");
            }

            formatContext->interrupt_callback.callback = NativeCallbacks.Interrupt;
            formatContext->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(interruptHandle);

            var error = ffmpeg.avformat_open_input(&formatContext, filePath, null, null);
            ThrowIfProbeFailed("FFmpeg could not open the media file", error, interruptState);

            error = ffmpeg.avformat_find_stream_info(formatContext, null);
            ThrowIfProbeFailed("FFmpeg could not read the media streams", error, interruptState);

            cancellationToken.ThrowIfCancellationRequested();
            if (interruptState.HasTimedOut)
            {
                throw new TimeoutException($"Timed out while reading media information for '{filePath}'.");
            }

            return ReadMediaInfo(
                formatContext,
                filePath,
                fileSize,
                includeAttachedPictures);
        }
        finally
        {
            if (formatContext != null)
            {
                ffmpeg.avformat_close_input(&formatContext);
            }

            interruptHandle.Free();
        }
    }

    private static unsafe FfmpegMediaInfo ReadMediaInfo(
        AVFormatContext* formatContext,
        string filePath,
        long fileSize,
        bool includeAttachedPictures)
    {
        var duration = GetDuration(formatContext->duration, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE });
        var streams = new List<FfmpegMediaStreamInfo>(
            (int)Math.Min(formatContext->nb_streams, MaximumStreamCount));
        var streamCount = (int)Math.Min(formatContext->nb_streams, MaximumStreamCount);
        var remainingAttachedPictureBytes = includeAttachedPictures
            ? MaximumProbeAttachedPictureBytes
            : 0;

        for (var index = 0; index < streamCount; index++)
        {
            var stream = formatContext->streams[index];
            if (stream == null || stream->codecpar == null)
            {
                continue;
            }

            streams.Add(ReadStream(stream, ref remainingAttachedPictureBytes));
        }

        duration ??= streams
            .Where(static stream => !stream.IsAttachedPicture)
            .Select(static stream => stream.Duration)
            .Where(static value => value.HasValue)
            .Max();

        return new FfmpegMediaInfo(
            filePath,
            fileSize,
            duration,
            PositiveOrNull(formatContext->bit_rate),
            GetString(formatContext->iformat == null ? null : formatContext->iformat->name),
            GetString(formatContext->iformat == null ? null : formatContext->iformat->long_name),
            ReadTags(formatContext->metadata),
            streams);
    }

    private static unsafe FfmpegMediaStreamInfo ReadStream(
        AVStream* stream,
        ref int remainingAttachedPictureBytes)
    {
        var parameters = stream->codecpar;
        var isAttachedPicture = (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0;
        var kind = GetStreamKind(parameters->codec_type);
        var frameRate = GetFrameRate(stream->avg_frame_rate)
            ?? GetFrameRate(stream->r_frame_rate)
            ?? GetFrameRate(parameters->framerate);
        var tags = ReadTags(stream->metadata);
        var rotation = GetRotation(parameters, tags);
        var pixelFormat = parameters->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && parameters->format >= 0
            ? NullIfWhiteSpace(ffmpeg.av_get_pix_fmt_name((AVPixelFormat)parameters->format))
            : null;
        var sampleFormat = parameters->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && parameters->format >= 0
            ? NullIfWhiteSpace(ffmpeg.av_get_sample_fmt_name((AVSampleFormat)parameters->format))
            : null;

        return new FfmpegMediaStreamInfo(
            stream->index,
            stream->id,
            kind,
            NullIfWhiteSpace(ffmpeg.avcodec_get_name(parameters->codec_id)),
            GetCodecDescription(parameters->codec_id),
            parameters->profile >= 0
                ? NullIfWhiteSpace(ffmpeg.avcodec_profile_name(parameters->codec_id, parameters->profile))
                : null,
            GetDuration(stream->duration, stream->time_base),
            PositiveOrNull(parameters->bit_rate),
            PositiveOrNull(parameters->width),
            PositiveOrNull(parameters->height),
            frameRate,
            pixelFormat,
            rotation,
            PositiveOrNull(parameters->ch_layout.nb_channels),
            GetChannelLayout(parameters),
            PositiveOrNull(parameters->sample_rate),
            GetBitsPerSample(parameters),
            sampleFormat,
            (stream->disposition & ffmpeg.AV_DISPOSITION_DEFAULT) != 0,
            isAttachedPicture,
            tags,
            isAttachedPicture
                ? CopyAttachedPicture(stream->attached_pic, ref remainingAttachedPictureBytes)
                : null);
    }

    private static unsafe string? GetCodecDescription(AVCodecID codecId)
    {
        var descriptor = ffmpeg.avcodec_descriptor_get(codecId);
        return descriptor == null ? null : GetString(descriptor->long_name);
    }

    private static unsafe string? GetChannelLayout(AVCodecParameters* parameters)
    {
        if (parameters->ch_layout.nb_channels <= 0)
        {
            return null;
        }

        var buffer = stackalloc byte[TextBufferSize];
        return ffmpeg.av_channel_layout_describe(&parameters->ch_layout, buffer, TextBufferSize) >= 0
            ? GetString(buffer)
            : null;
    }

    private static unsafe int? GetBitsPerSample(AVCodecParameters* parameters)
    {
        if (parameters->bits_per_raw_sample > 0)
        {
            return parameters->bits_per_raw_sample;
        }

        if (parameters->bits_per_coded_sample > 0)
        {
            return parameters->bits_per_coded_sample;
        }

        return PositiveOrNull(ffmpeg.av_get_bits_per_sample(parameters->codec_id));
    }

    private static unsafe int? GetRotation(
        AVCodecParameters* parameters,
        IReadOnlyDictionary<string, string> tags)
    {
        var sideData = ffmpeg.av_packet_side_data_get(
            parameters->coded_side_data,
            parameters->nb_coded_side_data,
            AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX);
        if (sideData != null && sideData->data != null && sideData->size >= (ulong)sizeof(int9))
        {
            var matrix = *(int9*)sideData->data;
            var counterClockwiseRotation = ffmpeg.av_display_rotation_get(in matrix);
            if (double.IsFinite(counterClockwiseRotation))
            {
                return NormalizeRotation(-(int)Math.Round(counterClockwiseRotation));
            }
        }

        return tags.TryGetValue("rotate", out var rawRotation) &&
               int.TryParse(rawRotation, out var rotation)
            ? NormalizeRotation(rotation)
            : null;
    }

    private static int NormalizeRotation(int rotation)
    {
        rotation %= 360;
        return rotation < 0 ? rotation + 360 : rotation;
    }

    private static unsafe byte[]? CopyAttachedPicture(
        AVPacket packet,
        ref int remainingAttachedPictureBytes)
    {
        if (packet.data == null ||
            packet.size <= 0 ||
            packet.size > remainingAttachedPictureBytes)
        {
            return null;
        }

        var picture = new byte[packet.size];
        Marshal.Copy((nint)packet.data, picture, 0, packet.size);
        remainingAttachedPictureBytes -= packet.size;
        return picture;
    }

    private static unsafe IReadOnlyDictionary<string, string> ReadTags(AVDictionary* dictionary)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AVDictionaryEntry* entry = null;
        while ((entry = ffmpeg.av_dict_get(
                   dictionary,
                   string.Empty,
                   entry,
                   ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
        {
            var key = GetString(entry->key);
            var value = GetString(entry->value);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                tags[key] = value;
            }
        }

        return tags;
    }

    private static FfmpegMediaStreamKind GetStreamKind(AVMediaType mediaType)
    {
        return mediaType switch
        {
            AVMediaType.AVMEDIA_TYPE_VIDEO => FfmpegMediaStreamKind.Video,
            AVMediaType.AVMEDIA_TYPE_AUDIO => FfmpegMediaStreamKind.Audio,
            AVMediaType.AVMEDIA_TYPE_SUBTITLE => FfmpegMediaStreamKind.Subtitle,
            AVMediaType.AVMEDIA_TYPE_DATA => FfmpegMediaStreamKind.Data,
            AVMediaType.AVMEDIA_TYPE_ATTACHMENT => FfmpegMediaStreamKind.Attachment,
            _ => FfmpegMediaStreamKind.Unknown
        };
    }

    private static TimeSpan? GetDuration(long value, AVRational timeBase)
    {
        if (value == ffmpeg.AV_NOPTS_VALUE || value <= 0 || timeBase.num <= 0 || timeBase.den <= 0)
        {
            return null;
        }

        var seconds = value * ffmpeg.av_q2d(timeBase);
        return double.IsFinite(seconds) && seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static double? GetFrameRate(AVRational value)
    {
        if (value.num <= 0 || value.den <= 0)
        {
            return null;
        }

        var frameRate = ffmpeg.av_q2d(value);
        return double.IsFinite(frameRate) && frameRate > 0 ? frameRate : null;
    }

    private static unsafe string? GetString(byte* value)
    {
        return value == null ? null : NullIfWhiteSpace(Marshal.PtrToStringUTF8((nint)value));
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? PositiveOrNull(int value) => value > 0 ? value : null;

    private static long? PositiveOrNull(long value) => value > 0 ? value : null;

    private static string GetCachePath(string fullPath, bool includeAttachedPictures) =>
        $"{(includeAttachedPictures ? '1' : '0')}|{fullPath}";

    private static void ThrowIfProbeFailed(string message, int error, InterruptState state)
    {
        if (error >= 0)
        {
            return;
        }

        state.CancellationToken.ThrowIfCancellationRequested();
        if (state.HasTimedOut)
        {
            throw new TimeoutException(message);
        }

        throw new FfmpegException(message, error);
    }

    private static unsafe int InterruptCallback(void* opaque)
    {
        if (opaque == null)
        {
            return 0;
        }

        try
        {
            var handle = GCHandle.FromIntPtr((nint)opaque);
            return handle.Target is InterruptState state &&
                   (state.CancellationToken.IsCancellationRequested || state.HasTimedOut)
                ? 1
                : 0;
        }
        catch
        {
            return 1;
        }
    }

    private readonly record struct CacheEntry(
        FileIdentity Identity,
        FfmpegMediaInfo MediaInfo,
        long Sequence,
        long PayloadBytes);

    private readonly record struct FileIdentity(long Length, DateTime LastWriteTimeUtc);

    private sealed class InterruptState(CancellationToken cancellationToken, long deadlineTimestamp)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;

        public bool HasTimedOut => Stopwatch.GetTimestamp() >= deadlineTimestamp;
    }

    private static unsafe class NativeCallbacks
    {
        internal static readonly AVIOInterruptCB_callback Interrupt = InterruptCallback;
    }
}
