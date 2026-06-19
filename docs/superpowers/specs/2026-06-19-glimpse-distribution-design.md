# Glimpse Distribution (use anywhere, incl. Recorder) — Design

> Status: **Design / spec** (brainstormed + approved 2026-06-19; revised to the
> skills-directory-plugin approach after exploring Claude Code's plugin system)
> Author: Pond + Claude
> Goal: use the `glimpse` + `diagram-design` skills (and the Glimpse CLI) from *any*
> repo on this machine — Recorder in particular — without copying or path hacks.

## 1. Problem & Motivation

The `glimpse` and `diagram-design` skills live in this repo's `.claude/skills/` and call
repo-relative paths (`./scripts/glimpse`, `dotnet run --project tools/Glimpse.Capture`,
`./scripts/check-d2-icons.sh`). So they only work *inside* the Glimpse repo. To get value
in Recorder (and elsewhere) we need the CLI on `PATH` and the skills available globally,
while keeping this repo the single source of truth.

**Key scoping insight (from brainstorm):** the high-value uses need *zero* coupling to
Recorder's code — making diagrams (diagram-design) and screenshotting the *running*
Recorder app (`glimpse --renderer app --window "Recorder"`) both work with only
distribution. The headless render of Recorder's own Avalonia components (the "preview
console") is a separate, still-deferred problem and is **out of scope** here.

## 2. Goals / Non-Goals

### Goals
- `glimpse` available as a command on `PATH` from any directory.
- `glimpse` and `diagram-design` skills available in every repo (incl. Recorder).
- This repo stays the **single source of truth**; skill edits go live with no re-sync.
- Skills + CLI ship as **one unit**, installed with a single idempotent command.
- No marketplace needed.

### Non-Goals (explicit)
- **Windows support** — macOS-only for now (`ToolLocator` uses `which`; live-capture is
  macOS-only; the `bin/` wrapper is bash). The Windows port is later (see §9).
- **The Recorder headless-component preview console** — separate deferred work.
- **A public marketplace / sharing to other machines** — that's the future Option C
  (see §9); this spec is local install only.
- **The description-optimization loop** for skill triggering — unrelated, deferred.

## 3. Approach & Key Decisions

Package the skills + CLI as a **Claude Code "skills-directory plugin"** — a single plugin
directory that Claude Code auto-loads globally when placed under `~/.claude/skills/`, with
**no marketplace required**. Chosen over (A) plain personal-skills + a separate
`dotnet tool`, and (C) a full marketplace plugin, because it:
- unifies skills **and** the CLI into one installable unit (the plugin's `bin/` dir puts
  an executable on `PATH` while the plugin is enabled);
- needs no marketplace and no `dotnet tool` pack/update dance;
- keeps SKILL.md edits **hot-reloaded** (live), matching the "repo is source of truth,
  edits go live" goal via a symlink;
- reuses cleanly toward Option C later (the same `bin/` slot takes a pre-built
  cross-platform binary when we publish).

**The CLI rides in `bin/` as a wrapper, not a packaged tool.** `bin/glimpse` is a small
bash script that runs the repo's built `Glimpse.Capture` DLL — so it's always-current
(no re-pack) and macOS-appropriate now. (Option C later swaps it for a real pre-built
binary; that work isn't wasted.)

**Fold the D2 icon check into the CLI** (`glimpse --check-icons`) so the skills depend
only on the `glimpse` command — no bundled bash script.

## 4. Components

### 4.1 Plugin layout (in this repo)

```
<repo>/plugin/
  .claude-plugin/
    plugin.json
  bin/
    glimpse                  # wrapper -> runs <repo>/tools/Glimpse.Capture built DLL
  skills/
    glimpse/                 # MOVED from .claude/skills/glimpse
      SKILL.md
    diagram-design/          # MOVED from .claude/skills/diagram-design
      SKILL.md, templates/, assets/, reference.md, examples/, evals/
```

The two skills move out of `.claude/skills/` into `plugin/skills/`. After install the
plugin serves them globally — including back in this repo — so there's no duplication.

### 4.2 `plugin/.claude-plugin/plugin.json`

```json
{
  "name": "glimpse",
  "description": "Render any UI or diagram to a PNG and design clear diagrams — the glimpse render-loop CLI plus the diagram-design skill.",
  "version": "0.1.0",
  "author": { "name": "Purin Tavilsup" }
}
```

Skills auto-discover from `skills/`; `bin/` auto-adds to `PATH` while enabled. Plugin name
`glimpse` → skills invocable as `/glimpse:glimpse` and `/glimpse:diagram-design` (the
namespace is cosmetic; triggering is by description, and the CLI command stays `glimpse`).

### 4.3 `plugin/bin/glimpse` (the CLI on PATH)

A bash wrapper that resolves its **real** location (through the install symlink) back to
the repo, then runs the built DLL — so it works whether called in-repo or via the
symlinked plugin:
```bash
#!/usr/bin/env bash
set -euo pipefail
real="$(readlink -f "${BASH_SOURCE[0]}")"          # resolve the install symlink
repo="$(cd "$(dirname "$real")/../.." && pwd)"     # plugin/bin -> plugin -> repo
dll="$repo/tools/Glimpse.Capture/bin/Debug/net10.0/Glimpse.Capture.dll"
[ -f "$dll" ] || dotnet build "$repo/tools/Glimpse.Capture/Glimpse.Capture.csproj" -v quiet >&2
exec dotnet "$dll" "$@"
```
Supports every existing flag plus the new `--check-icons` (§4.4). Replaces today's
`scripts/glimpse`.

### 4.4 `--check-icons` CLI mode — folds in `check-d2-icons.sh`

`glimpse --check-icons <file>.d2`:
- Extract `icon:` URLs from the `.d2` (the same extraction `check-d2-icons.sh` does).
- HTTP-GET each (10s timeout); print `ok <code> <url>` / `BAD <code> <url>`.
- Exit `0` all resolve (or none present), `1` any fail, `2` bad usage.
- A distinct CLI mode dispatched before the render flow in `Program.cs` — **not** in the
  render hot path. Replaces and removes `scripts/check-d2-icons.sh`.

### 4.5 Skill de-repo-pathing

- `glimpse` skill `SKILL.md`: `./scripts/glimpse <source> …` → `glimpse <source> …`; drop
  the `dotnet run --project …` fallback line.
- `diagram-design` skill `SKILL.md` + `reference.md`: same `glimpse` swap; and
  `./scripts/check-d2-icons.sh <file>.d2` → `glimpse --check-icons <file>.d2`. Exemplar
  `--out` paths update to the new skill location (`plugin/skills/diagram-design/examples`).

### 4.6 `scripts/install.sh` (and `--uninstall`)

Idempotent, one command:
1. Check `dotnet` is present; `dotnet build tools/Glimpse.Capture` once (so `bin/glimpse`
   has a DLL to run).
2. `mkdir -p ~/.claude/skills`; symlink `~/.claude/skills/glimpse` → `<repo>/plugin`
   (only replace an existing symlink or absent entry — never clobber a real directory we
   didn't create; warn and stop if one exists).
3. Make `plugin/bin/glimpse` executable.
4. Print what was linked + a verification hint (and note: run `/reload-plugins` or restart
   Claude Code to pick the plugin up the first time).

`--uninstall`: remove the symlink (only if it points into this repo).

### 4.7 Local-dev + smoke test

- `scripts/glimpse` is superseded by `plugin/bin/glimpse`; remove it (or make it a thin
  forwarder). Local dev calls `plugin/bin/glimpse` directly (no install needed — it builds
  on first run).
- `scripts/check-diagram-templates.sh` switches to `plugin/bin/glimpse` for both rendering
  and `plugin/bin/glimpse --check-icons`, so the smoke test runs on a fresh checkout
  without a global install.

## 5. What Recorder gets (no Recorder code changes)

Once installed, from `~/dev/Recorder`:
- **Diagrams:** the `diagram-design` skill triggers; `glimpse foo.mmd` renders into
  Recorder's `.claude/tmp/ui-snapshots/glimpse/` (existing per-repo `OutputLocator`).
- **Live screenshots:** `glimpse --renderer app --window "Recorder"` captures the real
  app window (Screen-Recording permission caveat as documented).
- Recorder is otherwise **untouched** — no project reference, submodule, or copied files.

## 6. Error Handling / Edge Cases

- A real (non-symlink) dir already at `~/.claude/skills/glimpse` → do not delete; warn and
  stop, telling the user to resolve it.
- `dotnet` missing → `install.sh` fails fast with a clear message; `mmdc`/`d2` missing →
  existing per-renderer fail-fast hints apply.
- Moving the repo breaks the symlink (accepted) — re-run `install.sh` to re-fix.
- First DLL build is lazy in `bin/glimpse` (and eager in `install.sh`) so the wrapper never
  runs a missing DLL.

## 7. Testing / Verification

- **Unit:** `--check-icons` URL extraction + exit-code mapping — extend the CLI tests (the
  HTTP call itself is integration).
- **Smoke:** `scripts/check-diagram-templates.sh` (via `plugin/bin/glimpse`) renders all 6
  templates clean and checks `cloud.d2` via `--check-icons` — no global install required.
- **Install verification (manual — the real proof, and the #1 risk):**
  1. **Does a *symlinked* skills-directory plugin load, and does `bin/glimpse` land on
     `PATH`?** This is the load-bearing assumption of Option B. ✅ **VALIDATED 2026-06-19
     via a throwaway spike:** a symlinked `~/.claude/skills/glimpse-spike` showed in
     `claude plugin list` as `glimpse-spike@skills-dir`, its skill was recognized by
     `claude plugin details`, and a fresh headless session resolved `which glimpse-spike`
     to the plugin's `bin/`. Note: skills-dir plugins **auto-load next session** (restart
     or `/reload-plugins`), not mid-session. **Fallback if it ever regresses:**
     `install.sh` *copies* the plugin instead of symlinking (loses always-current).
  2. From `~/dev/Recorder` (a different repo): `glimpse <a test .mmd>` renders a real PNG
     (proves the CLI + SkiaSharp natives work outside this repo), and the `diagram-design`
     skill is available there; plus a live `--window "Recorder"` capture.

## 8. Build Order (for the plan)

1. `--check-icons` CLI mode (+ remove `check-d2-icons.sh`; tests).
2. Create `plugin/` layout: `.claude-plugin/plugin.json`, `bin/glimpse`; **move** the two
   skills from `.claude/skills/` into `plugin/skills/`.
3. De-repo-path the skills (use `glimpse` / `glimpse --check-icons`); fix exemplar `--out`.
4. Update `scripts/check-diagram-templates.sh` to `plugin/bin/glimpse`; remove
   `scripts/glimpse`; gitignore nothing new (no `.dist`).
5. `scripts/install.sh` (+ `--uninstall`, copy-fallback).
6. Run install; **verify the symlinked plugin loads + `glimpse` on PATH**, then verify
   rendering + a live capture from `~/dev/Recorder`, and that the skills are available there.

## 9. Future: Option C — public marketplace (when stable + sharing)

Turn this public repo into a plugin marketplace so anyone can install Glimpse:
- Add a `marketplace.json` listing the `glimpse` plugin; users
  `/plugin marketplace add purin-tavilsup/Glimpse` → `/plugin install glimpse`.
- Replace the `bin/glimpse` bash wrapper with a **pre-built, committed** cross-platform
  binary (`dotnet publish` self-contained, or `PackAsTool`), since a marketplace can't
  build .NET. The Windows port (§2 non-goal) folds in here — same `bin/` slot, a real
  binary per platform.
- Version via `plugin.json` (pinned) or omit version (commit-SHA auto-update).
