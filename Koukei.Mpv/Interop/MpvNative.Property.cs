using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

public static partial class MpvNative
{
    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_load_config_file", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvLoadConfigFile(MpvHandle context, string filename);

    #region Option API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_set_option", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvSetOption(MpvHandle context, string name, MpvFormat format, in MpvNode data);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvSetOptionString(MpvHandle context, string name, string data);

    #endregion

    #region Property API

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvSetProperty(MpvHandle context, string name, MpvFormat format, in MpvNode data);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvSetPropertyString(MpvHandle context, string name, string data);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_del_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvDelProperty(MpvHandle context, string name);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_set_property_async", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvSetPropertyAsync(MpvHandle context, ulong replyUserdata, string name,
        MpvFormat format, in MpvNode data);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvGetProperty(MpvHandle context, string name, MpvFormat format, out MpvNode data);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr MpvGetPropertyStringPointer(MpvHandle context, string name);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_get_property_osd_string",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr MpvGetPropertyOsdStringPointer(MpvHandle context, string name);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_get_property_async", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvGetPropertyAsync(MpvHandle context, ulong replyUserdata, string name,
        MpvFormat format);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial MpvError MpvObserveProperty(MpvHandle mpv, ulong replyUserdata, string name,
        MpvFormat format);

    [LibraryImport("libmpv-2.dll", EntryPoint = "mpv_unobserve_property")]
    internal static partial int MpvUnobserveProperty(MpvHandle mpv, ulong registeredReplyUserdata);

    #endregion
}
