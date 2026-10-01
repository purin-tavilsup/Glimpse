# Cross-Platform Support (macOS + Windows) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every Glimpse renderer — including the live-window `app` capture — work on both macOS and Windows from one single-target codebase, with CI proving both.

**Architecture:** Three moves. (1) `ToolLocator` stops shelling out to `/usr/bin/which` and scans `PATH` in managed code. (2) A new `IAppCapturer` seam lets macOS keep its exact `screencapture` path while Windows gets an in-process GDI capturer, with the pure `WindowSelector` untouched. (3) A `PlatformSupport` factory removes all OS branching from `Program.cs`.

**Tech Stack:** .NET 10 (`net10.0`, single target), xUnit 2.9.2, `Xunit.SkippableFact` 1.5.23, SkiaSharp 2.88.9, Win32 P/Invoke (user32 / gdi32 / dwmapi), `Microsoft.Win32.Registry` (in-box, no package reference).

**Spec:** `docs/superpowers/specs/2026-08-01-cross-platform-windows-design.md`

## Global Constraints

- **Single target framework.** `net10.0` only. Do NOT introduce `net10.0-windows`, multi-targeting, new projects, or conditional `ProjectReference`s.
- **No new NuGet packages.** SkiaSharp is already referenced by `Glimpse.Core.csproj:8`. `Microsoft.Win32.Registry` is in-box on `net10.0` — **verified 2026-08-01**, needs no `PackageReference`.
- **`TreatWarningsAsErrors=true`** (`Directory.Build.props:7`). Any warning fails the build. Critically, **CA1416 is an error**: a `[SupportedOSPlatform]` attribute alone is NOT enough — the *call site* must sit inside an `OperatingSystem.IsWindows()` / `IsMacOS()` guard. Verified 2026-08-01.
- **`Nullable=enable`**, **`ImplicitUsings=enable`**, file-scoped namespaces.
- **No behaviour change on macOS.** The `screencapture` argument list and `WindowSelector` semantics must stay identical.
- **Test naming:** `Method_Condition_ShouldExpectedBehavior`. Arrange/Act/Assert separated by blank lines. No `#region`.
- **Functions ≤20 lines, ≤3 parameters, do one thing.** Group related parameters into a `record` rather than adding a fourth.
- **Commit identity:** this repo has a local override (`Purin Tavilsup <19279956+purin-tavilsup@users.noreply.github.com>`). Do not change it; do not commit as the Mimica identity.
- **Verify before claiming.** Every task's final step runs the real command and reads the real output.

**Baseline, measured on Windows 11 on 2026-08-01 before any change:**
- `dotnet build Glimpse.slnx` → succeeds, 0 warnings, 0 errors
- `dotnet test` → **7 failures**: `ToolLocatorTests` ×2, `RenderEngineTests` ×4, `EndToEndGateTests` ×1. All trace to `/usr/bin/which`.
- `Glimpse.Avalonia.Tests` → 13 passed / 2 skipped (already cross-platform, needs no work)

**Available render tools on the dev box:** `mmdc` 11.16.0 (as `mmdc.cmd`), `d2` v0.7.1, `dot` 15.1.0, Chrome, Edge, Node, .NET 10.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `.gitattributes` | Pin LF for shell scripts, CRLF for `.cmd`/`.ps1` | 1 |
| `src/Glimpse.Core/ToolLocator.cs` | Resolve a tool name to an absolute executable path; platform install hints | 2 |
| `src/Glimpse.Core/WindowInfo.cs` | `WindowId` widens `uint` → `long` | 3 |
| `src/Glimpse.Core/RenderCommandBuilder.cs` | `RenderRequest.WindowId` widens `int?` → `long?` | 3 |
| `tools/Glimpse.Capture/CaptureOptions.cs` | `--window-id` parses `long` | 3 |
| `src/Glimpse.Core/RenderEngine.cs` | Add `RenderOutcomes.From` — the analyse tail, shared by both engines | 4 |
| `src/Glimpse.Core/AppCapturer.cs` | `IAppCapturer` + `MacAppCapturer` + `WindowsAppCapturer` | 4, 6 |
| `src/Glimpse.Core/WindowFinder.cs` | Add `WindowsWindowFinder` beside `MacWindowFinder` | 5 |
| `src/Glimpse.Core/PlatformSupport.cs` | OS → `(IWindowFinder?, IAppCapturer?)` | 7 |
| `tools/Glimpse.Capture/Program.cs` | Drop both `OperatingSystem.IsMacOS()` branches | 7 |
| `plugin/bin/glimpse.cmd` | Windows CLI wrapper | 8 |
| `scripts/install.ps1` | Windows install via directory junction | 8 |
| `.github/workflows/ci.yml` | build + test on `windows-latest` × `macos-latest` | 9 |

---

### Task 1: `.gitattributes` — protect every later commit

Must be first. `core.autocrlf=true` with no `.gitattributes`, and `git ls-files --eol` reports `i/lf w/crlf` for the three bash scripts. Once a CRLF shebang reaches the index, macOS fails with `bad interpreter: /usr/bin/env bash^M` and the `glimpse` command dies outright. This task adds `glimpse.cmd` and `install.ps1` beside those scripts, so the hazard is live.

**Files:**
- Create: `.gitattributes`

**Interfaces:**
- Consumes: nothing
- Produces: nothing (repository hygiene only)

- [ ] **Step 1: Record the current state so the fix is verifiable**

Run:
```bash
git ls-files --eol plugin/bin/glimpse scripts/install.sh scripts/check-diagram-templates.sh
```
Expected: each line shows `i/lf w/crlf attr/` — index LF, working tree CRLF, no attributes.

- [ ] **Step 2: Create `.gitattributes`**

```gitattributes
# Default: normalise to LF in the index for every text file.
* text=auto eol=lf

# Shell scripts MUST be LF. A CRLF shebang makes macOS fail with
# "bad interpreter: /usr/bin/env bash^M", which kills the glimpse command.
*.sh          text eol=lf
plugin/bin/glimpse text eol=lf

# Windows scripts MUST be CRLF. cmd.exe mis-parses LF-only batch files,
# especially labels and goto.
*.cmd         text eol=crlf
*.ps1         text eol=crlf

# Diagram sources are read by cross-platform tools; keep them LF.
*.mmd         text eol=lf
*.d2          text eol=lf

# Binary assets must never be line-ending converted.
*.png         binary
```

- [ ] **Step 3: Renormalise the working tree**

Run:
```bash
git add --renormalize .
git status --short
```
Expected: `.gitattributes` staged as new. Any file listed as modified is a file whose stored line endings just changed — that is the point of this step.

- [ ] **Step 4: Verify the shell scripts are now governed and LF**

Run:
```bash
git ls-files --eol plugin/bin/glimpse scripts/install.sh scripts/check-diagram-templates.sh
```
Expected: each line now shows `i/lf w/lf` and a non-empty `attr/` column containing `text eol=lf`.

- [ ] **Step 5: Verify the build still passes**

Run: `dotnet build Glimpse.slnx -v quiet`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add .gitattributes
git add --renormalize .
git commit -m "chore: pin line endings so shell scripts stay LF

core.autocrlf=true with no .gitattributes left plugin/bin/glimpse,
scripts/install.sh and check-diagram-templates.sh at i/lf w/crlf. A
CRLF shebang makes macOS fail with 'bad interpreter: ...^M', killing
the glimpse command outright.

Adds .cmd/.ps1 as eol=crlf ahead of the Windows entrypoints, since
cmd.exe mis-parses LF-only batch files."
```

---

### Task 2: `ToolLocator` — resolve tools in managed code

Clears all 7 baseline Windows failures. Everything downstream needs a working resolver.

**Files:**
- Modify: `src/Glimpse.Core/ToolLocator.cs` (full rewrite, 41 lines → ~95)
- Modify: `tests/Glimpse.Core.Tests/ToolLocatorTests.cs`
- Modify: `tests/Glimpse.Core.Tests/RenderEngineTests.cs:12` (its `ls` spec cannot resolve on Windows)

**Interfaces:**
- Consumes: nothing
- Produces:
  - `ToolSearchEnvironment(string PathVariable, string? PathExtVariable, bool WindowsRules)` — record
  - `ToolLocator.Resolve(string tool)` → `string?` (absolute path or null)
  - `ToolLocator.Resolve(string tool, ToolSearchEnvironment environment)` → `string?`
  - `ToolLocator.InstallHint(string tool)` → `string`

**Design note — why the two-overload shape.** `OutputLocator.Resolve()` already uses exactly this pattern (a no-arg version that reads the real environment, plus an overload taking the environment explicitly for tests). Following it keeps `Glimpse.Core` free of `InternalsVisibleTo`, and lets the Windows rules be tested **on macOS CI too** rather than being skipped there. `WindowsRules` is a parameter rather than an `OperatingSystem` call so both rule sets are exercised on both platforms. The three fields are grouped into a record to respect the ≤3-parameter rule.

- [ ] **Step 1: Write the failing tests**

Replace the whole body of `tests/Glimpse.Core.Tests/ToolLocatorTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class ToolLocatorTests
{
    /// <summary>A throwaway PATH directory containing exactly the files a test names.</summary>
    private static string DirectoryWith(params string[] fileNames)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"glimpse-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var name in fileNames)
            File.WriteAllText(Path.Combine(dir, name), "");
        return dir;
    }

    private static ToolSearchEnvironment WindowsEnv(string dir, string? pathExt = ".COM;.EXE;.BAT;.CMD;.VBS;.PY")
        => new(dir, pathExt, WindowsRules: true);

    private static ToolSearchEnvironment UnixEnv(string dir)
        => new(dir, null, WindowsRules: false);

    [Fact]
    public void Resolve_OnWindowsWithBothExtensionlessAndCmd_ShouldPickTheCmd()
    {
        // npm installs BOTH 'mmdc' (a bash script) and 'mmdc.cmd'. Verified 2026-08-01:
        // Process.Start on the extensionless file throws "not a valid application for this
        // OS platform", so resolving it would break every mermaid render on Windows.
        var dir = DirectoryWith("mmdc", "mmdc.cmd");

        var resolved = ToolLocator.Resolve("mmdc", WindowsEnv(dir));

        Assert.Equal(Path.Combine(dir, "mmdc.cmd"), resolved);
    }

    [Fact]
    public void Resolve_OnWindowsWithOnlyAnExtensionlessFile_ShouldReturnNull()
    {
        var dir = DirectoryWith("mmdc");

        Assert.Null(ToolLocator.Resolve("mmdc", WindowsEnv(dir)));
    }

    [Fact]
    public void Resolve_OnWindowsWithUnlaunchableExtensionOnly_ShouldReturnNull()
    {
        // PATHEXT lists .VBS and .PY, but CreateProcess cannot start either directly.
        var dir = DirectoryWith("thing.vbs", "thing.py");

        Assert.Null(ToolLocator.Resolve("thing", WindowsEnv(dir)));
    }

    [Fact]
    public void Resolve_OnWindowsWithSeveralLaunchableExtensions_ShouldFollowPathExtOrder()
    {
        var dir = DirectoryWith("tool.cmd", "tool.exe");

        var resolved = ToolLocator.Resolve("tool", WindowsEnv(dir, ".COM;.EXE;.BAT;.CMD"));

        Assert.Equal(Path.Combine(dir, "tool.exe"), resolved);
    }

    [Fact]
    public void Resolve_OnWindowsWhenPathExtIsEmpty_ShouldStillFindAnExe()
    {
        var dir = DirectoryWith("tool.exe");

        var resolved = ToolLocator.Resolve("tool", WindowsEnv(dir, pathExt: null));

        Assert.Equal(Path.Combine(dir, "tool.exe"), resolved);
    }

    [Fact]
    public void Resolve_OnWindowsWhenCallerSuppliesTheExtension_ShouldHonourItExactly()
    {
        var dir = DirectoryWith("tool.exe", "tool.cmd");

        var resolved = ToolLocator.Resolve("tool.cmd", WindowsEnv(dir));

        Assert.Equal(Path.Combine(dir, "tool.cmd"), resolved);
    }

    [Fact]
    public void Resolve_OnUnixWithAnExtensionlessFile_ShouldReturnIt()
    {
        var dir = DirectoryWith("mmdc");

        var resolved = ToolLocator.Resolve("mmdc", UnixEnv(dir));

        Assert.Equal(Path.Combine(dir, "mmdc"), resolved);
    }

    [Fact]
    public void Resolve_WithEarlierPathEntryWinning_ShouldReturnTheFirstMatch()
    {
        var first = DirectoryWith("tool.exe");
        var second = DirectoryWith("tool.exe");
        var env = new ToolSearchEnvironment(
            $"{first}{Path.PathSeparator}{second}", ".EXE", WindowsRules: true);

        Assert.Equal(Path.Combine(first, "tool.exe"), ToolLocator.Resolve("tool", env));
    }

    [Fact]
    public void Resolve_WithQuotedPathEntry_ShouldStillProbeIt()
    {
        // Windows PATH entries are sometimes quoted; an unstripped quote makes the
        // directory unopenable and silently hides every tool inside it.
        var dir = DirectoryWith("tool.exe");
        var env = new ToolSearchEnvironment($"\"{dir}\"", ".EXE", WindowsRules: true);

        Assert.Equal(Path.Combine(dir, "tool.exe"), ToolLocator.Resolve("tool", env));
    }

    [Fact]
    public void Resolve_WithNonexistentPathEntry_ShouldNotThrow()
    {
        var env = new ToolSearchEnvironment(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"), ".EXE", WindowsRules: true);

        Assert.Null(ToolLocator.Resolve("tool", env));
    }

    [Fact]
    public void Resolve_WithRealToolOnRealPath_ShouldReturnAbsolutePath()
    {
        // 'dotnet' is on PATH by definition wherever this suite runs — unlike 'ls',
        // which the previous version of this test used and which Windows lacks.
        var path = ToolLocator.Resolve("dotnet");

        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void Resolve_WithNonexistentTool_ShouldReturnNull()
    {
        Assert.Null(ToolLocator.Resolve("definitely-not-a-real-tool-xyz"));
    }

    [Fact]
    public void Resolve_ForChrome_ShouldReturnAnExistingFileOrNull()
    {
        // Chrome is never on PATH, so it has a per-OS special case. Assert the contract
        // (a real file, or null) rather than a machine-specific path.
        var path = ToolLocator.Resolve("chrome");

        Assert.True(path is null || File.Exists(path));
    }

    [Theory]
    [InlineData("mmdc", "mermaid-cli")]
    [InlineData("dot", "graphviz")]
    [InlineData("d2", "d2")]
    public void InstallHint_ForKnownTool_ShouldMentionThePackage(string tool, string fragment)
    {
        var hint = ToolLocator.InstallHint(tool);

        Assert.Contains(fragment, hint);
    }

    [Fact]
    public void InstallHint_ShouldNameThePackageManagerForThisPlatform()
    {
        // A hint naming the wrong package manager is worse than no hint: it sends the
        // reader somewhere that cannot possibly work.
        var hint = ToolLocator.InstallHint("d2");

        var expected = OperatingSystem.IsWindows() ? "winget" : "brew";
        Assert.Contains(expected, hint);
    }

    [Fact]
    public void InstallHint_ForUnknownTool_ShouldFallBackToAGenericMessage()
    {
        Assert.Contains("mystery-tool", ToolLocator.InstallHint("mystery-tool"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Glimpse.Core.Tests --filter ToolLocatorTests`
Expected: compile error — `ToolSearchEnvironment` does not exist, and `Resolve` has no two-argument overload.

- [ ] **Step 3: Rewrite `ToolLocator`**

Replace the entire contents of `src/Glimpse.Core/ToolLocator.cs`:

```csharp
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
```

- [ ] **Step 4: Fix `RenderEngineTests`' unresolvable `ls` tool**

`RenderEngineTests.cs:12` builds a spec around `ls`, which does not exist on Windows, so `ToolLocator` cannot resolve it and 4 tests fail before reaching their assertions. Replace lines 10–12:

```csharp
    // Uses 'dotnet' as a real, always-present tool so ToolLocator resolves it on any OS;
    // the fake runner simulates the tool's effect (writing a PNG or not) without invoking it.
    private static RendererSpec DotnetSpec() => new("fake", "dotnet", ["{out}"], [".x"]);
```

Then replace all four `LsSpec()` call sites (lines 56, 70, 84) with `DotnetSpec()`. Line 94 already uses its own inline spec and needs no change.

`RenderEngineTests.cs:98` also passes a hardcoded `/tmp/x.png`. It never gets written, so it does not fail on Windows — but change it to `Path.Combine(Path.GetTempPath(), "x.png")` so the suite carries no POSIX-only paths.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test Glimpse.slnx`
Expected: **0 failures.** All 7 baseline failures are gone, including `EndToEndGateTests`, which can now correctly *skip* (or pass — `mmdc` 11.16.0 is installed on this box, so expect a real pass).

- [ ] **Step 6: Verify a real render end to end**

Run:
```bash
dotnet run --project tools/Glimpse.Capture -- docs/diagrams/glimpse-architecture.mmd --name plan-check --no-manifest
```
Expected: `Status:   ok (WxH)` with no warnings, and a real PNG at the printed path. This is the first render Glimpse has ever done on Windows.

- [ ] **Step 7: Commit**

```bash
git add src/Glimpse.Core/ToolLocator.cs tests/Glimpse.Core.Tests/ToolLocatorTests.cs tests/Glimpse.Core.Tests/RenderEngineTests.cs
git commit -m "feat(core): resolve render tools cross-platform without /usr/bin/which

Replaces the hardcoded /usr/bin/which subprocess with a managed PATH
scan: no process spawn, faster, and testable without a real binary.

On Windows the scan honours PATHEXT but narrows it to extensions
CreateProcess can actually launch (.com/.exe/.bat/.cmd) and never
accepts an extensionless match. npm installs BOTH 'mmdc' and
'mmdc.cmd'; Process.Start throws on the former, so a first-name-wins
scan would resolve the one file that can never run.

Chrome gains a three-way lookup: macOS app bundle, Windows App Paths
registry key with well-known-dir fallback, Linux PATH. Install hints
are now per-platform, since a hint naming the wrong package manager
sends the reader somewhere that cannot work.

Clears all 7 Windows test failures. Retargets RenderEngineTests off
'ls', which Windows lacks."
```

---

### Task 3: Widen `WindowId` to `long`

An `HWND` is pointer-sized; a `CGWindowID` is `uint32`. Three declarations must widen together or the value truncates somewhere between CLI argument and captured window. All three are widenings, so every current value round-trips and `--window-id 42` keeps parsing.

**Files:**
- Modify: `src/Glimpse.Core/WindowInfo.cs:5`
- Modify: `src/Glimpse.Core/RenderCommandBuilder.cs:11`
- Modify: `tools/Glimpse.Capture/CaptureOptions.cs`
- Modify: `src/Glimpse.Core/WindowFinder.cs:69` (the `(uint)` cast in `MacWindowFinder.ReadWindow`)
- Modify: `tools/Glimpse.Capture/Program.cs:149`
- Modify: `tests/Glimpse.Core.Tests/WindowSelectorTests.cs`
- Modify: `tests/Glimpse.Capture.Tests/CaptureOptionsTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `WindowInfo(long WindowId, string OwnerName, string? Title, int X, int Y, int Width, int Height, int Layer, bool OnScreen)`
  - `RenderRequest(string Source, string OutputPath, int Width, int Height, SnapshotTheme Theme, long? WindowId = null)`
  - `CaptureOptions.WindowId` is `long?`

- [ ] **Step 1: Write the failing test**

Append to `tests/Glimpse.Capture.Tests/CaptureOptionsTests.cs`:

```csharp
    [Fact]
    public void Parse_WithWindowIdBeyondIntRange_ShouldKeepTheFullValue()
    {
        // A Windows HWND is pointer-sized. Truncating it to int silently targets the
        // wrong window (or none), which looks like a capture bug rather than a parse bug.
        const long hwnd = 4_294_967_296L; // 2^32, unrepresentable as int

        var options = CaptureOptions.Parse(["--renderer", "app", "--window-id", "4294967296"]);

        Assert.Equal(hwnd, options.WindowId);
    }

    [Fact]
    public void Parse_WithSmallWindowId_ShouldStillParse()
    {
        var options = CaptureOptions.Parse(["--renderer", "app", "--window-id", "42"]);

        Assert.Equal(42L, options.WindowId);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Glimpse.Capture.Tests --filter CaptureOptionsTests`
Expected: FAIL — `--window-id requires an integer.` is thrown, because `int.TryParse` rejects 2^32.

- [ ] **Step 3: Widen the three declarations**

`src/Glimpse.Core/WindowInfo.cs` — change the first property:
```csharp
public sealed record WindowInfo(
    long WindowId,
```

`src/Glimpse.Core/RenderCommandBuilder.cs` — change the last property of `RenderRequest`:
```csharp
    long? WindowId = null);
```

`tools/Glimpse.Capture/CaptureOptions.cs` — change the record property, the local, and the parse:
```csharp
    long? WindowId,
```
```csharp
        int width = 1280, height = 800;
        long windowId = 0;
```
```csharp
                case "--window-id":
                    var windowIdRaw = Next(args, ref i);
                    if (!long.TryParse(windowIdRaw, out windowId))
                        throw new ArgumentException("--window-id requires an integer.");
                    hasWindowId = true;
                    break;
```

- [ ] **Step 4: Fix the two now-invalid casts**

`src/Glimpse.Core/WindowFinder.cs:69` — the `uint` cast narrows what is now a `long` field:
```csharp
        return new WindowInfo(id.Value, owner, title, x, y, w, h, (int)layer, onScreen);
```

`tools/Glimpse.Capture/Program.cs:139` and `:149` — the local and its assignment:
```csharp
    long? windowId = o.WindowId;
```
```csharp
            windowId = selected.WindowId;
```

- [ ] **Step 5: Update `WindowSelectorTests` for the widened type**

In `tests/Glimpse.Core.Tests/WindowSelectorTests.cs`, change the helper signature at line 8 and the four `u`-suffixed literals:

```csharp
    private static WindowInfo Win(long id, string owner, string? title = null,
        int w = 800, int h = 600, int layer = 0, bool onScreen = true) =>
        new(id, owner, title, 0, 0, w, h, layer, onScreen);
```

Then replace `Assert.Equal(1u, ...)` → `Assert.Equal(1L, ...)`, `10u` → `10L`, `21u` → `21L`, `33u` → `33L`.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test Glimpse.slnx`
Expected: 0 failures, including the two new `CaptureOptionsTests`.

- [ ] **Step 7: Commit**

```bash
git add src/Glimpse.Core/WindowInfo.cs src/Glimpse.Core/RenderCommandBuilder.cs src/Glimpse.Core/WindowFinder.cs tools/Glimpse.Capture/CaptureOptions.cs tools/Glimpse.Capture/Program.cs tests/
git commit -m "refactor: widen WindowId to long for Windows HWND

A CGWindowID is uint32 but an HWND is pointer-sized. WindowInfo.WindowId,
RenderRequest.WindowId and --window-id parsing all widen together;
truncating anywhere between them would silently target the wrong window,
which reads as a capture bug rather than a parse bug.

All three are widenings, so existing values round-trip unchanged."
```

---

### Task 4: `IAppCapturer` seam + `MacAppCapturer` (pure refactor)

No new behaviour. This must land green **before** any Windows capture code exists, so a later macOS regression is unambiguously attributable.

**Files:**
- Modify: `src/Glimpse.Core/RenderEngine.cs` (add `RenderOutcomes.From`)
- Create: `src/Glimpse.Core/AppCapturer.cs`
- Create: `tests/Glimpse.Core.Tests/AppCapturerTests.cs`

**Interfaces:**
- Consumes: `RenderRequest` with `long? WindowId` (Task 3); `RenderOutcome`; `RendererRegistry.Default()`; `IProcessRunner`
- Produces:
  - `RenderOutcomes.From(string outputPath, bool succeeded, string standardError)` → `RenderOutcome`
  - `IAppCapturer.CaptureAsync(RenderRequest request)` → `Task<RenderOutcome>`
  - `MacAppCapturer(IProcessRunner runner)` — ctor takes the runner
  - `MacAppCapturer.FullScreenSpec` → `RendererSpec` (static, so tests can assert the args)

- [ ] **Step 1: Write the failing test**

Create `tests/Glimpse.Core.Tests/AppCapturerTests.cs`:

```csharp
using Glimpse.Abstractions;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class AppCapturerTests
{
    private sealed class RecordingRunner(int exitCode) : IProcessRunner
    {
        public List<string> LastArgs { get; } = [];

        public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args)
        {
            LastArgs.Clear();
            LastArgs.AddRange(args);
            return Task.FromResult(new ProcessResult(exitCode, exitCode == 0 ? "" : "boom"));
        }
    }

    [Fact]
    public void FullScreenSpec_ShouldMatchTheArgsShippedBeforeTheRefactor()
    {
        // Program.cs built ["-x", "{out}"] inline. Pinning it here is what makes this
        // task a refactor rather than a behaviour change.
        var spec = MacAppCapturer.FullScreenSpec;

        Assert.Equal("screencapture", spec.Tool);
        Assert.Equal(["-x", "{out}"], spec.Args);
    }

    [Fact]
    public void WindowSpec_ShouldMatchTheBuiltInAppRenderer()
    {
        var builtIn = RendererRegistry.Default().Resolve("app", null);

        Assert.Equal("screencapture", builtIn.Tool);
        Assert.Equal(["-x", "-o", "-l{windowid}", "{out}"], builtIn.Args);
    }

    [Fact]
    public void RenderOutcomes_From_WhenCommandFailed_ShouldReportFailedExitTwo()
    {
        var outcome = RenderOutcomes.From(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
            succeeded: false,
            standardError: "  boom  ");

        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
        Assert.Contains("render-failed:boom", outcome.Warnings);
    }

    [Fact]
    public void RenderOutcomes_From_WhenFileIsMissingDespiteSuccess_ShouldReportFailed()
    {
        var outcome = RenderOutcomes.From(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
            succeeded: true,
            standardError: "");

        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter AppCapturerTests`
Expected: compile error — `MacAppCapturer` and `RenderOutcomes` do not exist.

- [ ] **Step 3: Extract the analyse tail into `RenderOutcomes`**

In `src/Glimpse.Core/RenderEngine.cs`, add this above the `RenderEngine` class:

```csharp
/// <summary>The shared tail of every render path: a written file becomes an analysed outcome.
/// Used by both <see cref="RenderEngine"/> (external tools) and <see cref="IAppCapturer"/>
/// (in-process capture) so the two can never disagree about status or exit codes.</summary>
public static class RenderOutcomes
{
    public static RenderOutcome From(string outputPath, bool succeeded, string standardError)
    {
        if (!succeeded || !File.Exists(outputPath))
            return new RenderOutcome("failed", 2, outputPath, 0, 0,
                [$"render-failed:{standardError.Trim()}"]);

        var inspection = PngAnalysis.Inspect(outputPath);
        var exitCode = inspection.Warnings.Count > 0 ? 1 : 0;
        return new RenderOutcome("ok", exitCode, outputPath,
            inspection.Width, inspection.Height, inspection.Warnings);
    }
}
```

Then replace `RenderEngine.RenderAsync`'s body after the `runner.RunAsync` call:

```csharp
        var result = await runner.RunAsync(command.Executable, command.Args);
        return RenderOutcomes.From(request.OutputPath, result.ExitCode == 0, result.StdErr);
```

- [ ] **Step 4: Create the seam and the macOS implementation**

Create `src/Glimpse.Core/AppCapturer.cs`:

```csharp
using System.Runtime.Versioning;

namespace Glimpse.Core;

/// <summary>
/// Captures a live window (or the whole screen) to a PNG. Implementations are OS-specific:
/// macOS shells out to <c>screencapture</c>, Windows captures in-process via GDI, because
/// Windows ships no screenshot command for the renderer contract to wrap.
/// </summary>
public interface IAppCapturer
{
    /// <summary>A null <see cref="RenderRequest.WindowId"/> means "capture the whole screen".</summary>
    Task<RenderOutcome> CaptureAsync(RenderRequest request);
}

/// <summary>macOS capture via <c>screencapture</c>. Needs Screen Recording (TCC) permission.</summary>
public sealed class MacAppCapturer(IProcessRunner runner) : IAppCapturer
{
    /// <summary>Whole-screen args. Kept here (not in the registry) because the built-in
    /// "app" spec is the window-targeted variant and needs a {windowid}.</summary>
    public static RendererSpec FullScreenSpec { get; } =
        new("app", "screencapture", ["-x", "{out}"], []);

    // The platform attribute sits on the BEHAVIOUR, not the type: FullScreenSpec is a plain
    // RendererSpec record with no interop, so attributing the whole class would force an
    // untrue [SupportedOSPlatform("macos")] onto every test that merely reads that data.
    [SupportedOSPlatform("macos")]
    public Task<RenderOutcome> CaptureAsync(RenderRequest request)
    {
        var spec = request.WindowId is null
            ? FullScreenSpec
            : RendererRegistry.Default().Resolve("app", null);

        return new RenderEngine(runner).RenderAsync(spec, request);
    }
}
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test Glimpse.slnx`
Expected: 0 failures. `RenderEngineTests` must still pass unchanged — it is the proof that extracting `RenderOutcomes` altered nothing.

- [ ] **Step 6: Commit**

```bash
git add src/Glimpse.Core/RenderEngine.cs src/Glimpse.Core/AppCapturer.cs tests/Glimpse.Core.Tests/AppCapturerTests.cs
git commit -m "refactor(core): add IAppCapturer seam with the macOS implementation

Windows ships no screenshot command, so 'app' cannot stay pure
configuration the way the other four renderers do. This adds the seam
and moves today's screencapture behaviour behind it unchanged --
FullScreenSpec pins the exact args Program.cs built inline, so the
refactor is provably behaviour-preserving.

Also extracts the analyse tail into RenderOutcomes.From so the external-
tool and in-process paths cannot disagree about status or exit codes.

No Windows code yet, deliberately: this lands green first so any later
macOS regression is unambiguously attributable."
```

---

### Task 5: `WindowsWindowFinder`

Enumerates top-level windows so `--window "Recorder"` and `--list-windows` work. `WindowSelector` is **not** touched — Windows is mapped onto its existing contract.

**Files:**
- Modify: `src/Glimpse.Core/WindowFinder.cs` (append the new class)
- Modify: `tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj` (add `Xunit.SkippableFact`)
- Create: `tests/Glimpse.Core.Tests/WindowsWindowFinderTests.cs`
- Modify: `tests/Glimpse.Core.Tests/WindowSelectorTests.cs` (add Windows-shaped fixtures)

**Interfaces:**
- Consumes: `IWindowFinder`; `WindowInfo` with `long WindowId` (Task 3)
- Produces: `WindowsWindowFinder()` implementing `IWindowFinder.ListOnScreen()`

**Mapping contract** (`WindowSelector` already filters on these, so getting them right is what makes it reusable):

| `WindowInfo` field | Windows source |
|---|---|
| `WindowId` | `(long)hwnd` |
| `OwnerName` | owning process name via `GetWindowThreadProcessId` |
| `Title` | `GetWindowTextW` — **no permission needed**, unlike macOS |
| `Layer` | `0` for a normal window; `1` for `WS_EX_TOOLWINDOW` — non-zero is excluded by `WindowSelector` |
| `OnScreen` | `IsWindowVisible` && not DWM-cloaked && title non-empty |
| bounds | `DWMWA_EXTENDED_FRAME_BOUNDS`, falling back to `GetWindowRect` |

- [ ] **Step 1: Write the failing tests**

Create `tests/Glimpse.Core.Tests/WindowsWindowFinderTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class WindowsWindowFinderTests
{
    /// <summary>
    /// Enumerates on Windows, or returns null when this OS cannot.
    /// The <c>if (OperatingSystem.IsWindows())</c> is not redundant with <c>Skip.IfNot</c>:
    /// verified 2026-08-01 that CA1416 does NOT accept <c>Skip.IfNot</c> as a guard (it is an
    /// ordinary method call the analyzer cannot reason about), and CA1416 is a build error
    /// here. The real if-guard is the only form that compiles.
    /// </summary>
    private static IReadOnlyList<WindowInfo>? EnumerateOrNull()
        => OperatingSystem.IsWindows() ? new WindowsWindowFinder().ListOnScreen() : null;

    [SkippableFact]
    public void ListOnScreen_OnWindows_ShouldFindAtLeastOneRealWindow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only window enumeration.");

        var windows = EnumerateOrNull();

        Assert.NotNull(windows);
        Assert.NotEmpty(windows);
    }

    [SkippableFact]
    public void ListOnScreen_OnWindows_ShouldPopulateOwnerAndBoundsForSelectableWindows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only window enumeration.");

        var selectable = (EnumerateOrNull() ?? [])
            .Where(w => w.OnScreen && w.Layer == 0 && w.Width >= 50 && w.Height >= 50)
            .ToList();

        Skip.If(selectable.Count == 0, "No normal on-screen window on this session.");
        Assert.All(selectable, w =>
        {
            Assert.False(string.IsNullOrWhiteSpace(w.OwnerName));
            Assert.NotEqual(0, w.WindowId);
            Assert.True(w.Width > 0 && w.Height > 0);
        });
    }

    [SkippableFact]
    public void ListOnScreen_OnWindows_ShouldReturnDistinctWindowIds()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only window enumeration.");

        var windows = EnumerateOrNull() ?? [];

        Assert.Equal(windows.Count, windows.Select(w => w.WindowId).Distinct().Count());
    }
}
```

Append to `tests/Glimpse.Core.Tests/WindowSelectorTests.cs` — these are pure and run on **both** OSes, which is what proves the mapping is reusable:

```csharp
    [Fact]
    public void SelectFrontmost_WithWindowsToolWindowAtLayerOne_ShouldSkipIt()
    {
        // WindowsWindowFinder maps WS_EX_TOOLWINDOW to layer 1 so this existing
        // macOS-shaped filter excludes it without WindowSelector needing to change.
        var windows = new[]
        {
            Win(50, "Recorder", "tooltip", layer: 1),
            Win(51, "Recorder", "Mimica Recorder", w: 1400, h: 900),
        };

        var result = WindowSelector.SelectFrontmost(windows, "Recorder");

        Assert.Equal(51L, result!.WindowId);
    }

    [Fact]
    public void SelectFrontmost_WithCloakedWindowMarkedOffScreen_ShouldSkipIt()
    {
        var windows = new[]
        {
            Win(60, "Recorder", "ghost", onScreen: false),   // DWM-cloaked
            Win(61, "Recorder", "Mimica Recorder", w: 1400, h: 900),
        };

        var result = WindowSelector.SelectFrontmost(windows, "Recorder");

        Assert.Equal(61L, result!.WindowId);
    }
```

- [ ] **Step 2: Add the `Xunit.SkippableFact` reference to `Glimpse.Core.Tests`**

Verified 2026-08-01: `Glimpse.Core.Tests.csproj` does **not** reference it yet (only `Glimpse.Capture.Tests` does, for `EndToEndGateTests`). Add it to the first `ItemGroup` of `tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj`:

```xml
    <PackageReference Include="Xunit.SkippableFact" />
```

The version is already pinned in `Directory.Packages.props:15` (1.5.23), so no version attribute — `ManagePackageVersionsCentrally` is on. `using Xunit;` already covers `Skip`; the `[SkippableFact]` attribute needs no extra using.

Run: `dotnet build tests/Glimpse.Core.Tests -v quiet`
Expected: `Build succeeded` — confirms the package resolves before you depend on it.

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter WindowsWindowFinderTests`
Expected: compile error — `WindowsWindowFinder` does not exist.

- [ ] **Step 4: Implement `WindowsWindowFinder`**

Append to `src/Glimpse.Core/WindowFinder.cs`:

```csharp
/// <summary>
/// Windows window enumeration via <c>EnumWindows</c>. Enumerates in Z-order (top first),
/// which is the front-to-back order <see cref="WindowSelector"/> already assumes from
/// macOS's CGWindowList — so the selector needs no platform knowledge. Window titles need
/// no special permission here, unlike macOS.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWindowFinder : IWindowFinder
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;
    private const int DwmwaExtendedFrameBounds = 9;

    private const int NormalLayer = 0;
    private const int OverlayLayer = 1; // any non-zero layer is excluded by WindowSelector

    public IReadOnlyList<WindowInfo> ListOnScreen()
    {
        var windows = new List<WindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            var info = ReadWindow(hwnd);
            if (info is not null)
                windows.Add(info);
            return true; // keep enumerating
        }, IntPtr.Zero);

        return windows;
    }

    private static WindowInfo? ReadWindow(IntPtr hwnd)
    {
        var title = ReadTitle(hwnd);
        if (title.Length == 0)
            return null; // untitled top-levels are framework helpers, never capture targets

        var owner = ReadOwnerProcessName(hwnd);
        if (owner is null)
            return null; // process gone or protected — not something we can target

        var (x, y, width, height) = ReadBounds(hwnd);
        var isToolWindow = (ReadExStyle(hwnd) & WsExToolWindow) != 0;
        var onScreen = IsWindowVisible(hwnd) && !IsCloaked(hwnd);

        return new WindowInfo(hwnd.ToInt64(), owner, title, x, y, width, height,
            isToolWindow ? OverlayLayer : NormalLayer, onScreen);
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        var buffer = new char[length + 1];
        var copied = GetWindowText(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : "";
    }

    private static string? ReadOwnerProcessName(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
            return null;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null; // exited between enumeration and lookup
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Prefers the DWM frame bounds: GetWindowRect includes the invisible
    /// resize border, which would bake dead pixels into every capture.</summary>
    private static (int X, int Y, int Width, int Height) ReadBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out Rect frame,
                System.Runtime.InteropServices.Marshal.SizeOf<Rect>()) == 0)
            return ToSize(frame);

        return GetWindowRect(hwnd, out var rect) ? ToSize(rect) : (0, 0, 0, 0);
    }

    private static (int X, int Y, int Width, int Height) ToSize(Rect rect)
        => (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static bool IsCloaked(IntPtr hwnd)
        => DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0
           && cloaked != 0;

    private static long ReadExStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet =
        System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr hwnd, char[] buffer, int maxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet =
        System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
```

**Note on the fully-qualified attributes:** `WindowFinder.cs:1` already has `using System.Runtime.InteropServices;`, so if you are appending to that file you can drop the `System.Runtime.InteropServices.` prefixes and write `[DllImport(...)]`, `[StructLayout(...)]`, `[MarshalAs(...)]`, `Marshal.SizeOf<Rect>()` directly. Prefer that — it matches `MacWindowFinder` in the same file. The qualified form above is written out only so the block is unambiguous if it lands in a new file.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Glimpse.Core.Tests --filter "WindowsWindowFinderTests|WindowSelectorTests"`
Expected: PASS. On Windows the three finder tests run for real; on macOS they skip.

- [ ] **Step 6: Verify against the real desktop**

Once Task 7 wires `--list-windows`, this is re-verified end to end. For now, assert via the test above and confirm the count is plausible:

Run: `dotnet test tests/Glimpse.Core.Tests --filter WindowsWindowFinderTests -v normal`
Expected: all three pass, none skipped (you are on Windows).

- [ ] **Step 7: Commit**

```bash
git add src/Glimpse.Core/WindowFinder.cs tests/Glimpse.Core.Tests/
git commit -m "feat(core): enumerate windows on Windows via EnumWindows

EnumWindows returns Z-order top-first, which is the front-to-back order
WindowSelector already assumes from macOS's CGWindowList -- so the pure
selector needs no platform knowledge and stays untouched. WS_EX_TOOLWINDOW
maps to layer 1 and DWM-cloaked windows to OnScreen=false, which its
existing filters then exclude for free.

Bounds come from DWMWA_EXTENDED_FRAME_BOUNDS rather than GetWindowRect,
which includes the invisible resize border and would bake dead pixels
into every capture.

The two new WindowSelector fixtures are pure and run on both OSes, which
is what proves the mapping is genuinely reusable."
```

---

### Task 6: `WindowsAppCapturer`

The only component with no existing code to lean on. Both failure modes here produce a *plausible-looking but wrong* PNG rather than an error, so the fallback chain and the orientation test matter more than usual.

**Files:**
- Create: `src/Glimpse.Core/BgraPngEncoder.cs`
- Modify: `src/Glimpse.Core/AppCapturer.cs` (append)
- Create: `tests/Glimpse.Core.Tests/BgraPngEncoderTests.cs`
- Create: `tests/Glimpse.Core.Tests/WindowsAppCapturerTests.cs`

**Interfaces:**
- Consumes: `IAppCapturer`, `RenderOutcomes.From` (Task 4); `RenderRequest` with `long? WindowId` (Task 3)
- Produces:
  - `BgraPngEncoder.Write(byte[] bgraBuffer, int width, int height, string outputPath)` — `public static`, **no platform attribute**
  - `WindowsAppCapturer()` implementing `IAppCapturer`

**Why the encoder is its own type.** Encoding BGRA bytes to PNG is pure SkiaSharp with no interop, so it is genuinely cross-platform and must run on macOS CI too — that is the one piece of genuinely new image code, and the platform this repo cannot test locally is exactly where it needs proving. Keeping it inside a `[SupportedOSPlatform("windows")]` class would force an untrue attribute onto all five encode tests (C# cannot un-attribute a member of an attributed type), and attributing `WindowsAppCapturer`'s seven native members individually instead would be noise. A separate unattributed type gets both: `WindowsAppCapturer` keeps its class-level attribute, and the encoder is freely testable everywhere.

**Two silent-failure traps** (both from the spec):
1. `PrintWindow` **must** pass `PW_RENDERFULLCONTENT` (`0x2`). Without it, DWM-composited windows — WPF, Chrome, Avalonia, i.e. exactly what Glimpse exists to screenshot — capture as solid black.
2. The DIB **must** be created with a **negative `biHeight`** for top-down rows. A positive value yields a vertically flipped capture, which reads as a rendering bug rather than a buffer-orientation one.

- [ ] **Step 1: Write the failing tests**

Create **two** test files. First `tests/Glimpse.Core.Tests/BgraPngEncoderTests.cs` — these are plain `[Fact]`s with **no guard and no platform attribute**, so they run on macOS CI too:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class BgraPngEncoderTests
{
    private static string TempPng() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

    /// <summary>BGRA, 4 bytes per pixel, top-down. Row 0 is red, every other row is blue.</summary>
    private static byte[] TwoToneBgra(int width, int height)
    {
        var buffer = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var isFirstRow = y == 0;
                buffer[i + 0] = isFirstRow ? (byte)0 : (byte)255;   // B
                buffer[i + 1] = 0;                                   // G
                buffer[i + 2] = isFirstRow ? (byte)255 : (byte)0;    // R
                buffer[i + 3] = 255;                                 // A
            }
        return buffer;
    }

    private static byte[] UniformBgra(int width, int height)
    {
        var buffer = new byte[width * height * 4];
        Array.Fill(buffer, (byte)255);
        return buffer;
    }

    [Fact]
    public void Write_ShouldPreserveDimensions()
    {
        var path = TempPng();

        BgraPngEncoder.Write(TwoToneBgra(24, 16), 24, 16, path);

        var inspection = PngAnalysis.Inspect(path);
        Assert.Equal(24, inspection.Width);
        Assert.Equal(16, inspection.Height);
        File.Delete(path);
    }

    [Fact]
    public void Write_WithVariedPixels_ShouldNotBeFlaggedSingleColor()
    {
        var path = TempPng();

        BgraPngEncoder.Write(TwoToneBgra(32, 32), 32, 32, path);

        Assert.Empty(PngAnalysis.Inspect(path).Warnings);
        File.Delete(path);
    }

    [Fact]
    public void Write_WithUniformPixels_ShouldBeFlaggedSingleColor()
    {
        // This is the safety net that catches a black PrintWindow result.
        var path = TempPng();

        BgraPngEncoder.Write(UniformBgra(32, 32), 32, 32, path);

        Assert.Contains(PngAnalysis.Inspect(path).Warnings,
            w => w.StartsWith("single-color-frame:"));
        File.Delete(path);
    }

    [Fact]
    public void Write_ShouldWriteRowsTopDownNotFlipped()
    {
        // WindowsAppCapturer creates its DIB with a negative biHeight for top-down rows. If
        // that sign is wrong the image is vertically flipped -- which looks like a rendering
        // bug, so pin the orientation here: row 0 is red, the last row is blue.
        var path = TempPng();
        BgraPngEncoder.Write(TwoToneBgra(8, 8), 8, 8, path);

        using var decoded = SkiaSharp.SKBitmap.Decode(File.ReadAllBytes(path));

        Assert.Equal(255, decoded.GetPixel(0, 0).Red);
        Assert.Equal(0, decoded.GetPixel(0, 0).Blue);
        Assert.Equal(255, decoded.GetPixel(0, 7).Blue);
        Assert.Equal(0, decoded.GetPixel(0, 7).Red);
        File.Delete(path);
    }

    [Fact]
    public void Write_WithABareFilename_ShouldNotThrow()
    {
        // Path.GetDirectoryName returns "" for a bare filename, and Directory.CreateDirectory("")
        // throws -- so the encoder must skip the create in that case.
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.GetTempPath());
        var name = $"{Guid.NewGuid():N}.png";
        try
        {
            BgraPngEncoder.Write(TwoToneBgra(4, 4), 4, 4, name);

            Assert.True(File.Exists(name));
            File.Delete(name);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}
```

Then `tests/Glimpse.Core.Tests/WindowsAppCapturerTests.cs` — the two real captures. Note the `if (OperatingSystem.IsWindows())` is **not** redundant with `Skip.IfNot`: verified 2026-08-01 that CA1416 does not accept `Skip.IfNot` as a guard, and CA1416 is a build error here.

```csharp
using Glimpse.Abstractions;
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class WindowsAppCapturerTests
{
    private static string TempPng() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

    /// <summary>Captures on Windows, or returns null when this OS cannot. The real if-guard
    /// is what satisfies CA1416; Skip.IfNot is an ordinary call the analyzer cannot read.</summary>
    private static async Task<RenderOutcome?> CaptureOrNull(string path, long? windowId)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        return await new WindowsAppCapturer().CaptureAsync(
            new RenderRequest("", path, 1280, 800, SnapshotTheme.Light, windowId));
    }

    [SkippableFact]
    public async Task CaptureAsync_OnWindowsForFullScreen_ShouldProduceANonBlankPng()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only capture.");
        var path = TempPng();

        var outcome = await CaptureOrNull(path, windowId: null);

        Assert.NotNull(outcome);
        Assert.Equal("ok", outcome.Status);
        Assert.True(outcome.Width > 0 && outcome.Height > 0);
        File.Delete(path);
    }

    [SkippableFact]
    public async Task CaptureAsync_OnWindowsWithAnInvalidWindowId_ShouldFailNotThrow()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only capture.");
        var path = TempPng();

        var outcome = await CaptureOrNull(path, windowId: 999_999_999L);

        Assert.NotNull(outcome);
        Assert.Equal("failed", outcome.Status);
        Assert.Equal(2, outcome.ExitCode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter WindowsAppCapturerTests`
Expected: compile error — `WindowsAppCapturer` does not exist.

- [ ] **Step 3: Implement `WindowsAppCapturer`**

First create `src/Glimpse.Core/BgraPngEncoder.cs` — pure SkiaSharp, no interop, deliberately **not** platform-attributed so it runs on macOS CI too:

```csharp
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Glimpse.Core;

/// <summary>
/// Writes a raw BGRA pixel buffer to a PNG. Uses SkiaSharp, which <see cref="PngAnalysis"/>
/// already decodes with — so encode and decode can never disagree about the format.
/// Cross-platform on purpose: the Windows capturer produces the buffer, but nothing here
/// touches Win32, so the encoder is testable on every OS.
/// </summary>
public static class BgraPngEncoder
{
    /// <summary><paramref name="bgraBuffer"/> must be top-down, 4 bytes per pixel.</summary>
    public static void Write(byte[] bgraBuffer, int width, int height, string outputPath)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(bgraBuffer, 0, bitmap.GetPixels(), bgraBuffer.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        // GetDirectoryName returns "" for a bare filename, and CreateDirectory("") throws.
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(outputPath, data.ToArray());
    }
}
```

Then append the capturer to `src/Glimpse.Core/AppCapturer.cs`. Add `using System.Runtime.InteropServices;` at the top of the file (`SkiaSharp` is no longer needed there — the encoder owns it).

```csharp
/// <summary>
/// Windows capture via GDI. Windows ships no screenshot command, so this runs in-process:
/// PrintWindow into a top-down DIB, then encode with SkiaSharp (already a Glimpse.Core
/// dependency — see PngAnalysis, which decodes with the same library).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppCapturer : IAppCapturer
{
    private const uint PrintWindowRenderFullContent = 0x2; // without this, DWM windows capture black
    private const int DibRgbColors = 0;
    private const int BiRgb = 0;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000; // includes layered windows
    private const int DwmwaExtendedFrameBounds = 9;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private static bool dpiDeclared;

    /// <summary>A captured frame: BGRA pixels plus the dimensions they describe.</summary>
    private sealed record CapturedPixels(byte[] Buffer, int Width, int Height);

    public Task<RenderOutcome> CaptureAsync(RenderRequest request)
    {
        DeclareDpiAwareness();

        try
        {
            var captured = request.WindowId is { } id
                ? CaptureWindow(new IntPtr(id))
                : CaptureVirtualScreen();

            BgraPngEncoder.Write(captured.Buffer, captured.Width, captured.Height, request.OutputPath);
            return Task.FromResult(RenderOutcomes.From(request.OutputPath, true, ""));
        }
        catch (GlimpseCaptureException ex)
        {
            return Task.FromResult(RenderOutcomes.From(request.OutputPath, false, ex.Message));
        }
    }

    /// <summary>Without per-monitor-v2 awareness Win32 reports virtualised coordinates and
    /// captures come back downscaled and blurry on any scaled display.</summary>
    private static void DeclareDpiAwareness()
    {
        if (dpiDeclared)
            return;

        dpiDeclared = true;
        // Fails harmlessly if awareness was already set (e.g. by an app manifest).
        SetProcessDpiAwarenessContext(PerMonitorAwareV2);
    }

    private static CapturedPixels CaptureWindow(IntPtr hwnd)
    {
        if (!IsWindow(hwnd))
            throw new GlimpseCaptureException($"No such window: {hwnd.ToInt64()}.");

        var (x, y, width, height) = WindowBounds(hwnd);
        if (width <= 0 || height <= 0)
            throw new GlimpseCaptureException("Window has no capturable area.");

        var buffer = WithDib(width, height, memoryDc =>
            PrintWindow(hwnd, memoryDc, PrintWindowRenderFullContent)
            // PrintWindow refused: fall back to lifting the window's region off the screen.
            || BlitFromScreen(memoryDc, x, y, width, height));

        return new CapturedPixels(buffer, width, height);
    }

    private static CapturedPixels CaptureVirtualScreen()
    {
        var x = GetSystemMetrics(SmXVirtualScreen);
        var y = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);

        if (width <= 0 || height <= 0)
            throw new GlimpseCaptureException("Could not determine the virtual screen size.");

        var buffer = WithDib(width, height, memoryDc =>
            BlitFromScreen(memoryDc, x, y, width, height));

        return new CapturedPixels(buffer, width, height);
    }

    private static bool BlitFromScreen(IntPtr memoryDc, int x, int y, int width, int height)
    {
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            return BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SrcCopy | CaptureBlt);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Creates a top-down 32bpp DIB, runs <paramref name="draw"/> into it, and
    /// copies the pixels out. Owns every GDI handle it creates.</summary>
    private static byte[] WithDib(int width, int height, Func<IntPtr, bool> draw)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // NEGATIVE = top-down rows; positive yields a flipped capture
            Planes = 1,
            BitCount = 32,
            Compression = BiRgb,
        };

        var memoryDc = CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc == IntPtr.Zero)
            throw new GlimpseCaptureException("CreateCompatibleDC failed.");

        var dib = IntPtr.Zero;
        try
        {
            dib = CreateDIBSection(memoryDc, ref header, DibRgbColors, out var pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || pixels == IntPtr.Zero)
                throw new GlimpseCaptureException("CreateDIBSection failed.");

            var previous = SelectObject(memoryDc, dib);
            try
            {
                if (!draw(memoryDc))
                    throw new GlimpseCaptureException("PrintWindow and BitBlt both failed.");

                var buffer = new byte[width * height * 4];
                Marshal.Copy(pixels, buffer, 0, buffer.Length);
                return buffer;
            }
            finally
            {
                SelectObject(memoryDc, previous);
            }
        }
        finally
        {
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            DeleteDC(memoryDc);
        }
    }

    private static (int X, int Y, int Width, int Height) WindowBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out Rect frame,
                Marshal.SizeOf<Rect>()) == 0)
            return (frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);

        if (GetWindowRect(hwnd, out var rect))
            return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

        return (0, 0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr deviceContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref BitmapInfoHeader header,
        uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
}
```

**Note:** `GlimpseCaptureException` already exists at `src/Glimpse.Core/ScreenCapture.cs:5`. Reuse it; do not declare a second one.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Glimpse.Core.Tests --filter "BgraPngEncoderTests|WindowsAppCapturerTests"`
Expected: all 7 pass on Windows — 5 `BgraPngEncoderTests` (which also run on macOS) plus 2 `WindowsAppCapturerTests`. `Write_ShouldWriteRowsTopDownNotFlipped` is the one that catches a wrong `biHeight` sign.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test Glimpse.slnx`
Expected: 0 failures.

- [ ] **Step 6: Commit**

```bash
git add src/Glimpse.Core/BgraPngEncoder.cs src/Glimpse.Core/AppCapturer.cs tests/Glimpse.Core.Tests/
git commit -m "feat(core): capture live windows on Windows via GDI

Windows ships no screencapture equivalent, so this captures in-process:
PrintWindow into a top-down DIB, encoded with SkiaSharp (already a
Glimpse.Core dependency -- PngAnalysis decodes with the same library, so
encode and decode cannot disagree about the format).

The encoder is its own unattributed type rather than a member of the
[SupportedOSPlatform(\"windows\")] capturer: it touches no Win32, so it
runs on macOS CI too -- which matters because it is the one piece of
genuinely new image code and macOS is the platform we cannot test locally.

Two traps that fail SILENTLY rather than erroring, both pinned by tests:
- PW_RENDERFULLCONTENT is required or DWM-composited windows (WPF,
  Chrome, Avalonia -- exactly what Glimpse screenshots) capture black.
- biHeight must be NEGATIVE for top-down rows; the wrong sign yields a
  vertically flipped image that reads as a rendering bug.

Falls back to a screen-region BitBlt when PrintWindow refuses, and
declares per-monitor-v2 DPI awareness so captures are not downscaled."
```

---

### Task 7: `PlatformSupport` + de-branch `Program.cs`

Removes both `OperatingSystem.IsMacOS()` checks from the CLI and makes `app` work on Windows end to end.

**Files:**
- Create: `src/Glimpse.Core/PlatformSupport.cs`
- Modify: `tools/Glimpse.Capture/Program.cs` (lines 6–17 and 137–170)
- Create: `tests/Glimpse.Core.Tests/PlatformSupportTests.cs`

**Interfaces:**
- Consumes: `MacWindowFinder`, `WindowsWindowFinder` (Task 5); `MacAppCapturer` (Task 4), `WindowsAppCapturer` (Task 6); `IProcessRunner`
- Produces:
  - `PlatformSupport.WindowFinder()` → `IWindowFinder?`
  - `PlatformSupport.AppCapturer(IProcessRunner runner)` → `IAppCapturer?`
  - `PlatformSupport.UnsupportedMessage(string feature)` → `string`

- [ ] **Step 1: Write the failing test**

Create `tests/Glimpse.Core.Tests/PlatformSupportTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class PlatformSupportTests
{
    [Fact]
    public void WindowFinder_OnASupportedOs_ShouldReturnThatOsImplementation()
    {
        var finder = PlatformSupport.WindowFinder();

        if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsWindowFinder>(finder);
        else if (OperatingSystem.IsMacOS())
            Assert.IsType<MacWindowFinder>(finder);
        else
            Assert.Null(finder);
    }

    [Fact]
    public void AppCapturer_OnASupportedOs_ShouldReturnThatOsImplementation()
    {
        var capturer = PlatformSupport.AppCapturer(new ProcessRunner());

        if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsAppCapturer>(capturer);
        else if (OperatingSystem.IsMacOS())
            Assert.IsType<MacAppCapturer>(capturer);
        else
            Assert.Null(capturer);
    }

    [Fact]
    public void UnsupportedMessage_ShouldNameTheFeatureAndTheSupportedPlatforms()
    {
        var message = PlatformSupport.UnsupportedMessage("--list-windows");

        Assert.Contains("--list-windows", message);
        Assert.Contains("macOS", message);
        Assert.Contains("Windows", message);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests --filter PlatformSupportTests`
Expected: compile error — `PlatformSupport` does not exist.

- [ ] **Step 3: Create `PlatformSupport`**

Create `src/Glimpse.Core/PlatformSupport.cs`:

```csharp
namespace Glimpse.Core;

/// <summary>
/// Picks the OS-specific window finder and capturer. A null return is the honest
/// "this OS has no live capture" signal, so callers raise one clear message instead of
/// scattering OperatingSystem checks.
/// </summary>
public static class PlatformSupport
{
    public static IWindowFinder? WindowFinder()
    {
        if (OperatingSystem.IsMacOS())
            return new MacWindowFinder();

        if (OperatingSystem.IsWindows())
            return new WindowsWindowFinder();

        return null;
    }

    public static IAppCapturer? AppCapturer(IProcessRunner runner)
    {
        if (OperatingSystem.IsMacOS())
            return new MacAppCapturer(runner);

        if (OperatingSystem.IsWindows())
            return new WindowsAppCapturer();

        return null;
    }

    public static string UnsupportedMessage(string feature)
        => $"{feature} needs live-window support, which Glimpse provides on macOS and Windows only.";
}
```

- [ ] **Step 4: De-branch `Program.cs`**

Replace lines 6–17 (the `--list-windows` block):

```csharp
if (options.ListWindows)
{
    var finder = PlatformSupport.WindowFinder();
    if (finder is null)
    {
        Console.Error.WriteLine(PlatformSupport.UnsupportedMessage("--list-windows"));
        return 2;
    }

    foreach (var w in finder.ListOnScreen())
        Console.WriteLine($"[id {w.WindowId,-6}] layer {w.Layer,-3} {w.Width}x{w.Height}  {w.OwnerName} — {w.Title ?? "(untitled)"}");
    return 0;
}
```

Replace the whole `CaptureAppAsync` local function (lines 137–170):

```csharp
async Task<RenderOutcome> CaptureAppAsync(CaptureOptions o, string outPath, List<string> warnings)
{
    var capturer = PlatformSupport.AppCapturer(new ProcessRunner())
        ?? throw new ArgumentException(PlatformSupport.UnsupportedMessage("The 'app' renderer"));

    var windowId = o.WindowId ?? ResolveWindowId(o, warnings);

    return await capturer.CaptureAsync(
        new RenderRequest("", outPath, o.Width, o.Height, o.Theme, windowId));
}

long? ResolveWindowId(CaptureOptions o, List<string> warnings)
{
    if (o.Window is null)
        return null;

    var finder = PlatformSupport.WindowFinder()
        ?? throw new ArgumentException(PlatformSupport.UnsupportedMessage("--window lookup"));

    var selected = WindowSelector.SelectFrontmost(finder.ListOnScreen(), o.Window, o.Title);
    if (selected is null)
    {
        warnings.Add($"fullscreen-fallback:no on-screen window matching '{o.Window}'");
        return null;
    }

    Console.WriteLine($"Window:   {selected.OwnerName} — {selected.Title ?? "(untitled)"} [id {selected.WindowId}]");
    return selected.WindowId;
}
```

- [ ] **Step 5: Confirm no OS branching is left in the CLI**

Run: `grep -n "OperatingSystem\." tools/Glimpse.Capture/Program.cs`
Expected: **no output.** Both checks are gone.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test Glimpse.slnx`
Expected: 0 failures.

- [ ] **Step 7: Verify live capture end to end on Windows**

Run:
```bash
dotnet run --project tools/Glimpse.Capture -- --list-windows
```
Expected: a real list of on-screen windows with ids, layers, sizes, owner process names and titles.

Then, with Chrome open:
```bash
dotnet run --project tools/Glimpse.Capture -- --renderer app --window "chrome" --name chrome-win --no-manifest
```
Expected: a `Window:  chrome — … [id …]` line, `Status:   ok (WxH)`, no warnings. **Read the PNG** and confirm it shows the actual Chrome window — not black (that would mean `PW_RENDERFULLCONTENT` is not taking effect), not upside down (wrong `biHeight` sign), and not the whole desktop (the window lookup silently fell back).

- [ ] **Step 8: Commit**

```bash
git add src/Glimpse.Core/PlatformSupport.cs tools/Glimpse.Capture/Program.cs tests/Glimpse.Core.Tests/PlatformSupportTests.cs
git commit -m "feat(cli): select window finder and capturer by platform

Replaces both OperatingSystem.IsMacOS() branches in Program.cs with a
PlatformSupport factory. A null return is the honest 'this OS has no
live capture' signal, so unsupported platforms get one clear message
instead of scattered checks.

The app renderer now works on Windows end to end: --list-windows,
--window lookup by process name, and --window-id all share the same
code path as macOS."
```

---

### Task 8: Distribution — `glimpse.cmd`, `install.ps1`, global tool

**Files:**
- Create: `plugin/bin/glimpse.cmd`
- Create: `scripts/install.ps1`
- Modify: `tools/Glimpse.Capture/Glimpse.Capture.csproj`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: a built `Glimpse.Capture.dll`
- Produces: `glimpse` on `PATH` on Windows; a packable `dotnet tool`

**Design note — why a sidecar file.** The bash `bin/glimpse` resolves its own symlink chain with `readlink` to find the repo, then reaches *outside* the plugin dir to `<repo>/tools/…`. A **junction is transparent to lexical `..` traversal**, so from `~/.claude/skills/glimpse/bin` the batch expression `..\..` resolves to `~/.claude/skills`, not the repo. Batch cannot resolve an ancestor junction without spawning PowerShell (~300 ms per invocation). So `install.ps1` writes the resolved repo path to a gitignored `glimpse.repo` sidecar beside the script, and the script falls back to lexical traversal when run directly from a real repo path.

- [ ] **Step 1: Create `plugin/bin/glimpse.cmd`**

```bat
@echo off
rem Glimpse CLI on PATH (via the plugin's bin/). Runs the in-repo built DLL so it is
rem always current; builds once if the DLL is missing.
rem
rem A junction is transparent to lexical ".." traversal, so from the installed location
rem (~/.claude/skills/glimpse/bin) "..\..\" resolves to ~/.claude/skills, NOT the repo.
rem install.ps1 therefore writes the real repo path to the glimpse.repo sidecar; the
rem lexical fallback covers running this script directly from a clone.
setlocal EnableExtensions
set "BIN=%~dp0"
set "REPO="
if exist "%BIN%glimpse.repo" set /p REPO=<"%BIN%glimpse.repo"
if not defined REPO for %%I in ("%BIN%..\..") do set "REPO=%%~fI"

set "PROJECT=%REPO%\tools\Glimpse.Capture\Glimpse.Capture.csproj"
set "DLL=%REPO%\tools\Glimpse.Capture\bin\Debug\net10.0\Glimpse.Capture.dll"

if not exist "%DLL%" (
    dotnet build "%PROJECT%" -v quiet 1>&2
    if errorlevel 1 exit /b 1
)

dotnet "%DLL%" %*
exit /b %errorlevel%
```

- [ ] **Step 2: Create `scripts/install.ps1`**

```powershell
#!/usr/bin/env pwsh
# Install the Glimpse plugin (CLI + skills) for use in any repo on this machine.
# Junctions ~/.claude/skills/glimpse -> <repo>/plugin (repo stays canonical, edits live).
# A junction is used rather than a symlink because it needs neither admin rights nor
# Developer Mode.
#   Usage:  ./scripts/install.ps1              install/refresh
#           ./scripts/install.ps1 -Uninstall
[CmdletBinding()]
param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'

$repo   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$plugin = Join-Path $repo 'plugin'
$link   = Join-Path $HOME '.claude/skills/glimpse'

if ($Uninstall) {
    if (Test-Path $link) {
        (Get-Item $link).Delete()
        Write-Output "Removed $link"
    } else {
        Write-Output "No link at $link"
    }
    exit 0
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'dotnet not found — install the .NET 10 SDK first.'
}

Write-Output 'Building Glimpse.Capture...'
dotnet build (Join-Path $repo 'tools/Glimpse.Capture/Glimpse.Capture.csproj') -v quiet
if ($LASTEXITCODE -ne 0) { Write-Error 'Build failed.' }

# The wrapper cannot resolve an ancestor junction from batch, so hand it the repo path.
Set-Content -Path (Join-Path $plugin 'bin/glimpse.repo') -Value $repo -NoNewline -Encoding ascii

$skills = Join-Path $HOME '.claude/skills'
New-Item -ItemType Directory -Force -Path $skills | Out-Null

if (Test-Path $link) {
    $existing = Get-Item $link
    if (-not $existing.LinkType) {
        Write-Error "REFUSE: a real (non-link) entry exists at $link — remove it manually, then re-run."
    }
    $existing.Delete()
}

New-Item -ItemType Junction -Path $link -Target $plugin | Out-Null
Write-Output "Linked $link -> $plugin"
Write-Output ''
Write-Output "Done. Restart Claude Code (or run /reload-plugins) to load the 'glimpse' plugin."
Write-Output 'Verify (in a NEW session):  claude plugin list   and   where.exe glimpse'
```

- [ ] **Step 3: Gitignore the sidecar**

Append to `.gitignore`:
```gitignore
# Written by scripts/install.ps1 — machine-specific repo path for the Windows wrapper.
plugin/bin/glimpse.repo
```

- [ ] **Step 4: Make the CLI packable as a global tool**

Replace `tools/Glimpse.Capture/Glimpse.Capture.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <!-- Shared cross-platform distribution path, and the pre-built-artifact slot the
       marketplace plugin needs (it cannot build .NET itself). -->
  <PropertyGroup>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>glimpse</ToolCommandName>
    <PackageId>Glimpse.Capture</PackageId>
    <Version>0.1.0</Version>
    <Authors>Purin Tavilsup</Authors>
    <Description>Render any UI or diagram to a PNG so an agent can see it, critique it, and iterate.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageOutputPath>$(MSBuildThisFileDirectory)../../artifacts/nupkg</PackageOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Glimpse.Abstractions\Glimpse.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Glimpse.Core\Glimpse.Core.csproj" />
  </ItemGroup>

</Project>
```

Append to `.gitignore`:
```gitignore
artifacts/
```

- [ ] **Step 5: Verify the pack succeeds**

Run: `dotnet pack tools/Glimpse.Capture/Glimpse.Capture.csproj -c Release`
Expected: `Successfully created package ...artifacts/nupkg/Glimpse.Capture.0.1.0.nupkg`

- [ ] **Step 6: Verify the wrapper works through a real junction**

Run:
```bash
pwsh -File scripts/install.ps1
where.exe glimpse
```
Expected: the junction is created and reported. `where.exe glimpse` may print nothing until Claude Code restarts (it is Claude Code that puts the plugin `bin/` on `PATH`) — that is fine. Verify the wrapper resolves the repo correctly by invoking it through the junction directly:

```bash
cmd.exe /c "%USERPROFILE%\.claude\skills\glimpse\bin\glimpse.cmd" docs/diagrams/glimpse-architecture.mmd --name junction-check --no-manifest
```
Expected: `Status:   ok (WxH)`. If this prints a path-not-found for the DLL, the `glimpse.repo` sidecar was not written or not read.

- [ ] **Step 7: Verify uninstall is clean**

Run:
```bash
pwsh -File scripts/install.ps1 -Uninstall
ls ~/.claude/skills/
```
Expected: `Removed …/glimpse`, and the directory no longer lists `glimpse`. **Confirm the repo's `plugin/` directory still exists** — deleting a junction must not touch its target.

Then re-install so the environment is left working: `pwsh -File scripts/install.ps1`

- [ ] **Step 8: Commit**

```bash
git add plugin/bin/glimpse.cmd scripts/install.ps1 tools/Glimpse.Capture/Glimpse.Capture.csproj .gitignore
git commit -m "feat(install): Windows entrypoint and dotnet-tool packaging

glimpse.cmd mirrors the bash wrapper. install.ps1 uses a directory
junction rather than a symlink, so it needs neither admin rights nor
Developer Mode.

A junction is transparent to lexical '..' traversal, so the wrapper
cannot find the repo the way the bash version does with readlink --
install.ps1 writes the resolved path to a gitignored glimpse.repo
sidecar, with lexical traversal as the fallback for direct clone use.

PackAsTool adds the shared cross-platform distribution path and fills
the pre-built-artifact slot the marketplace plugin needs."
```

---

### Task 9: CI matrix

The macOS half cannot be executed from the Windows dev box, so it is the half most likely to break unnoticed. This is what makes "supports both" verified rather than claimed.

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: nothing
- Produces: nothing (verification only)

- [ ] **Step 1: Create the workflow**

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:

jobs:
  build-and-test:
    name: ${{ matrix.os }}
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, macos-latest]

    steps:
      - uses: actions/checkout@v5

      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore Glimpse.slnx

      - name: Build
        run: dotnet build Glimpse.slnx --no-restore -c Release

      # Renderer tools are deliberately NOT installed: the end-to-end gates skip when
      # their tool is absent, so CI covers compilation and all unit tests on both OSes.
      # Real-render verification stays manual (see the spec's Testing section).
      - name: Test
        run: dotnet test Glimpse.slnx --no-build -c Release --logger "console;verbosity=normal"
```

- [ ] **Step 2: Verify the workflow parses**

There is no local YAML linter in this environment, and GitHub's parser is the real check. Confirm the file is at least well-formed with the tool that is available:

Run: `pwsh -Command "ConvertFrom-Yaml (Get-Content .github/workflows/ci.yml -Raw)" 2>&1 | Select-Object -First 3`

If `ConvertFrom-Yaml` is unavailable (it ships with the `powershell-yaml` module, not PowerShell itself), skip this step — hand-check the indentation against the block above instead, and rely on GitHub's parse on push. Do **not** install a module just for this.

- [ ] **Step 3: Verify the Release configuration builds and tests locally**

CI uses `-c Release`, which this repo has never built. Prove it before pushing:

Run: `dotnet test Glimpse.slnx -c Release`
Expected: 0 failures. `TreatWarningsAsErrors=true` applies to Release too, so any config-specific warning surfaces here rather than in CI.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: build and test on windows-latest and macos-latest

The repo had no CI. Without a matrix, cross-platform support rots the
first time either side is touched -- and the macOS half cannot be run
from the Windows dev box, so it is the half most likely to break
unnoticed.

Renderer tools are not installed: the end-to-end gates skip when their
tool is absent, so this covers compilation and unit tests on both OSes
while real-render verification stays manual."
```

---

### Task 10: Documentation

**Files:**
- Modify: `README.md` (lines 21, 27–38, 57)
- Modify: `plugin/skills/glimpse/SKILL.md` (lines 3, 16, 20, 31–34)
- Modify: `plugin/skills/diagram-design/SKILL.md` (line 134)
- Modify: `STATUS.md` (items 7 and 8, plus the environment notes)

**Interfaces:**
- Consumes: everything above
- Produces: nothing

- [ ] **Step 1: Update the README renderer table and quick start**

In `README.md`, change the `app` row of the renderer table (line 21) from `live macOS window` / `screencapture` to:

```markdown
| live app window | `app` | macOS `screencapture` · Windows GDI (in-process) |
```

Replace the Quick start block (lines 29–38) so it does not imply macOS:

```bash
# Render a mermaid diagram (renderer inferred from .mmd)
dotnet run --project tools/Glimpse.Capture -- diagram.mmd --name my-diagram

# Explicit renderer, dark theme, custom size
dotnet run --project tools/Glimpse.Capture -- page.html --renderer web --theme dark --size 1440x900

# Screenshot a live app window by name (macOS and Windows)
dotnet run --project tools/Glimpse.Capture -- --renderer app --window "Chrome" --name app-shot

# List capturable windows and their ids
dotnet run --project tools/Glimpse.Capture -- --list-windows
```

Add a Platforms section immediately after the renderer table:

```markdown
## Platforms

Glimpse runs on **macOS and Windows**. All five renderers work on both; only the
underlying mechanism differs, and only for `app`:

| | macOS | Windows |
|---|---|---|
| Install | `./scripts/install.sh` (symlink) | `./scripts/install.ps1` (junction — no admin needed) |
| `app` capture | `screencapture` | in-process GDI (`PrintWindow`) |
| Permission needed | Screen Recording, for `app` | none |
| Diagram tools | `brew install graphviz d2` | `winget install Graphviz.Graphviz Terrastruct.d2` |

`mmdc` is `npm i -g @mermaid-js/mermaid-cli` on both. Linux is untested and unclaimed —
diagram renderers should work, `app` will not.
```

Update the `--window-id` flag description (line 57) to mention both: `Window id for the app renderer (CGWindowID on macOS, HWND on Windows)`.

- [ ] **Step 2: Update the glimpse skill**

In `plugin/skills/glimpse/SKILL.md`:

Line 3 — replace `live macOS app window` with `live app window`.

Line 16 — replace `**Live macOS app (autonomous — no human needed to pick a window):**` with `**Live app window (autonomous — no human needed to pick a window):**`.

Line 20 — the permission caveat is macOS-only. Replace it with:

```markdown
   - **macOS permission caveat:** capturing another app needs Screen Recording permission
     for the terminal. If it's denied, `screencapture` may still write a PNG showing only
     the desktop/wallpaper (no error) — so when you Read the PNG, confirm the app window is
     actually visible. If it's just desktop, grant: System Settings → Privacy & Security →
     Screen Recording → enable your terminal, then re-run.
   - **Windows caveat:** no permission is needed, but always Read the PNG and confirm it
     shows the window. A solid-black image means the window refused `PrintWindow` and the
     `single-color-frame` warning should have fired; a full-desktop image means the window
     lookup fell back (check for a `fullscreen-fallback:` warning).
```

Lines 31–34 — make the tool hints per-platform:

```markdown
- mermaid → `npm i -g @mermaid-js/mermaid-cli` (both platforms)
- graphviz → macOS `brew install graphviz` · Windows `winget install Graphviz.Graphviz`
  (then add its `bin\` to PATH — winget does not)
- d2 → macOS `brew install d2` · Windows `winget install Terrastruct.d2`
- web → Google Chrome (found via the app bundle on macOS, the App Paths registry key on Windows)
- app → macOS `screencapture` (+ Screen Recording permission) · Windows in-process GDI (no permission)
```

- [ ] **Step 3: Update the diagram-design skill**

In `plugin/skills/diagram-design/SKILL.md` line 134, replace `**D2** (`brew install d2`)` with:

```markdown
- **D2** (macOS `brew install d2`, Windows `winget install Terrastruct.d2`) only for icon-cloud architecture. If D2 is missing, tell the
```

- [ ] **Step 4: Update STATUS.md**

Replace deferred item 7 with a done entry, and narrow item 8 to what actually remains:

```markdown
7. ~~**Cross-platform (Windows)**~~ — ✅ **DONE.** Full parity, single `net10.0` target.
   `ToolLocator` resolves tools by managed PATH scan (honouring `PATHEXT` but only for
   extensions `CreateProcess` can launch — npm ships both `mmdc` and `mmdc.cmd`, and only
   the latter starts). New `IAppCapturer` seam: macOS keeps `screencapture` unchanged,
   Windows captures in-process via `PrintWindow` + `PW_RENDERFULLCONTENT` into a top-down
   DIB, encoded with SkiaSharp (already a `Glimpse.Core` dependency). `WindowSelector`
   untouched — `EnumWindows` Z-order and a tool-window→layer-1 mapping satisfy its existing
   contract. `WindowId` widened to `long` for HWND. Windows entrypoints: `plugin/bin/glimpse.cmd`
   + `scripts/install.ps1` (junction, no admin). `PackAsTool` added. **CI matrix
   (windows-latest × macos-latest) now guards both halves.** Spec/plan:
   `docs/superpowers/{specs,plans}/2026-08-01-cross-platform-windows*`.
8. **🔭 Option C — publish as a marketplace plugin (later, when stable + sharing).** Add a
   `marketplace.json` so anyone can `/plugin marketplace add purin-tavilsup/Glimpse` →
   `/plugin install glimpse`. The blocker item 7 used to share is gone — `PackAsTool` now
   produces the pre-built artifact the marketplace needs (it cannot build .NET itself).
   Remaining: decide bundled-binary vs `dotnet tool` acquisition, and publish. Detail in
   the distribution spec §9.
```

Update the environment notes to cover both machines:

```markdown
## Environment notes
- **macOS:** `mmdc` (mermaid-cli) + `d2` (`brew install d2`) installed. Chrome present for `web`.
- **Windows:** `mmdc` 11.16.0 (npm), `d2` v0.7.1 + `dot` 15.1.0 (winget — Graphviz's `bin\`
  was added to the user PATH by hand, winget does not do it). Chrome + Edge present.
- Python 3.13 installed (`/opt/homebrew/bin/python3.13`) — needed for the skill-creator
  eval viewer (`generate_review.py`, requires 3.10+).
- `gh` has two accounts; this repo's git identity + push routing use `purin-tavilsup`.
  The repo now carries a **local** git identity override, so a fresh clone must re-set it.
```

Also update the "Current state" heading line to note the third half shipped, and the test count — run the suite first and use the real number.

- [ ] **Step 5: Verify every documented command actually works**

Run each command quoted in the README Quick start and the skill, and confirm the output matches what the docs claim:

```bash
dotnet run --project tools/Glimpse.Capture -- docs/diagrams/glimpse-architecture.mmd --name doc-check --no-manifest
dotnet run --project tools/Glimpse.Capture -- --list-windows
dotnet run --project tools/Glimpse.Capture -- --renderer app --window "Chrome" --name doc-app --no-manifest
```
Expected: all three succeed. Do not document a flag or path you have not just run.

- [ ] **Step 6: Verify the diagram templates still render**

Run: `bash scripts/check-diagram-templates.sh`
Expected: all 6 templates render clean. If the script fails for a bash/Windows-path reason rather than a render reason, note it — porting that script is **not** in this plan's scope, and saying so is better than a silent skip.

- [ ] **Step 7: Commit**

```bash
git add README.md plugin/skills/glimpse/SKILL.md plugin/skills/diagram-design/SKILL.md STATUS.md
git commit -m "docs: Glimpse supports macOS and Windows

README gains a Platforms table; the renderer table no longer calls app
macOS-only. The glimpse skill's Screen Recording caveat is now labelled
macOS-only and gains a Windows counterpart (black PNG = PrintWindow
refused, full-desktop PNG = window lookup fell back). Tool install hints
are per-platform throughout, since a hint naming the wrong package
manager sends the reader somewhere that cannot work.

STATUS item 7 closes; item 8 narrows -- PackAsTool now fills the
pre-built-artifact slot the marketplace was waiting on."
```

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:

| Spec section | Task |
|---|---|
| §3.1 managed PATH scan, PATHEXT, 3-way Chrome, platform hints | 2 |
| §3.2 `IAppCapturer` seam, Mac + Windows impls, `WindowSelector` untouched | 4, 5, 6 |
| §3.3 SkiaSharp encode, top-down DIB | 6 |
| §3.4 `WindowId` → `long` (all three declarations) | 3 |
| §3.5 `glimpse.cmd`, `install.ps1`, `PackAsTool` | 8 |
| §3.6 `.gitattributes` | 1 |
| §3.7 CI matrix | 9 |
| §4 component list | 1–10 |
| §5 error/edge cases | see below |
| §6 testing | tests in 2–7; manual verification in 2, 7, 8, 10 |
| §7 build order | task order (spec step 2 = Task 2, etc.) |

Spec §5 edge cases, each with a home: `app` on Linux → Task 7 (`PlatformSupport` returns null); extensionless `mmdc` → Task 2 test; Chrome absent → Task 2 (`WindowsChromeFromWellKnownPaths` + hint); `PrintWindow` black → Task 6 (BitBlt fallback + single-color test); minimised window → Task 6 (`PrintWindow` renders without restoring; no focus stealing); junction over a real directory → Task 8 Step 2 refusal; `--list-windows` on an unsupported OS → Task 7.

**Two spec items deliberately NOT implemented, called out rather than silently dropped:**
- **Chrome `--user-data-dir`** (§5, "headless Chrome while Chrome is already running"). The `web` renderer's args live in `BuiltInRenderers` and a temp dir cannot be expressed as a static placeholder — `RenderCommandBuilder` substitutes only `{source} {out} {width} {height} {theme} {windowid}`. Adding a `{tempdir}` placeholder is a change to the renderer contract that touches macOS equally, so it belongs in its own change. **Verify during Task 2 Step 6 whether a `web` render actually fails with Chrome running**; if it does, that is a follow-up ticket, not a silent gap.
- **`scripts/check-diagram-templates.sh`** is bash and stays bash (Task 10 Step 6 records the outcome rather than porting it). Git Bash is present on the dev box, so it should run; a `.ps1` port is out of scope.

**2. Placeholder scan** — no `TBD`, no "add error handling", no "similar to Task N", no "write tests for the above". Every code step contains complete code. Every test step contains the actual assertions.

**3. Type consistency** — checked across tasks:
- `WindowInfo.WindowId` is `long` from Task 3 onward; Task 5 constructs it with `hwnd.ToInt64()`; Task 5's `WindowSelector` fixtures assert `51L`/`61L`, matching Task 3's updated `Win(long id, …)` helper.
- `RenderRequest.WindowId` is `long?` from Task 3; Task 4's `MacAppCapturer` branches on `request.WindowId is null`; Task 6 destructures `request.WindowId is { } id` into `new IntPtr(id)`; Task 7 passes `long? windowId`.
- `RenderOutcomes.From(string, bool, string)` is defined in Task 4 and consumed in Task 6 with the same three arguments in the same order.
- `IAppCapturer.CaptureAsync(RenderRequest)` — single parameter — is defined in Task 4 and implemented identically in Task 6; Task 7 calls it with one argument.
- `MacAppCapturer(IProcessRunner)` takes a runner; `WindowsAppCapturer()` takes none. Task 7's `PlatformSupport.AppCapturer(IProcessRunner runner)` supplies the runner only to the Mac implementation, and Task 7's test constructs it with `new ProcessRunner()`.
- `ToolSearchEnvironment(string, string?, bool)` is defined in Task 2 and used only within Task 2.
- `GlimpseCaptureException` is **reused** from `ScreenCapture.cs:5`, not redeclared (flagged explicitly in Task 6 Step 3).
- `MacAppCapturer.FullScreenSpec` is asserted in Task 4's test and is the same symbol the implementation exposes.

**One known duplication accepted:** `WindowsWindowFinder` (Task 5) and `WindowsAppCapturer` (Task 6) each declare a private `Rect` struct and a `DwmGetWindowAttribute` import. Extracting a shared interop class after the second occurrence would be the DRY move, but they live in different files with different lifetimes and P/Invoke declarations are conventionally local. If a **third** consumer appears, extract then.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-01-cross-platform-windows.md`.
