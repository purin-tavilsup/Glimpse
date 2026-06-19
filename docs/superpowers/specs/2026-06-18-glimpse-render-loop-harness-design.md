# Glimpse Render-Loop Harness — Design

> Status: **Design / spec** (brainstormed + approved 2026-06-18)
> Author: Pond + Claude
> Supersedes the framing of the earlier specs: Glimpse is **no longer locked to
> Avalonia**. It becomes a renderer-agnostic *visual-feedback loop harness* — take
> any artifact, get a PNG (via whatever renderer fits the source), let the agent
> Read it, critique, improve, repeat. Builds on the shipped engine
> (`2026-06-15-ui-snapshot-tool-design.md`) and the live-screenshot work
> (`2026-06-16-glimpse-live-screenshot-skill-design.md`), reusing `Glimpse.Core`.

## 1. Problem & Motivation

An agent can only iterate on something visual if it can *see* the result. The one
universal capability that closes the loop is: **produce a PNG → the agent Reads it →
judges → fixes the source → repeats.** The renderers differ by source type, but the
loop is identical.

The shipped Glimpse renders *isolated Avalonia components headlessly* — powerful, but
narrow. The new goal is to make Glimpse work for **any UI or diagram**: a mermaid /
graphviz / d2 diagram, an HTML/CSS page, or a live desktop app. Rather than grow a
big multi-backend renderer, we keep the loop logic tiny and fixed and push all the
variety behind a single **renderer boundary**.

## 2. Goals / Non-Goals

### Goals
- A **harness** (CLI + skill) that runs one pipeline per invocation:
  **resolve renderer → run its command → analyse the PNG → record + report.**
- A **generic "command → PNG" renderer contract** with zero-config built-ins for
  diagrams (mermaid/graphviz/d2), web (headless browser), and live apps
  (screencapture). Adding a renderer is config, not C#.
- **Mechanical sanity checks** on the output PNG (blank / single-color / uniformity)
  so pipeline breaks (e.g. a failed render) never masquerade as a design problem.
- A **`glimpse` skill** packaging the render → Read → critique → re-run → stop loop,
  so the agent runs it consistently instead of improvising.
- **No accumulation:** stable-name overwrite + optional per-run prune.

### Non-Goals (YAGNI / deferred)
- **Reference-image / golden diff.** Valuable for *regression* (did my change alter an
  existing view?), but this is a *creation* loop with no baseline. Parked as a clean
  future add (originally spec §8 "visual diff").
- **Expectation checks** ("must contain 3 nodes", "no overlap"). Hard to detect well;
  the diagram renderers already lay out without overlap; the agent judges these
  better visually. Skipped.
- **Semantic verification by Glimpse.** "Is this diagram clear?" is the agent's call,
  made by Reading the PNG. Glimpse only does mechanics.
- **Extending the Avalonia engine.** It is parked (see §7), not grown.
- **App lifecycle** (build/launch/relaunch) — assumed already running; composes with
  the `run` skill. Carried over from the live-screenshot spec.
- **Windows/Linux capture** — macOS only for the `app` renderer (matches dev machine).

## 3. Architecture

```
                         glimpse  (CLI + skill)
                                |
        +-----------------------+------------------------+
        |                       |                        |
   1. RESOLVE             2. RENDER                3. ANALYSE
   renderer +             run command template     PNG analyser on output
   source                 -> writes PNG to         (blank/single-color,
   (config lookup)        stable {out} path         uniformity)
        |                       |                        |
        +-----------------------+------------------------+
                                |
                         4. RECORD + REPORT
                         manifest upsert (stable name),
                         print PNG path + warnings
                                |
                                v
                    [ agent Reads the PNG, judges it ]
                                |
                  good? --no--> edit source, re-run  (loop)
                    |
                   yes --> done
```

The harness is a thin pipeline. **The loop itself lives in the agent's reasoning** —
Glimpse makes each turn fast, stable, and mechanically sanity-checked, but the
"is it good?" judgment is the agent's, made by Reading the PNG.

## 4. Renderer Contract

A renderer is a named entry: a **command template** + the **tool it requires**.
Built-in defaults ship in code (works with zero config); an optional
`glimpse.renderers.json` merges on top so users add/override without recompiling.

| Renderer | Needs | Command template |
|----------|-------|------------------|
| `mermaid`  | `mmdc`          | `mmdc -i {source} -o {out} -t {theme} -w {width}` |
| `graphviz` | `dot`           | `dot -Tpng {source} -o {out}` |
| `d2`       | `d2`            | `d2 --theme {theme} {source} {out}` |
| `web`      | a Chromium binary | `{chrome} --headless --screenshot={out} --window-size={width},{height} {source}` |
| `app`      | `screencapture` | `screencapture -l{windowid} {out}` |

**Placeholders:** `{source}`, `{out}`, `{width}`, `{height}`, `{theme}`,
`{windowid}`, `{chrome}`. A renderer entry declares which it uses; the harness fills
them. Theme reuses `SnapshotTheme` (light/dark) from `Glimpse.Abstractions`.

**Renderer selection:**
- Inferred from source extension: `.mmd`→mermaid, `.dot`/`.gv`→graphviz, `.d2`→d2,
  `.html`/`.htm`→web.
- `--renderer <name>` overrides inference.
- `app` is always explicit (`--window "Name"`); window-id lookup is reused from the
  live-screenshot work (macOS), falling back to whole-screen if not found.

**Fail fast on missing tools.** Before running, the harness checks the required tool
is on PATH. If absent, it exits with an actionable message
(e.g. `mermaid-cli not found — install: npm i -g @mermaid-js/mermaid-cli`). Same
fail-fast spirit as the existing `EnvironmentGuard`.

## 5. The Loop & Verify

Each invocation = **one** render → analyse → report. The agent runs it, Reads the
PNG, judges, edits the source, re-runs.

- **Mechanical checks** on the output PNG: **blank / single-color / suspiciously
  uniform** (reliable). The agent catches tofu/missing-font and layout issues
  **visually** — it can see boxes and clipping in the PNG — so no over-engineered
  detection for the command-rendered path.
- **Exit codes:** `0` rendered + clean · `1` rendered + warnings · `2` render failed
  (command error or blank output). The agent Reads the PNG whenever one exists.
- **Stop rules (documented in the skill):** stop when the agent judges the artifact
  meets intent *and* mechanical checks are clean. Guardrail: cap at ~5–6 iterations;
  if not converging, stop and report what's stuck rather than spinning.

## 6. Output & Manifest

- **Stable-name overwrite:** each target writes to a deterministic path,
  `…/glimpse/<name>.png`, so iterations overwrite instead of accumulating. Optional
  `--prune` clears stale PNGs from a run.
- **Reuse `Glimpse.Core` wholesale:** `OutputLocator` (per-repo vs central),
  `Manifest` / `ManifestMerge` (atomic upsert), `SnapshotWriter`.
- **Manifest key** generalises from `(Scene, Theme)` → `(Name, Theme)`. Each entry
  records: name, renderer, source path, output path, size, theme, timestamp, status,
  warnings.

## 7. Avalonia Engine + the PNG Analyser

- **Avalonia engine: parked, not deleted.** It is merged, tested, and still serves the
  future Recorder-preview consumer. It does not fit "command → PNG" (in-process
  render), but `Glimpse.ScratchConsole` *already is* a CLI that renders scenes to PNG
  — so if ever wanted, Avalonia plugs in as just another command-renderer. **Decision:
  don't extend it, don't tear it out.** The new harness is the center of gravity.
- **New piece needed:** the existing `FrameAnalysis` operates on Avalonia bitmaps. The
  harness must analyse an arbitrary **PNG file**, so add a small framework-free
  analyser in `Glimpse.Core` that decodes the PNG and computes
  blank/single-color/uniformity. **Decode via SkiaSharp** — already transitively
  present through the Skia stack, so no new heavy dependency.

## 8. Delivery: CLI + Skill

- **CLI** — `tools/Glimpse.Capture` (the binary the live-screenshot spec earmarked):
  - `glimpse <source>` (infer renderer by extension)
  - `glimpse --renderer mermaid <source>`
  - `glimpse app --window "Name"`
  - flags: `--name`, `--out`, `--size WxH`, `--theme light|dark`, `--prune`
  - prints the **absolute PNG path** (so the agent Reads it immediately) + warnings +
    manifest path.
- **`glimpse` skill** — documents the loop so the agent triggers it consistently
  (render → Read → critique → re-run → stop). This packaging is what makes the harness
  real for the agent.

## 9. Build Order (for the plan)

1. **PNG analyser** in `Glimpse.Core` (SkiaSharp decode + blank/single-color/uniformity)
   — TDD with fixture PNGs.
2. **Renderer registry**: config model, built-in defaults, optional JSON merge,
   extension→renderer inference, tool-on-PATH detection + fail-fast.
3. **Render pipeline**: fill placeholders, run command, capture stdout/stderr + exit,
   map to `0/1/2`, run analyser on output.
4. **Manifest generalisation**: `(Scene,Theme)` → `(Name,Theme)`; wire writer +
   `OutputLocator` + stable-name + `--prune`.
5. **`Glimpse.Capture` CLI**: arg parsing, the five built-in renderers wired, report
   output (PNG path + warnings + manifest path).
6. **`app` renderer**: fold in the existing `ScreenCapture`/window-id lookup.
7. **`glimpse` skill**: the loop, stop rules, tool-install hints.
8. **e2e gate**: a real diagram (graphviz/mermaid) → PNG → analyser → manifest, run in
   CI-style as the existing ScratchConsole e2e does.

## 10. Open Dependencies

- Renderers need their tool installed (`mmdc`, `dot`, `d2`, a Chromium binary). The
  harness detects + fails fast with install hints; CI/e2e uses whichever is present.
- SkiaSharp added as a direct dependency of `Glimpse.Core` for PNG decode.

## 11. Futures (not now)

- Reference-image / golden diff (regression mode).
- More renderers via config (PlantUML, Excalidraw CLI, …).
- Windows/Linux `app` capture.
- Packaging the `glimpse` skill for distribution across repos.
