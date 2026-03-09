using System;
using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

public struct MpvHandle
{
    public IntPtr Handle;
}

[StructLayout(LayoutKind.Explicit)]
public struct MpvNode
{
    [FieldOffset(0)] public IntPtr String;

    [FieldOffset(0)] public int Flag;

    [FieldOffset(0)] public long Int64;

    [FieldOffset(0)] public double Double;

    [FieldOffset(0)] public IntPtr List;

    [FieldOffset(0)] public IntPtr ByteArray;

    [FieldOffset(8)] public MpvFormat Format;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvNodeList
{
    public int Num;

    public IntPtr Value;

    public IntPtr Keys;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvByteArray
{
    public IntPtr Data;

    public nuint Size;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventProperty
{
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string Name;

    public MpvFormat Format;

    public IntPtr Data;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventLogMessage
{
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string Prefix;

    [MarshalAs(UnmanagedType.LPUTF8Str)] public string Level;

    [MarshalAs(UnmanagedType.LPUTF8Str)] public string Text;

    public MpvLogLevel LogLevel;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventStartFile
{
    public long PlaylistEntryId;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventEndFile
{
    public MpvEndFileReason Reason;

    public MpvError Error;

    public long PlaylistEntryId;

    public long PlaylistInsertId;

    public int PlaylistInsertNumEntries;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventClientMessage
{
    public int NumArgs;

    public IntPtr Args;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventHook
{
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string Name;

    public ulong Id;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventCommand
{
    public MpvNode Result;
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEvent
{
    public MpvEventId EventId;

    public MpvError Error;

    public ulong ReplyUserdata;

    public IntPtr Data;
}