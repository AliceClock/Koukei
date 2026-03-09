using FFmpeg.AutoGen.Abstractions;
using System.Runtime.InteropServices;

namespace Koukei.Ffmpeg;

internal static unsafe class FfmpegError
{
    private const int ErrorBufferSize = 1024;

    public static string Describe(int errorCode)
    {
        var buffer = stackalloc byte[ErrorBufferSize];
        return ffmpeg.av_strerror(errorCode, buffer, ErrorBufferSize) >= 0
            ? Marshal.PtrToStringUTF8((nint)buffer) ?? "Unknown FFmpeg error"
            : "Unknown FFmpeg error";
    }
}
