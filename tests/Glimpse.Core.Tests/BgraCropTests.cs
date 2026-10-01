using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class BgraCropTests
{
    /// <summary>Every pixel's B byte is its own row-major index, so a crop can be checked by
    /// reading back which source pixel ended up where.</summary>
    private static byte[] IndexedBgra(int width, int height)
    {
        var buffer = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
            buffer[i * 4] = (byte)i;
        return buffer;
    }

    private static int IndexAt(byte[] bgra, int width, int x, int y) => bgra[(y * width + x) * 4];

    [Fact]
    public void Apply_WithNonZeroInsets_ShouldReturnExpectedSubRectangle()
    {
        var source = new CapturedFrame(IndexedBgra(4, 4), 4, 4);

        var cropped = BgraCrop.Apply(source, new BgraCrop.Insets(Left: 1, Top: 1, Right: 1, Bottom: 1));

        Assert.Equal(2, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(5, IndexAt(cropped.Bgra, cropped.Width, 0, 0));
        Assert.Equal(6, IndexAt(cropped.Bgra, cropped.Width, 1, 0));
        Assert.Equal(9, IndexAt(cropped.Bgra, cropped.Width, 0, 1));
        Assert.Equal(10, IndexAt(cropped.Bgra, cropped.Width, 1, 1));
    }

    [Fact]
    public void Apply_WithAsymmetricInsets_ShouldCropEachEdgeIndependently()
    {
        // Guards against a transposed Left/Top/Right/Bottom -- a symmetric inset would not catch that.
        var source = new CapturedFrame(IndexedBgra(5, 4), 5, 4);

        var cropped = BgraCrop.Apply(source, new BgraCrop.Insets(Left: 2, Top: 1, Right: 0, Bottom: 2));

        Assert.Equal(3, cropped.Width);
        Assert.Equal(1, cropped.Height);
        Assert.Equal(7, IndexAt(cropped.Bgra, cropped.Width, 0, 0));
        Assert.Equal(8, IndexAt(cropped.Bgra, cropped.Width, 1, 0));
        Assert.Equal(9, IndexAt(cropped.Bgra, cropped.Width, 2, 0));
    }

    [Fact]
    public void Apply_WithZeroInsets_ShouldReturnSourceUnchanged()
    {
        // The no-op path: DwmGetWindowAttribute failing falls back to a zero inset, so this must
        // not allocate or copy -- it should hand back the exact same buffer.
        var source = new CapturedFrame(IndexedBgra(3, 3), 3, 3);

        var result = BgraCrop.Apply(source, default);

        Assert.Same(source.Bgra, result.Bgra);
        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
    }
}
