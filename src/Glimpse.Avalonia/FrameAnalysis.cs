using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Glimpse.Avalonia;

/// <summary>Heuristics that flag misleading frames an agent might trust (blank / single-color).</summary>
internal static class FrameAnalysis
{
    private const int SampleStride = 8; // sample a grid, not every pixel

    public static IReadOnlyList<string> Inspect(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var first = ReadPixel(buffer, 0, 0);
        var uniform = true;

        for (var y = 0; y < buffer.Size.Height && uniform; y += SampleStride)
            for (var x = 0; x < buffer.Size.Width; x += SampleStride)
                if (ReadPixel(buffer, x, y) != first)
                {
                    uniform = false;
                    break;
                }

        return uniform
            ? [$"single-color-frame:{first:X8}"]
            : [];
    }

    private static unsafe uint ReadPixel(ILockedFramebuffer buffer, int x, int y)
    {
        var row = (byte*)buffer.Address + (y * buffer.RowBytes);
        return ((uint*)row)[x]; // Bgra8888 / Rgba8888 — exact channel order irrelevant for equality
    }
}
