using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Glimpse.Core;

/// <summary>Enumerates the on-screen windows. Implementations are OS-specific.</summary>
public interface IWindowFinder
{
    IReadOnlyList<WindowInfo> ListOnScreen();
}

/// <summary>
/// macOS window enumeration via CoreGraphics <c>CGWindowListCopyWindowInfo</c>. The CFType
/// marshalling lives here; the selection logic stays in the pure <see cref="WindowSelector"/>.
/// Reading window <i>titles</i> needs Screen Recording permission; owner names do not, so
/// app-name matching works without it (the screenshot itself still needs the permission).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacWindowFinder : IWindowFinder
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint OnScreenOnly = 1;            // kCGWindowListOptionOnScreenOnly
    private const uint ExcludeDesktop = 16;         // kCGWindowListExcludeDesktopElements
    private const uint Utf8 = 0x08000100;           // kCFStringEncodingUTF8
    private const nint NumberSInt64 = 4;            // kCFNumberSInt64Type

    public IReadOnlyList<WindowInfo> ListOnScreen()
    {
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktop, 0);
        if (list == IntPtr.Zero)
            return [];

        try
        {
            var count = CFArrayGetCount(list);
            var result = new List<WindowInfo>((int)count);
            for (nint i = 0; i < count; i++)
            {
                var dict = CFArrayGetValueAtIndex(list, i);
                var info = ReadWindow(dict);
                if (info is not null)
                    result.Add(info);
            }

            return result;
        }
        finally
        {
            CFRelease(list);
        }
    }

    private static WindowInfo? ReadWindow(IntPtr dict)
    {
        var id = ReadNumber(dict, "kCGWindowNumber");
        var owner = ReadString(dict, "kCGWindowOwnerName");
        if (id is null || owner is null)
            return null; // a window with no number/owner is not something we can target

        var title = ReadString(dict, "kCGWindowName");
        var layer = ReadNumber(dict, "kCGWindowLayer") ?? 0;
        // We query OnScreenOnly, but the kCGWindowIsOnscreen key is often omitted in that
        // mode — so treat "absent" as on-screen rather than excluding the window.
        var onScreen = ReadBoolOrDefault(dict, "kCGWindowIsOnscreen", defaultValue: true);
        var (x, y, w, h) = ReadBounds(dict);

        return new WindowInfo((uint)id.Value, owner, title, x, y, w, h, (int)layer, onScreen);
    }

    private static long? ReadNumber(IntPtr dict, string key)
    {
        var value = DictValue(dict, key);
        if (value == IntPtr.Zero || !CFNumberGetValue(value, NumberSInt64, out var n))
            return null;
        return n;
    }

    private static bool ReadBoolOrDefault(IntPtr dict, string key, bool defaultValue)
    {
        var value = DictValue(dict, key);
        return value == IntPtr.Zero ? defaultValue : CFBooleanGetValue(value);
    }

    private static (int X, int Y, int W, int H) ReadBounds(IntPtr dict)
    {
        var boundsDict = DictValue(dict, "kCGWindowBounds");
        if (boundsDict != IntPtr.Zero && CGRectMakeWithDictionaryRepresentation(boundsDict, out var r))
            return ((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        return (0, 0, 0, 0);
    }

    private static string? ReadString(IntPtr dict, string key)
    {
        var value = DictValue(dict, key);
        if (value == IntPtr.Zero)
            return null;

        var length = CFStringGetLength(value);
        if (length == 0)
            return string.Empty;

        var bufferSize = length * 4 + 1; // UTF-8 worst case
        var buffer = new byte[bufferSize];
        if (!CFStringGetCString(value, buffer, bufferSize, Utf8))
            return null;

        var len = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
    }

    /// <summary>Looks up a value by a string key, creating (and releasing) the CFString key.</summary>
    private static IntPtr DictValue(IntPtr dict, string key)
    {
        var cfKey = CFStringCreateWithCString(IntPtr.Zero, key, Utf8);
        try
        {
            return CFDictionaryGetValue(dict, cfKey);
        }
        finally
        {
            CFRelease(cfKey);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X, Y, Width, Height;
    }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);

    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);

    [DllImport(CoreFoundation, CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(IntPtr str);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, nint bufferSize, uint encoding);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, nint type, out long value);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFBooleanGetValue(IntPtr boolean);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
