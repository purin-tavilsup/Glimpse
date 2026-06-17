using Avalonia.Controls;
using Avalonia.Media;
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
        // Use empty Border to expose the window's themed background (light ≈ white, dark ≈ black).
        // Create both controls before any Render calls to avoid thread state issues.
        var controlLight = new Border { };
        var controlDark = new Border { };

        var light = fixture.Session.Render(controlLight, new RenderOptions(Theme: ThemeVariant.Light));
        var dark = fixture.Session.Render(controlDark, new RenderOptions(Theme: ThemeVariant.Dark));

        Assert.NotEqual(Convert.ToHexString(light.Png), Convert.ToHexString(dark.Png));
    }
}
