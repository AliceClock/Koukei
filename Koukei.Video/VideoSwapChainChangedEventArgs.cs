namespace Koukei.Video;

public sealed class VideoSwapChainChangedEventArgs : EventArgs
{
    public VideoSwapChainChangedEventArgs(IntPtr swapChain)
    {
        SwapChain = swapChain;
    }

    public IntPtr SwapChain { get; }
}
