using Glimpse.Abstractions;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class RenderCommandBuilderTests
{
    private static RenderRequest Request(SnapshotTheme theme = SnapshotTheme.Light, int? windowId = null) =>
        new("in.mmd", "/out/diagram.png", 1280, 800, theme, windowId);

    [Fact]
    public void Build_ForMermaid_ShouldSubstituteSourceOutWidthAndTheme()
    {
        var spec = new RendererSpec("mermaid", "mmdc",
            ["-i", "{source}", "-o", "{out}", "-t", "{theme}", "-w", "{width}"], [".mmd"]);

        var command = RenderCommandBuilder.Build(spec, Request(SnapshotTheme.Dark), "/usr/bin/mmdc");

        Assert.Equal("/usr/bin/mmdc", command.Executable);
        Assert.Equal(new[] { "-i", "in.mmd", "-o", "/out/diagram.png", "-t", "dark", "-w", "1280" }, command.Args);
    }

    [Fact]
    public void Build_ForLightTheme_ShouldMapToDefault()
    {
        var spec = new RendererSpec("mermaid", "mmdc", ["-t", "{theme}"], [".mmd"]);

        var command = RenderCommandBuilder.Build(spec, Request(SnapshotTheme.Light), "mmdc");

        Assert.Equal(new[] { "-t", "default" }, command.Args);
    }

    [Fact]
    public void Build_ForWindowIdArg_WithWindowId_ShouldSubstitute()
    {
        var spec = new RendererSpec("app", "screencapture", ["-l{windowid}", "{out}"], []);

        var command = RenderCommandBuilder.Build(spec, Request(windowId: 42), "screencapture");

        Assert.Equal(new[] { "-l42", "/out/diagram.png" }, command.Args);
    }

    [Fact]
    public void Build_ForWindowIdArg_WithoutWindowId_ShouldThrow()
    {
        var spec = new RendererSpec("app", "screencapture", ["-l{windowid}", "{out}"], []);

        Assert.Throws<ArgumentException>(() => RenderCommandBuilder.Build(spec, Request(), "screencapture"));
    }
}
