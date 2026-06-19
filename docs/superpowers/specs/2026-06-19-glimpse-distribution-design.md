# Glimpse Distribution (use anywhere, incl. Recorder) — Design

> Status: **Design / spec** (brainstormed + approved 2026-06-19)
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
- Skills depend only on the `glimpse` command — no repo-relative paths.
- A one-command, idempotent `scripts/install.sh` (with `--uninstall`).

### Non-Goals (explicit)
- **Windows support** — macOS-only for now (`ToolLocator` uses `which`; live-capture is
  macOS-only). The chosen mechanisms keep the Windows door open (see §3) but we do not
  port now.
- **The Recorder headless-component preview console** — separate deferred work (needs
  Recorder's UI assembly).
- **The description-optimization loop** for skill triggering — unrelated, deferred.
- Publishing to a public NuGet feed / Claude plugin marketplace — local install only.

## 3. Approach & Key Decisions

Built code uses a build-based mechanism; no-build content uses symlinks.

- **CLI → global .NET tool.** `dotnet tool install --global` puts `glimpse` on `PATH`,
  decoupled from repo location. Chosen over a bash symlink wrapper because `dotnet tool`
  is itself cross-platform — when Windows support comes later, the *same* tool works once
  `ToolLocator` is ported, whereas a bash wrapper would be a macOS dead-end. Trade-off:
  the tool is not auto-current — a CLI change needs `dotnet pack` + `dotnet tool update`
  (rare; `install.sh` wraps it).
- **Skills → symlinked.** `~/.claude/skills/{glimpse,diagram-design}` symlink to this
  repo's skill dirs. Pure content (markdown/templates/assets), so symlinks keep the repo
  canonical and edits live instantly. (Templates, `assets/user.png`, and `reference.md`
  already live inside the skill dirs, so they travel; the user icon is an embedded data
  URI, already portable.)
- **Fold the D2 icon check into the CLI** (`glimpse --check-icons`) so the skill needs no
  bundled bash script — the only dependency becomes the `glimpse` command.

## 4. Components

### 4.1 Packable CLI — `tools/Glimpse.Capture/Glimpse.Capture.csproj`

Add tool-packaging properties:
```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>glimpse</ToolCommandName>
<PackageId>Glimpse.Capture</PackageId>
<Version>0.1.0</Version>
```
No code change to the CLI's behavior. SkiaSharp's native assets must ship in the tool
package — verified at install time by actually rendering (§7), since a tool that can't
load `libSkiaSharp` would fail only at runtime.

### 4.2 `--check-icons` mode — folds in `check-d2-icons.sh`

`glimpse --check-icons <file>.d2`:
- Parse `icon:` URLs from the `.d2` (the same extraction `check-d2-icons.sh` does:
  `icon:` followed by an `http(s)` URL).
- HTTP-GET each (HEAD-equivalent), 10s timeout; print `ok <code> <url>` / `BAD <code> <url>`.
- Exit `0` if all resolve (or none present), `1` if any fail, `2` on bad usage.
- Lives in its own path — **not** invoked during a normal render (keeps the render hot
  path fast). It is a distinct CLI mode dispatched before the render flow in `Program.cs`.

This replaces `scripts/check-d2-icons.sh`. The script is removed; the template smoke test
and the skill call `glimpse --check-icons` instead.

### 4.3 Skill de-repo-pathing

Replace repo-relative invocations with the global command:
- `glimpse` skill (`SKILL.md`): `./scripts/glimpse <source> …` → `glimpse <source> …`;
  drop the `dotnet run --project tools/Glimpse.Capture` fallback line.
- `diagram-design` skill (`SKILL.md`, `reference.md`): same `glimpse` swap; and
  `./scripts/check-d2-icons.sh <file>.d2` → `glimpse --check-icons <file>.d2`. The
  `--out .claude/skills/diagram-design/examples` exemplar path stays valid (it's repo-
  relative *for maintaining the skill in this repo*; in normal use the agent omits `--out`
  and gets the per-repo default).

### 4.4 `scripts/install.sh` (and `--uninstall`)

Idempotent, one command:
1. `dotnet pack tools/Glimpse.Capture/Glimpse.Capture.csproj -c Release -o <repo>/.dist`.
2. `dotnet tool install --global --add-source <repo>/.dist Glimpse.Capture` — or
   `dotnet tool update --global --add-source <repo>/.dist Glimpse.Capture` if already
   installed (detect and pick).
3. `mkdir -p ~/.claude/skills`; for each of `glimpse`, `diagram-design`: remove any
   existing symlink/dir at `~/.claude/skills/<name>` (only if it's a symlink or absent —
   never clobber a real directory we didn't create), then
   `ln -s <repo>/.claude/skills/<name> ~/.claude/skills/<name>`.
4. Print what was installed + a verification hint.

`scripts/install.sh --uninstall`: `dotnet tool uninstall --global Glimpse.Capture` and
remove the two symlinks (only if they point into this repo). `.dist/` is gitignored.

### 4.5 Local-dev scripts

- `scripts/glimpse` (wrapper) stays the **local-dev entrypoint**: it builds + runs the
  in-repo DLL, so it always reflects the latest source (incl. the new `--check-icons`
  mode) without a re-pack. The *skills* no longer reference it (they use the global
  `glimpse`), but local tooling does.
- `scripts/check-diagram-templates.sh` keeps using `./scripts/glimpse` for rendering and
  switches its icon check from `./scripts/check-d2-icons.sh` to
  `./scripts/glimpse --check-icons`. This keeps the smoke test runnable on a fresh
  checkout **without** requiring a global install first.

## 5. What Recorder gets (no Recorder code changes)

Once installed, from `~/dev/Recorder`:
- **Diagrams:** the `diagram-design` skill triggers; `glimpse foo.mmd` renders into
  Recorder's `.claude/tmp/ui-snapshots/glimpse/` (via the existing per-repo
  `OutputLocator`).
- **Live screenshots:** `glimpse --renderer app --window "Recorder"` (Recorder running)
  captures the real app window. Screen-Recording permission caveat as documented.
- Recorder is otherwise **untouched** — no project reference, no submodule, no copied files.

## 6. Error Handling / Edge Cases

- Tool already installed → `install.sh` updates rather than erroring.
- A real (non-symlink) directory already at `~/.claude/skills/<name>` → do not delete;
  warn and skip, telling the user to resolve it.
- `dotnet`/`mmdc`/`d2` missing → the existing fail-fast hints apply; `install.sh` checks
  `dotnet` is present up front.
- Moving the repo breaks the skill symlinks (accepted trade-off) and the tool keeps the
  packaged version until re-run; `install.sh` re-fixes both.

## 7. Testing / Verification

- **Unit:** `--check-icons` URL extraction + exit-code mapping — extend the existing
  CaptureOptions/CLI tests (pure parsing parts); the HTTP call itself is integration.
- **Smoke:** `scripts/check-diagram-templates.sh` (via the in-repo `./scripts/glimpse`)
  renders all 6 templates clean and checks `cloud.d2` icons via
  `./scripts/glimpse --check-icons` — no global install required.
- **Install verification (manual, the real proof):** run `scripts/install.sh`, then from
  `~/dev/Recorder` (a different repo): (a) `glimpse <a test .mmd>` renders a real PNG —
  proves the tool + SkiaSharp natives work outside this repo; (b) confirm the
  `diagram-design` skill is listed/available there.

## 8. Build Order (for the plan)

1. `--check-icons` CLI mode (+ remove `check-d2-icons.sh`; tests).
2. `Glimpse.Capture.csproj` tool-packaging props; verify `dotnet pack` produces a tool.
3. De-repo-path the skills (`glimpse` + `diagram-design` SKILL.md/reference.md).
4. Update `scripts/check-diagram-templates.sh` to the global command; gitignore `.dist/`.
5. `scripts/install.sh` (+ `--uninstall`).
6. Run install; verify rendering from `~/dev/Recorder` (incl. a live `--window "Recorder"`
   capture) and that the skills are available there.
