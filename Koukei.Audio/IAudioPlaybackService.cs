namespace Koukei.Audio;

public interface IAudioPlaybackService
{
    event EventHandler<AudioPlaybackStateChangedEventArgs>? StateChanged;

    event EventHandler<AudioMediaChangedEventArgs>? MediaChanged;

    event EventHandler<AudioPlaybackEndedEventArgs>? PlaybackEnded;

    Task PlayAsync(AudioPlaybackRequest request, CancellationToken cancellationToken = default);

    Task<AudioPlaybackState> GetPlaybackStateAsync(CancellationToken cancellationToken = default);

    Task SetPausedAsync(bool isPaused, CancellationToken cancellationToken = default);

    Task SeekAbsoluteAsync(double seconds, CancellationToken cancellationToken = default);

    Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default);

    Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default);

    Task SetSpeedAsync(double speed, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
