namespace Koukei.Audio;

public sealed class AudioPlaybackStateChangedEventArgs(AudioPlaybackState state) : EventArgs
{
    public AudioPlaybackState State { get; } = state;
}

public sealed class AudioMediaChangedEventArgs(AudioMediaMetadata metadata) : EventArgs
{
    public AudioMediaMetadata Metadata { get; } = metadata;
}

public sealed class AudioPlaybackEndedEventArgs(AudioPlaybackRequest request) : EventArgs
{
    public AudioPlaybackRequest Request { get; } = request;
}
