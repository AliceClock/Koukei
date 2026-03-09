namespace Koukei.Ffmpeg;

public sealed class FfmpegException : Exception
{
    public FfmpegException(string message, int errorCode)
        : base($"{message}: {FfmpegError.Describe(errorCode)} ({errorCode})")
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}
