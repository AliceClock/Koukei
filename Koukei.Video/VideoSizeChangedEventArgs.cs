namespace Koukei.Video;

public sealed class VideoSizeChangedEventArgs(string? filePath, int pixelWidth, int pixelHeight) : EventArgs
{
    public string? FilePath { get; } = filePath;

    public int PixelWidth { get; } = pixelWidth;

    public int PixelHeight { get; } = pixelHeight;
}
