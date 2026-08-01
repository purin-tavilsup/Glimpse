using System.Runtime.Versioning;
using Glimpse.Abstractions;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class AppCapturerTests
{
    private sealed class RecordingRunner(int exitCode) : IProcessRunner
    {
        public List<string> LastArgs { get; } = [];

        public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args)
        {
            LastArgs.Clear();
            LastArgs.AddRange(args);
            return Task.FromResult(new ProcessResult(exitCode, exitCode == 0 ? "" : "boom"));
        }
    }

    [Fact]
    [SupportedOSPlatform("macos")] // CA1416 guard: FullScreenSpec is plain data (no native
                                    // calls), so it runs fine cross-platform; this just tells
                                    // the analyzer the call site matches MacAppCapturer's attribute.
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
