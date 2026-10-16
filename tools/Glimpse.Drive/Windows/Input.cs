using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Glimpse.Drive.Windows;

/// <summary>Keystrokes and mouse clicks, as Win32 input events, which land in whichever window is in front.</summary>
[SupportedOSPlatform("windows")]
internal static class Input
{
    public const ushort Return = 0x0D, Tab = 0x09, Escape = 0x1B, Control = 0x11, W = 0x57;

    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint KeyUp = 0x0002, Unicode = 0x0004;
    private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Makes coordinates physical pixels, the unit UI Automation reports. Without it, on a scaled display a click
    /// aimed at an element's middle lands somewhere else.
    /// </summary>
    public static void UsePhysicalPixels() => SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2

    /// <summary>Presses one key, or a chord such as Control+W: modifiers go down first and come up last.</summary>
    public static void Keys(params ushort[] keys)
    {
        var events = keys.Select(k => Key(k, 0)).Concat(keys.Reverse().Select(k => Key(k, KeyUp)));
        Send([.. events]);
    }

    /// <summary>Types text as Unicode keystrokes, so it needs no key code for each character.</summary>
    public static void Text(string text)
    {
        foreach (var c in text)
        {
            Send([Character(c, 0), Character(c, KeyUp)]);
        }
    }

    public static void Click(int x, int y, bool right = false)
    {
        SetCursorPos(x, y);
        Thread.Sleep(Settle);
        Send([Mouse(right ? RightDown : LeftDown), Mouse(right ? RightUp : LeftUp)]);
        Thread.Sleep(Settle);
    }

    public static void BringToFront(IntPtr hwnd)
    {
        SetForegroundWindow(hwnd);
        Thread.Sleep(Cli.Poll);
    }

    private static void Send(Event[] events)
    {
        if (SendInput((uint)events.Length, events, Marshal.SizeOf<Event>()) != events.Length)
        {
            throw new InvalidOperationException($"Windows refused the input (error {Marshal.GetLastWin32Error()}); a higher-privilege window may be in front");
        }
        Thread.Sleep(Settle);
    }

    private static Event Key(ushort key, uint flags) => new() { Type = InputKeyboard, Data = new() { Keyboard = new() { Key = key, Flags = flags } } };

    private static Event Character(char c, uint flags) => new() { Type = InputKeyboard, Data = new() { Keyboard = new() { Scan = c, Flags = Unicode | flags } } };

    private static Event Mouse(uint flags) => new() { Type = InputMouse, Data = new() { Mouse = new() { Flags = flags } } };

    [StructLayout(LayoutKind.Sequential)]
    private struct Event
    {
        public uint Type;
        public EventData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct EventData
    {
        [FieldOffset(0)] public MouseData Mouse;
        [FieldOffset(0)] public KeyboardData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int X, Y;
        public uint MouseWheel, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Event[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
