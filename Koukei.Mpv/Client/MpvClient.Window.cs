//using System.Threading.Tasks;
//using Koukei.Mpv.Interop;

//namespace Koukei.Mpv.Client;

//public sealed partial class MpvClient
//{
//    public async Task<Result<bool>> GetFullScreenStateAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "fullscreen", MpvFormat.Flag, out mpvNode));
//        var result = MpvInteropResult<bool>("MPV | get 'fullscreen' failed", mpvError);

//        return result.IsSuccess ? Result<bool>.Ok(mpvNode.Flag != 0) : result;
//    }

//    public async Task<Result<int>> SetFullScreenStateAsync(bool isFullScreen)
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Flag = isFullScreen ? 1 : 0 };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "fullscreen", MpvFormat.Flag, in mpvNode));
//        return MpvInteropResult<int>("MPV | set 'fullscreen' failed", mpvError);
//    }

//    public async Task<Result<bool>> GetOnTopStateAsync()
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode();
//        await Task.Run(() => mpvError = MpvNative.MpvGetProperty(Handle, "ontop", MpvFormat.Flag, out mpvNode));
//        var result = MpvInteropResult<bool>("MPV | get 'ontop' failed", mpvError);

//        return result.IsSuccess ? Result<bool>.Ok(mpvNode.Flag != 0) : result;
//    }

//    public async Task<Result<int>> SetOnTopStateAsync(bool isOnTop)
//    {
//        var mpvError = MpvError.Success;
//        var mpvNode = new MpvNode() { Flag = isOnTop ? 1 : 0 };
//        await Task.Run(() => mpvError = MpvNative.MpvSetProperty(Handle, "ontop", MpvFormat.Flag, in mpvNode));
//        return MpvInteropResult<int>("MPV | set 'ontop' failed", mpvError);
//    }
//}