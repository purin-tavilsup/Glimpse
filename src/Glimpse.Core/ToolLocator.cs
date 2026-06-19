using System.Diagnostics;

namespace Glimpse.Core;

/// <summary>Finds render tools on PATH (with a Chrome special-case) and gives install hints.</summary>
public static class ToolLocator
{
    private const string MacChrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";

    private static readonly IReadOnlyDictionary<string, string> Hints = new Dictionary<string, string>
    {
        ["mmdc"] = "mermaid-cli not found — install: npm i -g @mermaid-js/mermaid-cli",
        ["dot"] = "graphviz not found — install: brew install graphviz",
        ["d2"] = "d2 not found — install: brew install d2",
        ["chrome"] = "Chrome not found — install Google Chrome, or set a {chrome} override.",
        ["screencapture"] = "screencapture is macOS-only and ships with the OS.",
    };

    public static string? Resolve(string tool)
    {
        if (tool == "chrome")
            return WhichOrNull("google-chrome") ?? WhichOrNull("chromium") ?? (File.Exists(MacChrome) ? MacChrome : null);

        return WhichOrNull(tool);
    }

    public static string InstallHint(string tool)
        => Hints.TryGetValue(tool, out var hint) ? hint : $"'{tool}' not found on PATH.";

    private static string? WhichOrNull(string tool)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/which", tool) { RedirectStandardOutput = true };
        using var process = Process.Start(startInfo);
        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 && output.Length > 0 ? output : null;
    }
}
