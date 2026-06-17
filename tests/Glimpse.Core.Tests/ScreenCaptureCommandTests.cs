using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ScreenCaptureCommandTests
{
    [Fact]
    public void BuildArgs_FullScreen_ShouldBeSilentCapture()
    {
        var args = ScreenCaptureCommand.BuildArgs(CaptureMode.FullScreen, "/tmp/s.png");

        Assert.Equal(["-x", "/tmp/s.png"], args);
    }

    [Fact]
    public void BuildArgs_Window_ShouldTargetWindowIdWithoutShadow()
    {
        var args = ScreenCaptureCommand.BuildArgs(CaptureMode.Window, "/tmp/w.png", windowId: 42);

        Assert.Equal(["-x", "-o", "-l42", "/tmp/w.png"], args);
    }

    [Fact]
    public void BuildArgs_Interactive_ShouldUseInteractiveFlag()
    {
        var args = ScreenCaptureCommand.BuildArgs(CaptureMode.Interactive, "/tmp/i.png");

        Assert.Equal(["-i", "/tmp/i.png"], args);
    }

    [Fact]
    public void BuildArgs_WindowWithoutId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ScreenCaptureCommand.BuildArgs(CaptureMode.Window, "/tmp/w.png"));
    }
}
