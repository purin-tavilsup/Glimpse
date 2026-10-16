using System.Diagnostics;
using System.Runtime.Versioning;
using Glimpse.Core;

namespace Glimpse.Drive.MacOS;

/// <summary>
/// The window server's own list of windows. It answers while an app's main thread is blocked, which an
/// accessibility call does not, so the watchdog reads from it.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class WindowServer
{
    // App windows, floating ones included, sit below the Dock's window level; the Dock, the menu bar and the
    // status items sit at or above it.
    private const int DockLayer = 20;

    /// <summary>Every on-screen app window, front to back.</summary>
    public static IReadOnlyList<WindowInfo> AppWindows() => [.. Windows().Where(w => w.Layer < DockLayer)];

    public static HashSet<int> ProcessIds(string proc) => [.. Read(Process.GetProcessesByName(proc), p => p.Id)];

    public static IReadOnlyList<(int Pid, string Name)> Processes() => Read(Process.GetProcesses(), p => (p.Id, NameOf(p)));

    public static string ProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return ""; // the process exited between enumeration and lookup
        }
    }

    /// <summary>Prints the visible windows of a stuck step's process. Titles show only with the Screen Recording permission.</summary>
    public static void ReportVisibleWindows(string[] step)
    {
        if (step.Length < 2)
        {
            return;
        }

        var pids = ProcessIds(step[1]);
        Console.Error.WriteLine("Its visible windows:");
        foreach (var w in AppWindows().Where(w => pids.Contains(w.OwnerPid)))
        {
            Console.Error.WriteLine($"  '{w.Title}' rect=({w.X},{w.Y})-({w.X + w.Width},{w.Y + w.Height})");
        }
    }

    private static IReadOnlyList<WindowInfo> Windows() => new MacWindowFinder().ListOnScreen();

    private static List<T> Read<T>(Process[] processes, Func<Process, T> read)
    {
        try
        {
            return [.. processes.Select(read)];
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string NameOf(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return ""; // the process exited after the list was taken
        }
    }
}
