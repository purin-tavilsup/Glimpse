using Glimpse.Core;

namespace Glimpse.Drive.Windows;

/// <summary>
/// Win32's own list of top-level windows. It never asks an app anything, so a stuck or suspended app can't stall it.
/// UI Automation's desktop listing does: it sometimes takes tens of seconds, which is why the driver reads windows
/// from here and builds UI Automation elements only for the windows of the app it drives.
/// </summary>
internal static class WindowList
{
    /// <summary>Every visible window that isn't cloaked by the window manager and has a size, front to back.</summary>
    /// <remarks>
    /// The same windows UI Automation's desktop listing holds, which decides what <c>expect</c> counts: it leaves out
    /// cloaked windows (suspended store apps, input bars) and empty ones (the pseudo-console every terminal session owns).
    /// </remarks>
    public static IReadOnlyList<WindowInfo> AppWindows() =>
        [.. new WindowsWindowFinder().ListOnScreen().Where(w => w.OnScreen && w.Width > 0 && w.Height > 0)];

    public static IEnumerable<WindowInfo> Of(string proc) =>
        AppWindows().Where(w => w.OwnerName.Equals(proc, StringComparison.OrdinalIgnoreCase));

    /// <summary>Prints the visible windows of a stuck step's process.</summary>
    public static void ReportVisibleWindows(string[] step)
    {
        if (step.Length < 2)
        {
            return;
        }

        Console.Error.WriteLine("Its visible windows:");
        foreach (var w in Of(step[1]))
        {
            Console.Error.WriteLine($"  '{w.Title}' rect=({w.X},{w.Y})-({w.X + w.Width},{w.Y + w.Height})");
        }
    }
}
