using SkiaSharp;

namespace Glimpse.Core;

public sealed record PngInspection(int Width, int Height, IReadOnlyList<string> Warnings);

/// <summary>Decodes a PNG and flags misleading frames an agent might trust (blank / single-color).</summary>
public static class PngAnalysis
{
    private const int SampleStride = 8; // sample a grid, not every pixel

    public static PngInspection Inspect(string pngPath)
    {
        if (!File.Exists(pngPath))
            throw new FileNotFoundException("PNG not found.", pngPath);

        return Inspect(File.ReadAllBytes(pngPath));
    }

    public static PngInspection Inspect(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png)
            ?? throw new ArgumentException("Bytes are not a decodable image.", nameof(png));

        var first = bitmap.GetPixel(0, 0);
        var uniform = true;

        for (var y = 0; y < bitmap.Height && uniform; y += SampleStride)
            for (var x = 0; x < bitmap.Width; x += SampleStride)
                if (bitmap.GetPixel(x, y) != first)
                {
                    uniform = false;
                    break;
                }

        var warnings = uniform
            ? new[] { $"single-color-frame:{first.Red:X2}{first.Green:X2}{first.Blue:X2}{first.Alpha:X2}" }
            : Array.Empty<string>();

        return new PngInspection(bitmap.Width, bitmap.Height, warnings);
    }
}
