//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Logging.Abstractions;
//using System;
//using System.Runtime.InteropServices;
//using System.Threading;
//using System.Threading.Tasks;
//using Koukei.Mpv.Interop;

//namespace Koukei.Mpv.Client;

//public sealed partial class MpvClient : IAsyncDisposable
//{
//    private readonly ILogger _logger;
//    private Task? _eventLoop;
//    private CancellationTokenSource? _eventLoopCancellation;
//    private double? _cachedDuration;
//    private MpvPlayerSnapshot? _cachedSnapshot;

//    internal MpvClient(MpvHandle handle, ILogger? logger = null)
//    {
//        Handle = handle;
//        _logger = logger ?? new NullLogger<MpvClient>();
//    }

//    public event EventHandler<MpvClientNotifyEventArgs> DataNotify = delegate { };

//    public bool IsInitialized { get; private set; }

//    public bool IsDisposed { get; private set; }

//    public MpvHandle Handle { get; }

//    public static async Task<MpvClient> CreateAsync(MpvInitializeOptions? options = null, ILogger? logger = null)
//    {
//        var instanceHandle = MpvNative.MpvCreate();
//        var instance = new MpvClient(instanceHandle, logger);
//        await instance.InitializeAsync(options).ConfigureAwait(false);

//        await ObservePropertyAsync("duration", MpvFormat.Double);
//        await ObservePropertyAsync("time-pos", MpvFormat.Double);
//        await ObservePropertyAsync("pause", MpvFormat.Flag);
//        await ObservePropertyAsync("eof-reached", MpvFormat.Flag);
//        await ObservePropertyAsync("track-list", MpvFormat.Node);
//        await ObservePropertyAsync("chapter-list", MpvFormat.Node);
//        await ObservePropertyAsync("sub-delay", MpvFormat.Int64);
//        await ObservePropertyAsync("speed", MpvFormat.Double);
//        await ObservePropertyAsync("volume", MpvFormat.Double);
//        await ObservePropertyAsync("mute", MpvFormat.Flag);
//        await ObservePropertyAsync("seekable", MpvFormat.Flag);
//        await ObservePropertyAsync("paused-for-cache", MpvFormat.Flag);
//        await ObservePropertyAsync("cache-buffering-state", MpvFormat.Int64);
//        await ObservePropertyAsync("cache-speed", MpvFormat.Double);
//        await ObservePropertyAsync("fullscreen", MpvFormat.Flag);
//        await ObservePropertyAsync("ontop", MpvFormat.Flag);

//        return instance;

//        async Task ObservePropertyAsync(string propertyName, MpvFormat format)
//        {
//            var mpvError = MpvError.Success;
//            await Task.Run(() =>
//                mpvError = (MpvError)MpvNative.MpvObserveProperty(instanceHandle, 0, propertyName, format));
//            ThrowMpvException($"MPV | observe property '{propertyName}' failed", mpvError);
//        }
//    }

//    public static string ToMpvLogLevelString(MpvLogLevel level)
//    {
//        return level switch
//        {
//            MpvLogLevel.None => "no",
//            MpvLogLevel.Fatal => "fatal",
//            MpvLogLevel.Error => "error",
//            MpvLogLevel.Warn => "warn",
//            MpvLogLevel.Info => "info",
//            MpvLogLevel.V => "v",
//            MpvLogLevel.Debug => "debug",
//            MpvLogLevel.Trace => "trace",
//            _ => ""
//        };
//    }

//    public async Task<Result<int>> SetLogLevelAsync(MpvLogLevel level)
//    {
//        var mpvError = MpvError.Success;
//        _logger.LogInformation($"Set Mpv log level to {level}.");
//        var levelStr = ToMpvLogLevelString(level);
//        if (levelStr == "")
//        {
//            return Result<int>.Fail(
//                new Error($"The specified log level {level} cannot be converted into a corresponding identifier"));
//        }

//        await Task.Run(() => mpvError = MpvNative.MpvRequestLogMessages(Handle, levelStr));
//        return MpvInteropResult<int>("MPV | set log level failed", mpvError);
//    }

//    public async Task<Result<int>> SetConfigFileAsync(string filePath)
//    {
//        var mpvError = MpvError.Success;
//        await Task.Run(() => mpvError = MpvNative.MpvLoadConfigFile(Handle, filePath));
//        return MpvInteropResult<int>("MPV | load config file failed", mpvError);
//    }

//    public async Task<Result<int>> UseIdleAsync(bool? idleEnable)
//    {
//        var state = idleEnable == null ? "once" : idleEnable == true ? "yes" : "no";
//        var mpvError = MpvError.Success;
//        await Task.Run(() => mpvError = MpvNative.MpvSetOptionString(Handle, "idle", state));
//        return MpvInteropResult<int>("MPV | set 'idle' failed", mpvError);
//    }


//    public async Task<Result<int>> UseKeepOpenAsync(bool isKeepOpen)
//    {
//        var mpvError = MpvError.Success;
//        var state = isKeepOpen ? "yes" : "no";
//        await Task.Run(() => mpvError = MpvNative.MpvSetOptionString(Handle, "keep-open", state));
//        return MpvInteropResult<int>("MPV | set 'keep-open' failed", mpvError);
//    }

//    private void Run()
//    {
//        ObjectDisposedException.ThrowIf(IsDisposed, typeof(MpvClient));

//        if (_eventLoopCancellation != null)
//        {
//            return;
//        }

//        _eventLoopCancellation = new CancellationTokenSource();
//        _eventLoop = Task.Run(() =>
//        {
//            while (_eventLoopCancellation is { Token.IsCancellationRequested: false })
//            {
//                var eventPtr = MpvNative.MpvWaitEvent(Handle, -1);
//                var eventData = Marshal.PtrToStructure<MpvEvent>(eventPtr);

//                if (eventData.EventId != MpvEventId.Shutdown)
//                {
//                    continue;
//                }

//                Shutdown?.Invoke(this, EventArgs.Empty);
//                break;
//            }
//        }, _eventLoopCancellation.Token);
//    }

//    private async Task InitializeAsync(MpvInitializeOptions? options = null)
//    {
//        if (IsInitialized)
//        {
//            return;
//        }

//        var mpvError = MpvError.Success;
//        await Task.Run(() =>
//        {
//            if (options is not null)
//            {
//                if (options.UseConfig != null)
//                {
//                    mpvError = (MpvError)MpvNative.MpvSetOptionString(Handle, "config",
//                        options.UseConfig.Value ? "yes" : "no");
//                    ThrowMpvException("Instance | set 'config' failed", mpvError);
//                }

//                if (!string.IsNullOrEmpty(options.ConfigDirectory))
//                {
//                    mpvError = (MpvError)MpvNative.MpvSetOptionString(Handle, "config-dir", options.ConfigDirectory!);
//                    ThrowMpvException("Instance | set 'config-dir' failed", mpvError);
//                }

//                if (!string.IsNullOrEmpty(options.InputConfigPath))
//                {
//                    mpvError = (MpvError)MpvNative.MpvSetOptionString(Handle, "input-conf", options.InputConfigPath!);
//                    ThrowMpvException("Instance | set 'input-conf' failed", mpvError);
//                }

//                if (options.LoadScripts != null)
//                {
//                    mpvError = (MpvError)MpvNative.MpvSetOptionString(Handle, "load-scripts",
//                        options.LoadScripts.Value ? "yes" : "no");
//                    ThrowMpvException("Instance | set 'load-scripts' failed", mpvError);
//                }

//                if (!string.IsNullOrEmpty(options.ScriptPath))
//                {
//                    mpvError = (MpvError)MpvNative.MpvSetOptionString(Handle, "script", options.ScriptPath!);
//                    ThrowMpvException("Instance | set 'script' failed", mpvError);
//                }

//                if (options.PlayerOperationMode != null)
//                {
//                    var mode = options.PlayerOperationMode switch
//                    {
//                        MpvPlayerOperationMode.PseudoGui => "pseudo-gui",
//                        _ => "cplayer",
//                    };
//                    mpvError = (MpvError)MpvNative.MpvSetOptionString(Handle, "player-operation-mode", mode);
//                    ThrowMpvException("Instance | set 'player-operation-mode' failed", mpvError);
//                }
//            }

//            mpvError = MpvNative.MpvInitialize(Handle);
//            ThrowMpvException("Instance | MPV initialize failed", mpvError);
//        });

//        IsInitialized = true;
//    }

//    private static void ThrowMpvException(string message, MpvError mpvError)
//    {
//        if (mpvError != MpvError.Success)
//        {
//            throw new MpvException(message, mpvError);
//        }
//    }

//    private static Result<T> MpvInteropResult<T>(string message, MpvError mpvError)
//    {
//        return (mpvError == MpvError.Success)
//            ? Result<T>.Ok()
//            : Result<T>.Fail(new Error("MPV interop failed.", new MpvException(message, mpvError)));
//    }

//    private void SendNotify(MpvClientEventId id, object data) =>
//        DataNotify?.Invoke(this, new MpvClientNotifyEventArgs(id, data));

//    public async ValueTask DisposeAsync()
//    {
//        if (IsDisposed)
//        {
//            return;
//        }

//        IsDisposed = true;

//        await Task.Run(() => { MpvNative.MpvCommandString(Handle, "quit"); });
//        await Task.Run(() => { MpvNative.MpvTerminateDestroy(Handle); });
//    }
//}