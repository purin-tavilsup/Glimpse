using System.Runtime.InteropServices;

namespace Glimpse.Core.Tests;

/// <summary>A visible top-level Win32 window with an empty title, destroyed on dispose.</summary>
internal sealed class UntitledWindow : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;

    private readonly IntPtr _hwnd;

    private UntitledWindow(IntPtr hwnd) => _hwnd = hwnd;

    public long Handle => _hwnd.ToInt64();

    // STATIC is a built-in class, so no RegisterClass or window procedure is needed, and
    // EnumWindows sees the window without a message pump.
    public static UntitledWindow Show(int width, int height)
    {
        var hwnd = CreateWindowExW(0, "STATIC", "", WsPopup | WsVisible,
            100, 100, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");

        return new UntitledWindow(hwnd);
    }

    public void Dispose() => DestroyWindow(_hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
