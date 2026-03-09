using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Koukei.UI.Helpers;

internal static class NativeWindowEffects
{
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly IntPtr HwndTopMost = new(-1);

    private const int DwmwaBorderColor = 34;
    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpNoActivate = 0x0010;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpShowWindow = 0x0040;
    private const long WsCaption = 0x00C00000L;
    private const long WsExTopMost = 0x00000008L;
    private const int WsExLayered = 0x00080000;
    private const long WsThickFrame = 0x00040000L;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const int LwaAlpha = 0x00000002;

    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var isTopMost = (GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExTopMost) != 0;
        _ = ShowWindow(hwnd, IsIconic(hwnd) ? SwRestore : SwShow);
        _ = SetWindowPos(
            hwnd,
            isTopMost ? HwndTopMost : HwndTop,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpShowWindow);
        _ = BringWindowToTop(hwnd);
        if (SetForegroundWindow(hwnd))
        {
            return;
        }

        TryActivateAcrossWindowThreads(hwnd);
    }

    public static bool IsForegroundWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;

    public static bool IsForegroundWindowOwnedByCurrentProcess()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
        return processId == unchecked((uint)Environment.ProcessId);
    }

    public static void SetTopMost(IntPtr hwnd, bool isTopMost)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(
            hwnd,
            isTopMost ? HwndTopMost : HwndNoTopMost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpShowWindow);
    }

    public static void ApplyBorderlessChrome(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlStyle);
        var borderlessStyle = new IntPtr(style.ToInt64() & ~(WsCaption | WsThickFrame));
        if (borderlessStyle != style)
        {
            _ = SetWindowLongPtr(hwnd, GwlStyle, borderlessStyle);
        }

        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        TryHideDwmBorder(hwnd);
    }

    public static void SetOpacity(IntPtr hwnd, byte opacity)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        EnsureLayered(hwnd);
        ApplyOpacity(hwnd, opacity);
    }

    public static async Task FadeAsync(
        IntPtr hwnd,
        byte from,
        byte to,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (from == to || duration <= TimeSpan.Zero)
        {
            SetOpacity(hwnd, to);
            return;
        }

        EnsureLayered(hwnd);
        ApplyOpacity(hwnd, from);

        var stopwatch = Stopwatch.StartNew();
        var frameInterval = TimeSpan.FromMilliseconds(1000d / 120);
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            var value = from + ((to - from) * easedProgress);
            ApplyOpacity(hwnd, (byte)Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue));

            var remaining = duration - stopwatch.Elapsed;
            var delay = remaining < frameInterval ? remaining : frameInterval;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ApplyOpacity(hwnd, to);
    }

    public static void ClearOpacity(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        var normalStyle = new IntPtr(style.ToInt64() & ~WsExLayered);
        if (normalStyle != style)
        {
            _ = SetWindowLongPtr(hwnd, GwlExStyle, normalStyle);
        }
    }

    private static void TryHideDwmBorder(IntPtr hwnd)
    {
        try
        {
            var borderColor = DwmColorNone;
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void EnsureLayered(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        var layeredStyle = new IntPtr(style.ToInt64() | WsExLayered);
        if (layeredStyle != style)
        {
            _ = SetWindowLongPtr(hwnd, GwlExStyle, layeredStyle);
        }
    }

    private static void ApplyOpacity(IntPtr hwnd, byte opacity) =>
        _ = SetLayeredWindowAttributes(hwnd, 0, opacity, LwaAlpha);

    private static void TryActivateAcrossWindowThreads(IntPtr hwnd)
    {
        var foregroundWindow = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var attachedForeground = false;
        var attachedTarget = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }

            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            _ = BringWindowToTop(hwnd);
            _ = SetActiveWindow(hwnd);
            _ = SetFocus(hwnd);
            if (SetForegroundWindow(hwnd))
            {
                return;
            }

            // A short topmost pulse keeps the requested window visible when Windows
            // rejects foreground activation without changing a persistent topmost window.
            var isTopMost = (GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExTopMost) != 0;
            if (!isTopMost)
            {
                _ = SetWindowPos(
                    hwnd,
                    HwndTopMost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpShowWindow);
                _ = SetWindowPos(
                    hwnd,
                    HwndNoTopMost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpShowWindow);
            }

            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                _ = AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : new IntPtr(GetWindowLong32(hwnd, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint attributeValue, int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetActiveWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hwnd);
}
