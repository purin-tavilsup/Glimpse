using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;

namespace Glimpse.Drive.MacOS;

/// <summary>
/// One accessibility element of another app. Every read is a call the app answers on its main thread, so a
/// blocked app makes each one wait out the Accessibility messaging timeout and then report nothing.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class AxElement(CfRef handle)
{
    // Deep enough for a browser page, and a guard against apps whose tree points back into itself.
    private const int MaxDepth = 40;

    private static readonly nuint ElementType = Native.AXUIElementGetTypeID();
    private static readonly nuint StringType = Native.CFStringGetTypeID();
    private static readonly nuint NumberType = Native.CFNumberGetTypeID();
    private static readonly nuint BooleanType = Native.CFBooleanGetTypeID();
    private static readonly nuint ArrayType = Native.CFArrayGetTypeID();

    public static AxElement Application(int pid) => new(Native.AXUIElementCreateApplication(pid));

    public string? Role => Text("AXRole");

    public string? Subrole => Text("AXSubrole");

    public string? Identifier => Text("AXIdentifier");

    public string? Title => Text("AXTitle");

    public string? Description => Text("AXDescription");

    public string? Value => Text("AXValue");

    public string? Help => Text("AXHelp");

    public bool? Enabled => Bool("AXEnabled");

    public bool Minimized => Bool("AXMinimized") == true;

    public bool Hidden => Bool("AXHidden") == true;

    public bool Frontmost => Bool("AXFrontmost") == true;

    public IReadOnlyList<AxElement> Children => Elements("AXChildren");

    public IReadOnlyList<AxElement> Windows => Elements("AXWindows");

    public int Pid => Native.AXUIElementGetPid(handle, out var pid) == Native.Success ? pid : 0;

    /// <summary>The element's bounds in screen points, top-left origin: the space mouse events use.</summary>
    public Rectangle? Frame
    {
        get
        {
            using var position = Copy("AXPosition");
            using var size = Copy("AXSize");
            if (position is null || size is null
                || !Native.AXValueGetValue(position, Native.PointValue, out Native.CGPoint origin)
                || !Native.AXValueGetValue(size, Native.SizeValue, out Native.CGSize extent))
            {
                return null;
            }
            return new Rectangle((int)origin.X, (int)origin.Y, (int)extent.Width, (int)extent.Height);
        }
    }

    public IEnumerable<AxElement> Descendants() => Descendants(this, 0);

    public AxElement? Element(string attribute)
    {
        var value = Copy(attribute);
        if (value is null || Native.CFGetTypeID(value) != ElementType)
        {
            value?.Dispose();
            return null;
        }
        return new AxElement(value);
    }

    public int Perform(string action) => Native.AXUIElementPerformAction(handle, Native.Name(action));

    public int Set(string attribute, bool value) =>
        Native.AXUIElementSetAttributeValue(handle, Native.Name(attribute), value ? Native.True : Native.False);

    public int Set(string attribute, string value)
    {
        using var text = Native.NewString(value);
        return Native.AXUIElementSetAttributeValue(handle, Native.Name(attribute), text);
    }

    private static IEnumerable<AxElement> Descendants(AxElement root, int depth)
    {
        if (depth >= MaxDepth)
        {
            yield break;
        }
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var below in Descendants(child, depth + 1))
            {
                yield return below;
            }
        }
    }

    // Empty reads as absent, as it does for a UI Automation name: an empty title never identifies anything.
    private string? Text(string attribute)
    {
        using var value = Copy(attribute);
        var text = value is null ? null : AsText(value);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    // A checkbox or slider reports a number as its value; it still prints as text.
    private static string? AsText(CfRef value)
    {
        var type = Native.CFGetTypeID(value);
        if (type == StringType)
        {
            return Native.ReadString(value);
        }
        return type == NumberType && Native.CFNumberGetValue(value, Native.DoubleNumber, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private bool? Bool(string attribute)
    {
        using var value = Copy(attribute);
        return value is not null && Native.CFGetTypeID(value) == BooleanType ? Native.CFBooleanGetValue(value) : null;
    }

    private IReadOnlyList<AxElement> Elements(string attribute)
    {
        using var array = Copy(attribute);
        if (array is null || Native.CFGetTypeID(array) != ArrayType)
        {
            return [];
        }
        var count = Native.CFArrayGetCount(array);
        var items = new List<AxElement>((int)count);
        for (nint i = 0; i < count; i++)
        {
            items.Add(new AxElement(CfRef.Retain(Native.CFArrayGetValueAtIndex(array, i))));
        }
        return items;
    }

    private CfRef? Copy(string attribute)
    {
        if (Native.AXUIElementCopyAttributeValue(handle, Native.Name(attribute), out var value) == Native.Success && !value.IsInvalid)
        {
            return value;
        }
        value.Dispose();
        return null;
    }
}
