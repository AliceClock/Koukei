using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

public static partial class MpvNative
{
    #region Command API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_command")]
    internal static partial MpvError MpvCommand(MpvHandle context, IntPtr args);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_command_node")]
    internal static partial MpvError MpvCommandNode(MpvHandle context, in MpvNode args, out MpvNode result);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_command_ret")]
    internal static partial MpvError MpvCommandRet(MpvHandle context, IntPtr args, out MpvNode result);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_command_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvCommandString(MpvHandle context, string args);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_command_async")]
    internal static partial MpvError MpvCommandAsync(MpvHandle context, ulong replyUserdata, IntPtr args);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_command_node_async")]
    internal static partial MpvError MpvCommandNodeAsync(MpvHandle context, ulong replyUserdata, in MpvNode args);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_abort_async_command")]
    internal static partial void MpvAbortAsyncCommand(MpvHandle context, ulong replyUserdata);

    #endregion
}
