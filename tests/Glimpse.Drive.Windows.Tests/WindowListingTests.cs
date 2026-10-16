using System.Diagnostics;
using FlaUI.UIA3;
using Glimpse.Drive.Windows;
using Xunit;

namespace Glimpse.Drive.Windows.Tests;

public class WindowListingTests
{
    // Windows open and close while the two listings are taken (tooltips, notifications), so a mismatch is
    // retried before it counts.
    private const int Attempts = 3;

    [Fact]
    [Trait("Category", TestCategories.RealDesktop)]
    public void WindowLabels_ForEveryAppOnScreen_ShouldCountTheWindowsUiAutomationsDesktopListingCounts()
    {
        var mismatches = new List<string>();
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            mismatches = Mismatches();
            if (mismatches.Count == 0)
            {
                break;
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static List<string> Mismatches()
    {
        var driver = new WindowsDesktopDriver();
        var apps = WindowList.AppWindows().Select(w => w.OwnerName).Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. apps.Select(proc => (proc, actual: Sorted(driver.WindowLabels(proc, [])), expected: Sorted(DesktopListingLabels(proc))))
                       .Where(x => !x.actual.SequenceEqual(x.expected))
                       .Select(x => $"{x.proc}: driver [{string.Join("; ", x.actual)}], UI Automation [{string.Join("; ", x.expected)}]")];
    }

    // UI Automation's own desktop listing, read through FlaUI so the reference shares no code with the driver.
    private static IEnumerable<string> DesktopListingLabels(string proc)
    {
        var pids = Process.GetProcessesByName(proc).Select(p => p.Id).ToHashSet();
        using var automation = new UIA3Automation();
        return [.. automation.GetDesktop().FindAllChildren()
                             .Where(w => pids.Contains(w.Properties.ProcessId.ValueOrDefault) && !w.Properties.IsOffscreen.ValueOrDefault)
                             .Select(w => $"unexpected window {w.Properties.BoundingRectangle.ValueOrDefault}")];
    }

    private static List<string> Sorted(IEnumerable<string> labels) => [.. labels.Order(StringComparer.Ordinal)];
}
