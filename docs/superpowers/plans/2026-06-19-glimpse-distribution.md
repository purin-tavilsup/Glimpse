# Glimpse Distribution (skills-directory plugin) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `glimpse` CLI and the `glimpse` + `diagram-design` skills usable from any repo on this machine (incl. Recorder) by packaging them as a symlinked Claude Code skills-directory plugin — no marketplace, repo stays canonical.

**Architecture:** A `plugin/` dir in this repo holds `.claude-plugin/plugin.json`, a `bin/glimpse` wrapper (runs the in-repo built CLI DLL), and the two skills moved from `.claude/skills/`. `scripts/install.sh` symlinks `~/.claude/skills/glimpse` → `plugin/`, so Claude Code auto-loads it globally and puts `bin/glimpse` on PATH. The D2 icon check folds into the CLI (`glimpse --check-icons`) so skills depend only on the `glimpse` command.

**Tech Stack:** C# / .NET 10, xUnit, bash, Claude Code plugins.

## Global Constraints

- Target framework `net10.0`; `Nullable` + `ImplicitUsings` enabled; `TreatWarningsAsErrors=true` — zero warnings.
- Central package management (versions in `Directory.Packages.props`; `<PackageReference>` with no `Version`). This plan needs **no new packages** (`System.Net.Http` is in-box).
- file-scoped namespaces; PascalCase public; records for data; xUnit naming `Method_Condition_ShouldExpectedBehavior`; no `#region`.
- `Glimpse.Core` stays Avalonia-free.
- macOS-only scope. **Do not use `readlink -f`** (unsupported on BSD/macOS) — use the portable symlink-resolution loop shown in Task 2.
- Plugin name is `glimpse`; the symlink is `~/.claude/skills/glimpse` → `<repo>/plugin`.

---

## File Structure

**Created:**
- `src/Glimpse.Core/D2IconCheck.cs` — pure extraction of `icon:` URLs from D2 source.
- `tests/Glimpse.Core.Tests/D2IconCheckTests.cs`
- `plugin/.claude-plugin/plugin.json` — plugin manifest.
- `plugin/bin/glimpse` — CLI wrapper (portable symlink resolution → built DLL).
- `scripts/install.sh` — symlink installer (+ `--uninstall`).

**Moved (via `git mv`):**
- `.claude/skills/glimpse/` → `plugin/skills/glimpse/`
- `.claude/skills/diagram-design/` → `plugin/skills/diagram-design/`

**Modified:**
- `tools/Glimpse.Capture/CaptureOptions.cs` — add `CheckIcons` flag.
- `tools/Glimpse.Capture/Program.cs` — `--check-icons` mode.
- `scripts/check-diagram-templates.sh` — use `plugin/bin/glimpse` + `--check-icons`, new template path.
- `plugin/skills/glimpse/SKILL.md`, `plugin/skills/diagram-design/SKILL.md`, `plugin/skills/diagram-design/reference.md` — de-repo-path (use `glimpse`).

**Removed:**
- `scripts/check-d2-icons.sh` (folded into `glimpse --check-icons`).
- `scripts/glimpse` (superseded by `plugin/bin/glimpse`).

---

### Task 1: `--check-icons` CLI mode (folds in check-d2-icons.sh)

**Files:**
- Create: `src/Glimpse.Core/D2IconCheck.cs`
- Test: `tests/Glimpse.Core.Tests/D2IconCheckTests.cs`
- Modify: `tools/Glimpse.Capture/CaptureOptions.cs`, `tools/Glimpse.Capture/Program.cs`
- Modify: `scripts/check-diagram-templates.sh`
- Remove: `scripts/check-d2-icons.sh`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `static class D2IconCheck { static IReadOnlyList<string> ExtractIconUrls(string d2Source); }`
  - `CaptureOptions` gains a `bool CheckIcons` (last positional record param).
  - CLI: `glimpse --check-icons <file>.d2` → prints `ok <code> <url>` / `BAD <code> <url>`; exit `0` all-resolve-or-none, `1` any fail, `2` bad usage.

- [ ] **Step 1: Write the failing extraction test**

Create `tests/Glimpse.Core.Tests/D2IconCheckTests.cs`:

```csharp
using Glimpse.Core;
using Xunit;

namespace Glimpse.Core.Tests;

public class D2IconCheckTests
{
    [Fact]
    public void ExtractIconUrls_FindsAllHttpUrls()
    {
        var src = "a: A {\n  shape: image\n  icon: https://x/a.svg\n}\n" +
                  "b: B { shape: image; icon: https://y/b%20c.svg }\n" +
                  "c: plain box";

        var urls = D2IconCheck.ExtractIconUrls(src);

        Assert.Equal(new[] { "https://x/a.svg", "https://y/b%20c.svg" }, urls);
    }

    [Fact]
    public void ExtractIconUrls_WithNoIcons_ReturnsEmpty()
    {
        Assert.Empty(D2IconCheck.ExtractIconUrls("x: hello\nx -> y: label"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter D2IconCheckTests`
Expected: FAIL — `D2IconCheck` does not exist.

- [ ] **Step 3: Implement `D2IconCheck`**

Create `src/Glimpse.Core/D2IconCheck.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter D2IconCheckTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Add the `--check-icons` flag to CaptureOptions**

In `tools/Glimpse.Capture/CaptureOptions.cs`, add the record param after `ListWindows`:

```csharp
    bool ListWindows,
    bool CheckIcons)
```

Add the local + parse case (near the other bools):

```csharp
        var checkIcons = false;
```
```csharp
                case "--check-icons": checkIcons = true; break;
```

And pass it in the constructor call (append after `listWindows`):

```csharp
        return new CaptureOptions(source, renderer, resolvedName, outDir, width, height, theme,
            hasWindowId ? windowId : null, window, title, prune, noManifest, listWindows, checkIcons);
```

- [ ] **Step 6: Add a CaptureOptions parse test**

Add to `tests/Glimpse.Capture.Tests/CaptureOptionsTests.cs`:

```csharp
    [Fact]
    public void Parse_WithCheckIcons_ShouldSetFlagAndKeepSource()
    {
        var options = CaptureOptions.Parse(["diagram.d2", "--check-icons"]);

        Assert.True(options.CheckIcons);
        Assert.Equal("diagram.d2", options.Source);
    }
```

- [ ] **Step 7: Implement the `--check-icons` mode in Program.cs**

In `tools/Glimpse.Capture/Program.cs`, immediately after the `if (options.ListWindows) { … }` block, add:

```csharp
if (options.CheckIcons)
{
    if (options.Source is null)
    {
        Console.Error.WriteLine("--check-icons requires a .d2 file path.");
        return 2;
    }
    if (!File.Exists(options.Source))
    {
        Console.Error.WriteLine($"not found: {options.Source}");
        return 2;
    }

    var urls = D2IconCheck.ExtractIconUrls(File.ReadAllText(options.Source));
    if (urls.Count == 0)
    {
        Console.WriteLine($"No icon: URLs in {options.Source} (nothing to check).");
        return 0;
    }

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var anyFailed = false;
    foreach (var url in urls)
    {
        int code;
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            code = (int)resp.StatusCode;
        }
        catch
        {
            code = 0;
        }

        if (code == 200)
        {
            Console.WriteLine($"ok   {code}  {url}");
        }
        else
        {
            Console.WriteLine($"BAD  {code}  {url}");
            anyFailed = true;
        }
    }

    if (anyFailed)
    {
        Console.Error.WriteLine("Broken icon URL(s) — fix them or those icons will silently vanish.");
        return 1;
    }
    Console.WriteLine("All icon URLs resolve.");
    return 0;
}
```

- [ ] **Step 8: Run unit tests (Core + Capture)**

Run: `dotnet test tests/Glimpse.Core.Tests/Glimpse.Core.Tests.csproj --filter D2IconCheckTests && dotnet test tests/Glimpse.Capture.Tests/Glimpse.Capture.Tests.csproj --filter CaptureOptionsTests`
Expected: PASS.

- [ ] **Step 9: Verify the mode end-to-end + repoint the smoke test, remove old script**

Run (good icons, exit 0):
`dotnet run --project tools/Glimpse.Capture -- .claude/skills/diagram-design/templates/cloud.d2 --check-icons`
Expected: five `ok   200  …` lines + `All icon URLs resolve.`

In `scripts/check-diagram-templates.sh`, replace the icon-check line
`! "$ROOT/scripts/check-d2-icons.sh" "$f"` with
`! "$ROOT/scripts/glimpse" --check-icons "$f"`.

Then: `rm scripts/check-d2-icons.sh` and run `./scripts/check-diagram-templates.sh`
Expected: all 6 templates `ok`, "All diagram-design templates render cleanly."

- [ ] **Step 10: Commit**

```bash
git add src/Glimpse.Core/D2IconCheck.cs tests/Glimpse.Core.Tests/D2IconCheckTests.cs \
  tools/Glimpse.Capture/CaptureOptions.cs tools/Glimpse.Capture/Program.cs \
  tests/Glimpse.Capture.Tests/CaptureOptionsTests.cs scripts/check-diagram-templates.sh
git rm scripts/check-d2-icons.sh
git commit -m "feat(cli): glimpse --check-icons (fold in check-d2-icons.sh)"
```

---

### Task 2: Plugin scaffold + move skills

**Files:**
- Create: `plugin/.claude-plugin/plugin.json`, `plugin/bin/glimpse`
- Move: `.claude/skills/glimpse/` → `plugin/skills/glimpse/`; `.claude/skills/diagram-design/` → `plugin/skills/diagram-design/`
- Modify: `scripts/check-diagram-templates.sh`
- Remove: `scripts/glimpse`

**Interfaces:**
- Consumes: the built `Glimpse.Capture` DLL.
- Produces: `plugin/bin/glimpse` — a CLI on PATH once installed; same args as the CLI (incl. `--check-icons`). Plugin name `glimpse`.

- [ ] **Step 1: Create the plugin manifest**

Create `plugin/.claude-plugin/plugin.json`:

```json
{
  "name": "glimpse",
  "description": "Render any UI or diagram to a PNG and design clear diagrams — the glimpse render-loop CLI plus the diagram-design skill.",
  "version": "0.1.0",
  "author": { "name": "Purin Tavilsup" }
}
```

- [ ] **Step 2: Create the `bin/glimpse` wrapper (portable symlink resolution)**

Create `plugin/bin/glimpse`:

```bash
#!/usr/bin/env bash
# Glimpse CLI on PATH (via the plugin's bin/). Resolves this script's REAL location
# through the install symlink — without `readlink -f` (unsupported on macOS) — then runs
# the in-repo built DLL so it's always current. Builds once if the DLL is missing.
set -euo pipefail

src="${BASH_SOURCE[0]}"
while [ -L "$src" ]; do
    dir="$(cd -P "$(dirname "$src")" && pwd)"
    src="$(readlink "$src")"
    [[ $src != /* ]] && src="$dir/$src"
done
bin_dir="$(cd -P "$(dirname "$src")" && pwd)"
repo="$(cd "$bin_dir/../.." && pwd)"   # plugin/bin -> plugin -> repo
dll="$repo/tools/Glimpse.Capture/bin/Debug/net10.0/Glimpse.Capture.dll"

[ -f "$dll" ] || dotnet build "$repo/tools/Glimpse.Capture/Glimpse.Capture.csproj" -v quiet >&2
exec dotnet "$dll" "$@"
```

- [ ] **Step 3: Make it executable and smoke-test it directly**

Run:
```bash
chmod +x plugin/bin/glimpse
./plugin/bin/glimpse .claude/skills/diagram-design/templates/state.mmd --name t --out /tmp/glimpse-bin-test --no-manifest
```
Expected: `Status:   ok (...)` (the wrapper builds if needed, then renders).

- [ ] **Step 4: Move the two skills into the plugin (preserve history)**

Run:
```bash
mkdir -p plugin/skills
git mv .claude/skills/glimpse plugin/skills/glimpse
git mv .claude/skills/diagram-design plugin/skills/diagram-design
```

- [ ] **Step 5: Repoint the smoke test to the new locations + remove the old wrapper**

In `scripts/check-diagram-templates.sh`:
- change `TPL="$ROOT/.claude/skills/diagram-design/templates"` → `TPL="$ROOT/plugin/skills/diagram-design/templates"`
- change both `"$ROOT/scripts/glimpse"` references (render + `--check-icons`) → `"$ROOT/plugin/bin/glimpse"`

Then: `rm scripts/glimpse`

- [ ] **Step 6: Run the smoke test from the new layout**

Run: `./scripts/check-diagram-templates.sh`
Expected: all 6 templates `ok`, "All diagram-design templates render cleanly."

- [ ] **Step 7: Validate the plugin.json is well-formed**

Run: `python3 -c "import json; json.load(open('plugin/.claude-plugin/plugin.json')); print('valid')"`
Expected: `valid`

- [ ] **Step 8: Commit**

```bash
git add plugin/ scripts/check-diagram-templates.sh
git rm scripts/glimpse
git commit -m "feat(plugin): scaffold glimpse plugin (manifest + bin/glimpse) and move skills in"
```

---

### Task 3: De-repo-path the skills

**Files:**
- Modify: `plugin/skills/glimpse/SKILL.md`
- Modify: `plugin/skills/diagram-design/SKILL.md`, `plugin/skills/diagram-design/reference.md`

**Interfaces:**
- Consumes: the `glimpse` command (Task 2) on PATH.
- Produces: skills that reference only the `glimpse` command — no repo-relative paths.

- [ ] **Step 1: Update the `glimpse` skill render command**

In `plugin/skills/glimpse/SKILL.md`, replace the render bullet block. Find:

```
1. **Render:** `./scripts/glimpse <source> [--renderer NAME] [--name NAME] [--theme dark] [--size WxH]`
   (fast wrapper; equivalent to `dotnet run --project tools/Glimpse.Capture -- <source> …` but skips the per-call rebuild)
```

Replace with:

```
1. **Render:** `glimpse <source> [--renderer NAME] [--name NAME] [--theme dark] [--size WxH]`
```

- [ ] **Step 2: Update the diagram-design rendering commands**

In `plugin/skills/diagram-design/SKILL.md`, find the Rendering code block:

```
# Fast wrapper (preferred — runs the built CLI directly, no rebuild each call):
./scripts/glimpse <file>.mmd --name <name>     # mermaid: sequence/state/ER/flowchart/layered
./scripts/glimpse <file>.d2  --name <name>     # D2 icon-cloud (renderer inferred from .d2)

# Equivalent, slower (rebuilds every call):
dotnet run --project tools/Glimpse.Capture -- <file>.mmd --name <name>
```

Replace with:

```
glimpse <file>.mmd --name <name>     # mermaid: sequence/state/ER/flowchart/layered
glimpse <file>.d2  --name <name>     # D2 icon-cloud (renderer inferred from .d2)
```

- [ ] **Step 3: Update the exemplar `--out` path**

In `plugin/skills/diagram-design/SKILL.md`, replace
`--out .claude/skills/diagram-design/examples` with
`--out plugin/skills/diagram-design/examples` (this path is for maintaining the skill in this repo; in normal use the agent omits `--out`).

- [ ] **Step 4: Update the D2 icon-check command (SKILL.md + reference.md)**

In `plugin/skills/diagram-design/SKILL.md` and `plugin/skills/diagram-design/reference.md`, replace every
`./scripts/check-d2-icons.sh <file>.d2` with `glimpse --check-icons <file>.d2`.

- [ ] **Step 5: Verify no repo-relative invocations remain**

Run:
```bash
grep -rnE '\./scripts/|dotnet run --project tools/Glimpse\.Capture' plugin/skills/ || echo "CLEAN"
```
Expected: `CLEAN` (no matches).

- [ ] **Step 6: Commit**

```bash
git add plugin/skills/glimpse/SKILL.md plugin/skills/diagram-design/SKILL.md plugin/skills/diagram-design/reference.md
git commit -m "docs(skills): use the global glimpse command (de-repo-path)"
```

---

### Task 4: `scripts/install.sh`

**Files:**
- Create: `scripts/install.sh`

**Interfaces:**
- Consumes: `plugin/` (Task 2).
- Produces: a symlink `~/.claude/skills/glimpse` → `<repo>/plugin`, and a built CLI DLL.

- [ ] **Step 1: Write the installer**

Create `scripts/install.sh`:

```bash
#!/usr/bin/env bash
# Install the Glimpse plugin (CLI + skills) for use in any repo on this machine.
# Symlinks ~/.claude/skills/glimpse -> <repo>/plugin (repo stays canonical, edits live).
# Usage:  ./scripts/install.sh            install/refresh
#         ./scripts/install.sh --uninstall
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
plugin="$repo/plugin"
link="$HOME/.claude/skills/glimpse"

if [ "${1:-}" = "--uninstall" ]; then
    if [ -L "$link" ]; then rm "$link"; echo "Removed $link"; else echo "No symlink at $link"; fi
    exit 0
fi

command -v dotnet >/dev/null 2>&1 || { echo "dotnet not found — install the .NET 10 SDK first." >&2; exit 1; }

echo "Building Glimpse.Capture..."
dotnet build "$repo/tools/Glimpse.Capture/Glimpse.Capture.csproj" -v quiet
chmod +x "$plugin/bin/glimpse"

mkdir -p "$HOME/.claude/skills"
if [ -e "$link" ] && [ ! -L "$link" ]; then
    echo "REFUSE: a real (non-symlink) entry exists at $link — remove it manually, then re-run." >&2
    exit 1
fi
rm -f "$link"
ln -s "$plugin" "$link"
echo "Linked $link -> $plugin"
echo
echo "Done. Restart Claude Code (or run /reload-plugins) to load the 'glimpse' plugin."
echo "Verify (in a NEW session):  claude plugin list | grep glimpse   and   which glimpse"
```

- [ ] **Step 2: Make it executable and run it**

Run:
```bash
chmod +x scripts/install.sh
./scripts/install.sh
```
Expected: "Building…", "Linked …/.claude/skills/glimpse -> …/plugin", "Done."

- [ ] **Step 3: Verify the plugin is discovered**

Run: `claude plugin list | grep -A1 -i "skills-directory\|glimpse@skills-dir"`
Expected: shows `glimpse@skills-dir` with `Path: ~/.claude/skills/glimpse`.

- [ ] **Step 4: Verify `--uninstall`, then reinstall (leave it installed)**

Run:
```bash
./scripts/install.sh --uninstall
claude plugin list | grep -i "glimpse@skills-dir" || echo "gone-as-expected"
./scripts/install.sh
```
Expected: `Removed …`, `gone-as-expected`, then re-linked.

- [ ] **Step 5: Commit**

```bash
git add scripts/install.sh
git commit -m "feat(install): scripts/install.sh symlinks the glimpse plugin globally"
```

---

### Task 5: Cross-repo acceptance (the real proof)

**Files:** none (verification only).

**Interfaces:** Consumes everything above.

This task confirms the whole point: the CLI + skills work from a *different* repo (Recorder). `bin/` lands on PATH only in a session that loaded the plugin, so use a fresh headless `claude -p` session for the PATH-dependent checks (mirrors the spike).

- [ ] **Step 1: Confirm `glimpse` resolves on PATH from the Recorder repo (fresh session)**

Run:
```bash
cd ~/dev/Recorder 2>/dev/null && claude -p "Run exactly this and report only stdout: which glimpse || echo NOTFOUND" --dangerously-skip-permissions
```
Expected: a path ending in `/.claude/skills/glimpse/bin/glimpse` (NOT `NOTFOUND`).

- [ ] **Step 2: Render a diagram from the Recorder repo**

Run:
```bash
printf 'flowchart TD\n  A[Recorder] --> B[Glimpse]\n' > /tmp/recorder-smoke.mmd
cd ~/dev/Recorder && claude -p "Run exactly this and report only stdout: glimpse /tmp/recorder-smoke.mmd --name recorder-smoke --out /tmp/recorder-smoke --no-manifest" --dangerously-skip-permissions
ls -la /tmp/recorder-smoke/recorder-smoke.png
```
Expected: CLI prints `Status:   ok (...)`; the PNG exists and is non-trivial in size.

- [ ] **Step 3: Confirm the skills are available in Recorder**

Run: `cd ~/dev/Recorder && claude -p "List your available skills whose name contains 'glimpse' or 'diagram'. Output just the names." --dangerously-skip-permissions`
Expected: mentions the `glimpse` and `diagram-design` skills (namespaced `glimpse:…`).

- [ ] **Step 4: (Optional, manual) live-window capture of Recorder**

Only if the Recorder app is running and Screen Recording permission is granted for the terminal:
```bash
cd ~/dev/Recorder && glimpse --renderer app --window "Recorder" --out /tmp/recorder-shot --no-manifest
```
Expected: a `Window: Recorder …` line and a PNG of the live app. (If permission is denied, the PNG shows only desktop — grant permission and retry. If Recorder isn't running, skip.)

- [ ] **Step 5: Record the result**

Update `STATUS.md` item 6 to mark distribution DONE (installed + verified from Recorder), then commit:
```bash
git add STATUS.md
git commit -m "docs: distribution verified — glimpse + skills usable from Recorder"
```

---

## Self-Review

**Spec coverage:**
- §3 skills-directory plugin, symlinked, bin/ carries CLI → Tasks 2 + 4. ✓
- §3 fold check-icons into CLI → Task 1. ✓
- §4.1 plugin layout + move skills → Task 2. ✓
- §4.2 plugin.json → Task 2. ✓
- §4.3 bin/glimpse wrapper (portable resolution — fixed from spec's `readlink -f`) → Task 2. ✓
- §4.4 `--check-icons` → Task 1. ✓
- §4.5 skill de-repo-pathing → Task 3. ✓
- §4.6 install.sh (+ uninstall, refuse-real-dir) → Task 4. ✓
- §4.7 smoke test repoint + remove scripts/glimpse → Task 2. ✓
- §5/§7 Recorder acceptance + symlink-load verification → Task 5 (load-bearing assumption already spike-validated). ✓

**Placeholder scan:** none — all steps have concrete code/commands.

**Type consistency:** `CaptureOptions.CheckIcons` (Task 1) appended consistently in record decl, Parse locals, and constructor call; `D2IconCheck.ExtractIconUrls` used identically in test, Program.cs, and the wrapper path. Plugin name `glimpse` and symlink `~/.claude/skills/glimpse` consistent across Tasks 2/4/5.

**Known carry-over:** the copy-fallback (if symlinks ever stop being honored) is documented in the spec §7; not built here because the spike validated symlinks work.
