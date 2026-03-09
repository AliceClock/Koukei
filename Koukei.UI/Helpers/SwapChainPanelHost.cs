using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using WinRT;

namespace Koukei.UI.Helpers;

internal static class SwapChainPanelHost
{
    private const int SetMatrixTransformVTableIndex = 34;
    private static readonly Guid SwapChain2InterfaceId = new("A8BE2AC4-199F-4946-B331-79599FB98DE7");

    public static void SetSwapChain(
        SwapChainPanel panel,
        IntPtr swapChain,
        double compositionScaleX = 1,
        double compositionScaleY = 1)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (swapChain != IntPtr.Zero)
        {
            TrySetSwapChainScaleTransform(swapChain, compositionScaleX, compositionScaleY);
        }

        panel.As<ISwapChainPanelNative>().SetSwapChain(swapChain);
    }

    private static void TrySetSwapChainScaleTransform(
        IntPtr swapChain,
        double compositionScaleX,
        double compositionScaleY)
    {
        var swapChain2 = IntPtr.Zero;
        try
        {
            var interfaceId = SwapChain2InterfaceId;
            if (Marshal.QueryInterface(swapChain, ref interfaceId, out swapChain2) != 0 ||
                swapChain2 == IntPtr.Zero)
            {
                return;
            }

            var vtable = Marshal.ReadIntPtr(swapChain2);
            var setMatrixTransformPointer = Marshal.ReadIntPtr(
                vtable,
                SetMatrixTransformVTableIndex * IntPtr.Size);
            var setMatrixTransform = Marshal.GetDelegateForFunctionPointer<SetMatrixTransformDelegate>(
                setMatrixTransformPointer);
            var matrix = new DxgiMatrix3X2F
            {
                M11 = GetInverseScale(compositionScaleX),
                M22 = GetInverseScale(compositionScaleY)
            };

            _ = setMatrixTransform(swapChain2, ref matrix);
        }
        finally
        {
            if (swapChain2 != IntPtr.Zero)
            {
                _ = Marshal.Release(swapChain2);
            }
        }
    }

    private static float GetInverseScale(double scale)
    {
        return scale > 0 ? (float)(1 / scale) : 1;
    }

    [ComImport]
    [Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISwapChainPanelNative
    {
        void SetSwapChain(IntPtr swapChain);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiMatrix3X2F
    {
        public float M11;
        public float M12;
        public float M21;
        public float M22;
        public float M31;
        public float M32;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetMatrixTransformDelegate(IntPtr swapChain, ref DxgiMatrix3X2F matrix);
}
