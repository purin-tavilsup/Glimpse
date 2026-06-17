# STATUS — UI Snapshot Tool (Glimpse)

> Goal: a general-purpose tool so Claude can *see* rendered UI by reading PNGs.
> Last updated: 2026-06-16.

## Current state: ✅ ENGINE IMPLEMENTED + MERGED TO `main` (local)

Built subagent-driven (TDD per task + spec/quality review gate per task + opus
whole-branch review). All 13 implementation tasks done; merged `--no-ff` into
`main` (merge commit `7d7033e`). **Full suite: 42 passed + 2 skipped** (both
documented), format clean. **No git remote yet** — local only.

### What shipped
- `Glimpse.Abstractions` — `SnapshotTheme`, `SnapshotSize` (framework-free).
- `Glimpse.Avalonia.Abstractions` — `IScene` (returns `Control`, default `ReadyAsync`).
- `Glimpse.Avalonia` — headless engine: `HeadlessSnapshotApp` (Skia + Inter +
  Fluent), `SnapshotSession` (one-shot/process, stability-based settle loop,
  `Render(Func<Control>)` builds on the UI thread, `RenderSceneAsync(IScene)`),
  `EnvironmentGuard` (net10 + Avalonia 11.3.x fail-fast), `FrameAnalysis`
  (single-color + font/tofu warnings), theme variants, determinism.
- `Glimpse.Core` (framework-free) — `OutputLocator` (per-repo/central),
  `Manifest`/`SceneEntry`/`Failure` + `ManifestMerge` (atomic, upsert by
  `(Scene,Theme)` for BOTH scenes and failures), `SnapshotWriter`,
  `ScreenCapture`/`ScreenCaptureCommand` (macOS `screencapture`).
- `Glimpse.ScratchConsole` (+ `Glimpse.ScratchConsole.Tests`) — CLI +
  `SnapshotRunner` (exit 0/1/2, failed-never-ok) + sample scenes +
  **automated e2e gate** (real session → writer → manifest; `ThemedControls`
  scene proves FluentTheme resource/template resolution).

### Key facts learned (verified vs Avalonia 11.3.17)
- Headless `HeadlessUnitTestSession.StartNew(type)` reflectively calls the type's
  static `BuildAvaloniaApp()`. `CaptureRenderedFrame()` → `WriteableBitmap?`.
- **No headless DPI/scaling knob** → scaling descoped to 1.0 (`scaling-ignored`
  warning + skipped contract test).
- **Controls must be built on the UI thread** (TextBlock throws "Call from
  invalid thread" off-thread) → sync API is `Render(Func<Control>)`.

## Active thread: live-screenshot skill (spec written, AWAITING POND REVIEW)

Second delivery mode, brainstormed + spec'd 2026-06-16: a `glimpse` skill that
screenshots a **live running desktop app** via macOS `screencapture` (any app, no
Avalonia engine, sidesteps cross-repo distribution). Decisions: window-if-findable
else whole-screen; **.NET CLI-backed** (extend `Glimpse.Core` with CoreGraphics
window-id lookup + new `tools/Glimpse.Capture` console); **capture-only** (assumes app
running — composes with `/run`); **no screenshot accumulation** (stable-name overwrite +
per-run prune in a `screenshots/` subdir).
- Spec: `docs/superpowers/specs/2026-06-16-glimpse-live-screenshot-skill-design.md` (committed `7e0f1fa`).
- **NEXT:** Pond reviews the spec → `writing-plans` → implement (build order in spec §9).

## Deferred / next actions
1. **Recorder preview console** (spec §4.5) — the first *real* app consumer.
   Separate repo (`~/dev/Recorder` under `Tools/`); blocked on the cross-repo
   distribution decision (spec §9.5: project ref vs local/internal NuGet feed).
   Its own brainstorm→plan when picked up. **Recorder untouched by this work.**
2. **GitHub remote** — none configured. Add one if/when sharing.
3. **Follow-up minors** (fast-follow, non-blocking — from reviews):
   - `OutputLocator` now uses `Path.Exists` for `.git` (worktree-safe) ✅ done in fix wave.
   - `settle-cap-hit` warning path untested (no headless animation clock) — accepted.
   - Test-strength polish (temp-dir cleanup; tighten a couple of assertions).
4. Spec §8 futures: skill packaging (`glimpse`), web/mobile modules, visual diff.

## Artifacts
- Spec: `docs/superpowers/specs/2026-06-15-ui-snapshot-tool-design.md`
- Plan (executed): `docs/superpowers/plans/2026-06-16-glimpse-ui-snapshot-tool.md`
- SDD progress ledger: `.git/sdd/progress.md` (per-task commits + review notes)
