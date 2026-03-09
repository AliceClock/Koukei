//using System;
//using System.Collections.Generic;
//using System.Threading.Tasks;
//using Koukei.Mpv.Interop;

//namespace Koukei.Mpv.Client;

//public sealed partial class MpvClient
//{
//    public async Task PlayAsync(string filePath, MpvPlayOptions? options = null)
//    {
//        _cachedDuration = null;
//        _cachedSnapshot = new MpvPlayerSnapshot(filePath, options);
//        var mpvError = MpvError.Success;
//        List<string> commandArgs = ["load file", $"\"{filePath}\"", "replace", "0"];
//        List<string> commandOptions = [];

//        if (options != null)
//        {
//            if (options.WindowHandle != null)
//            {
//                var mpvNode = new MpvNode() { Int64 = options.WindowHandle.Value.ToInt64() };
//                await Task.Run(() => mpvError = MpvNative.MpvSetOption(Handle, "wid", MpvFormat.Int64, in mpvNode));
//                ThrowMpvException("MPV | set 'wid' failed", mpvError);
//            }

//            if (options.StartPosition != null)
//            {
//                commandOptions.Add($"start={Math.Round(options.StartPosition.Value)}");
//            }

//            if (options.InitialVolume != null)
//            {
//                commandOptions.Add($"volume={Math.Round(options.InitialVolume.Value)}");
//            }

//            if (options.InitialSpeed != null)
//            {
//                commandOptions.Add($"speed={Math.Round(options.InitialSpeed.Value)}");
//            }
//        }
//    }

//    public async Task<Result<int>> PauseAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Flag = 1 };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "pause", MpvFormat.Flag, in mpvNode));
//        return MpvInteropResult<int>("MPV | set 'pause' failed", mpvError);
//    }

//    public async Task<Result<int>> ResumeAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Flag = 0 };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "pause", MpvFormat.Flag, in mpvNode));
//        return MpvInteropResult<int>("MPV | set 'pause' failed", mpvError);
//    }

//    public async Task<Result<int>> ReplayAsync(double startPositon = 0d)
//    {
//        if (_cachedSnapshot == null)
//        {
//            return Result<int>.Fail(new Error("Replay failed. No video is playing."));
//        }

//        if (startPositon > 0)
//        {
//            _cachedSnapshot.Options ??= new MpvPlayOptions();
//            _cachedSnapshot.Options.StartPosition = startPositon;
//        }

//        try
//        {
//            await PlayAsync(_cachedSnapshot.FilePath!, _cachedSnapshot.Options);
//        }
//        catch (Exception exception)
//        {
//            return Result<int>.Fail(new Error("Replay failed.", exception));
//        }

//        return Result<int>.Ok();
//    }

//    public async Task<Result<int>> StopAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Flag = 1 };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "stop", MpvFormat.Flag, in mpvNode));
//        return MpvInteropResult<int>("MPV | set 'stop' failed", mpvError);
//    }

//    public async Task<Result<MpvPlayerState>> GetPlayerStateAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "core-idle", MpvFormat.Flag, out mpvNode));
//        var result = MpvInteropResult<MpvPlayerState>("MPV | get 'core-idle' failed", mpvError);
//        if (!result.IsSuccess)
//        {
//            return result;
//        }

//        var isPlaying = mpvNode.Flag == 0;
//        if (isPlaying)
//        {
//            return Result<MpvPlayerState>.Ok(MpvPlayerState.Playing);
//        }

//        mpvNode = new MpvNode();
//        await Task.Run(() =>
//            mpvError = MpvNative.MpvGetProperty(Handle, "paused-for-cache", MpvFormat.Flag, out mpvNode));
//        result = MpvInteropResult<MpvPlayerState>("MPV | get 'paused-for-cache' failed", mpvError);
//        if (!result.IsSuccess)
//        {
//            return result;
//        }

//        var isBuffering = mpvNode.Flag != 0;
//        if (isBuffering)
//        {
//            return Result<MpvPlayerState>.Ok(MpvPlayerState.Buffering);
//        }

//        mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "seeking", MpvFormat.Flag, out mpvNode));
//        result = MpvInteropResult<MpvPlayerState>("MPV | get 'seeking' failed", mpvError);
//        if (!result.IsSuccess)
//        {
//            return result;
//        }

//        var isSeeking = mpvNode.Flag != 0;
//        if (isSeeking)
//        {
//            return Result<MpvPlayerState>.Ok(MpvPlayerState.Seeking);
//        }

//        mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "eof-reached", MpvFormat.Flag, out mpvNode));
//        result = MpvInteropResult<MpvPlayerState>("MPV | get 'eof-reached' failed", mpvError);
//        if (!result.IsSuccess)
//        {
//            return result;
//        }

//        var isEnded = mpvNode.Flag != 0;
//        if (isEnded)
//        {
//            return Result<MpvPlayerState>.Ok(MpvPlayerState.Ended);
//        }

//        mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "idle-active", MpvFormat.Flag, out mpvNode));
//        result = MpvInteropResult<MpvPlayerState>("MPV | get 'idle-active' failed", mpvError);
//        if (!result.IsSuccess)
//        {
//            return result;
//        }

//        var isIdle = mpvNode.Flag != 0;
//        return Result<MpvPlayerState>.Ok(!isIdle ? MpvPlayerState.Paused : MpvPlayerState.Idle);
//    }

//    public async Task<Result<double>> GetTimePositionAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "time-pos", MpvFormat.Double, out mpvNode));
//        var result = MpvInteropResult<double>("MPV | get 'time-pos' failed", mpvError);

//        return result.IsSuccess ? Result<double>.Ok(mpvNode.Double) : result;
//    }

//    public async Task<Result<int>> SetTimePositionAsync(double position)
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Double = position };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "time-pos", MpvFormat.Double, in mpvNode));
//        return MpvInteropResult<int>("MPV | set 'time-pos' failed", mpvError);
//    }

//    public async Task<Result<double>> GetDurationAsync()
//    {
//        if (_cachedDuration != null)
//        {
//            return Result<double>.Ok(_cachedDuration.Value);
//        }

//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "duration", MpvFormat.Double, out mpvNode));
//        var result = MpvInteropResult<double>("MPV | get 'duration' failed", mpvError);
//        if (!result.IsSuccess)
//        {
//            return result;
//        }

//        _cachedDuration = mpvNode.Double;

//        return Result<double>.Ok(_cachedDuration.Value);
//    }

//    public async Task<Result<double>> GetVolumeAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "volume", MpvFormat.Double, out mpvNode));
//        var result = MpvInteropResult<double>("MPV | get 'volume' failed", mpvError);

//        return result.IsSuccess ? Result<double>.Ok(mpvNode.Double) : result;
//    }

//    public async Task<Result<int>> SetVolumeAsync(double volume)
//    {
//        if (volume < 0)
//        {
//            volume = 0;
//        }

//        if (volume > 100)
//        {
//            volume = 100;
//        }

//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Double = volume };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "volume", MpvFormat.Double, in mpvNode));
//        return MpvInteropResult<int>("MPV | set 'volume' failed", mpvError);
//    }

//    public async Task<Result<double>> GetSpeedAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "speed", MpvFormat.Double, out mpvNode));
//        var result = MpvInteropResult<double>("MPV | get 'speed' failed", mpvError);

//        return result.IsSuccess ? Result<double>.Ok(mpvNode.Double) : result;
//    }

//    public async Task<Result<int>> SetSpeedAsync(double speed)
//    {
//        if (speed < 0.01)
//        {
//            speed = 0.01;
//        }

//        if (speed > 100)
//        {
//            speed = 100;
//        }

//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Double = speed };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "speed", MpvFormat.Double, in mpvNode));
//        return MpvInteropResult<int>("Mpv | set speed failed", mpvError);
//    }
//}