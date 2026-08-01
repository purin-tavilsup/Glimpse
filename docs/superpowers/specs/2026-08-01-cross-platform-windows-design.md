# Cross-Platform Support (macOS + Windows) — Design

> Status: **Design / spec** (brainstormed + approved 2026-08-01)
> Author: Pond + Claude
> Goal: Glimpse works fully on **both macOS and Windows** — every renderer, the live-window
> `app` capture included — from one single-target codebase, with CI proving it.
>
> Closes STATUS.md deferred item **7** ("Cross-platform (Windows) — come back later") and
> fills the pre-built-binary slot that item **8** (marketplace) depends on.

## 1. Problem & Motivation

Glimpse is macOS-only by accident of where it was written, not by design. The harness
core is already platform-neutral — the macOS assumptions are concentrated in four places:

| # | Where | The assumption |
|---|-------|----------------|
| 1 | `ToolLocator.cs:32` | Shells out to hardcoded `/usr/bin/which`; Chrome hardcoded to `/Applications/…`; every install hint says `brew` |
| 2 | `app` renderer | `screencapture` (a macOS-only binary) + `MacWindowFinder` (CoreGraphics P/Invoke) |
| 3 | Entrypoints | `plugin/bin/glimpse` and `scripts/install.sh` are bash; install does `ln -s` |
| 4 | Docs | README renderer table says "live **macOS** window"; both SKILL.md files give `brew`-only hints |

**Measured baseline on Windows 11 (2026-08-01), before any change:**

- `dotnet build Glimpse.slnx` → **succeeds, 0 warnings, 0 errors**. The port is much
  smaller than it looks; nothing structural is wrong.
- `dotnet test` → **7 failures**, and *all seven trace to item 1 above*:
  - 6 direct (`ToolLocatorTests` ×2, `RenderEngineTests` ×4) — `Win32Exception` from `/usr/bin/which`
  - 1 indirect (`EndToEndGateTests`) — its "skip if `mmdc` is missing" guard routes through the
    same `ToolLocator.Resolve()`, so it cannot even *skip* correctly
- `Glimpse.Avalonia.Tests` → **13 passed / 2 skipped**. The headless Avalonia engine is
  already cross-platform and needs no work.

So item 1 is a single-file fix that clears 7 of 7 failures, and item 2 is the only part
requiring genuinely new code.

## 2. Goals / Non-Goals

### Goals
- All five renderers (`mermaid`, `graphviz`, `d2`, `web`, `app`) work on **both** OSes.
- **Live-window capture on Windows** — `--window "Recorder"`, `--list-windows`, `--window-id`
  behave as they do on macOS.
- **One target framework** (`net10.0`). No multi-targeting, no new projects, no conditional
  `ProjectReference`s.
- `glimpse` on `PATH` on Windows, plus a `dotnet tool` package as the shared distribution path.
- **No behaviour change on macOS.** The existing `screencapture` path stays byte-for-byte.
- CI proves both platforms on every push.

### Non-Goals (explicit)
- **Linux.** `ToolLocator` will resolve correctly there as a free side effect of the PATH
  rewrite, and diagram renderers should work, but Linux is untested and unclaimed. The
  `app` renderer will hard-fail there with a clear message.
- **Interactive capture** (`CaptureMode.Interactive`, macOS `screencapture -i`). Unused by
  the CLI today; no Windows equivalent is being built.
- **Retina/HiDPI normalisation between platforms.** Each OS captures at its own native
  pixel density. Making a mac and a Windows capture of the same app pixel-identical is
  out of scope.
- **Signing / notarising** any produced binary.

## 3. Approach & Key Decisions

### 3.1 Resolve tools in managed code, not by shelling out

Replace the `/usr/bin/which` subprocess with a **pure managed PATH scan**. Better than
swapping in `where.exe`: no process spawn, faster, and unit-testable without a real binary.

**This was validated empirically on 2026-08-01 and the result changed the design.**
`npm i -g @mermaid-js/mermaid-cli` installs *two* entries side by side:

```
C:\Users\purin\AppData\Roaming\npm\mmdc        <- bash script, no extension
C:\Users\purin\AppData\Roaming\npm\mmdc.cmd    <- Windows shim
```

Tested through `System.Diagnostics.Process` with `UseShellExecute = false`:

| Target | Result |
|--------|--------|
| `mmdc.cmd` | ✅ exit 0, prints `11.16.0` — **`.cmd` runs directly; no `cmd.exe /c` wrapper needed** |
| `mmdc` (extensionless) | ❌ throws `Win32Exception`: *"The specified executable is not a valid application for this OS platform"* |

A naive first-name-wins PATH scan therefore finds the **broken** one. The rule:

> On Windows, only accept candidates whose extension is one `CreateProcess` can launch —
> `.com`, `.exe`, `.bat`, `.cmd` — in `PATHEXT` order, and **never** accept an extensionless
> match. On macOS/Linux, accept the extensionless file if it is executable.

Note this is a *subset* of `PATHEXT`, which on this machine also lists `.VBS`, `.JS`,
`.MSC`, `.PY` — none of which `CreateProcess` can start directly. Using raw `PATHEXT`
would reintroduce the same class of failure.

**Chrome** is not on `PATH` on any platform and keeps a special case, now three-way:

- macOS — `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`
- Windows — the `App Paths` registry key
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe`
  (verified present on the dev box), falling back to `%ProgramFiles%`,
  `%ProgramFiles(x86)%`, `%LocalAppData%\Google\Chrome\Application\chrome.exe`
- Linux — `google-chrome` / `chromium` on `PATH` (existing behaviour, retained)

Registry access needs `Microsoft.Win32.Registry`, which is in-box for `net10.0` and safe to
reference from a cross-platform assembly as long as calls are guarded — the same
`[SupportedOSPlatform("windows")]` discipline used everywhere else here.

**Install hints become platform-aware** — `brew install d2` on macOS, `winget install
Terrastruct.d2` on Windows. A hint that names the wrong package manager is worse than no
hint, because it sends the reader somewhere that cannot work.

### 3.2 The `app` renderer needs a real seam (the crux)

The architecture's core claim is *"a renderer is just a command that writes a PNG to a
path — so adding one is configuration, not code."* macOS honours that via `screencapture`.
**Windows ships no such command.** `app` therefore cannot remain pure configuration, and
this is the one place the existing model has to bend.

Introduce one interface alongside the existing `IWindowFinder`:

```csharp
/// <summary>Captures a live window (or the whole screen) to a PNG. Implementations are OS-specific.</summary>
public interface IAppCapturer
{
    Task<RenderOutcome> CaptureAsync(RenderRequest request);
}
```

`RenderRequest` already carries both `OutputPath` and `WindowId`, so it is the whole input —
no extra parameters. A null `RenderRequest.WindowId` means "capture the full screen", which
is exactly how `Program.cs` distinguishes the two cases today.

| Implementation | How |
|---|---|
| `MacAppCapturer` | Delegates to today's `RenderEngine` + the `screencapture` `RendererSpec`. **Zero behaviour change.** |
| `WindowsAppCapturer` | In-process GDI capture + `PngWriter` (§3.3). |

Selected by a small factory so `Program.cs` never branches on OS again:

```csharp
public static class PlatformSupport
{
    public static IWindowFinder? WindowFinder();  // Mac / Windows / null
    public static IAppCapturer?  AppCapturer();   // Mac / Windows / null
}
```

A `null` return is the honest "this OS has no live capture" signal, and the CLI turns it
into one clear message instead of the current two scattered `OperatingSystem.IsMacOS()`
checks in `Program.cs` (lines 8 and 143).

**`WindowSelector` is not touched.** It is already pure, already tested, and Windows maps
onto its existing contract without changes:

| `WindowSelector` expects | macOS supplies | Windows supplies |
|---|---|---|
| List ordered frontmost-first | `CGWindowList` is front-to-back | `EnumWindows` enumerates in Z-order, top first |
| `Layer == 0` means "normal window" | `kCGWindowLayer` | Map tool-windows / cloaked UWP ghosts to non-zero; normal top-level → 0 |
| `OwnerName` | `kCGWindowOwnerName` | Owning process name |
| `Title` | `kCGWindowName` (needs Screen Recording) | `GetWindowTextW` (**no permission needed**) |
| `OnScreen` | `kCGWindowIsOnscreen` | `IsWindowVisible` && not cloaked |

Keeping the selector untouched means the `--window`/`--title` matching semantics stay
provably identical across platforms — the existing `WindowSelectorTests` cover both.

**Two Win32 details that fail silently if missed**, called out because each produces a
plausible-looking-but-wrong PNG rather than an error:

1. `PrintWindow` must pass **`PW_RENDERFULLCONTENT` (0x2)**. Without it, DWM-composited
   windows — WPF, Chrome, Avalonia, i.e. exactly what Glimpse exists to screenshot —
   capture as solid black.
2. The process must declare **per-monitor-DPI-v2 awareness** before capturing, or Win32
   reports virtualised coordinates and captures come back downscaled and blurry on any
   scaled display.

`PngAnalysis`'s existing blank/single-color warnings are a genuine safety net for both —
a black `PrintWindow` result trips the single-color warning and surfaces as exit code 1
rather than passing silently.

**Fallback chain:** `PrintWindow` → if it returns 0 *or* the result is single-color, retry
via screen-region `BitBlt` of the window's bounds → if the window cannot be resolved at
all, full-screen capture with a `fullscreen-fallback:` warning. That last rung mirrors what
the macOS path already does in `Program.cs:154`.

### 3.3 `PngWriter` — chosen to protect the single-TFM property

GDI returns raw BGRA pixels; something must encode them. Three options were weighed:

| Option | Verdict |
|---|---|
| **Hand-rolled `PngWriter` in `Glimpse.Core`** | **Chosen.** ~70 lines: IHDR + IDAT (via in-box `ZLibStream`) + IEND. |
| `System.Drawing.Common` | Rejected. Windows-only since .NET 7, so it forces `net10.0-windows` on `Glimpse.Core` **and** `Glimpse.Capture`, bringing conditional references, `#if WINDOWS` wiring, and a multi-TFM `PackAsTool`. More ceremony across the whole solution than an encoder is worth. |
| Bundled PowerShell script as a `RendererSpec` | Rejected. Fits the "renderer is a command" model neatly, but puts real logic outside the test suite, pays ~1s of PowerShell startup per capture, and is exposed to execution-policy and quoting problems. |

The chosen option keeps `Glimpse.Core` at plain `net10.0` guarded by
`[SupportedOSPlatform("windows")]` — **precisely the pattern `MacWindowFinder` already uses**
with `[SupportedOSPlatform("macos")]`. Mac and Windows stay symmetric, and no consumer
inherits a TFM split.

CRC32 is required by the PNG spec. Prefer the in-box-adjacent `System.IO.Hashing` package
(Microsoft-owned, tiny, added to `Directory.Packages.props`); if a zero-new-dependency
`Glimpse.Core` is preferred at implementation time, a table-driven CRC32 is ~15 lines and
is an acceptable substitute. Either way the encoder's correctness is pinned by the same
tests.

`PngWriter` is highly testable despite being new native-adjacent code, because the repo
**already contains a PNG reader**: encode a known bitmap → feed it to the existing
`PngAnalysis.Inspect` → assert dimensions, and assert blank/single-color detection fires
exactly when it should. That is a real round-trip, not a self-consistency check.

### 3.4 Widen `WindowId` to `long`

A macOS `CGWindowID` is a `uint32`, but a Windows `HWND` is a pointer-sized handle. Three
declarations must widen together, or the value is truncated somewhere along the path from
CLI argument to captured window:

| Declaration | Today | Becomes |
|---|---|---|
| `WindowInfo.WindowId` | `uint` | `long` |
| `RenderRequest.WindowId` | `int?` | `long?` |
| `CaptureOptions.WindowId` / `--window-id` parsing | `int?` / `int.TryParse` | `long?` / `long.TryParse` |

`RenderCommandBuilder`'s `{windowid}` substitution needs no change — it already calls
`.ToString()` on the value.

These are the only changes touching existing macOS-visible types. All three are widenings,
so every current value round-trips unchanged and `--window-id 42` keeps parsing. They are
called out explicitly because they edit shipped public records rather than adding beside
them.

### 3.5 Distribution — wrappers *and* a global tool

| Piece | Purpose |
|---|---|
| `plugin/bin/glimpse.cmd` | Windows sibling of the bash wrapper; same lazy-build-if-DLL-missing behaviour. Claude Code already puts a plugin's `bin/` on `PATH`, so no PATH work is needed. |
| `scripts/install.ps1` | Mirrors `install.sh` (including `--uninstall` and the refuse-if-real-directory guard) but uses a **directory junction**, which needs neither admin rights nor Developer Mode — unlike a symlink. |
| `PackAsTool` on `Glimpse.Capture` | `dotnet tool install -g` as the shared cross-platform path, and the pre-built-artifact slot STATUS.md item 8 (marketplace) requires. |

These coexist deliberately: the wrappers preserve "edits in the repo are instantly live"
(the property `bin/glimpse` was written to protect), while the global tool is the
shareable artifact.

### 3.6 CI

No CI exists in this repo today. Add a GitHub Actions workflow running `dotnet build` +
`dotnet test` on a `windows-latest` × `macos-latest` matrix. Without it, "supports both"
rots the first time either side is touched — and the macOS half is precisely the half that
cannot be executed from the Windows dev box, so it is the half most likely to break
unnoticed.

Renderer tools are **not** installed in CI. The end-to-end gates already skip when their
tool is absent (once §3.1 makes that skip work correctly), so CI covers compilation and all
unit tests on both OSes; real-render verification stays manual per §7.

## 4. Components

```
src/Glimpse.Core/
  ToolLocator.cs          MODIFIED  managed PATH+PATHEXT scan, 3-way Chrome, platform hints
  WindowInfo.cs           MODIFIED  WindowId uint -> long
  WindowFinder.cs         MODIFIED  + WindowsWindowFinder alongside MacWindowFinder
  AppCapturer.cs          NEW       IAppCapturer + MacAppCapturer + WindowsAppCapturer
  PlatformSupport.cs      NEW       OS -> (IWindowFinder?, IAppCapturer?) factory
  PngWriter.cs            NEW       BGRA -> PNG (IHDR/IDAT/IEND, ZLibStream, CRC32)
  RenderEngine.cs         MODIFIED  extract the shared analyse-tail so both engines reuse it
  RenderCommandBuilder.cs MODIFIED  RenderRequest.WindowId int? -> long?
  RendererSpec.cs         MODIFIED  chrome args: --user-data-dir; screencapture stays mac-only

tools/Glimpse.Capture/
  Program.cs              MODIFIED  drop both OperatingSystem.IsMacOS() branches; use PlatformSupport
  CaptureOptions.cs       MODIFIED  --window-id parses long
  Glimpse.Capture.csproj  MODIFIED  PackAsTool, ToolCommandName=glimpse

plugin/bin/glimpse.cmd    NEW
scripts/install.ps1       NEW
.github/workflows/ci.yml  NEW
README.md                 MODIFIED
plugin/skills/*/SKILL.md  MODIFIED
STATUS.md                 MODIFIED  close items 7 + 8
```

`Glimpse.Avalonia*` and `samples/` need **no changes** — verified by the passing Avalonia
suite in §1.

## 5. Error Handling / Edge Cases

| Case | Behaviour |
|---|---|
| `app` renderer on Linux | `PlatformSupport.AppCapturer()` returns null → exit 2, "live-window capture is macOS/Windows only" |
| Extensionless `mmdc` found first on Windows | Skipped by the launchable-extension filter (§3.1); `mmdc.cmd` is used |
| Chrome absent from registry *and* well-known dirs | Existing `GlimpseRenderToolException` + platform-correct install hint |
| Headless Chrome while Chrome is already running | Pass a temp `--user-data-dir`; a locked default profile otherwise makes the screenshot fail with an opaque error |
| `PrintWindow` returns black (DWM) | Detected as single-color → `BitBlt` fallback → warning if still degenerate |
| Window matched but minimised | `PrintWindow` still renders it; no restore/focus-stealing — capture must never disturb the user's desktop |
| Junction target already a real directory | `install.ps1` refuses and tells the user to remove it, matching `install.sh:24` |
| `--list-windows` on an unsupported OS | Exit 2 with a clear message (preserves today's behaviour, now via the factory) |

## 6. Testing

**Unit (run on both OSes in CI):**
- `ToolLocator` — PATHEXT preference order; extensionless rejected on Windows; nonexistent
  tool → null; platform-correct hints. The existing `Resolve_WithRealShellTool` test asserts
  on `ls`, which does not exist on Windows — retarget it at `dotnet`, which is by
  definition present wherever the suite runs.
- `PngWriter` — round-trip through `PngAnalysis`: dimensions preserved; a known-varied
  bitmap is *not* flagged blank; a uniform bitmap *is* flagged single-color.
- `WindowSelector` — unchanged tests must still pass; add Windows-shaped fixtures
  (tool-window at non-zero layer, cloaked window) to prove the `Layer` mapping.
- `PlatformSupport` — returns the right pair per OS.

**Windows-only integration (this dev box):** `--list-windows` lists real windows;
`--window "Chrome"` captures a real Chrome window; each of mermaid / graphviz / d2 / web
renders a real non-blank PNG.

**macOS regression (Pond, on the Mac):** the same renders plus `--window`, confirming §2's
"no behaviour change on macOS".

**Available on the dev box as of 2026-08-01:** mmdc `11.16.0`, d2 `v0.7.1`, dot `15.1.0`
(installed this session; Graphviz's `bin` was appended to the user `PATH`, as winget does
not do it), Chrome, Edge, Node, .NET 10.

## 7. Build Order (for the plan)

1. **`ToolLocator`** — managed PATH scan, 3-way Chrome, platform hints. *Clears all 7
   Windows test failures; everything else builds on a working tool resolver.*
2. **`PngWriter`** — pure, no P/Invoke, fully testable. Do it before any capture code so
   the Windows capturer has a verified encoder to write into.
3. **`WindowId` → `long`** — small mechanical widening; land it before the code that needs it.
4. **`IAppCapturer` seam + `MacAppCapturer`** — refactor only, no new behaviour. macOS
   suite must stay green *before* Windows code exists.
5. **`WindowsWindowFinder`** — verify against `--list-windows` on the real desktop.
6. **`WindowsAppCapturer`** — `PrintWindow` + DPI + fallback chain.
7. **`PlatformSupport` + `Program.cs`** — remove the OS branches.
8. **Distribution** — `glimpse.cmd`, `install.ps1`, `PackAsTool`.
9. **CI matrix.**
10. **Docs** — README, both SKILL.md files, STATUS.md items 7 + 8.

Steps 1–4 are safe on macOS by construction (1 and 3 are cross-platform; 2 is additive;
4 is a pure refactor). Steps 5–7 are the only genuinely new platform behaviour.

## 8. Open Questions

None blocking. One implementation-time judgement call is delegated explicitly: whether
`PngWriter`'s CRC32 comes from the `System.IO.Hashing` package or ~15 hand-rolled lines
(§3.3) — both satisfy the same tests, and the choice does not affect any other component.
