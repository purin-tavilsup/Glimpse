using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Glimpse.Drive.Windows;

// Win32 calls answer even while an app's UI thread is blocked, which UI Automation calls do not.
[SupportedOSPlatform("windows")]
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    public const int XVirtualScreen = 76, YVirtualScreen = 77;

    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
