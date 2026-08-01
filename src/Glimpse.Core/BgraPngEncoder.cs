using System.Runtime.InteropServices;
using SkiaSharp;

namespace Glimpse.Core;

/// <summary>
/// Writes a raw BGRA pixel buffer to a PNG. Uses SkiaSharp, which <see cref="PngAnalysis"/>
/// already decodes with — so encode and decode can never disagree about the format.
/// Cross-platform on purpose: the Windows capturer produces the buffer, but nothing here
/// touches Win32, so the encoder is testable on every OS.
/// </summary>
public static class BgraPngEncoder
{
    /// <summary><paramref name="bgraBuffer"/> must be top-down, 4 bytes per pixel.</summary>
    public static void Write(byte[] bgraBuffer, int width, int height, string outputPath)
    {
        var expected = checked(width * height * 4);
        if (bgraBuffer.Length != expected)
            throw new ArgumentException(
                $"Buffer is {bgraBuffer.Length} bytes; {width}x{height} BGRA needs exactly {expected}.",
                nameof(bgraBuffer));

        // Opaque, not Premul: some capture paths (a screen BitBlt, notably) never write the 4th
        // byte, so it can come back 0. Unpremultiplying a real RGB with A=0 yields (0,0,0,0) --
        // a fully transparent PNG that still reports success. Opaque makes Skia ignore the byte
        // entirely, so a stray zero alpha can never leak into the output.
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(bgraBuffer, 0, bitmap.GetPixels(), bgraBuffer.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        // GetDirectoryName returns "" for a bare filename, and CreateDirectory("") throws.
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(outputPath, data.ToArray());
    }
}
