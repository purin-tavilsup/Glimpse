using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class BgraPngEncoderTests
{
    private static string TempPng() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

    /// <summary>BGRA, 4 bytes per pixel, top-down. Row 0 is red, every other row is blue.</summary>
    private static byte[] TwoToneBgra(int width, int height)
    {
        var buffer = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var isFirstRow = y == 0;
                buffer[i + 0] = isFirstRow ? (byte)0 : (byte)255;   // B
                buffer[i + 1] = 0;                                   // G
                buffer[i + 2] = isFirstRow ? (byte)255 : (byte)0;    // R
                buffer[i + 3] = 255;                                 // A
            }
        return buffer;
    }

    private static byte[] UniformBgra(int width, int height)
    {
        var buffer = new byte[width * height * 4];
        Array.Fill(buffer, (byte)255);
        return buffer;
    }

    [Fact]
    public void Write_ShouldPreserveDimensions()
    {
        var path = TempPng();

        BgraPngEncoder.Write(TwoToneBgra(24, 16), 24, 16, path);

        var inspection = PngAnalysis.Inspect(path);
        Assert.Equal(24, inspection.Width);
        Assert.Equal(16, inspection.Height);
        File.Delete(path);
    }

    [Fact]
    public void Write_WithVariedPixels_ShouldNotBeFlaggedSingleColor()
    {
        var path = TempPng();

        BgraPngEncoder.Write(TwoToneBgra(32, 32), 32, 32, path);

        Assert.Empty(PngAnalysis.Inspect(path).Warnings);
        File.Delete(path);
    }

    [Fact]
    public void Write_WithUniformPixels_ShouldBeFlaggedSingleColor()
    {
        // This is the safety net that catches a black PrintWindow result.
        var path = TempPng();

        BgraPngEncoder.Write(UniformBgra(32, 32), 32, 32, path);

        Assert.Contains(PngAnalysis.Inspect(path).Warnings,
            w => w.StartsWith("single-color-frame:"));
        File.Delete(path);
    }

    [Fact]
    public void Write_ShouldWriteRowsTopDownNotFlipped()
    {
        // WindowsAppCapturer creates its DIB with a negative biHeight for top-down rows. If
        // that sign is wrong the image is vertically flipped -- which looks like a rendering
        // bug, so pin the orientation here: row 0 is red, the last row is blue.
        var path = TempPng();
        BgraPngEncoder.Write(TwoToneBgra(8, 8), 8, 8, path);

        using var decoded = SkiaSharp.SKBitmap.Decode(File.ReadAllBytes(path));

        Assert.Equal(255, decoded.GetPixel(0, 0).Red);
        Assert.Equal(0, decoded.GetPixel(0, 0).Blue);
        Assert.Equal(255, decoded.GetPixel(0, 7).Blue);
        Assert.Equal(0, decoded.GetPixel(0, 7).Red);
        File.Delete(path);
    }

    [Fact]
    public void Write_WithABareFilename_ShouldNotThrow()
    {
        // Path.GetDirectoryName returns "" for a bare filename, and Directory.CreateDirectory("")
        // throws -- so the encoder must skip the create in that case. Deliberately does NOT
        // mutate Directory.SetCurrentDirectory: xUnit can run test classes in parallel, and a
        // process-wide cwd change would be a latent flake for any concurrently-running test that
        // resolves a relative path. A bare filename already has an empty GetDirectoryName()
        // regardless of what the current directory happens to be.
        var name = $"{Guid.NewGuid():N}.png";

        BgraPngEncoder.Write(TwoToneBgra(4, 4), 4, 4, name);

        Assert.True(File.Exists(name));
        File.Delete(name);
    }

    [Fact]
    public void Write_WithMismatchedBufferLength_ShouldThrow()
    {
        // BgraPngEncoder.Write is the public contract Task 7 consumes; a caller passing a buffer
        // longer than width*height*4 would otherwise write past Skia's allocation -- silent heap
        // corruption, no exception. This guard turns that into a clear, immediate failure.
        var tooLong = new byte[8 * 8 * 4 + 4];

        Assert.Throws<ArgumentException>(() => BgraPngEncoder.Write(tooLong, 8, 8, TempPng()));
    }
}
