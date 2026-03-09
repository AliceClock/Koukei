using System.Runtime.InteropServices;

namespace Koukei.Mpv.Interop;

internal static class MpvCommandInvoker
{
    public static MpvError Invoke(MpvHandle context, params string[] args)
    {
        return InvokeCore(context, asynchronous: false, args: args);
    }

    public static MpvError InvokeAsynchronous(MpvHandle context, ulong replyUserdata, params string[] args)
    {
        return InvokeCore(context, asynchronous: true, replyUserdata, args);
    }

    private static MpvError InvokeCore(
        MpvHandle context,
        bool asynchronous,
        ulong replyUserdata = 0,
        params string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            throw new ArgumentException("At least one mpv command argument is required.", nameof(args));
        }

        var argumentPointers = new IntPtr[args.Length + 1];
        var argv = IntPtr.Zero;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                argumentPointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            argv = Marshal.AllocCoTaskMem(IntPtr.Size * argumentPointers.Length);
            Marshal.Copy(argumentPointers, 0, argv, argumentPointers.Length);

            return asynchronous
                ? MpvNative.MpvCommandAsync(context, replyUserdata, argv)
                : MpvNative.MpvCommand(context, argv);
        }
        finally
        {
            if (argv != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(argv);
            }

            foreach (var argumentPointer in argumentPointers)
            {
                if (argumentPointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(argumentPointer);
                }
            }
        }
    }
}
