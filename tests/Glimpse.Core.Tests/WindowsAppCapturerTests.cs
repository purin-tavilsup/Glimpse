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
}
