---
name: glimpse
description: Use when you need to SEE a UI or diagram you are creating or changing — render any artifact (mermaid/graphviz/d2 diagram, HTML page, or live macOS app window) to a PNG, Read it, critique, improve, repeat. Triggers on "render this diagram", "show me how this looks", "iterate on this diagram/UI until it's right", "screenshot the app".
---

# Glimpse — Visual Feedback Loop

Render an artifact to a PNG, Read the PNG, judge it, improve the source, repeat.

## The loop

1. **Render:** `./scripts/glimpse <source> [--renderer NAME] [--name NAME] [--theme dark] [--size WxH]`
   (fast wrapper; equivalent to `dotnet run --project tools/Glimpse.Capture -- <source> …` but skips the per-call rebuild)
   - Renderer is inferred from extension (`.mmd`→mermaid, `.dot`/`.gv`→graphviz, `.d2`→d2, `.html`→web).
   - Add `--no-manifest` when rendering into a directory you want kept clean (e.g. `--out docs/diagrams`) — it skips writing `manifest.json` there.

   **Live macOS app (autonomous — no human needed to pick a window):**
   - `./scripts/glimpse --renderer app --window "Chrome"` — finds the named app's frontmost on-screen window by itself and screenshots it (case-insensitive substring on the app name; add `--title "<substr>"` to disambiguate multiple windows). Falls back to a full-screen capture (with a `fullscreen-fallback` warning) if no window matches.
   - `./scripts/glimpse --list-windows` — discover the on-screen windows (id, size, app, title) to confirm what you'll capture or to grab a `--window-id N` directly.
   - The printed `Window:` line echoes the resolved window so you can confirm it captured the intended one.
   - **Permission caveat:** capturing another app needs Screen Recording permission for the terminal. If it's denied, `screencapture` may still write a PNG showing only the desktop/wallpaper (no error) — so when you Read the PNG, confirm the app window is actually visible. If it's just desktop, grant: System Settings → Privacy & Security → Screen Recording → enable your terminal, then re-run.
2. **Read the printed `PNG:` path** with the Read tool — actually look at it.
3. **Check the printed warnings.** `single-color-frame:*` or a non-zero exit means the render broke (missing font, bad source, blank output) — fix the pipeline before judging design.
4. **Judge against intent:** layout, overlap, clipped/tofu text, legibility, does it communicate the thing.
5. **If not right:** edit the source, re-run (same `--name` overwrites — no accumulation), go to step 2.
6. **Stop** when it meets intent and warnings are clean. Cap at ~5–6 iterations; if not converging, stop and report what's stuck.

## Requirements

The renderer's tool must be installed; the CLI fails fast with an install hint:
- mermaid → `npm i -g @mermaid-js/mermaid-cli`
- graphviz → `brew install graphviz`
- d2 → `brew install d2`
- web → Google Chrome
- app → macOS `screencapture` (+ Screen Recording permission)

## Output

PNGs + `manifest.json` go under `.claude/tmp/ui-snapshots/glimpse/` (per-repo) or `~/.claude/ui-snapshots/glimpse/` (outside a repo). Stable name per `--name`, so re-renders overwrite.
