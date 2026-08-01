using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Glimpse.Core;

/// <summary>The inputs a PATH search depends on. Passed explicitly so both platforms' rules
/// are testable on either OS (mirrors <see cref="OutputLocator"/>'s injectable overload).</summary>
public sealed record ToolSearchEnvironment(
    string PathVariable,
    string? PathExtVariable,
    bool WindowsRules);

/// <summary>Finds render tools on PATH (with a Chrome special-case) and gives install hints.</summary>
public static class ToolLocator
{
    private const string MacChrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";

    /// <summary>The only extensions CreateProcess can launch directly. PATHEXT also lists
    /// .VBS/.JS/.MSC/.PY, which it cannot — resolving one yields a path that always fails
    /// to start. Verified 2026-08-01 against npm's extensionless 'mmdc' shim.</summary>
    private static readonly string[] LaunchableExtensions = [".com", ".exe", ".bat", ".cmd"];

    private static readonly IReadOnlyDictionary<string, (string Mac, string Windows)> Hints =
        new Dictionary<string, (string, string)>
        {
            ["mmdc"] = ("mermaid-cli not found — install: npm i -g @mermaid-js/mermaid-cli",
                        "mermaid-cli not found — install: npm i -g @mermaid-js/mermaid-cli"),
            ["dot"] = ("graphviz not found — install: brew install graphviz",
                       "graphviz not found — install: winget install Graphviz.Graphviz (then add its bin\\ to PATH)"),
            ["d2"] = ("d2 not found — install: brew install d2",
                      "d2 not found — install: winget install Terrastruct.d2"),
            ["chrome"] = ("Chrome not found — install Google Chrome, or set a {chrome} override.",
                          "Chrome not found — install Google Chrome, or set a {chrome} override."),
            ["screencapture"] = ("screencapture is macOS-only and ships with the OS.",
                                 "screencapture is macOS-only; on Windows the app renderer captures in-process."),
        };

    public static string? Resolve(string tool)
        => tool == "chrome" ? ResolveChrome() : Resolve(tool, CurrentEnvironment());

    public static string? Resolve(string tool, ToolSearchEnvironment environment)
    {
        foreach (var directory in environment.PathVariable.Split(
                     Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var found = ProbeDirectory(directory.Trim().Trim('"'), tool, environment);
            if (found is not null)
                return found;
        }

        return null;
    }

    public static string InstallHint(string tool)
    {
        if (!Hints.TryGetValue(tool, out var hint))
            return $"'{tool}' not found on PATH.";

        return OperatingSystem.IsWindows() ? hint.Windows : hint.Mac;
    }

    private static ToolSearchEnvironment CurrentEnvironment() => new(
        Environment.GetEnvironmentVariable("PATH") ?? "",
        Environment.GetEnvironmentVariable("PATHEXT"),
        OperatingSystem.IsWindows());

    private static string? ProbeDirectory(string directory, string tool, ToolSearchEnvironment environment)
    {
        if (directory.Length == 0)
            return null;

        if (!environment.WindowsRules)
            return FileOrNull(directory, tool);

        // An explicit launchable extension from the caller is honoured as given.
        if (LaunchableExtensions.Contains(Path.GetExtension(tool), StringComparer.OrdinalIgnoreCase))
            return FileOrNull(directory, tool);

        foreach (var extension in SearchExtensions(environment.PathExtVariable))
        {
            var found = FileOrNull(directory, tool + extension);
            if (found is not null)
                return found;
        }

        return null; // never accept an extensionless match on Windows
    }

    /// <summary>PATHEXT order, narrowed to what CreateProcess can actually launch.</summary>
    private static IReadOnlyList<string> SearchExtensions(string? pathExtVariable)
    {
        if (string.IsNullOrWhiteSpace(pathExtVariable))
            return LaunchableExtensions;

        var ordered = pathExtVariable
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(extension => extension.Trim().ToLowerInvariant())
            .Where(extension => LaunchableExtensions.Contains(extension))
            .ToList();

        return ordered.Count > 0 ? ordered : LaunchableExtensions;
    }

    private static string? FileOrNull(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveChrome()
    {
        if (OperatingSystem.IsWindows())
            return ChromeFromRegistry() ?? WindowsChromeFromWellKnownPaths();

        if (OperatingSystem.IsMacOS())
            return File.Exists(MacChrome) ? MacChrome : null;

        var environment = CurrentEnvironment();
        return Resolve("google-chrome", environment) ?? Resolve("chromium", environment);
    }

    /// <summary>The App Paths key Windows itself uses, so a non-default install location works.</summary>
    [SupportedOSPlatform("windows")]
    private static string? ChromeFromRegistry()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = root.OpenSubKey(keyPath);
            if (key?.GetValue(null) is string path && File.Exists(path))
                return path;
        }

        return null;
    }

    private static string? WindowsChromeFromWellKnownPaths()
    {
        const string suffix = @"Google\Chrome\Application\chrome.exe";
        string[] roots =
        [
            Environment.GetEnvironmentVariable("ProgramFiles") ?? "",
            Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "",
            Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "",
        ];

        return roots
            .Where(root => root.Length > 0)
            .Select(root => Path.Combine(root, suffix))
            .FirstOrDefault(File.Exists);
    }
}
