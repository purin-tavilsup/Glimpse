namespace Glimpse.Core;

/// <summary>A top-down BGRA pixel buffer plus the dimensions it describes.</summary>
public readonly record struct CapturedFrame(byte[] Bgra, int Width, int Height);

/// <summary>
/// Pure pixel-buffer arithmetic: crops a top-down BGRA frame by a fixed inset on each edge.
/// No Win32, so -- like <see cref="BgraPngEncoder"/> -- it needs no platform attribute and is
/// testable on every OS. Exists because DWMWA_EXTENDED_FRAME_BOUNDS (the crop target) excludes
/// the invisible resize border that GetWindowRect (used to size and position the PrintWindow /
/// BitBlt capture) includes: the capture is taken at the larger window-rect size, then cropped
/// down to the DWM-visible bounds here.
/// </summary>
public static class BgraCrop
{
    /// <summary>Pixels to trim off each edge. The default (all zero) is a no-op.</summary>
    public readonly record struct Insets(int Left, int Top, int Right, int Bottom);

    public static CapturedFrame Apply(CapturedFrame source, Insets inset)
    {
        if (inset == default)
            return source;

        var width = source.Width - inset.Left - inset.Right;
        var height = source.Height - inset.Top - inset.Bottom;
        var sourceStride = source.Width * 4;
        var destStride = width * 4;
        var cropped = new byte[destStride * height];

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = (row + inset.Top) * sourceStride + inset.Left * 4;
            Array.Copy(source.Bgra, sourceOffset, cropped, row * destStride, destStride);
        }

        return new CapturedFrame(cropped, width, height);
    }
}
