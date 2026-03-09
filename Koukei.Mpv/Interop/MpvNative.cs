using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

public static partial class MpvNative
{
    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_client_api_version")]
    internal static partial ulong MpvClientApiVersion();

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_error_string")]
    private static partial IntPtr MpvErrorStringPointer(MpvError error);

    internal static string? MpvErrorString(MpvError error)
    {
        // mpv_error_string returns a library-owned static string. It must never be freed
        // by the .NET string marshaller.
        return Marshal.PtrToStringUTF8(MpvErrorStringPointer(error));
    }

    #region Memory API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_free")]
    internal static partial void MpvFree(IntPtr data);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_free_node_contents")]
    internal static partial void MpvFreeNodeContents(ref MpvNode node);

    #endregion

    #region Time API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_get_time_ns")]
    internal static partial long MpvGetTimeNs(MpvHandle context);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_get_time_us")]
    internal static partial long MpvGetTimeUs(MpvHandle context);

    #endregion
}
