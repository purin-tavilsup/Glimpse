using Glimpse.Capture;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Capture.Tests;

public class WindowListingTests
{
    private const string NotSelectableMarker = "(not selectable)";

    [Fact]
    public void FormatRow_WithAToolWindow_ShouldMarkItNotSelectable()
    {
        var window = new WindowInfo(197280, "DDPM.Subagent.User", "EAWorkWindow", 0, 0, 10, 10, 1, true);

        var row = WindowListing.FormatRow(window);

        Assert.EndsWith(NotSelectableMarker, row);
    }

    [Fact]
    public void FormatRow_WithAnOffScreenWindow_ShouldMarkItNotSelectable()
    {
        var window = new WindowInfo(42, "Mimica Recorder", "Sign in", 0, 0, 700, 300, 0, false);

        var row = WindowListing.FormatRow(window);

        Assert.EndsWith(NotSelectableMarker, row);
    }

    [Fact]
    public void FormatRow_WithASelectableWindow_ShouldNotMarkIt()
    {
        var window = new WindowInfo(43, "Mimica Recorder", "Sign in", 0, 0, 700, 300, 0, true);

        var row = WindowListing.FormatRow(window);

        Assert.DoesNotContain(NotSelectableMarker, row);
    }

    [Fact]
    public void FormatRow_WithAnUntitledWindow_ShouldKeepTheExistingColumns()
    {
        var window = new WindowInfo(44, "Code", null, 0, 0, 1400, 900, 0, true);

        var row = WindowListing.FormatRow(window);

        Assert.Equal("[id 44    ] layer 0   1400x900  Code — (untitled)", row);
    }
}
