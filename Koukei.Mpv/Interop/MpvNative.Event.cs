using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

public delegate void MpvWakeupCallback(IntPtr data);

public static partial class MpvNative
{
    #region Event API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_event_name")]
    private static partial IntPtr MpvEventNamePointer(MpvEventId @event);

    internal static string? MpvEventName(MpvEventId @event)
    {
        // mpv_event_name returns a library-owned static string.
        return Marshal.PtrToStringUTF8(MpvEventNamePointer(@event));
    }

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_event_to_node")]
    internal static partial MpvError MpvEventToNode(out MpvNode destination, in MpvEvent source);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_request_event")]
    internal static partial MpvError MpvRequestEvent(MpvHandle context, MpvEventId @event, int enabled);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvRequestLogMessages(MpvHandle context, string minLevel);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_wait_event")]
    internal static partial IntPtr MpvWaitEvent(MpvHandle context, double timeout);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_wakeup")]
    internal static partial void MpvWakeup(MpvHandle context);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_set_wakeup_callback")]
    internal static partial void MpvSetWakeupCallback(MpvHandle context, MpvWakeupCallback callback,
        IntPtr data);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_wait_async_requests")]
    internal static partial void MpvWaitAsyncRequests(MpvHandle context);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_get_wakeup_pipe")]
    internal static partial int MpvGetWakeupPipe(MpvHandle context);

    #endregion

    #region Hook API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_hook_add", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvHookAdd(MpvHandle context, ulong replyUserdata, string name, int priority);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_hook_continue")]
    internal static partial MpvError MpvHookContinue(MpvHandle context, ulong id);

    #endregion
}
