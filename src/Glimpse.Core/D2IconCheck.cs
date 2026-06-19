using System.Text.RegularExpressions;

namespace Glimpse.Core;

/// <summary>Extracts <c>icon:</c> URLs from D2 source. D2 icons fail silently, so the CLI
/// HTTP-checks these before a render is trusted.</summary>
public static partial class D2IconCheck
{
    [GeneratedRegex(@"icon:\s*(https?://\S+)")]
    private static partial Regex IconUrl();

    public static IReadOnlyList<string> ExtractIconUrls(string d2Source)
        => IconUrl().Matches(d2Source).Select(m => m.Groups[1].Value).ToList();
}
