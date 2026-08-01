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
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
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
