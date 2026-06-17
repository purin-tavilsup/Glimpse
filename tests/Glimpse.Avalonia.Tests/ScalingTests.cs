using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class ScalingTests(SnapshotSessionFixture fixture)
{
    [Fact(Skip = "Avalonia 11.3.x headless exposes no per-window render scaling; scaling is descoped to 1.0 in v1.")]
    public void Render_WithScalingTwo_ShouldDoubleOutputPixels()
    {
        var result = fixture.Session.Render(
            () => new Border { Background = Brushes.White },
            new RenderOptions(Width: 100, Height: 80, Scaling: 2.0));

        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(160, result.PixelHeight);
    }

    [Fact]
    public void Render_WithNonUnitScaling_ShouldWarnScalingIgnored()
    {
        var result = fixture.Session.Render(
            () => new Border { Background = Brushes.White },
            new RenderOptions(Width: 100, Height: 80, Scaling: 2.0));

        Assert.Contains("scaling-ignored", result.Warnings);
        Assert.Equal(100, result.PixelWidth); // honestly reports the 1x dimensions actually produced
    }
}
