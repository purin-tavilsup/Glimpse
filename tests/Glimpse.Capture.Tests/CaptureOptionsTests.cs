using Glimpse.Abstractions;
using Glimpse.Capture;
using Xunit;

namespace Glimpse.Capture.Tests;

public class CaptureOptionsTests
{
    [Fact]
    public void Parse_WithSourceOnly_ShouldDefaultNameFromFileName()
    {
        var options = CaptureOptions.Parse(["diagrams/architecture.mmd"]);

        Assert.Equal("diagrams/architecture.mmd", options.Source);
        Assert.Equal("architecture", options.Name);
        Assert.Equal(1280, options.Width);
        Assert.Equal(800, options.Height);
        Assert.Equal(SnapshotTheme.Light, options.Theme);
    }

    [Fact]
    public void Parse_WithSizeFlag_ShouldSplitWidthAndHeight()
    {
        var options = CaptureOptions.Parse(["x.mmd", "--size", "640x480"]);

        Assert.Equal(640, options.Width);
        Assert.Equal(480, options.Height);
    }

    [Fact]
    public void Parse_WithThemeAndRendererAndName_ShouldBind()
    {
        var options = CaptureOptions.Parse(
            ["x.mmd", "--renderer", "graphviz", "--name", "flow", "--theme", "dark"]);

        Assert.Equal("graphviz", options.Renderer);
        Assert.Equal("flow", options.Name);
        Assert.Equal(SnapshotTheme.Dark, options.Theme);
    }

    [Fact]
    public void Parse_WithAppRendererAndWindowId_ShouldDefaultNameToApp()
    {
        var options = CaptureOptions.Parse(["--renderer", "app", "--window-id", "42"]);

        Assert.Null(options.Source);
        Assert.Equal("app", options.Name);
        Assert.Equal(42, options.WindowId);
    }

    [Fact]
    public void Parse_WithUnknownFlag_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CaptureOptions.Parse(["x.mmd", "--bogus"]));
    }

    [Fact]
    public void Parse_WithNonIntegerWindowId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CaptureOptions.Parse(["--renderer", "app", "--window-id", "foo"]));
    }

    [Fact]
    public void Parse_WithNonPositiveSize_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => CaptureOptions.Parse(["x.mmd", "--size", "0x0"]));
    }
}
