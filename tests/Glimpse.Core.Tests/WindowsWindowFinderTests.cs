using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class WindowsWindowFinderTests
{
    /// <summary>
    /// Enumerates on Windows, or returns null when this OS cannot.
    /// The <c>if (OperatingSystem.IsWindows())</c> is not redundant with <c>Skip.IfNot</c>:
    /// verified 2026-08-01 that CA1416 does NOT accept <c>Skip.IfNot</c> as a guard (it is an
    /// ordinary method call the analyzer cannot reason about), and CA1416 is a build error
    /// here. The real if-guard is the only form that compiles.
    /// </summary>
    private static IReadOnlyList<WindowInfo>? EnumerateOrNull()
        => OperatingSystem.IsWindows() ? new WindowsWindowFinder().ListOnScreen() : null;

    [SkippableFact]
    [Trait("Category", TestCategories.RealDesktop)]
    public void ListOnScreen_OnWindows_ShouldFindAtLeastOneRealWindow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only window enumeration.");

        var windows = EnumerateOrNull();

        Assert.NotNull(windows);
        Assert.NotEmpty(windows);
    }

    [SkippableFact]
    public void ListOnScreen_OnWindows_ShouldPopulateOwnerAndBoundsForSelectableWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only window enumeration.");

        var selectable = (EnumerateOrNull() ?? [])
            .Where(w => w.OnScreen && w.Layer == 0 && w.Width >= 50 && w.Height >= 50)
            .ToList();

        Skip.If(selectable.Count == 0, "No normal on-screen window on this session.");
        Assert.All(selectable, w =>
        {
            Assert.False(string.IsNullOrWhiteSpace(w.OwnerName));
            Assert.NotEqual(0, w.WindowId);
            Assert.True(w.Width > 0 && w.Height > 0);
        });
    }

    [SkippableFact]
    public void ListOnScreen_OnWindows_ShouldReturnDistinctWindowIds()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only window enumeration.");

        var windows = EnumerateOrNull() ?? [];

        Assert.Equal(windows.Count, windows.Select(w => w.WindowId).Distinct().Count());
    }
}
