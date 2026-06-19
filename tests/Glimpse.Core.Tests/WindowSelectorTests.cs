using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class WindowSelectorTests
{
    private static WindowInfo Win(uint id, string owner, string? title = null,
        int w = 800, int h = 600, int layer = 0, bool onScreen = true) =>
        new(id, owner, title, 0, 0, w, h, layer, onScreen);

    [Fact]
    public void SelectFrontmost_WithCaseInsensitiveOwnerMatch_ShouldReturnIt()
    {
        var windows = new[] { Win(1, "Google Chrome"), Win(2, "Finder") };

        var result = WindowSelector.SelectFrontmost(windows, "chrome");

        Assert.NotNull(result);
        Assert.Equal(1u, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithMultipleMatches_ShouldReturnFirstFrontmost()
    {
        // CGWindowList order is front-to-back, so the first survivor is frontmost.
        var windows = new[] { Win(10, "Code", "main.cs"), Win(11, "Code", "other.cs") };

        var result = WindowSelector.SelectFrontmost(windows, "Code");

        Assert.Equal(10u, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithTitleFilter_ShouldPickMatchingTitle()
    {
        var windows = new[] { Win(20, "Code", "main.cs"), Win(21, "Code", "settings.json") };

        var result = WindowSelector.SelectFrontmost(windows, "Code", "settings");

        Assert.Equal(21u, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_ShouldExcludeNonZeroLayerOffscreenAndTinyWindows()
    {
        var windows = new[]
        {
            Win(30, "Safari", layer: 25),          // menubar/overlay layer -> excluded
            Win(31, "Safari", onScreen: false),    // offscreen -> excluded
            Win(32, "Safari", w: 8, h: 8),         // tiny helper -> excluded
            Win(33, "Safari", w: 1200, h: 800),    // the real window
        };

        var result = WindowSelector.SelectFrontmost(windows, "Safari");

        Assert.Equal(33u, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithNoMatch_ShouldReturnNull()
    {
        var windows = new[] { Win(40, "Finder") };

        Assert.Null(WindowSelector.SelectFrontmost(windows, "Notion"));
    }
}
