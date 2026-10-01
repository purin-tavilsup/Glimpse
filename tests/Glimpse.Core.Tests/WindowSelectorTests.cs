using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class WindowSelectorTests
{
    private static WindowInfo Win(long id, string owner, string? title = null,
        int w = 800, int h = 600, int layer = 0, bool onScreen = true) =>
        new(id, owner, title, 0, 0, w, h, layer, onScreen);

    [Fact]
    public void SelectFrontmost_WithCaseInsensitiveOwnerMatch_ShouldReturnIt()
    {
        var windows = new[] { Win(1, "Google Chrome"), Win(2, "Finder") };

        var result = WindowSelector.SelectFrontmost(windows, "chrome");

        Assert.NotNull(result);
        Assert.Equal(1L, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithMultipleMatches_ShouldReturnFirstFrontmost()
    {
        // CGWindowList order is front-to-back, so the first survivor is frontmost.
        var windows = new[] { Win(10, "Code", "main.cs"), Win(11, "Code", "other.cs") };

        var result = WindowSelector.SelectFrontmost(windows, "Code");

        Assert.Equal(10L, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithTitleFilter_ShouldPickMatchingTitle()
    {
        var windows = new[] { Win(20, "Code", "main.cs"), Win(21, "Code", "settings.json") };

        var result = WindowSelector.SelectFrontmost(windows, "Code", "settings");

        Assert.Equal(21L, result!.WindowId);
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

        Assert.Equal(33L, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithNoMatch_ShouldReturnNull()
    {
        var windows = new[] { Win(40, "Finder") };

        Assert.Null(WindowSelector.SelectFrontmost(windows, "Notion"));
    }

    [Fact]
    public void SelectFrontmost_WithWindowsToolWindowAtLayerOne_ShouldSkipIt()
    {
        // WindowsWindowFinder maps WS_EX_TOOLWINDOW to layer 1 so this existing
        // macOS-shaped filter excludes it without WindowSelector needing to change.
        var windows = new[]
        {
            Win(50, "Recorder", "tooltip", layer: 1),
            Win(51, "Recorder", "Mimica Recorder", w: 1400, h: 900),
        };

        var result = WindowSelector.SelectFrontmost(windows, "Recorder");

        Assert.Equal(51L, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithCloakedWindowMarkedOffScreen_ShouldSkipIt()
    {
        var windows = new[]
        {
            Win(60, "Recorder", "ghost", onScreen: false),   // DWM-cloaked
            Win(61, "Recorder", "Mimica Recorder", w: 1400, h: 900),
        };

        var result = WindowSelector.SelectFrontmost(windows, "Recorder");

        Assert.Equal(61L, result!.WindowId);
    }

    [Theory]
    [InlineData(1, 800, 600, true)]     // tool window / overlay layer
    [InlineData(0, 800, 600, false)]    // minimized or cloaked
    [InlineData(0, 49, 600, true)]      // narrower than the helper-window floor
    [InlineData(0, 800, 49, true)]      // shorter than the helper-window floor
    public void IsSelectable_WithAWindowTheSelectorSkips_ShouldReturnFalse(
        int layer, int width, int height, bool onScreen)
    {
        var window = Win(70, "Recorder", w: width, h: height, layer: layer, onScreen: onScreen);

        Assert.False(WindowSelector.IsSelectable(window));
    }

    [Fact]
    public void IsSelectable_WithANormalWindowAtTheSizeFloor_ShouldReturnTrue()
    {
        var window = Win(71, "Recorder", w: 50, h: 50);

        Assert.True(WindowSelector.IsSelectable(window));
    }
}
