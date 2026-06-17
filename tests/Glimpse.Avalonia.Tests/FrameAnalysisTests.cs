using Avalonia.Controls;
using Avalonia.Media;
using Glimpse.Avalonia;
using Xunit;

namespace Glimpse.Avalonia.Tests;

[Collection("snapshot")]
public class FrameAnalysisTests(SnapshotSessionFixture fixture)
{
    [Fact]
    public void Render_SolidColorControl_ShouldWarnSingleColorFrame()
    {
        var result = fixture.Session.Render(
            () => new Border { Background = Brushes.Red },
            new RenderOptions(Width: 64, Height: 64));

        Assert.Contains(result.Warnings, w => w.StartsWith("single-color-frame"));
    }

    [Fact]
    public void Render_ControlWithText_ShouldNotWarnSingleColorFrame()
    {
        var result = fixture.Session.Render(
            () => new TextBlock { Text = "content", Foreground = Brushes.Black, FontSize = 32 },
            new RenderOptions(Width: 200, Height: 80));

        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("single-color-frame"));
    }

    [Fact]
    public void Render_NormalRender_ShouldNotWarnFontInterUnresolved()
    {
        // WithInterFont() registers Inter; the resolution check must NOT false-positive (see Task 8 caveat).
        var result = fixture.Session.Render(
            () => new TextBlock { Text = "content", Foreground = Brushes.Black },
            new RenderOptions(Width: 200, Height: 80));

        Assert.DoesNotContain("font-inter-unresolved", result.Warnings);
    }
}
