using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class RenderSmokeTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SimpleControl_ShouldProduceNonEmptyPngOfRequestedSize()
    {
        var control = new Border { Background = Brushes.CornflowerBlue };

        var result = fixture.Session.Render(control, new RenderOptions(Width: 320, Height: 240));

        Assert.NotEmpty(result.Png);
        Assert.Equal(320, result.PixelWidth);
        Assert.Equal(240, result.PixelHeight);
        Assert.Equal(0x89, result.Png[0]); // PNG magic byte
    }
}
