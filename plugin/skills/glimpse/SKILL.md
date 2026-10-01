---
name: glimpse
description: Use when you need to SEE a UI or diagram you are creating or changing — render any artifact (mermaid/graphviz/d2 diagram, HTML page, or live app window) to a PNG, Read it, critique, improve, repeat. Triggers on "render this diagram", "show me how this looks", "iterate on this diagram/UI until it's right", "screenshot the app".
---

# Glimpse — Visual Feedback Loop

Render an artifact to a PNG, Read the PNG, judge it, improve the source, repeat.

## The loop

1. **Render:** `glimpse <source> [--renderer NAME] [--name NAME] [--theme dark] [--size WxH]`
   - Renderer is inferred from extension (`.mmd`→mermaid, `.dot`/`.gv`→graphviz, `.d2`→d2, `.html`→web).
   - Add `--no-manifest` when rendering into a directory you want kept clean (e.g. `--out docs/diagrams`) — it skips writing `manifest.json` there.

   **Live app window (autonomous — no human needed to pick a window):**
   - `glimpse --renderer app --window "Chrome"` — finds the named app's frontmost on-screen window by itself and screenshots it (case-insensitive substring on the app name; add `--title "<substr>"` to disambiguate multiple windows). Falls back to a full-screen capture (with a `fullscreen-fallback` warning) if no window matches. The window must be visible and not minimized.
   - `glimpse --list-windows` — discover the windows (id, layer, size, app, title) to confirm what you'll capture. Rows marked `(not selectable)` can never be picked by `--window`. To capture an exact id, use `--renderer app --window-id N` (`--window-id` alone does not select the `app` renderer).
   - The printed `Window:` line echoes the resolved window so you can confirm it captured the intended one.
   - **macOS permission caveat:** capturing another app needs Screen Recording permission
     for the terminal. If it's denied, `screencapture` may still write a PNG showing only
     the desktop/wallpaper (no error) — so when you Read the PNG, confirm the app window is
     actually visible. If it's just desktop, grant: System Settings → Privacy & Security →
     Screen Recording → enable your terminal, then re-run.
   - **Windows caveat:** no permission is needed, but always Read the PNG and confirm it
     shows the window. A solid-black image means the window refused `PrintWindow` and the
     `single-color-frame` warning should have fired; a full-desktop image means the window
     lookup fell back (check for a `fullscreen-fallback:` warning).
2. **Read the printed `PNG:` path** with the Read tool — actually look at it.
3. **Check the printed warnings.** `single-color-frame:*` or a non-zero exit means the render broke (missing font, bad source, blank output) — fix the pipeline before judging design.
4. **Judge against intent:** layout, overlap, clipped/tofu text, legibility, does it communicate the thing.
5. **If not right:** edit the source, re-run (same `--name` overwrites — no accumulation), go to step 2.
6. **Stop** when it meets intent and warnings are clean. Cap at ~5–6 iterations; if not converging, stop and report what's stuck.

## Requirements

The renderer's tool must be installed; the CLI fails fast with an install hint:
- mermaid → `npm i -g @mermaid-js/mermaid-cli` (both platforms)
- graphviz → macOS `brew install graphviz` · Windows `winget install Graphviz.Graphviz`
  (then add its `bin\` to PATH — winget does not)
- d2 → macOS `brew install d2` · Windows `winget install Terrastruct.d2`
- web → Google Chrome (found via the app bundle on macOS, the App Paths registry key on Windows)
- app → macOS `screencapture` (+ Screen Recording permission) · Windows in-process GDI (no permission)

## Output

PNGs + `manifest.json` go under `.claude/tmp/ui-snapshots/glimpse/` (per-repo) or `~/.claude/ui-snapshots/glimpse/` (outside a repo). Stable name per `--name`, so re-renders overwrite.
