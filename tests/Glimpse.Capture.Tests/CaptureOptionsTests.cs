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
    public void Parse_WithoutNoManifestFlag_ShouldDefaultToFalse()
    {
        var options = CaptureOptions.Parse(["x.mmd"]);

        Assert.False(options.NoManifest);
    }

    [Fact]
    public void Parse_WithNoManifestFlag_ShouldBeTrue()
    {
        var options = CaptureOptions.Parse(["x.mmd", "--no-manifest"]);

        Assert.True(options.NoManifest);
    }

    [Fact]
    public void Parse_WithWindowAndTitle_ShouldBindAndSlugDefaultName()
    {
        var options = CaptureOptions.Parse(["--renderer", "app", "--window", "Google Chrome", "--title", "Coda"]);

        Assert.Equal("Google Chrome", options.Window);
        Assert.Equal("Coda", options.Title);
        Assert.Equal("google-chrome", options.Name);
    }

    [Fact]
    public void Parse_WithListWindows_ShouldSetFlag()
    {
        var options = CaptureOptions.Parse(["--list-windows"]);

        Assert.True(options.ListWindows);
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

    [Fact]
    public void Parse_WithCheckIcons_ShouldSetFlagAndKeepSource()
    {
        var options = CaptureOptions.Parse(["diagram.d2", "--check-icons"]);

        Assert.True(options.CheckIcons);
        Assert.Equal("diagram.d2", options.Source);
    }

    [Fact]
    public void Parse_WithWindowIdBeyondIntRange_ShouldKeepTheFullValue()
    {
        // A Windows HWND is pointer-sized. Truncating it to int silently targets the
        // wrong window (or none), which looks like a capture bug rather than a parse bug.
        const long hwnd = 4_294_967_296L; // 2^32, unrepresentable as int

        var options = CaptureOptions.Parse(["--renderer", "app", "--window-id", "4294967296"]);

        Assert.Equal(hwnd, options.WindowId);
    }

    [Fact]
    public void Parse_WithSmallWindowId_ShouldStillParse()
    {
        var options = CaptureOptions.Parse(["--renderer", "app", "--window-id", "42"]);

        Assert.Equal(42L, options.WindowId);
    }
}
