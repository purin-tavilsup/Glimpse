using Glimpse.Abstractions;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class WindowsAppCapturerTests
{
    private static string TempPng() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

    /// <summary>Captures on Windows, or returns null when this OS cannot. The real if-guard
    /// is what satisfies CA1416; Skip.IfNot is an ordinary call the analyzer cannot read.</summary>
    private static async Task<RenderOutcome?> CaptureOrNull(string path, long? windowId)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        return await new WindowsAppCapturer().CaptureAsync(
            new RenderRequest("", path, 1280, 800, SnapshotTheme.Light, windowId));
    }

    [SkippableFact]
    public async Task CaptureAsync_OnWindowsForFullScreen_ShouldProduceANonBlankPng()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only capture.");
        var path = TempPng();

        var outcome = await CaptureOrNull(path, windowId: null);

        Assert.NotNull(outcome);
        Assert.Equal("ok", outcome.Status);
        Assert.True(outcome.Width > 0 && outcome.Height > 0);
        // Closes trap 1 (all-black PrintWindow) and the alpha-transparency risk in one stroke:
        // a black or fully transparent frame trips PngAnalysis's single-color-frame warning.
        // BlitFromScreen is the only path exercised here (there is no window id), so this is
        // the one automated guard on the real screen-capture pixels, not just the encoder.
        Assert.Empty(outcome.Warnings);
        File.Delete(path);
    }

    [SkippableFact]
    public async Task CaptureAsync_OnWindowsWithAnInvalidWindowId_ShouldFailNotThrow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only capture.");
        var path = TempPng();

        var outcome = await CaptureOrNull(path, windowId: 999_999_999L);

        Assert.NotNull(outcome);
        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
    }

    [SkippableFact]
    public async Task CaptureAsync_OnWindowsWithAnUnwritableOutputPath_ShouldFailNotThrow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only capture.");
        // An existing directory, not a file: File.WriteAllBytes throws UnauthorizedAccessException
        // trying to open it for writing. Deterministic without needing ACL changes on this
        // machine. This exercises the broad catch in CaptureAsync -- the specific
        // GlimpseCaptureException catch does not cover this failure, only the general one does.
        var unwritablePath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        var outcome = await CaptureOrNull(unwritablePath, windowId: null);

        Assert.NotNull(outcome);
        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
    }
}
