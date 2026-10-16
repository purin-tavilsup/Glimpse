using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Glimpse.Drive.MacOS;

/// <summary>A CoreFoundation reference this code owns (an AXUIElement, a CFString, an event), released when collected.</summary>
[SupportedOSPlatform("macos")]
internal sealed class CfRef : SafeHandleZeroOrMinusOneIsInvalid
{
    public CfRef() : base(ownsHandle: true)
    {
    }

    /// <summary>Takes a share of a reference this code did not create or copy, such as a CFArray item.</summary>
    public static CfRef Retain(IntPtr unowned)
    {
        var reference = new CfRef();
        reference.SetHandle(Native.CFRetain(unowned));
        return reference;
    }

    protected override bool ReleaseHandle()
    {
        Native.CFRelease(handle);
        return true;
    }
}

/// <summary>The Accessibility, CoreFoundation and CoreGraphics calls the macOS driver makes.</summary>
[SupportedOSPlatform("macos")]
internal static class Native
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public const int Success = 0;      // kAXErrorSuccess
    public const int CannotComplete = -25204; // kAXErrorCannotComplete: the app did not answer in time
    public const int ActionUnsupported = -25206; // kAXErrorActionUnsupported
    public const int PointValue = 1;   // kAXValueCGPointType
    public const int SizeValue = 2;    // kAXValueCGSizeType
    public const nint DoubleNumber = 13; // kCFNumberDoubleType
    public const uint HidEventTap = 0; // kCGHIDEventTap
    public const uint LeftMouseDown = 1, LeftMouseUp = 2; // kCGEventLeftMouseDown / Up

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public nint Location, Length;
    }

    private static readonly ConcurrentDictionary<string, CfRef> Names = new();

    public static readonly IntPtr True = Export("kCFBooleanTrue");
    public static readonly IntPtr False = Export("kCFBooleanFalse");

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServices)] public static extern CfRef AXUIElementCreateApplication(int pid);
    [DllImport(ApplicationServices)] public static extern nuint AXUIElementGetTypeID();
    [DllImport(ApplicationServices)] public static extern int AXUIElementCopyAttributeValue(CfRef element, CfRef attribute, out CfRef value);
    [DllImport(ApplicationServices)] public static extern int AXUIElementSetAttributeValue(CfRef element, CfRef attribute, IntPtr value);
    [DllImport(ApplicationServices)] public static extern int AXUIElementSetAttributeValue(CfRef element, CfRef attribute, CfRef value);
    [DllImport(ApplicationServices)] public static extern int AXUIElementPerformAction(CfRef element, CfRef action);
    [DllImport(ApplicationServices)] public static extern int AXUIElementGetPid(CfRef element, out int pid);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXValueGetValue(CfRef value, int type, out CGPoint point);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXValueGetValue(CfRef value, int type, out CGSize size);

    [DllImport(CoreFoundation)] public static extern IntPtr CFRetain(IntPtr cf);
    [DllImport(CoreFoundation)] public static extern void CFRelease(IntPtr cf);
    [DllImport(CoreFoundation)] public static extern nuint CFGetTypeID(CfRef cf);
    [DllImport(CoreFoundation)] public static extern nuint CFStringGetTypeID();
    [DllImport(CoreFoundation)] public static extern nuint CFNumberGetTypeID();
    [DllImport(CoreFoundation)] public static extern nuint CFBooleanGetTypeID();
    [DllImport(CoreFoundation)] public static extern nuint CFArrayGetTypeID();
    [DllImport(CoreFoundation)] public static extern nint CFArrayGetCount(CfRef array);
    [DllImport(CoreFoundation)] public static extern IntPtr CFArrayGetValueAtIndex(CfRef array, nint index);
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)] public static extern CfRef CFStringCreateWithCharacters(IntPtr allocator, string chars, nint length);
    [DllImport(CoreFoundation)] public static extern nint CFStringGetLength(CfRef text);
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode)] public static extern void CFStringGetCharacters(CfRef text, CFRange range, [Out] char[] buffer);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CFNumberGetValue(CfRef number, nint type, out double value);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CFBooleanGetValue(CfRef boolean);

    [DllImport(CoreGraphics)] public static extern CfRef CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CoreGraphics, CharSet = CharSet.Unicode)] public static extern void CGEventKeyboardSetUnicodeString(CfRef evt, nuint length, string text);
    [DllImport(CoreGraphics)] public static extern void CGEventSetFlags(CfRef evt, ulong flags);
    [DllImport(CoreGraphics)] public static extern void CGEventPostToPid(int pid, CfRef evt);
    [DllImport(CoreGraphics)] public static extern CfRef CGEventCreateMouseEvent(IntPtr source, uint type, CGPoint position, uint button);
    [DllImport(CoreGraphics)] public static extern void CGEventPost(uint tap, CfRef evt);
    [DllImport(CoreGraphics)] public static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] public static extern CGRect CGDisplayBounds(uint display);

    /// <summary>The CFString for an attribute or action name, kept for the life of the process: the set is small and fixed.</summary>
    public static CfRef Name(string name) => Names.GetOrAdd(name, NewString);

    public static CfRef NewString(string text) => CFStringCreateWithCharacters(IntPtr.Zero, text, text.Length);

    public static string ReadString(CfRef text)
    {
        var length = CFStringGetLength(text);
        var buffer = new char[length];
        CFStringGetCharacters(text, new CFRange { Location = 0, Length = length }, buffer);
        return new string(buffer);
    }

    // kCFBooleanTrue and kCFBooleanFalse are exported variables, not functions, so DllImport cannot reach them.
    private static IntPtr Export(string name) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), name));
}
