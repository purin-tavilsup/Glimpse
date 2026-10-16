using System.Runtime.InteropServices;
using System.Text;

namespace Glimpse.Drive.Windows;

// Win32 calls answer even while an app's UI thread is blocked, which UI Automation calls do not.
internal static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    public const int XVirtualScreen = 76, YVirtualScreen = 77;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

    /// <summary>Prints the visible top-level windows of a stuck step's process, read without touching its UI thread.</summary>
    public static void ReportVisibleWindows(string[] step)
    {
        if (step.Length < 2)
        {
            return;
        }

        var pids = System.Diagnostics.Process.GetProcessesByName(step[1]).Select(p => (uint)p.Id).ToHashSet();
        Console.Error.WriteLine("Its visible windows:");
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pids.Contains(pid) && IsWindowVisible(hwnd))
            {
                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, title.Capacity);
                GetWindowRect(hwnd, out var r);
                Console.Error.WriteLine($"  '{title}' rect=({r.Left},{r.Top})-({r.Right},{r.Bottom})");
            }
            return true;
        }, IntPtr.Zero);
    }
}
