namespace Glimpse.Core;

/// <summary>
/// Pure selection of the frontmost real window of a named app from a window list.
/// Kept free of any native code so it's fully unit-testable; the OS enumeration lives
/// behind <see cref="IWindowFinder"/>.
/// </summary>
public static class WindowSelector
{
    // Excludes 1px helper/overlay windows that share an app's owner name.
    private const int MinWidth = 50;
    private const int MinHeight = 50;

    /// <summary>
    /// First (= frontmost; CGWindowList is front-to-back) on-screen, normal-layer window
    /// whose owner contains <paramref name="appMatch"/> (and, if given, whose title
    /// contains <paramref name="titleMatch"/>) — both case-insensitive. Null if none.
    /// </summary>
    public static WindowInfo? SelectFrontmost(
        IReadOnlyList<WindowInfo> windows, string appMatch, string? titleMatch = null)
    {
        foreach (var w in windows)
        {
            if (!IsSelectable(w))
                continue;
            if (!w.OwnerName.Contains(appMatch, StringComparison.OrdinalIgnoreCase))
                continue;
            if (titleMatch is not null &&
                (w.Title is null || !w.Title.Contains(titleMatch, StringComparison.OrdinalIgnoreCase)))
                continue;

            return w;
        }

        return null;
    }

    /// <summary>
    /// Whether <see cref="SelectFrontmost"/> would ever consider this window: on screen,
    /// normal layer, and at least the helper-window size floor. Public so a window listing
    /// can mark rejected rows from this same rule instead of a copy that drifts.
    /// </summary>
    public static bool IsSelectable(WindowInfo window) =>
        window.OnScreen
        && window.Layer == 0
        && window.Width >= MinWidth
        && window.Height >= MinHeight;
}
