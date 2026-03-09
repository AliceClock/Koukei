using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Koukei.UI.Helpers;

internal static class NativeMpvHostWindow
{
    private const string HostWindowClassName = "KoukeiMpvHostWindow";

    private static readonly object ClassRegistrationLock = new();
    private static readonly object KeyDownHandlersLock = new();
    private static readonly object MouseInputHandlersLock = new();
    private static readonly Dictionary<IntPtr, Func<int, bool>> KeyDownHandlers = [];
    private static readonly Dictionary<IntPtr, Action<NativeMpvMouseInput>> MouseInputHandlers = [];
    private static readonly WindowProcedure HostWindowProcedure = OnHostWindowMessage;

    private static bool s_isClassRegistered;
    private static IntPtr s_arrowCursor;

    private const int ErrorClassAlreadyExists = 1410;
    private const int DlgcWantAllKeys = 0x0004;
    private const int HtClient = 1;
    private const int IdcArrow = 32512;
    private const int MaActivate = 1;
    private const int SwpNoActivate = 0x0010;
    private const int SwpShowWindow = 0x0040;
    private const int TmeLeave = 0x00000002;
    private const int WmChar = 0x0102;
    private const int WmGetDlgCode = 0x0087;
    private const int WmKeyDown = 0x0100;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmLButtonUp = 0x0202;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonDblClk = 0x0209;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseActivate = 0x0021;
    private const int WmMouseHWheel = 0x020E;
    private const int WmMouseLeave = 0x02A3;
    private const int WmMouseMove = 0x0200;
    private const int WmMouseWheel = 0x020A;
    private const int WmNcHitTest = 0x0084;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonDblClk = 0x0206;
    private const int WmRButtonUp = 0x0205;
    private const int WmSetCursor = 0x0020;
    private const int WmSysChar = 0x0106;
    private const int WmSysKeyDown = 0x0104;
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;

    public static IntPtr Create(
        IntPtr parentHwnd,
        Func<int, bool>? keyDownHandler = null,
        Action<NativeMpvMouseInput>? mouseInputHandler = null)
    {
        if (parentHwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        EnsureWindowClass();

        var hwnd = CreateWindowEx(
            0,
            HostWindowClassName,
            string.Empty,
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0,
            0,
            1,
            1,
            parentHwnd,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (hwnd != IntPtr.Zero && keyDownHandler is not null)
        {
            lock (KeyDownHandlersLock)
            {
                KeyDownHandlers[hwnd] = keyDownHandler;
            }
        }

        if (hwnd != IntPtr.Zero && mouseInputHandler is not null)
        {
            lock (MouseInputHandlersLock)
            {
                MouseInputHandlers[hwnd] = mouseInputHandler;
            }
        }

        ResizeToParent(parentHwnd, hwnd);
        return hwnd;
    }

    private static void EnsureWindowClass()
    {
        if (s_isClassRegistered)
        {
            return;
        }

        lock (ClassRegistrationLock)
        {
            if (s_isClassRegistered)
            {
                return;
            }

            s_arrowCursor = LoadCursor(IntPtr.Zero, new IntPtr(IdcArrow));

            var windowClass = new WindowClassEx
            {
                Size = Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = HostWindowProcedure,
                Instance = GetModuleHandle(null),
                Cursor = s_arrowCursor,
                ClassName = HostWindowClassName
            };

            var atom = RegisterClassEx(ref windowClass);
            if (atom == 0 && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
            {
                return;
            }

            s_isClassRegistered = true;
        }
    }

    private static IntPtr OnHostWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        return message switch
        {
            WmGetDlgCode => new IntPtr(DlgcWantAllKeys),
            WmNcHitTest => new IntPtr(HtClient),
            WmMouseActivate => new IntPtr(MaActivate),
            WmSetCursor => SetArrowCursor(),
            WmMouseMove => NotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.Move)
                ? IntPtr.Zero
                : DefWindowProc(hwnd, message, wParam, lParam),
            WmMouseLeave => NotifyMouseLeave(hwnd)
                ? IntPtr.Zero
                : DefWindowProc(hwnd, message, wParam, lParam),
            WmLButtonDown => CaptureAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.LeftDown),
            WmLButtonUp => ReleaseAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.LeftUp),
            WmLButtonDblClk => CaptureAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.LeftDoubleClick),
            WmRButtonDown => CaptureAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.RightDown),
            WmRButtonUp => ReleaseAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.RightUp),
            WmRButtonDblClk => CaptureAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.RightDoubleClick),
            WmMButtonDown => CaptureAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.MiddleDown),
            WmMButtonUp => ReleaseAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.MiddleUp),
            WmMButtonDblClk => CaptureAndNotifyMouseInput(hwnd, message, wParam, lParam, NativeMpvMouseInputKind.MiddleDoubleClick),
            WmMouseWheel => NotifyWheelInput(hwnd, wParam, lParam, isHorizontal: false)
                ? IntPtr.Zero
                : DefWindowProc(hwnd, message, wParam, lParam),
            WmMouseHWheel => NotifyWheelInput(hwnd, wParam, lParam, isHorizontal: true)
                ? IntPtr.Zero
                : DefWindowProc(hwnd, message, wParam, lParam),
            WmKeyDown or WmSysKeyDown => NotifyKeyDown(hwnd, wParam)
                ? IntPtr.Zero
                : DefWindowProc(hwnd, message, wParam, lParam),
            WmChar or WmSysChar => IntPtr.Zero,
            _ => DefWindowProc(hwnd, message, wParam, lParam)
        };
    }

    private static bool NotifyKeyDown(IntPtr hwnd, IntPtr virtualKey)
    {
        Func<int, bool>? keyDownHandler;
        lock (KeyDownHandlersLock)
        {
            KeyDownHandlers.TryGetValue(hwnd, out keyDownHandler);
        }

        return keyDownHandler?.Invoke(virtualKey.ToInt32()) == true;
    }

    private static bool NotifyMouseInput(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        NativeMpvMouseInputKind kind)
    {
        if (message == WmMouseMove)
        {
            TrackMouseLeave(hwnd);
        }

        var point = GetClientPointFromLParam(lParam);
        return NotifyMouseInput(hwnd, new NativeMpvMouseInput(kind, point.X, point.Y, 0));
    }

    private static bool NotifyMouseLeave(IntPtr hwnd)
    {
        return NotifyMouseInput(hwnd, new NativeMpvMouseInput(NativeMpvMouseInputKind.Leave, 0, 0, 0));
    }

    private static bool NotifyWheelInput(IntPtr hwnd, IntPtr wParam, IntPtr lParam, bool isHorizontal)
    {
        var point = GetClientPointFromScreenLParam(hwnd, lParam);
        var wheelDelta = GetSignedHighWord(wParam);
        var kind = isHorizontal
            ? NativeMpvMouseInputKind.HorizontalWheel
            : NativeMpvMouseInputKind.VerticalWheel;

        return NotifyMouseInput(hwnd, new NativeMpvMouseInput(kind, point.X, point.Y, wheelDelta));
    }

    private static bool NotifyMouseInput(IntPtr hwnd, NativeMpvMouseInput input)
    {
        Action<NativeMpvMouseInput>? mouseInputHandler;
        lock (MouseInputHandlersLock)
        {
            MouseInputHandlers.TryGetValue(hwnd, out mouseInputHandler);
        }

        if (mouseInputHandler is null)
        {
            return false;
        }

        mouseInputHandler(input);
        return true;
    }

    private static IntPtr CaptureAndNotifyMouseInput(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        NativeMpvMouseInputKind kind)
    {
        Focus(hwnd);
        _ = SetCapture(hwnd);

        return NotifyMouseInput(hwnd, message, wParam, lParam, kind)
            ? IntPtr.Zero
            : DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static IntPtr ReleaseAndNotifyMouseInput(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        NativeMpvMouseInputKind kind)
    {
        _ = ReleaseCapture();

        return NotifyMouseInput(hwnd, message, wParam, lParam, kind)
            ? IntPtr.Zero
            : DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static IntPtr SetArrowCursor()
    {
        if (s_arrowCursor == IntPtr.Zero)
        {
            s_arrowCursor = LoadCursor(IntPtr.Zero, new IntPtr(IdcArrow));
        }

        _ = SetCursor(s_arrowCursor);
        return new IntPtr(1);
    }

    public static void Focus(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            _ = SetFocus(hwnd);
        }
    }

    private static Point GetClientPointFromLParam(IntPtr lParam)
    {
        return new Point(GetSignedLowWord(lParam), GetSignedHighWord(lParam));
    }

    private static Point GetClientPointFromScreenLParam(IntPtr hwnd, IntPtr lParam)
    {
        var point = GetClientPointFromLParam(lParam);
        _ = ScreenToClient(hwnd, ref point);
        return point;
    }

    private static int GetSignedLowWord(IntPtr value)
    {
        return unchecked((short)(value.ToInt64() & 0xFFFF));
    }

    private static int GetSignedHighWord(IntPtr value)
    {
        return unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
    }

    private static void TrackMouseLeave(IntPtr hwnd)
    {
        var trackEvent = new TrackMouseEventOptions
        {
            Size = Marshal.SizeOf<TrackMouseEventOptions>(),
            Flags = TmeLeave,
            Track = hwnd
        };

        _ = TrackMouseEvent(ref trackEvent);
    }

    public static void ResizeToParent(IntPtr parentHwnd, IntPtr childHwnd)
    {
        if (parentHwnd == IntPtr.Zero || childHwnd == IntPtr.Zero)
        {
            return;
        }

        if (!GetClientRect(parentHwnd, out var clientRect))
        {
            return;
        }

        _ = SetWindowPos(
            childHwnd,
            IntPtr.Zero,
            0,
            0,
            Math.Max(1, clientRect.Right - clientRect.Left),
            Math.Max(1, clientRect.Bottom - clientRect.Top),
            SwpNoActivate | SwpShowWindow);
    }

    public static void Destroy(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            lock (KeyDownHandlersLock)
            {
                KeyDownHandlers.Remove(hwnd);
            }

            lock (MouseInputHandlersLock)
            {
                MouseInputHandlers.Remove(hwnd);
            }

            _ = DestroyWindow(hwnd);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventOptions
    {
        public int Size;
        public int Flags;
        public IntPtr Track;
        public int HoverTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public int Size;
        public int Style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WindowProcedure WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;
        public IntPtr SmallIcon;
    }

    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackMouseEvent(ref TrackMouseEventOptions eventTrack);
}

internal readonly record struct NativeMpvMouseInput(
    NativeMpvMouseInputKind Kind,
    int X,
    int Y,
    int WheelDelta);

internal enum NativeMpvMouseInputKind
{
    Move,
    Leave,
    LeftDown,
    LeftUp,
    LeftDoubleClick,
    RightDown,
    RightUp,
    RightDoubleClick,
    MiddleDown,
    MiddleUp,
    MiddleDoubleClick,
    VerticalWheel,
    HorizontalWheel
}
