using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace Koukei.Audio;

internal sealed class RecoverableSoundPlayer(
    AudioEngine engine,
    AudioFormat format,
    ISoundDataProvider dataProvider)
    : SoundPlayerBase(engine, format, dataProvider)
{
    private readonly object _decoderAccessLock = new();
    private int _hasFailed;

    public event EventHandler<SoundFlowPlaybackFailedEventArgs>? PlaybackFailed;

    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        lock (_decoderAccessLock)
        {
            if (Volatile.Read(ref _hasFailed) != 0)
            {
                buffer.Clear();
                return;
            }

            try
            {
                base.GenerateAudio(buffer, channels);
            }
            catch (Exception exception)
            {
                buffer.Clear();
                if (Interlocked.Exchange(ref _hasFailed, 1) == 0)
                {
                    _ = Task.Run(() => RaisePlaybackFailed(exception));
                }
            }
        }
    }

    public bool SeekSafely(double seconds)
    {
        if (!double.IsFinite(seconds) || Volatile.Read(ref _hasFailed) != 0)
        {
            return false;
        }

        lock (_decoderAccessLock)
        {
            var duration = Math.Max(0, Duration);
            var target = duration > 0
                ? Math.Clamp(seconds, 0, duration)
                : Math.Max(0, seconds);
            return base.Seek(TimeSpan.FromSeconds(target));
        }
    }

    public bool TryGetPlaybackSnapshot(out SoundFlowPlayerSnapshot snapshot)
    {
        // State polling is best-effort. It must never wait in front of the
        // real-time audio callback and delay the next decoded buffer.
        if (!Monitor.TryEnter(_decoderAccessLock))
        {
            snapshot = default;
            return false;
        }

        try
        {
            if (Volatile.Read(ref _hasFailed) != 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = new SoundFlowPlayerSnapshot(
                State,
                Math.Max(0, Time),
                Math.Max(0, Duration),
                Math.Clamp(Volume * 100d, 0, 100),
                Mute,
                Math.Clamp(PlaybackSpeed, 0.25f, 4f),
                DataProvider.CanSeek);
            return true;
        }
        finally
        {
            Monitor.Exit(_decoderAccessLock);
        }
    }

    public void StopSafely()
    {
        lock (_decoderAccessLock)
        {
            base.Stop();
        }
    }

    public void DisposeSafely()
    {
        lock (_decoderAccessLock)
        {
            Dispose();
        }
    }

    private void RaisePlaybackFailed(Exception exception)
    {
        try
        {
            PlaybackFailed?.Invoke(this, new SoundFlowPlaybackFailedEventArgs(exception));
        }
        catch
        {
            // An error notification must never escape onto a worker or audio callback thread.
        }
    }
}

internal readonly record struct SoundFlowPlayerSnapshot(
    PlaybackState State,
    double Position,
    double Duration,
    double Volume,
    bool IsMuted,
    double Speed,
    bool IsSeekable);

internal sealed class SoundFlowPlaybackFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
