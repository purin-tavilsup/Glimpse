using Glimpse.Core;

namespace Glimpse.Capture;

/// <summary>One line of <c>--list-windows</c> output per window.</summary>
public static class WindowListing
{
    public static string FormatRow(WindowInfo w)
    {
        var row = $"[id {w.WindowId,-6}] layer {w.Layer,-3} {w.Width}x{w.Height}  {w.OwnerName} — {w.Title ?? "(untitled)"}";
        return WindowSelector.IsSelectable(w) ? row : $"{row}   (not selectable)";
    }
}
