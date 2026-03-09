//using Microsoft.Extensions.Logging;
//using System;
//using System.Runtime.InteropServices;
//using System.Threading.Tasks;
//using Koukei.Mpv.Interop;

//namespace Koukei.Mpv.Client;

//public sealed partial class MpvClient
//{
//    public event EventHandler Shutdown = delegate { };
//    public event EventHandler EndFile = delegate { };
//    public event EventHandler StartFile = delegate { };
//    public event EventHandler FileLoaded = delegate { };

//    private async Task HandleEvent(MpvEvent mpvEvent)
//    {
//        switch (mpvEvent.EventId)
//        {
//            case MpvEventId.Shutdown:
//                Shutdown?.Invoke(this, EventArgs.Empty);
//                break;
//            case MpvEventId.LogMessage:
//                var logMessage = Marshal.PtrToStructure<MpvEventLogMessage>(mpvEvent.Data);
//                _logger.LogInformation("[MPV] Log message: {Level} - {Text}", logMessage.Level, logMessage.Text);
//                break;
//            case MpvEventId.StartFile:
//                StartFile?.Invoke(this, EventArgs.Empty);
//                break;
//            case MpvEventId.EndFile:
//                EndFile?.Invoke(this, EventArgs.Empty);
//                break;
//            case MpvEventId.FileLoaded:
//                FileLoaded?.Invoke(this, EventArgs.Empty);
//                break;
//            case MpvEventId.Idle:
//                break;
//            case MpvEventId.Seek:
//            {
//                var playerState = await GetPlayerStateAsync();
//                if (!playerState.IsSuccess)
//                {
//                    _logger.LogError("[MPV] Failed to get player state: {Error}", playerState.Error);
//                    return;
//                }

//                SendNotify(MpvClientEventId.StateChanged, playerState.Value);
//            }
//                break;
//            case MpvEventId.PropertyChange:
//                var eventProp = Marshal.PtrToStructure<MpvEventProperty>(mpvEvent.Data);
//                _ = HandleObservePropertyChanged(eventProp);
//                break;
//            case MpvEventId.None:
//            case MpvEventId.GetPropertyReply:
//            case MpvEventId.SetPropertyReply:
//            case MpvEventId.CommandReply:
//            case MpvEventId.Tick:
//            case MpvEventId.ClientMessage:
//            case MpvEventId.VideoReconfig:
//            case MpvEventId.AudioReconfig:
//            case MpvEventId.PlaybackRestart:
//            case MpvEventId.QueueOverflow:
//            case MpvEventId.Hook:
//            default:
//                _logger.LogInformation("[MPV] Event received: {EventId}", mpvEvent.EventId);
//                break;
//        }
//    }

//    private async Task HandleObservePropertyChanged(MpvEventProperty eventProp)
//    {
//        if (eventProp.Data == IntPtr.Zero)
//        {
//            return;
//        }

//        switch (eventProp.Name)
//        {
//            case "pause":
//            case "core-idle":
//            case "seeking":
//            {
//                var playerState = await GetPlayerStateAsync();
//                if (!playerState.IsSuccess)
//                {
//                    _logger.LogError("[MPV] Failed to get player state: {Error}", playerState.Error);
//                    return;
//                }

//                SendNotify(MpvClientEventId.StateChanged, playerState.Value);
//                break;
//            }
//            case "volume":
//            {
//                var volume = Marshal.PtrToStructure<double>(eventProp.Data);
//                SendNotify(MpvClientEventId.VolumeChanged, volume);
//                break;
//            }
//            case "duration":
//            {
//                var duration = Marshal.PtrToStructure<double>(eventProp.Data);
//                SendNotify(MpvClientEventId.DurationChanged, duration);
//                break;
//            }
//            case "time-pos":
//            {
//                var timePosition = Marshal.PtrToStructure<double>(eventProp.Data);
//                SendNotify(MpvClientEventId.PositionChanged, timePosition);
//                break;
//            }
//            case "fullscreen":
//            {
//                var isFullscreen = Marshal.PtrToStructure<MpvNode>(eventProp.Data);
//                SendNotify(MpvClientEventId.FullScreenChanged, isFullscreen.Flag != 0);
//                break;
//            }
//            case "ontop":
//            {
//                var isOntop = Marshal.PtrToStructure<MpvNode>(eventProp.Data);
//                SendNotify(MpvClientEventId.OnTopChanged, isOntop.Flag != 0);
//                break;
//            }
//            case "speed":
//            {
//                var speed = Marshal.PtrToStructure<double>(eventProp.Data);
//                SendNotify(MpvClientEventId.SpeedChanged, speed);
//                break;
//            }
//        }
//    }
//}