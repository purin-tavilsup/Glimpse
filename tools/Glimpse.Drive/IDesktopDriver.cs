namespace Glimpse.Drive;

/// <summary>
/// What one operating system's accessibility API must provide. Each member backs one CLI command, so the
/// command set, the key grammar (<see cref="ElementKey"/>) and the window-set assertions (<see cref="WindowSet"/>)
/// stay the same on every OS. A <c>proc</c> is a process name as the OS reports it, without an extension.
/// Lookups wait up to their timeout and throw <see cref="InvalidOperationException"/> when nothing matches.
/// </summary>
public interface IDesktopDriver
{
    /// <summary>Prints every top-level window, front to back, or only those of <paramref name="processNames"/>.</summary>
    void ListWindows(string[]? processNames);

    void Tree(string proc, int depth);

    /// <summary>Prints the text boxes, buttons and documents of the window whose title contains <paramref name="titlePart"/>.</summary>
    void Controls(string proc, string titlePart);

    void Wait(string proc, string key, TimeSpan timeout);

    /// <summary>
    /// One label per visible window of <paramref name="proc"/>: the first of <paramref name="anchors"/> that window holds,
    /// or <c>unexpected window &lt;bounds&gt;</c>.
    /// </summary>
    IReadOnlyList<string> WindowLabels(string proc, IReadOnlyList<string> anchors);

    /// <summary>A real mouse click on the element.</summary>
    void Click(string proc, string key);

    /// <summary>Presses the element through the accessibility API, without the mouse.</summary>
    void Invoke(string proc, string key);

    void Type(string proc, string key, string text);

    string Value(string proc, string key);

    string Name(string proc, string key);

    /// <summary>Brings the titled window to the front, then types; tokens are <c>{ENTER} {TAB} {ESC} {WAITFOR:key}</c> or text.</summary>
    void Keys(string proc, string titlePart, IReadOnlyList<string> tokens);

    void ClickAt(int x, int y);

    void Front(string proc, string titlePart);

    /// <summary>Saves the whole desktop, or one window brought to the front, and prints its screen origin.</summary>
    void Shot(string file, string? proc, string? titlePart);

    /// <summary>Prints the window holding keyboard focus; with <paramref name="proc"/>, returns 0 only if it is that process's visible window.</summary>
    int Foreground(string? proc, string? anchor);

    /// <summary>Clicks the status-area icon whose tooltip contains <paramref name="tooltipPart"/>.</summary>
    void Tray(string tooltipPart, bool rightClick);

    /// <summary>Closes the browser's active tab while its address contains one of the comma-separated parts; returns how many closed.</summary>
    int CloseTabs(string urlParts);
}
