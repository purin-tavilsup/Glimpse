using Glimpse.Core;
using SkiaSharp;
using Xunit;

namespace Glimpse.Core.Tests;

public class PngAnalysisTests
{
    private static byte[] SolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] TwoColorPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(0, 0, width / 2f, height, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Inspect_WithSolidColor_ShouldReturnSingleColorWarning()
    {
        var png = SolidPng(64, 48, new SKColor(0x11, 0x22, 0x33, 0xFF));

        var result = PngAnalysis.Inspect(png);

        Assert.Equal(64, result.Width);
        Assert.Equal(48, result.Height);
        Assert.Contains("single-color-frame:112233FF", result.Warnings);
    }

    [Fact]
    public void Inspect_WithMultipleColors_ShouldReturnNoWarnings()
    {
        var png = TwoColorPng(64, 48);

        var result = PngAnalysis.Inspect(png);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Inspect_WithMissingFile_ShouldThrowFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => PngAnalysis.Inspect("/no/such/file.png"));
    }
}
