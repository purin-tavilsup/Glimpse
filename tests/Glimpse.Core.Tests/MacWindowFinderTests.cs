using System.Diagnostics;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class MacWindowFinderTests
{
    // A real if-guard rather than Skip.IfNot alone: CA1416 does not treat Skip.IfNot as a platform guard.
    private static IReadOnlyList<WindowInfo>? EnumerateOrNull()
        => OperatingSystem.IsMacOS() ? new MacWindowFinder().ListOnScreen() : null;

    [SkippableFact]
    [Trait("Category", TestCategories.RealDesktop)]
    public void ListOnScreen_OnMacOS_ShouldReportTheProcessOwningEachWindow()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS-only window enumeration.");
        using var dock = Process.GetProcessesByName("Dock").Single();

        var windows = EnumerateOrNull() ?? [];

        Assert.Contains(windows, w => w.OwnerPid == dock.Id);
    }
}
