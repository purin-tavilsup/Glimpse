# Glimpse Live-Screenshot Skill — Design

> Status: **Design / spec** (brainstormed + approved 2026-06-16)
> Author: Pond + Claude
> Builds on: the shipped Glimpse engine (`2026-06-15-ui-snapshot-tool-design.md`,
> merged to `main`). This spec covers the **live running-app screenshot** delivery
> mode (spec §8 "skill packaging" + "cross-platform OS capture") — it does NOT use
> the headless Avalonia engine.

## 1. Problem & Motivation

When an agent iterates on a desktop app's UI, it can't see the result. The shipped
Glimpse engine renders *isolated Avalonia components headlessly*, which requires a
per-app preview console referencing the app's UI assembly (deferred — blocked on
cross-repo distribution). The faster, framework-agnostic path is to **screenshot the
live running app window** via macOS `screencapture`, then `Read` the PNG. This works
for any app/toolkit, needs no reference to the app's code, and captures real runtime
state. We package this loop as a Claude Code **skill** so any agent can use it.

## 2. Goals / Non-Goals

### Goals
- A **capture CLI** that screenshots a named app's frontmost window (falling back to
  whole-screen), writes the PNG + a manifest, and self-cleans so screenshots never
  accumulate.
- A **window-id lookup** (macOS CoreGraphics) so capture is autonomous — no human
  clicking to select a window.
- A **skill** (`glimpse`) wrapping the capture → `Read` → iterate loop, including
  Screen-Recording-permission guidance.

### Non-Goals (YAGNI / deferred)
- **App lifecycle** — the skill does NOT build, launch, or relaunch the app. It
  assumes the app is already running; the user (or the `run` skill) owns
  build+launch+relaunch between UI edits. (Decided.)
- The headless Avalonia rendering engine and per-app preview console (separate,
  already-deferred work).
- Windows/Linux capture (macOS only — matches the dev machine + the original spec).
- Interactive (`-i`) window selection — breaks autonomous loops; out of scope.
- Visual-diff / golden baselines.

## 3. Architecture

```
+-----------------------------------------------------------+
|  Skill: ~/.claude/skills/glimpse/SKILL.md                 |
|  - ensure app is running (defer build/launch to user/run) |
|  - run the capture CLI; Read manifest; Read PNG; iterate  |
|  - permission-grant guidance                              |
+-----------------------------------------------------------+
                        | drives (dotnet run)
                        v
+-----------------------------------------------------------+
|  tools/Glimpse.Capture (console, net10.0, refs Core only) |
|  - parse args; resolve output dir; prune; find window;    |
|    activate; screencapture (window | fullscreen);         |
|    validate PNG; write manifest; print summary            |
+-----------------------------------------------------------+
                        | uses
                        v
+-----------------------------------------------------------+
|  Glimpse.Core (extend, framework-free, no Avalonia)       |
|  - WindowSelector (pure)  +  macOS WindowFinder (P/Invoke) |
|  - ScreenCaptureCommand / ScreenCapture (already exist)    |
|  - OutputLocator (reuse)  +  CaptureManifest + writer/prune|
+-----------------------------------------------------------+
```

The capture path stays in **`Glimpse.Core`** (the framework-free "generic OS capture"
home) — no Avalonia dependency. The new console references only `Glimpse.Core`.

## 4. Components

### 4.1 Window lookup (macOS) — split native vs pure

- **`WindowInfo`** (plain record, the parsed shape of one CGWindow dict):
  ```csharp
  public sealed record WindowInfo(
      uint WindowId, string OwnerName, string? Title,
      int X, int Y, int Width, int Height, int Layer, bool OnScreen);
  ```
- **`WindowSelector` (pure, unit-tested):**
  ```csharp
  public static WindowInfo? SelectFrontmost(
      IReadOnlyList<WindowInfo> windows, string appMatch, string? titleMatch = null);
  ```
  Rules: keep windows where `OwnerName` contains `appMatch` (case-insensitive) AND
  (`titleMatch` null OR `Title` contains it, case-insensitive) AND `OnScreen` AND
  `Layer == 0` (normal app windows — excludes menubar/overlays) AND `Width*Height`
  above a small threshold (excludes 1px helper windows). `CGWindowListCopyWindowInfo`
  returns front-to-back order, so the **first** survivor is the frontmost window.
  Returns null if none match.
- **`IWindowFinder` / `MacWindowFinder` (native, not unit-tested):**
  ```csharp
  public interface IWindowFinder { IReadOnlyList<WindowInfo> ListOnScreen(); }
  ```
  `MacWindowFinder` P/Invokes `CGWindowListCopyWindowInfo(OnScreenOnly, kCGNullWindowID)`
  and converts the `CFArray<CFDictionary>` into `WindowInfo[]` (reading
  `kCGWindowNumber`, `kCGWindowOwnerName`, `kCGWindowName`, `kCGWindowBounds`,
  `kCGWindowLayer`, `kCGWindowIsOnscreen`). The CFType marshalling lives here; the
  selection logic lives in the pure `WindowSelector`.

### 4.2 Capture manifest + cleanup — own subdir, no accumulation

- **Output dir:** `OutputLocator.Resolve()` base + a dedicated **`screenshots/`** subdir
  (`<base>/screenshots/`). Isolating captures from the engine's rendered-scene manifest
  avoids any schema clash and makes pruning safe (the subdir is Glimpse-owned).
- **Stable name + overwrite:** default PNG name `<app-slug>.png` (or `--name <stem>`),
  overwritten every capture → repeated captures of the same target never pile up.
- **Prune (per run):** before writing, delete the PNGs listed in the *prior*
  `manifest.json` (Glimpse-owned only) + the old manifest, then write the fresh set.
  Precise — never deletes a file Glimpse didn't create.
- **`CaptureManifest`:**
  ```jsonc
  {
    "schemaVersion": 1,
    "runId": "<guid>",
    "generatedAt": "<iso8601>",
    "captures": [
      { "name": "Recorder", "mode": "window",   // window | fullscreen
        "app": "Recorder", "windowId": 4271, "windowTitle": "Mimica Recorder",
        "path": "Recorder.png", "width": 1280, "height": 800,
        "status": "ok",                          // ok | failed
        "capturedAt": "<iso8601>",
        "warnings": ["fullscreen-fallback"] }    // structured, see §6
    ]
  }
  ```
  Echoing the resolved `windowId`/`windowTitle` lets the agent **confirm it captured
  the intended window** (or sees that it fell back to full screen). Atomic write reuses
  the shipped temp-then-rename pattern.

### 4.3 Capture CLI — `tools/Glimpse.Capture`

```
dotnet run --project tools/Glimpse.Capture -- \
    --app "Recorder" [--title "<substr>"] [--out <dir>] [--name <stem>]
```
Flow:
1. Parse + validate args (`--app` required; unknown flag / missing value → error, exit 2).
2. Resolve output dir → `<base>/screenshots/`; ensure it exists; **prune** prior captures.
3. `WindowSelector.SelectFrontmost(finder.ListOnScreen(), app, title)`.
4. **If found:** best-effort activate the owner app (osascript `tell application … to
   activate`, ignore failure) → `screencapture -l<id> -o <out>` (mode `window`).
   **Else:** `screencapture -x <out>` (mode `fullscreen`, add `fullscreen-fallback` warning).
5. **Validate** the PNG: file exists and size is above a small threshold (a failed or
   denied capture is missing or near-empty). `Glimpse.Core` is framework-free (no image
   decoder), so it does **not** pixel-analyze the image — *content* correctness is
   confirmed by the agent `Read`ing it (§5). If the file is missing/empty, mark `failed`
   + print the permission guidance (§5) to stderr.
6. Write the manifest; print a one-line summary (`mode`, resolved window, path).
7. Exit 0 on a valid capture; non-zero on failure (no window AND fullscreen failed, or
   permission-denied).

### 4.4 The skill — `~/.claude/skills/glimpse/SKILL.md`

A personal skill (peer of `using-paired-cc`). Procedure:
1. Confirm the target app is **running** (if not, tell the user / suggest `/run`; the
   skill does not launch it).
2. Run the capture CLI for the app.
3. `Read` the manifest, confirm `status: ok` and the resolved window matches intent
   (watch for `fullscreen-fallback`).
4. `Read` the referenced PNG.
5. Iterate: after a UI edit, the app must be **relaunched** (user/`run`) to reflect the
   change, then re-capture.
Plus: the Screen-Recording permission steps (§5), and the note that the manifest is the
source of truth (cross-check PNG ↔ manifest, never read a PNG blind).

## 5. Permission Handling (TCC)

Capturing another app's window needs **Screen Recording** permission for the host
process (the terminal running `dotnet`). When missing, `screencapture` typically still
writes a file but with the **target window absent** (desktop/wallpaper only) — hard to
distinguish from bytes alone. So detection is two-layered: (a) the CLI flags a
missing/empty output file as `failed`; (b) the **skill instructs the agent to verify the
target window is actually visible** in the `Read` PNG — if it shows only desktop, that's
denied permission. Either way the guidance is the same and shown proactively on first
failure: **System Settings → Privacy & Security → Screen Recording → enable your terminal
app, then re-run.** First-run UX is "grant once."

## 6. Error Handling & Capture Validity

- **`--app` missing / bad args** → exit 2 with usage.
- **No matching window** → fall back to full screen, `fullscreen-fallback` warning,
  still exit 0 if the screen capture is valid.
- **`screencapture` non-zero / missing / empty output file** → `failed` entry, non-zero
  exit, + permission guidance (§5).
- **Window content absent in a written PNG** (Screen-Recording denied) → not detectable
  from bytes; the agent catches it by `Read`ing the PNG (§5) and grants permission.
- **Multiple matching windows** → the frontmost (first in CGWindowList order) wins; the
  manifest records which (id/title) so the agent can correct with `--title`.
- Validate at boundaries: output dir writable, app name non-empty.

## 7. Testing Strategy

- **Unit (pure, cross-platform):**
  - `WindowSelector.SelectFrontmost` — synthetic `WindowInfo` lists: owner match
    (case-insensitive), title filter, layer/onscreen/size exclusion, frontmost-first,
    no-match → null.
  - Capture-arg construction (window vs fullscreen) — reuses/extends
    `ScreenCaptureCommand` tests.
  - **Prune** — deletes only prior-manifest PNGs + old manifest, leaves unrelated files;
    stable-name overwrite produces no accumulation across runs.
  - `CaptureManifest` round-trip (atomic write).
- **Integration (manual — needs macOS TCC + a live window):** capture a real running
  app end-to-end (e.g. Safari or the Recorder app), confirm the PNG shows that window
  and the manifest's resolved id/title are correct. Not unit-tested (TCC + a live window
  can't run headless/CI).

## 8. Open Decisions (resolved 2026-06-16)

1. **Primary use case** — live running-app OS screenshot (not headless render). ✅
2. **Window targeting** — frontmost window of a named app; **fall back to whole screen**
   if not findable. ✅
3. **Backing** — **.NET CLI** (extend `Glimpse.Core` + new `Glimpse.Capture` console);
   the skill wraps `dotnet run`. ✅
4. **App lifecycle** — **capture only**; assume the app is already running. ✅
5. **Cleanup** — stable-name overwrite + per-run prune of prior Glimpse captures; no
   accumulation. ✅
6. **Skill location** — personal skill `~/.claude/skills/glimpse/`. Window match =
   case-insensitive substring on owner name. ✅

## 9. Build Order (when we implement)

1. `WindowInfo` + pure `WindowSelector` + tests.
2. `CaptureManifest` + writer/prune (atomic, reuse `OutputLocator`) + tests.
3. `MacWindowFinder` (CoreGraphics P/Invoke → `WindowInfo[]`) — native, manual-verified.
4. `tools/Glimpse.Capture` console: arg parse → prune → find → activate → capture →
   validate → manifest → summary.
5. End-to-end manual capture against a real running app (permission grant on first run).
6. The `glimpse` skill (`SKILL.md`) + a real-loop dry run.
```
