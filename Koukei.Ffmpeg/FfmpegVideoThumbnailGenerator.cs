using FFmpeg.AutoGen.Abstractions;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Koukei.Ffmpeg;

public sealed class FfmpegVideoThumbnailGenerator : IFfmpegVideoThumbnailGenerator
{
    private static readonly TimeSpan ThumbnailTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GatePollingInterval = TimeSpan.FromMilliseconds(50);
    private const int BlankFrameAnalysisWidth = 96;
    private const int BlankFrameAnalysisHeight = 54;
    private const int MaximumBlankFrameScanCount = 240;
    private const byte NearBlackFrameBrightnessThreshold = 64;
    private const double MaximumBlackFrameBrightPixelRatio = 0.18;
    private const byte NearWhiteFrameBrightnessThreshold = 191;
    private const double MaximumWhiteFrameDarkPixelRatio = 0.18;
    private const double BlankFrameContrastReference = 55;
    private const double BlankFrameGradientReference = 32;
    private const double BlankFrameColorSpreadReference = 72;
    private const double MaximumBlankFrameVisualInformationScore = 0.55;

    public async Task<string?> CreateAsync(
        string filePath,
        string outputPath,
        TimeSpan? position = null,
        int maximumDimension = 512,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDimension, 2);

        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterGateAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return await Task.Run(
                () => CreateCore(
                    Path.GetFullPath(filePath),
                    Path.GetFullPath(outputPath),
                    position,
                    maximumDimension,
                    cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FFmpeg thumbnail generation failed for '{filePath}': {ex.Message}");
            return null;
        }
        finally
        {
            FfmpegRuntime.NativeOperationGate.Release();
        }
    }

    private static async Task<bool> TryEnterGateAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await FfmpegRuntime.NativeOperationGate
                    .WaitAsync(GatePollingInterval)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static unsafe string? CreateCore(
        string filePath,
        string outputPath,
        TimeSpan? position,
        int maximumDimension,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        FfmpegRuntime.EnsureInitialized();
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var interruptState = new InterruptState(
            cancellationToken,
            Stopwatch.GetTimestamp() + (long)(ThumbnailTimeout.TotalSeconds * Stopwatch.Frequency));
        var interruptHandle = GCHandle.Alloc(interruptState);
        AVFormatContext* formatContext = null;
        AVCodecContext* decoderContext = null;
        AVPacket* packet = null;
        AVFrame* decodedFrame = null;
        AVFrame* lastDecodedFrame = null;
        AVFrame* candidateFrame = null;

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
            if (!IsSuccessful("FFmpeg could not open the video", error, interruptState))
            {
                return null;
            }

            error = ffmpeg.avformat_find_stream_info(formatContext, null);
            if (!IsSuccessful("FFmpeg could not read the video streams", error, interruptState))
            {
                return null;
            }

            var attachedPictureResult = TryEncodeAttachedPicture(
                formatContext,
                outputPath,
                maximumDimension,
                interruptState);
            if (attachedPictureResult != null || interruptState.ShouldStop)
            {
                return attachedPictureResult;
            }

            AVCodec* decoder = null;
            var videoStreamIndex = ffmpeg.av_find_best_stream(
                formatContext,
                AVMediaType.AVMEDIA_TYPE_VIDEO,
                -1,
                -1,
                &decoder,
                0);
            if (!IsSuccessful("FFmpeg found no decodable video stream", videoStreamIndex, interruptState))
            {
                return null;
            }

            var videoStream = formatContext->streams[videoStreamIndex];
            if (videoStream == null || videoStream->codecpar == null || decoder == null)
            {
                return null;
            }

            decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
            if (decoderContext == null)
            {
                throw new OutOfMemoryException("FFmpeg could not allocate a video decoder.");
            }

            error = ffmpeg.avcodec_parameters_to_context(decoderContext, videoStream->codecpar);
            if (!IsSuccessful("FFmpeg could not configure the video decoder", error, interruptState))
            {
                return null;
            }

            decoderContext->thread_count = 1;
            error = ffmpeg.avcodec_open2(decoderContext, decoder, null);
            if (!IsSuccessful("FFmpeg could not open the video decoder", error, interruptState))
            {
                return null;
            }

            packet = ffmpeg.av_packet_alloc();
            decodedFrame = ffmpeg.av_frame_alloc();
            lastDecodedFrame = ffmpeg.av_frame_alloc();
            candidateFrame = ffmpeg.av_frame_alloc();
            if (packet == null || decodedFrame == null || lastDecodedFrame == null ||
                candidateFrame == null)
            {
                throw new OutOfMemoryException("FFmpeg could not allocate thumbnail decoding buffers.");
            }

            bool decoded;
            if (position is null)
            {
                _ = TryPrepareSeek(
                    formatContext,
                    decoderContext,
                    videoStream,
                    videoStreamIndex,
                    positionMicroseconds: 0,
                    out _);
                decoded = TryDecodeFirstNonBlankFrame(
                    formatContext,
                    decoderContext,
                    videoStreamIndex,
                    packet,
                    decodedFrame,
                    candidateFrame,
                    interruptState);
            }
            else
            {
                var targetMicroseconds = GetRequestedMicroseconds(position.Value);
                decoded = TryPrepareSeek(
                        formatContext,
                        decoderContext,
                        videoStream,
                        videoStreamIndex,
                        targetMicroseconds,
                        out var targetTimestamp) &&
                    TryDecodeFrameAtTimestamp(
                        formatContext,
                        decoderContext,
                        videoStreamIndex,
                        packet,
                        decodedFrame,
                        lastDecodedFrame,
                        candidateFrame,
                        targetTimestamp,
                        interruptState);
            }

            return decoded && !interruptState.ShouldStop
                ? EncodeFrame(
                    candidateFrame,
                    decoderContext,
                    outputPath,
                    maximumDimension,
                    interruptState)
                : null;
        }
        finally
        {
            if (packet != null)
            {
                ffmpeg.av_packet_free(&packet);
            }

            if (decodedFrame != null)
            {
                ffmpeg.av_frame_free(&decodedFrame);
            }

            if (lastDecodedFrame != null)
            {
                ffmpeg.av_frame_free(&lastDecodedFrame);
            }

            if (candidateFrame != null)
            {
                ffmpeg.av_frame_free(&candidateFrame);
            }

            if (decoderContext != null)
            {
                ffmpeg.avcodec_free_context(&decoderContext);
            }

            if (formatContext != null)
            {
                ffmpeg.avformat_close_input(&formatContext);
            }

            interruptHandle.Free();
        }
    }

    private static unsafe string? TryEncodeAttachedPicture(
        AVFormatContext* formatContext,
        string outputPath,
        int maximumDimension,
        InterruptState interruptState)
    {
        for (var streamIndex = 0u; streamIndex < formatContext->nb_streams; streamIndex++)
        {
            if (interruptState.ShouldStop)
            {
                return null;
            }

            var stream = formatContext->streams[streamIndex];
            if (stream == null || stream->codecpar == null ||
                (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0 ||
                stream->attached_pic.data == null || stream->attached_pic.size <= 0)
            {
                continue;
            }

            var decoder = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (decoder == null)
            {
                continue;
            }

            AVCodecContext* decoderContext = null;
            AVFrame* decodedFrame = null;
            try
            {
                decoderContext = ffmpeg.avcodec_alloc_context3(decoder);
                if (decoderContext == null ||
                    ffmpeg.avcodec_parameters_to_context(decoderContext, stream->codecpar) < 0 ||
                    ffmpeg.avcodec_open2(decoderContext, decoder, null) < 0)
                {
                    continue;
                }

                decodedFrame = ffmpeg.av_frame_alloc();
                if (decodedFrame == null)
                {
                    throw new OutOfMemoryException("FFmpeg could not allocate an attached-picture frame.");
                }

                if (ffmpeg.avcodec_send_packet(decoderContext, &stream->attached_pic) >= 0 &&
                    ffmpeg.avcodec_receive_frame(decoderContext, decodedFrame) >= 0)
                {
                    var result = EncodeFrame(
                        decodedFrame,
                        decoderContext,
                        outputPath,
                        maximumDimension,
                        interruptState);
                    if (result != null || interruptState.ShouldStop)
                    {
                        return result;
                    }
                }
            }
            finally
            {
                if (decodedFrame != null)
                {
                    ffmpeg.av_frame_free(&decodedFrame);
                }

                if (decoderContext != null)
                {
                    ffmpeg.avcodec_free_context(&decoderContext);
                }
            }
        }

        return null;
    }

    private static unsafe bool TryPrepareSeek(
        AVFormatContext* formatContext,
        AVCodecContext* decoderContext,
        AVStream* videoStream,
        int videoStreamIndex,
        long positionMicroseconds,
        out long targetTimestamp)
    {
        targetTimestamp = GetTargetTimestamp(formatContext, videoStream, positionMicroseconds);
        var error = ffmpeg.av_seek_frame(
            formatContext,
            videoStreamIndex,
            targetTimestamp,
            ffmpeg.AVSEEK_FLAG_BACKWARD);
        if (error < 0)
        {
            return false;
        }

        ffmpeg.avcodec_flush_buffers(decoderContext);
        return true;
    }

    private static unsafe bool TryDecodeFrameAtTimestamp(
        AVFormatContext* formatContext,
        AVCodecContext* decoderContext,
        int videoStreamIndex,
        AVPacket* packet,
        AVFrame* decodedFrame,
        AVFrame* lastDecodedFrame,
        AVFrame* targetFrame,
        long targetTimestamp,
        InterruptState interruptState)
    {
        ffmpeg.av_packet_unref(packet);
        ffmpeg.av_frame_unref(decodedFrame);
        ffmpeg.av_frame_unref(lastDecodedFrame);
        ffmpeg.av_frame_unref(targetFrame);

        while (!interruptState.ShouldStop && ffmpeg.av_read_frame(formatContext, packet) >= 0)
        {
            var foundTarget = false;
            if (packet->stream_index == videoStreamIndex &&
                ffmpeg.avcodec_send_packet(decoderContext, packet) >= 0)
            {
                foundTarget = TryReceiveTargetFrame(
                    decoderContext,
                    decodedFrame,
                    lastDecodedFrame,
                    targetFrame,
                    targetTimestamp,
                    interruptState);
            }

            ffmpeg.av_packet_unref(packet);
            if (foundTarget)
            {
                return true;
            }
        }

        ffmpeg.av_packet_unref(packet);
        if (interruptState.ShouldStop)
        {
            return false;
        }

        if (ffmpeg.avcodec_send_packet(decoderContext, null) >= 0 &&
            TryReceiveTargetFrame(
                decoderContext,
                decodedFrame,
                lastDecodedFrame,
                targetFrame,
                targetTimestamp,
                interruptState))
        {
            return true;
        }

        if (lastDecodedFrame->width <= 0 || lastDecodedFrame->height <= 0)
        {
            return false;
        }

        ffmpeg.av_frame_unref(targetFrame);
        return ffmpeg.av_frame_ref(targetFrame, lastDecodedFrame) >= 0;
    }

    private static unsafe bool TryDecodeFirstNonBlankFrame(
        AVFormatContext* formatContext,
        AVCodecContext* decoderContext,
        int videoStreamIndex,
        AVPacket* packet,
        AVFrame* decodedFrame,
        AVFrame* targetFrame,
        InterruptState interruptState)
    {
        ffmpeg.av_packet_unref(packet);
        ffmpeg.av_frame_unref(decodedFrame);
        ffmpeg.av_frame_unref(targetFrame);
        var decodedFrameCount = 0;
        using var blankFrameAnalyzer = new BlankFrameAnalyzer();

        while (!interruptState.ShouldStop &&
            decodedFrameCount < MaximumBlankFrameScanCount &&
            ffmpeg.av_read_frame(formatContext, packet) >= 0)
        {
            var foundFrame = false;
            if (packet->stream_index == videoStreamIndex &&
                ffmpeg.avcodec_send_packet(decoderContext, packet) >= 0)
            {
                foundFrame = TryReceiveFirstNonBlankFrame(
                    decoderContext,
                    decodedFrame,
                    targetFrame,
                    ref decodedFrameCount,
                    blankFrameAnalyzer,
                    interruptState);
            }

            ffmpeg.av_packet_unref(packet);
            if (foundFrame)
            {
                return true;
            }
        }

        ffmpeg.av_packet_unref(packet);
        if (interruptState.ShouldStop)
        {
            return false;
        }

        if (decodedFrameCount >= MaximumBlankFrameScanCount)
        {
            return targetFrame->width > 0 && targetFrame->height > 0;
        }

        if (ffmpeg.avcodec_send_packet(decoderContext, null) >= 0 &&
            TryReceiveFirstNonBlankFrame(
                decoderContext,
                decodedFrame,
                targetFrame,
                ref decodedFrameCount,
                blankFrameAnalyzer,
                interruptState))
        {
            return true;
        }

        return targetFrame->width > 0 && targetFrame->height > 0;
    }

    private static unsafe bool TryReceiveFirstNonBlankFrame(
        AVCodecContext* decoderContext,
        AVFrame* decodedFrame,
        AVFrame* targetFrame,
        ref int decodedFrameCount,
        BlankFrameAnalyzer blankFrameAnalyzer,
        InterruptState interruptState)
    {
        while (!interruptState.ShouldStop &&
            decodedFrameCount < MaximumBlankFrameScanCount &&
            ffmpeg.avcodec_receive_frame(decoderContext, decodedFrame) >= 0)
        {
            decodedFrameCount++;
            if (!blankFrameAnalyzer.IsBlank(decodedFrame) ||
                decodedFrameCount >= MaximumBlankFrameScanCount)
            {
                ffmpeg.av_frame_unref(targetFrame);
                return ffmpeg.av_frame_ref(targetFrame, decodedFrame) >= 0;
            }

            ffmpeg.av_frame_unref(targetFrame);
            if (ffmpeg.av_frame_ref(targetFrame, decodedFrame) < 0)
            {
                return false;
            }

            ffmpeg.av_frame_unref(decodedFrame);
        }

        return false;
    }

    private static unsafe bool IsPackedBlankFrame(
        AVFrame* frame,
        int bytesPerPixel,
        int colorOffset)
    {
        var lineSize = Math.Abs((long)frame->linesize[0]);
        var sampledPixelCount = (int)Math.Min(
            frame->width,
            Math.Max(0, (lineSize - colorOffset) / bytesPerPixel));
        if (sampledPixelCount <= 0)
        {
            return false;
        }

        var horizontalStep = Math.Max(1, sampledPixelCount / 96);
        var verticalStep = Math.Max(1, frame->height / 54);
        var metrics = new BlankFrameMetrics();

        for (var y = 0; y < frame->height; y += verticalStep)
        {
            metrics.StartRow();
            var row = frame->data[0] + (y * frame->linesize[0]);
            for (var x = 0; x < sampledPixelCount; x += horizontalStep)
            {
                var offset = (x * bytesPerPixel) + colorOffset;
                var firstChannel = row[offset];
                var secondChannel = row[offset + 1];
                var thirdChannel = row[offset + 2];
                var brightness = (firstChannel + secondChannel + thirdChannel) / 3;
                var colorSpread = Math.Max(
                    firstChannel,
                    Math.Max(secondChannel, thirdChannel)) -
                    Math.Min(firstChannel, Math.Min(secondChannel, thirdChannel));
                metrics.Add(brightness, colorSpread);
            }
        }

        return metrics.IsBlank(hasColorSamples: true);
    }

    private struct BlankFrameMetrics
    {
        private long _brightnessTotal;
        private long _brightnessSquaredTotal;
        private long _gradientTotal;
        private long _colorSpreadTotal;
        private int _brightPixelCount;
        private int _darkPixelCount;
        private int _gradientSampleCount;
        private int _sampleCount;
        private int _previousBrightness;
        private bool _hasPreviousBrightness;

        public void StartRow()
        {
            _hasPreviousBrightness = false;
        }

        public void Add(int brightness, int colorSpread = 0)
        {
            _brightnessTotal += brightness;
            _brightnessSquaredTotal += brightness * brightness;
            _colorSpreadTotal += colorSpread;
            if (brightness > NearBlackFrameBrightnessThreshold)
            {
                _brightPixelCount++;
            }

            if (brightness < NearWhiteFrameBrightnessThreshold)
            {
                _darkPixelCount++;
            }

            if (_hasPreviousBrightness)
            {
                _gradientTotal += Math.Abs(brightness - _previousBrightness);
                _gradientSampleCount++;
            }

            _previousBrightness = brightness;
            _hasPreviousBrightness = true;
            _sampleCount++;
        }

        public readonly bool IsBlank(bool hasColorSamples)
        {
            if (_sampleCount == 0)
            {
                return false;
            }

            var averageBrightness = _brightnessTotal / (double)_sampleCount;
            var variance = Math.Max(
                0,
                (_brightnessSquaredTotal / (double)_sampleCount) -
                (averageBrightness * averageBrightness));
            var standardDeviation = Math.Sqrt(variance);
            var averageGradient = _gradientSampleCount > 0
                ? _gradientTotal / (double)_gradientSampleCount
                : 0;
            var averageColorSpread = hasColorSamples
                ? _colorSpreadTotal / (double)_sampleCount
                : 0;
            var contrastInformation = Math.Clamp(
                standardDeviation / BlankFrameContrastReference,
                0,
                1);
            var edgeInformation = Math.Clamp(
                averageGradient / BlankFrameGradientReference,
                0,
                1);
            var colorInformation = hasColorSamples
                ? Math.Clamp(
                    averageColorSpread / BlankFrameColorSpreadReference,
                    0,
                    1)
                : 0;
            var visualInformationScore = (contrastInformation * 0.45) +
                (edgeInformation * 0.40) +
                (colorInformation * 0.15);
            if (visualInformationScore > MaximumBlankFrameVisualInformationScore)
            {
                return false;
            }

            var brightPixelRatio = _brightPixelCount / (double)_sampleCount;
            var darkPixelRatio = _darkPixelCount / (double)_sampleCount;
            var isBlack = averageBrightness <= NearBlackFrameBrightnessThreshold &&
                brightPixelRatio <= MaximumBlackFrameBrightPixelRatio;
            var isWhite = averageBrightness >= NearWhiteFrameBrightnessThreshold &&
                darkPixelRatio <= MaximumWhiteFrameDarkPixelRatio;
            return isBlack || isWhite;
        }
    }

    private sealed unsafe class BlankFrameAnalyzer : IDisposable
    {
        private AVFrame* _analysisFrame;
        private SwsContext* _scaleContext;

        public bool IsBlank(AVFrame* sourceFrame)
        {
            if (sourceFrame == null || sourceFrame->width <= 0 || sourceFrame->height <= 0 ||
                sourceFrame->format < 0)
            {
                return false;
            }

            if (!EnsureAnalysisFrame() ||
                ffmpeg.av_frame_make_writable(_analysisFrame) < 0)
            {
                return false;
            }

            _scaleContext = ffmpeg.sws_getCachedContext(
                _scaleContext,
                sourceFrame->width,
                sourceFrame->height,
                (AVPixelFormat)sourceFrame->format,
                BlankFrameAnalysisWidth,
                BlankFrameAnalysisHeight,
                AVPixelFormat.AV_PIX_FMT_RGB24,
                (int)SwsFlags.SWS_FAST_BILINEAR,
                null,
                null,
                null);
            if (_scaleContext == null ||
                ffmpeg.sws_scale_frame(_scaleContext, _analysisFrame, sourceFrame) < 0)
            {
                return false;
            }

            return IsPackedBlankFrame(
                _analysisFrame,
                bytesPerPixel: 3,
                colorOffset: 0);
        }

        private bool EnsureAnalysisFrame()
        {
            if (_analysisFrame != null)
            {
                return true;
            }

            _analysisFrame = ffmpeg.av_frame_alloc();
            if (_analysisFrame == null)
            {
                return false;
            }

            _analysisFrame->format = (int)AVPixelFormat.AV_PIX_FMT_RGB24;
            _analysisFrame->width = BlankFrameAnalysisWidth;
            _analysisFrame->height = BlankFrameAnalysisHeight;
            return ffmpeg.av_frame_get_buffer(_analysisFrame, 32) >= 0;
        }

        public void Dispose()
        {
            if (_scaleContext != null)
            {
                ffmpeg.sws_freeContext(_scaleContext);
                _scaleContext = null;
            }

            if (_analysisFrame != null)
            {
                var analysisFrame = _analysisFrame;
                ffmpeg.av_frame_free(&analysisFrame);
                _analysisFrame = null;
            }
        }
    }

    private static unsafe bool TryReceiveTargetFrame(
        AVCodecContext* decoderContext,
        AVFrame* decodedFrame,
        AVFrame* lastDecodedFrame,
        AVFrame* targetFrame,
        long targetTimestamp,
        InterruptState interruptState)
    {
        while (!interruptState.ShouldStop &&
            ffmpeg.avcodec_receive_frame(decoderContext, decodedFrame) >= 0)
        {
            ffmpeg.av_frame_unref(lastDecodedFrame);
            if (ffmpeg.av_frame_ref(lastDecodedFrame, decodedFrame) < 0)
            {
                return false;
            }

            var timestamp = decodedFrame->best_effort_timestamp;
            if (targetTimestamp <= 0 ||
                timestamp == ffmpeg.AV_NOPTS_VALUE ||
                timestamp >= targetTimestamp)
            {
                ffmpeg.av_frame_unref(targetFrame);
                return ffmpeg.av_frame_ref(targetFrame, decodedFrame) >= 0;
            }

            ffmpeg.av_frame_unref(decodedFrame);
        }

        return false;
    }

    private static long GetRequestedMicroseconds(TimeSpan position)
    {
        return (long)Math.Max(
            0,
            Math.Min(position.TotalSeconds, long.MaxValue / (double)ffmpeg.AV_TIME_BASE) *
            ffmpeg.AV_TIME_BASE);
    }

    private static unsafe long GetTargetTimestamp(
        AVFormatContext* formatContext,
        AVStream* videoStream,
        long positionMicroseconds)
    {
        var relativeTimestamp = ffmpeg.av_rescale_q(
            positionMicroseconds,
            new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
            videoStream->time_base);
        var startTimestamp = videoStream->start_time != ffmpeg.AV_NOPTS_VALUE
            ? videoStream->start_time
            : formatContext->start_time != ffmpeg.AV_NOPTS_VALUE
                ? ffmpeg.av_rescale_q(
                    formatContext->start_time,
                    new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE },
                    videoStream->time_base)
                : 0;

        return relativeTimestamp > 0 && startTimestamp > long.MaxValue - relativeTimestamp
            ? long.MaxValue
            : startTimestamp + relativeTimestamp;
    }

    private static unsafe string? EncodeFrame(
        AVFrame* sourceFrame,
        AVCodecContext* decoderContext,
        string outputPath,
        int maximumDimension,
        InterruptState interruptState)
    {
        if (interruptState.ShouldStop ||
            sourceFrame == null || sourceFrame->width <= 0 || sourceFrame->height <= 0)
        {
            return null;
        }

        ApplyFallbackColorMetadata(sourceFrame, decoderContext);
        var isHdr = IsHdrFrame(sourceFrame);

        var isJpeg = string.Equals(Path.GetExtension(outputPath), ".jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(outputPath), ".jpeg", StringComparison.OrdinalIgnoreCase);
        var codecId = isJpeg ? AVCodecID.AV_CODEC_ID_MJPEG : AVCodecID.AV_CODEC_ID_PNG;
        var outputPixelFormat = isJpeg
            ? AVPixelFormat.AV_PIX_FMT_YUVJ420P
            : AVPixelFormat.AV_PIX_FMT_RGBA;
        var (outputWidth, outputHeight) = GetOutputSize(
            sourceFrame->width,
            sourceFrame->height,
            maximumDimension,
            requiresEvenDimensions: isJpeg);

        AVCodecContext* encoderContext = null;
        AVFrame* outputFrame = null;
        AVPacket* outputPacket = null;
        SwsContext* scaleContext = null;
        string? temporaryPath = null;

        try
        {
            var encoder = ffmpeg.avcodec_find_encoder(codecId);
            if (encoder == null)
            {
                return null;
            }

            encoderContext = ffmpeg.avcodec_alloc_context3(encoder);
            if (encoderContext == null)
            {
                throw new OutOfMemoryException("FFmpeg could not allocate a thumbnail encoder.");
            }

            encoderContext->width = outputWidth;
            encoderContext->height = outputHeight;
            encoderContext->pix_fmt = outputPixelFormat;
            encoderContext->time_base = new AVRational { num = 1, den = 25 };
            if (isHdr)
            {
                ConfigureSdrOutputColorMetadata(encoderContext, isJpeg);
            }
            else if (isJpeg)
            {
                encoderContext->color_range = AVColorRange.AVCOL_RANGE_JPEG;
            }

            var error = ffmpeg.avcodec_open2(encoderContext, encoder, null);
            if (!IsSuccessful("FFmpeg could not open the thumbnail encoder", error, interruptState))
            {
                return null;
            }

            outputFrame = ffmpeg.av_frame_alloc();
            outputPacket = ffmpeg.av_packet_alloc();
            if (outputFrame == null || outputPacket == null)
            {
                throw new OutOfMemoryException("FFmpeg could not allocate thumbnail encoding buffers.");
            }

            outputFrame->format = (int)outputPixelFormat;
            outputFrame->width = outputWidth;
            outputFrame->height = outputHeight;
            if (isHdr)
            {
                ConfigureSdrOutputColorMetadata(outputFrame, isJpeg);
            }

            error = ffmpeg.av_frame_get_buffer(outputFrame, 32);
            if (!IsSuccessful("FFmpeg could not allocate the thumbnail image", error, interruptState))
            {
                return null;
            }

            scaleContext = isHdr
                ? CreateHdrScaleContext()
                : ffmpeg.sws_getContext(
                    sourceFrame->width,
                    sourceFrame->height,
                    (AVPixelFormat)sourceFrame->format,
                    outputWidth,
                    outputHeight,
                    outputPixelFormat,
                    (int)SwsFlags.SWS_BICUBIC,
                    null,
                    null,
                    null);
            if (scaleContext == null)
            {
                return null;
            }

            error = ffmpeg.sws_scale_frame(scaleContext, outputFrame, sourceFrame);
            if (!IsSuccessful("FFmpeg could not scale the thumbnail", error, interruptState))
            {
                return null;
            }

            outputFrame->pts = 0;
            error = ffmpeg.avcodec_send_frame(encoderContext, outputFrame);
            if (!IsSuccessful("FFmpeg could not encode the thumbnail", error, interruptState))
            {
                return null;
            }

            error = ffmpeg.avcodec_receive_packet(encoderContext, outputPacket);
            if (!IsSuccessful("FFmpeg did not produce a thumbnail image", error, interruptState) ||
                interruptState.ShouldStop ||
                outputPacket->data == null || outputPacket->size <= 0)
            {
                return null;
            }

            var imageBytes = new byte[outputPacket->size];
            Marshal.Copy((nint)outputPacket->data, imageBytes, 0, imageBytes.Length);
            if (interruptState.ShouldStop)
            {
                return null;
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temporaryPath, imageBytes);
            if (interruptState.ShouldStop)
            {
                return null;
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            temporaryPath = null;
            return outputPath;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }

            if (scaleContext != null)
            {
                ffmpeg.sws_freeContext(scaleContext);
            }

            if (outputPacket != null)
            {
                ffmpeg.av_packet_free(&outputPacket);
            }

            if (outputFrame != null)
            {
                ffmpeg.av_frame_free(&outputFrame);
            }

            if (encoderContext != null)
            {
                ffmpeg.avcodec_free_context(&encoderContext);
            }
        }
    }

    private static unsafe void ApplyFallbackColorMetadata(
        AVFrame* frame,
        AVCodecContext* decoderContext)
    {
        if (decoderContext != null)
        {
            if (frame->color_range == AVColorRange.AVCOL_RANGE_UNSPECIFIED)
            {
                frame->color_range = decoderContext->color_range;
            }

            if (frame->color_primaries == AVColorPrimaries.AVCOL_PRI_UNSPECIFIED)
            {
                frame->color_primaries = decoderContext->color_primaries;
            }

            if (frame->color_trc == AVColorTransferCharacteristic.AVCOL_TRC_UNSPECIFIED)
            {
                frame->color_trc = decoderContext->color_trc;
            }

            if (frame->colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED)
            {
                frame->colorspace = decoderContext->colorspace;
            }

            if (frame->chroma_location == AVChromaLocation.AVCHROMA_LOC_UNSPECIFIED)
            {
                frame->chroma_location = decoderContext->chroma_sample_location;
            }
        }

        if (!IsHdrTransfer(frame->color_trc))
        {
            return;
        }

        if (frame->color_range == AVColorRange.AVCOL_RANGE_UNSPECIFIED)
        {
            frame->color_range = AVColorRange.AVCOL_RANGE_MPEG;
        }

        if (frame->color_primaries == AVColorPrimaries.AVCOL_PRI_UNSPECIFIED)
        {
            frame->color_primaries = AVColorPrimaries.AVCOL_PRI_BT2020;
        }

        if (frame->colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED)
        {
            frame->colorspace = AVColorSpace.AVCOL_SPC_BT2020_NCL;
        }
    }

    private static unsafe bool IsHdrFrame(AVFrame* frame)
    {
        return frame != null && IsHdrTransfer(frame->color_trc);
    }

    private static bool IsHdrTransfer(AVColorTransferCharacteristic transfer)
    {
        return transfer is AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084 or
            AVColorTransferCharacteristic.AVCOL_TRC_ARIB_STD_B67;
    }

    private static unsafe SwsContext* CreateHdrScaleContext()
    {
        var scaleContext = ffmpeg.sws_alloc_context();
        if (scaleContext == null)
        {
            return null;
        }

        scaleContext->flags = (uint)SwsFlags.SWS_BICUBIC;
        scaleContext->threads = 0;
        scaleContext->dither = SwsDither.SWS_DITHER_ED;
        scaleContext->intent = (int)SwsIntent.SWS_INTENT_PERCEPTUAL;
        return scaleContext;
    }

    private static unsafe void ConfigureSdrOutputColorMetadata(
        AVCodecContext* context,
        bool isJpeg)
    {
        context->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        context->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
        context->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1;
        context->colorspace = isJpeg
            ? AVColorSpace.AVCOL_SPC_BT470BG
            : AVColorSpace.AVCOL_SPC_RGB;
    }

    private static unsafe void ConfigureSdrOutputColorMetadata(AVFrame* frame, bool isJpeg)
    {
        frame->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        frame->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
        frame->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1;
        frame->colorspace = isJpeg
            ? AVColorSpace.AVCOL_SPC_BT470BG
            : AVColorSpace.AVCOL_SPC_RGB;
    }

    private static (int Width, int Height) GetOutputSize(
        int sourceWidth,
        int sourceHeight,
        int maximumDimension,
        bool requiresEvenDimensions)
    {
        var scale = Math.Min(1d, maximumDimension / (double)Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        if (requiresEvenDimensions)
        {
            width = Math.Max(2, width & ~1);
            height = Math.Max(2, height & ~1);
        }

        return (width, height);
    }

    private static bool IsSuccessful(string message, int error, InterruptState state)
    {
        if (error >= 0)
        {
            return true;
        }

        if (state.ShouldStop)
        {
            return false;
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
            return handle.Target is InterruptState state && state.ShouldStop
                ? 1
                : 0;
        }
        catch
        {
            return 1;
        }
    }

    private sealed class InterruptState(CancellationToken cancellationToken, long deadlineTimestamp)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;

        public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;

        public bool HasTimedOut => Stopwatch.GetTimestamp() >= deadlineTimestamp;

        public bool ShouldStop => IsCancellationRequested || HasTimedOut;
    }

    private static unsafe class NativeCallbacks
    {
        internal static readonly AVIOInterruptCB_callback Interrupt = InterruptCallback;
    }
}
