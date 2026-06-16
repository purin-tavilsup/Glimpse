# UI Snapshot Tool — Design

> Status: **Design / plan only** (no implementation yet — 2026-06-15)
> Author: Pond + Claude (brainstormed)
> Reviewed by: Architect / .NET-SWE / QA subagents (2026-06-15) — feedback incorporated below.

## 1. Problem & Motivation

When iterating on desktop UI (Avalonia today, possibly web/mobile later), Claude
cannot *see* the rendered result of XAML/view edits. The feedback loop is blind:
edit → guess → ask a human to look. We want a tool that **renders UI to a PNG**
that Claude can `Read` (Claude can view images directly), closing the loop.

Constraints from brainstorming:

- **General-purpose**, not Recorder-specific — usable for any future UI work.
- **Modular**: a generic core with **per-framework modules/extensions**
  (Avalonia first; web, mobile later).
- **Hybrid capture**: fast *isolated component* rendering **and** *live running app*
  screenshots.
- **Stub-VM-first** with a clean seam to plug in real ViewModels later.
- **Lightweight**: no MCP server; lean on the existing `Read`-a-PNG ability.

## 2. Goals / Non-Goals

### Goals

- A generic **snapshot core**: an output-folder + naming + `manifest.json`
  convention that Claude reads, plus a generic OS screen/window capture (macOS first).
- A generic **Avalonia rendering engine**: render *any* Avalonia `Control` to pixels,
  headlessly, with no app-specific dependencies.
- A **thin per-project "preview console"** pattern so a specific app's real compiled
  views can be rendered; **Recorder preview console** is the first consumer.
- A small **CLI surface** (the preview console itself) that drives the engine and asks
  Core to write PNG(s) + manifest.

### Non-Goals (YAGNI — explicitly deferred, seams only)

- Web / mobile rendering modules (design the contract, don't build them).
- MCP server.
- **Reflection-loaded global tool** that renders arbitrary host DLLs — *rejected for v1*
  (see §3; ALC/version/native-lib fragility). Revisit only if the per-app console
  pattern proves too heavy.
- Visual-diff / golden-image regression baselines.
- Windows/Linux OS screen capture (macOS first; provider seam noted).
- CI integration.
- Packaging as a Claude Code **skill** (planned future delivery mode — §8).

## 3. Architecture

A generic, app-agnostic **engine + core**, consumed by a **thin per-app preview
console**. The only thing that knows about a specific app is its console project.

```
+---------------------------------------------------+
|  Snapshot Core (conventions, no UI-framework deps)|
|  - output dir + file naming                       |
|  - manifest.json (schema, runId, per-entry status)|
|  - atomic write; owns ALL disk I/O                |
|  - generic OS capture (macOS `screencapture`)     |
+---------------------------------------------------+
                        ^ pixels (byte[]) + metadata
                        |
+---------------------------------------------------+
|  Snapshots.Avalonia Engine (generic, reusable)    |
|  - boots headless Avalonia + Skia (once/process)  |
|  - registers Fluent theme + Inter font            |
|  - settle loop -> CaptureRenderedFrame -> pixels  |
|  - pure: Control -> RenderResult (no disk I/O)    |
+---------------------------------------------------+
         ^ references engine + IScene directly
         |
+---------------------------------------------------+
|  Per-app Preview Console (Exe, `dotnet run`)      |
|  - refs Snapshots.Avalonia + the app's UI assembly|
|  - registers that app's scenes (real views +      |
|    stub VMs), injects app Styles/resources        |
|  - e.g. Mimica.Recorder.UI.Previews               |
+---------------------------------------------------+
```

### Distribution model — **per-app preview console** (revised after review)

The original plan (a global tool that reflection-loads a host DLL) was rejected by the
Architect + SWE reviews: loading an app assembly that transitively references Avalonia +
Skia native libs into a separate tool process invites `AssemblyLoadContext`/version/
native-lib conflicts, and a net10.0 host cannot load into an older-runtime tool.

Instead, **each consuming app owns a thin preview console** (an `Exe`) that references
the engine *and* its own UI assembly, so everything compiles and resolves in **one
coherent dependency graph**. Run it with `dotnet run`:

```
dotnet run --project Tools/Mimica.Recorder.UI.Previews -- \
    --scene LoginWindow --theme both --out .claude/tmp/ui-snapshots
```

This still satisfies the real goal — the engine's heavy deps live in the **separate
preview project**, never in the shipping `Mimica.Recorder.UI`.

**Hard constraint (state explicitly, enforce at startup):** the engine and every
consuming app must share the **same Avalonia 11.3.x** version and target **net10.0**
(matches Recorder). The console fails fast with a clear message on mismatch.

For greenfield/ad-hoc design with no app, a tiny **scratch preview console** ships with
the repo; I write inline scenes there — no app references at all.

## 4. Components

### 4.1 Snapshot Core (conventions) — owns all disk I/O

- **Output dir**: per-repo `.claude/tmp/ui-snapshots/` when working inside a repo;
  central `~/.claude/ui-snapshots/` for repo-less ad-hoc design. (Open Q §9.)
- **Naming**: stable latest name `<scene>.<theme>.png` (easy for Claude to `Read` the
  current result). The manifest is the source of truth for whether that PNG is current.
- **`manifest.json`** (atomic temp-file-then-rename; per-output-dir so concurrent repos
  don't clash):
  ```jsonc
  {
    "schemaVersion": 1,
    "runId": "<guid>",
    "generatedAt": "<iso8601>",
    "scenes": [
      { "scene": "LoginWindow", "theme": "dark", "path": "LoginWindow.dark.png",
        "width": 1024, "height": 768, "scaling": 1.0,
        "status": "ok",            // ok | failed | stale
        "renderedAt": "<iso8601>",
        "warnings": ["..."] }      // structured: tofu-detected, single-color, etc.
    ],
    "failures": [ { "scene": "X", "error": "..." } ]
  }
  ```
  - Entry identity/upsert key is `(scene, theme)` — a re-render replaces that entry.
  - On scene failure: mark the entry `failed` (don't leave a fresh-looking stale PNG
    that the agent would trust). The agent must cross-check PNG ↔ manifest, never read
    a PNG blind.

### 4.2 Snapshots.Avalonia Engine (generic) — pure Control → pixels

- **Project**: `Snapshots.Avalonia` class library, **net10.0**. References Avalonia
  11.3.x, `Avalonia.Skia`, `Avalonia.Headless`, `Avalonia.Themes.Fluent`,
  `Avalonia.Fonts.Inter`.
- **Public API** (engine returns *pixels*, not a path — Core writes files):
  ```csharp
  public sealed record RenderOptions(
      int Width = 1024, int Height = 768,
      double Scaling = 1.0,
      ThemeVariant Theme = /* Light */,
      IBrush? Background = null);

  public sealed record RenderResult(
      byte[] Png, int PixelWidth, int PixelHeight,
      IReadOnlyList<string> Warnings);

  public interface ISnapshotRenderer
  {
      RenderResult Render(Control control, RenderOptions options);
  }
  ```
- **Headless mechanics** (verified vs Avalonia 11.3 docs/source — note the dispatcher
  step the first draft omitted):
  - Configure **once per process** (platform is global, cannot re-init):
    `AppBuilder.Configure<HeadlessApp>().UseSkia()`
    `.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`
    `.WithInterFont()` + `FluentTheme` registered in `HeadlessApp`.
  - **A dispatcher must be running.** Drive the flow on the Avalonia thread inside the
    headless session/`Start` callback (reuse `Avalonia.Headless` session plumbing);
    `Show()` + a single `ForceRenderTimerTick()` is **not** sufficient on its own.
  - **Settle loop before capture**: pump `Dispatcher.UIThread.RunJobs()` +
    `ForceRenderTimerTick()` until the `LayoutManager` is idle and no dispatcher jobs
    remain (cap iterations; warn if cap hit). Guarantees a fully laid-out frame.
  - **Disable animations** (zero-duration transitions / freeze the animation clock) so
    spinners/fades don't yield non-deterministic frames.
  - `window.CaptureRenderedFrame()` → `WriteableBitmap?` → encode to PNG bytes.
    Null/`NotSupportedException` ⇒ Skia/`UseHeadlessDrawing=false` not configured →
    surface that exact cause.
- **Scaling**: wire `RenderOptions.Scaling` to the headless top-level render scaling and
  **assert** output pixel dims == `Width*Scaling × Height*Scaling` (don't silently emit 1x).
- **Theme variants**: swap `RequestedThemeVariant`, then re-run the settle loop **between
  variants** (else stale pixels).
- **Culture**: pin `CultureInfo` (invariant or fixed) for the render so dates/numbers in
  stub data are portable.
- **App resources**: the engine boots the `AppBuilder`, so the consuming app's
  `App.axaml` styles are *not* auto-applied. The scene/console must attach the app's
  `Styles`/resource dictionaries to the rendered `Window`, or views relying on
  app-level `DynamicResource` will render unstyled or hard-throw at XAML load.
- **Session**: a `SnapshotSession` sets up the headless app **once** and renders many
  scenes single-threaded; document it as one-shot per process, non-reentrant.

### 4.3 Scene abstraction (stub-VM seam)

- **Two contract layers** (keep "generic core" honest — `Control` is an Avalonia type):
  - `Snapshots.Abstractions` — framework-free (scene name, theme enum, size).
  - `Snapshots.Avalonia.Abstractions` — `IScene` returning `Control`. Preview consoles
    reference this one.
- ```csharp
  public interface IScene
  {
      string Name { get; }
      Control Build();                 // view + DataContext (stub VM by default)
      Task ReadyAsync() => Task.CompletedTask;  // real-VM scenes await async load here
  }
  ```
  - Default scenes set `DataContext` to a **hand-built stub VM** with canned values —
    deterministic, no DI, no services.
  - **Real-VM seam**: a scene may build a real DI-backed VM; if it loads async it MUST
    signal completion via `ReadyAsync()`, which the engine awaits before the settle loop.

### 4.4 Invocation

- **Preview console** (per app): `dotnet run --project <console> -- --scene <name|all>
  --theme light|dark|both [--out <dir>]`.
  - `--scene all`: deterministic order; **empty discovery = error + non-zero exit**;
    **duplicate scene names = hard error** (they'd collide on `<scene>.png`).
  - Batch is fail-soft per scene but the run reports partial success via manifest +
    non-zero exit if any failed.
- **Generic OS capture** (live app, any framework): thin wrapper over macOS
  `screencapture` — whole screen (`-x`), window by id (`-l<id>`), or interactive region.
  - **Prereqs noted**: needs Screen Recording (TCC) permission; window-id enumeration is
    not built into `screencapture` (needs a `CGWindowList`/AppleScript helper). Echo the
    resolved window id/title into the manifest so the agent confirms the right capture.
- **Claude's loop**: run the console (or `screencapture`) → read `manifest.json` →
  `Read` the referenced PNG → iterate.

### 4.5 Recorder preview console (first consumer)

- **Project**: `Mimica.Recorder.UI.Previews` (Exe, in the Recorder repo under `Tools/`),
  net10.0, references `Snapshots.Avalonia` + `Mimica.Recorder.UI`.
- Registers 1–2 real views (e.g. the themed login window) with stub VMs, attaches the
  app's `Styles`/resources, outputs to Recorder's `.claude/tmp/ui-snapshots/`.
- This is the **end-to-end gating check** (see §7/§10) — it proves the dependency graph,
  resources, and capture all work together against real app views.

## 5. Data Flow

```
scene (IScene) --> preview console --> engine (settle loop, disable anim, culture)
              --> CaptureRenderedFrame --> RenderResult(pixels)
              --> Core writes PNG + atomic manifest (status/warnings)
              --> Claude reads manifest --> Reads current PNG --> iterate
```

## 6. Error Handling & Capture Validity

- **No pixels** (`CaptureRenderedFrame` null / `NotSupportedException`) → fail with the
  exact "needs Skia + UseHeadlessDrawing=false" cause.
- **Blank/misleading frame heuristics** → warn (and record in manifest `warnings`):
  single-color frame, or non-background pixel ratio below a threshold. Don't rely on
  "all-transparent" alone (false-positives on legitimately transparent UI).
- **Missing fonts → tofu (□□□)**: detect unresolved font families; warn loudly and record
  resolved fonts — tofu looks like "real UI" to an agent reading the PNG.
- **Scene build/Ready throws** → record under `failures`, continue batch, non-zero exit.
- **Missing theme/app resource** → often hard-throws at XAML load; report the scene +
  the missing key rather than pretending to degrade.
- **Version/TF mismatch** → fail fast at startup with the required Avalonia/net version.
- Validate at boundaries: output dir writable, scene name resolves, theme arg valid.

## 7. Testing Strategy

- **Determinism test (most important for a screenshot tool):** render the same scene N
  times → byte-identical (or within tight tolerance) PNGs.
- **Engine integration** (`Avalonia.Headless.XUnit`/NUnit): render a known control →
  PNG non-empty, expected pixel dims (incl. a **scaling ≠ 1.0** case), sanity corner pixel.
- **Theme-applied differential**: render the same control light vs dark → captures must
  differ (or a theme-keyed brush resolves to the expected value). Catches "dark silently
  renders light".
- **Font/tofu test**: render known glyphs → assert not tofu.
- **Async-ready test**: a scene whose VM loads async → engine awaits `ReadyAsync()`
  before capture; data is present in the frame.
- **Scene smoke**: every registered scene `Build()`s without throwing.
- **End-to-end Recorder console** is a **gating check run early** (not the last step) —
  it's the only test that exercises the real app dependency graph + resources.
- Visual-diff / golden baselines remain out of scope; naming + manifest leave room.

## 8. Modularity & Future Extensions (seams, not built now)

- **Module contract**: "produce PNG(s) into the snapshot dir and update the manifest."
  Any technology can implement it.
  - **Web module**: headless Chromium (e.g. Playwright) screenshots a URL/HTML.
  - **Mobile module**: emulator/simulator screenshot.
  - **Cross-platform OS capture**: Windows/Linux screen-capture providers behind the same
    Core capture interface.
- **Skill packaging** (planned): wrap the capture→Read loop as a Claude Code skill (e.g.
  `snapshot-ui`) that knows how to run the preview console and surface the current PNG.
  The console + conventions are the substrate; the skill is a thin convenience layer.

## 9. Open Questions / Decisions

1. **Tool name** — **DECIDED: `Glimpse`** (2026-06-16). Namespace/assembly map:
   - `Glimpse.Abstractions` (framework-free) + `Glimpse.Avalonia.Abstractions` (`IScene`).
   - `Glimpse.Avalonia` (rendering engine) — replaces `Snapshots.Avalonia` throughout.
   - `Glimpse.Core` (conventions: output dir, naming, manifest, OS capture).
   - Repo stays `ui-snapshot-tool`; consuming consoles keep their own names
     (e.g. `Mimica.Recorder.UI.Previews`).
   - *Note:* §1–§8 still say `Snapshots.*` — read those as `Glimpse.*`; the
     implementation plan uses the final names.
2. **Output location** — **DECIDED (2026-06-16): per-repo when inside a repo, central
   otherwise.** `.claude/tmp/ui-snapshots/` when a repo root is detected; fall back to
   `~/.claude/ui-snapshots/` for repo-less ad-hoc design.
3. **Target framework** — **DECIDED: net10.0** (must match Recorder; can't load a net10
   assembly into an older-runtime process).
4. **Avalonia version pinning** — **DECIDED: engine + all consumers pin the same Avalonia
   11.3.x**, enforced by a startup check.
5. **Abstractions distribution** — with the per-app-console model, consoles can use
   project references within their own repo; a published `Glimpse.Avalonia` package
   (local folder feed vs internal feed) is only needed for cross-repo reuse. Defer.

## 10. Build Order (when we implement — not today)

1. `Snapshots.Abstractions` (framework-free) + `Snapshots.Avalonia.Abstractions`
   (`IScene`, `RenderOptions`).
2. `Snapshots.Avalonia` engine: headless+Skia session, settle loop, animation-off,
   culture pinning, scaling, theme variants, capture → `RenderResult`.
3. Snapshot **Core**: output dir + naming + atomic manifest (status/warnings) + PNG write.
4. A **scratch preview console** + the determinism/theme/font/scaling tests.
5. **Recorder preview console** with 1–2 real views (stub VMs) — the e2e gating check.
6. Generic macOS `screencapture` wrapper + permission/window-id helper + docs.
7. (Later) skill packaging; web/mobile modules; visual diff.
