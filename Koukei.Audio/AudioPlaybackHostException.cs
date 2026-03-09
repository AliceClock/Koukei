namespace Koukei.Audio;

public sealed class AudioPlaybackHostException : Exception
{
    public AudioPlaybackHostException(string message)
        : base(message)
    {
    }

    public AudioPlaybackHostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
