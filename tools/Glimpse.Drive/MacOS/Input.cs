using System.Runtime.Versioning;

namespace Glimpse.Drive.MacOS;

/// <summary>Keystrokes and mouse clicks, as CoreGraphics events.</summary>
[SupportedOSPlatform("macos")]
internal static class Input
{
    // Virtual key codes of the ANSI keyboard layout (Carbon's kVK_* constants).
    public const ushort Return = 36, Tab = 48, Escape = 53, W = 13;
    public const ulong Command = 1UL << 20; // kCGEventFlagMaskCommand

    /// <summary>
    /// Sends a key to one process, not to whichever app is in front, so it lands in that app's key window even
    /// when another app took the focus in between.
    /// </summary>
    public static void Key(int pid, ushort key, ulong flags = 0)
    {
        foreach (var down in new[] { true, false })
        {
            using var keystroke = Native.CGEventCreateKeyboardEvent(IntPtr.Zero, key, down);
            Native.CGEventSetFlags(keystroke, flags);
            Native.CGEventPostToPid(pid, keystroke);
        }
        Thread.Sleep(50);
    }

    /// <summary>Types text as Unicode keystrokes, so it needs no key code for each character.</summary>
    public static void Text(int pid, string text)
    {
        foreach (var character in text)
        {
            foreach (var down in new[] { true, false })
            {
                using var keystroke = Native.CGEventCreateKeyboardEvent(IntPtr.Zero, 0, down);
                Native.CGEventKeyboardSetUnicodeString(keystroke, 1, character.ToString());
                Native.CGEventPostToPid(pid, keystroke);
            }
        }
        Thread.Sleep(100);
    }

    /// <summary>A real left click at a point in screen points; it moves the person's mouse.</summary>
    public static void Click(double x, double y)
    {
        var point = new Native.CGPoint { X = x, Y = y };
        foreach (var type in new[] { Native.LeftMouseDown, Native.LeftMouseUp })
        {
            using var click = Native.CGEventCreateMouseEvent(IntPtr.Zero, type, point, 0);
            Native.CGEventPost(Native.HidEventTap, click);
            Thread.Sleep(50);
        }
    }
}
