using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Interop.UIAutomationClient;

namespace Glimpse.Drive.Windows;

/// <summary>
/// UI Automation through its COM interface. FlaUI would be the friendlier wrapper, but it ships only Windows-targeted
/// builds, and a .NET tool can't hold one; this interop library targets plain .NET, so one build carries every driver.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Uia
{
    private static readonly string[] ControlTypeNames =
    [
        "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image", "ListItem", "List", "Menu",
        "MenuBar", "MenuItem", "ProgressBar", "RadioButton", "ScrollBar", "Slider", "Spinner", "StatusBar", "Tab",
        "TabItem", "Text", "ToolBar", "ToolTip", "Tree", "TreeItem", "Custom", "Group", "Thumb", "DataGrid",
        "DataItem", "Document", "SplitButton", "Window", "Pane", "Header", "HeaderItem", "Table", "TitleBar",
        "Separator", "SemanticZoom", "AppBar",
    ];

    private readonly CUIAutomation8 automation = new();

    public IUIAutomationCondition All => automation.CreateTrueCondition();

    /// <returns>Null when the window closed between the listing and this lookup.</returns>
    public IUIAutomationElement? FromHandle(IntPtr hwnd)
    {
        try
        {
            return automation.ElementFromHandle(hwnd);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public IUIAutomationCondition IdOrName(string key) => automation.CreateOrCondition(
        automation.CreatePropertyCondition(UIA_PropertyIds.UIA_AutomationIdPropertyId, key),
        automation.CreatePropertyCondition(UIA_PropertyIds.UIA_NamePropertyId, key));

    public IUIAutomationCondition Id(string id) => automation.CreatePropertyCondition(UIA_PropertyIds.UIA_AutomationIdPropertyId, id);

    public IUIAutomationCondition AnyControlType(params int[] controlTypes) => controlTypes
        .Select(t => automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, t))
        .Aggregate(automation.CreateOrCondition);

    public static string ControlTypeName(int controlType)
    {
        var index = controlType - UIA_ControlTypeIds.UIA_ButtonControlTypeId;
        return index >= 0 && index < ControlTypeNames.Length ? ControlTypeNames[index] : "Unknown";
    }
}

/// <summary>
/// Reads that tolerate an element going away mid-read, which is normal for a live desktop: they fall back to an
/// empty value instead of throwing, so a closing window drops out of a listing rather than failing the command.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UiaElement
{
    public static string Name(this IUIAutomationElement e) => Read(() => e.CurrentName, "") ?? "";

    public static string Id(this IUIAutomationElement e) => Read(() => e.CurrentAutomationId, "") ?? "";

    public static string ControlType(this IUIAutomationElement e) => Uia.ControlTypeName(Read(() => e.CurrentControlType, 0));

    public static bool IsEnabled(this IUIAutomationElement e) => Read(() => e.CurrentIsEnabled, 0) != 0;

    public static bool IsOffscreen(this IUIAutomationElement e) => Read(() => e.CurrentIsOffscreen, 0) != 0;

    public static IntPtr Handle(this IUIAutomationElement e) => Read(() => e.CurrentNativeWindowHandle, IntPtr.Zero);

    public static Rectangle Bounds(this IUIAutomationElement e)
    {
        var r = Read(() => e.CurrentBoundingRectangle, default);
        return Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
    }

    public static IEnumerable<IUIAutomationElement> Find(this IUIAutomationElement e, TreeScope scope, IUIAutomationCondition condition)
    {
        var found = Read(() => e.FindAll(scope, condition), null);
        for (var i = 0; i < (found?.Length ?? 0); i++)
        {
            yield return found!.GetElement(i);
        }
    }

    public static IUIAutomationElement? FindFirst(this IUIAutomationElement e, TreeScope scope, IUIAutomationCondition condition) =>
        Read(() => e.FindFirst(scope, condition), null);

    /// <returns>The pattern, or null when the element doesn't support it.</returns>
    public static T? Pattern<T>(this IUIAutomationElement e, int patternId) where T : class =>
        Read(() => e.GetCurrentPattern(patternId), null) as T;

    /// <summary>Where a click lands: the element's own clickable point, else the middle of its bounds.</summary>
    public static Point ClickPoint(this IUIAutomationElement e)
    {
        if (Read(() => e.GetClickablePoint(out var p) != 0 ? new Point(p.x, p.y) : (Point?)null, null) is { } point)
        {
            return point;
        }
        var r = e.Bounds();
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (COMException)
        {
            return fallback; // the element went away, or its app refused the call
        }
    }
}
