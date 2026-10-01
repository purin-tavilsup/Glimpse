using Glimpse.Abstractions;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class AppCapturerTests
{
    [Fact]
    public void FullScreenSpec_ShouldMatchTheArgsShippedBeforeTheRefactor()
    {
        // Program.cs built ["-x", "{out}"] inline. Pinning it here is what makes this
        // task a refactor rather than a behaviour change.
        var spec = MacAppCapturer.FullScreenSpec;

        Assert.Equal("screencapture", spec.Tool);
        Assert.Equal(["-x", "{out}"], spec.Args);
    }

    [Fact]
    public void WindowSpec_ShouldMatchTheBuiltInAppRenderer()
    {
        var builtIn = RendererRegistry.Default().Resolve("app", null);

        Assert.Equal("screencapture", builtIn.Tool);
        Assert.Equal(["-x", "-o", "-l{windowid}", "{out}"], builtIn.Args);
    }

    [Fact]
    public void RenderOutcomes_From_WhenCommandFailed_ShouldReportFailedExitTwo()
    {
        var outcome = RenderOutcomes.From(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
            succeeded: false,
            standardError: "  boom  ");

        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
        Assert.Contains("render-failed:boom", outcome.Warnings);
    }

    [Fact]
    public void RenderOutcomes_From_WhenFileIsMissingDespiteSuccess_ShouldReportFailed()
    {
        var outcome = RenderOutcomes.From(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
            succeeded: true,
            standardError: "");

        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
    }
}
