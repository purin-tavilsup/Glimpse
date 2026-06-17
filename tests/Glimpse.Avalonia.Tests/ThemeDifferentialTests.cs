using Avalonia.Controls;
using Avalonia.Styling;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class ThemeDifferentialTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SameControlLightVsDark_ShouldProduceDifferentPixels()
    {
        // A themed control: FluentTheme paints the window background per variant.
        Control NewControl() => new TextBlock { Text = "Glimpse" };

        var light = fixture.Session.Render(NewControl, new RenderOptions(Theme: ThemeVariant.Light));
        var dark = fixture.Session.Render(NewControl, new RenderOptions(Theme: ThemeVariant.Dark));

        Assert.NotEqual(Convert.ToHexString(light.Png), Convert.ToHexString(dark.Png));
    }
}
