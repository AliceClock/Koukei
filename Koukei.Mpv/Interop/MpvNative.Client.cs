using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

public static partial class MpvNative
{
    #region Client API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_client_name")]
    private static partial IntPtr MpvClientNamePointer(MpvHandle context);

    internal static string? MpvClientName(MpvHandle context)
    {
        // The returned name is owned by the mpv handle and remains valid until that
        // handle is destroyed.
        return Marshal.PtrToStringUTF8(MpvClientNamePointer(context));
    }

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_client_id")]
    internal static partial long MpvClientId(MpvHandle context);

    #endregion

    #region Lifecycle API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_create")]
    internal static partial MpvHandle MpvCreate();

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_initialize")]
    internal static partial MpvError MpvInitialize(MpvHandle context);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_destroy")]
    internal static partial void MpvDestroy(MpvHandle context);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_terminate_destroy")]
    internal static partial void MpvTerminateDestroy(MpvHandle context);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_create_client", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvHandle MpvCreateClient(MpvHandle context, string name);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_create_weak_client", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvHandle MpvCreateWeakClient(MpvHandle context, string name);

    #endregion
}
