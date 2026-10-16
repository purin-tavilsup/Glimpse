namespace Glimpse.Drive;

/// <summary>
/// A key naming one element: an AutomationId or exact Name, <c>~text</c> for a Name that contains text, and
/// <c>Parent&gt;Key</c> for Key inside Parent. <c>@Anchor/Key</c> first picks the window that holds Anchor,
/// because several windows often share ids (every title bar has a CloseButton).
/// </summary>
public readonly record struct ElementKey(string? Anchor, string Target)
{
    public static ElementKey Parse(string key)
    {
        var slash = key.IndexOf('/');
        return key.StartsWith('@') && slash > 0
            ? new ElementKey(key[1..slash], key[(slash + 1)..])
            : new ElementKey(null, key);
    }

    /// <summary>The scopes to search, outermost first: WinForms custom text boxes all wrap an Edit named textBox1.</summary>
    public static string[] Path(string key) => key.Split('>');
}
