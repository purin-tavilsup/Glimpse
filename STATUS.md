# STATUS — Glimpse

> Goal: tools so an agent can *see* rendered UI/diagrams (read PNGs) and iterate.
> Repo: https://github.com/purin-tavilsup/Glimpse (public, MIT). Last updated: 2026-10-01.

## ✅ SHIPPED: cross-platform (macOS + Windows) — PR #2, merged 2026-10-01 (`378d243`)

**Merged as PR #2.** First CI run green on both legs (147 tests each, RealDesktop excluded).
Closes deferred item 7 below.

- Spec: `docs/superpowers/specs/2026-08-01-cross-platform-windows-design.md`
- Plan: `docs/superpowers/plans/2026-08-01-cross-platform-windows.md`
- Ledgers (gitignored, every commit, ruling and verification): `.superpowers/sdd/2026-08-01-cross-platform-windows/progress.md`
  and `.superpowers/sdd/2026-10-01-final-review-fixes.md`. **Read them first on resume.**

**What the branch delivers:** single `net10.0` target. `ToolLocator` resolves tools by a managed
PATH scan (npm ships `mmdc` and `mmdc.cmd`; only the latter launches). `IAppCapturer` seam:
macOS keeps `screencapture`, Windows captures in-process (`PrintWindow` + GDI → `BgraPngEncoder`).
`WindowsWindowFinder` via `EnumWindows`; `WindowId` widened to `long` for HWND. Windows
distribution: `glimpse.cmd`, `install.ps1` (junction, no admin), `PackAsTool`. CI matrix
`windows-latest` × `macos-latest`; desktop-bound tests are `Category=RealDesktop` and run locally
only. `--list-windows` marks rows `WindowSelector.IsSelectable` rejects. Docs are per-platform.

**Final-review fix round (2026-10-01):** untitled windows are listed (the Recorder's Avalonia
prompt has `Title=""` and was invisible); the engine waits for a PNG a tool writes just after it
exits (headless Chrome on Windows, ~1 in 5 renders); `web` passes Chrome a `file://` URL, so a
relative source works; the sidecar is UTF-8 read under code page 65001 and the installer checks
it under 437 and 65001; a stale sidecar falls back to the lexical path; uninstall removes it.
Suite **143 passed / 0 failed / 2 skipped** on Windows.

**Known gaps (not blocking this PR):**
- CI does not prove parity: `MacWindowFinder` has no tests and the Windows-only facts skip on macOS.
- Real-capture orientation has no automated guard (flipping `biHeight` leaves every test green).
- `ToolLocator` on macOS accepts a file without the exec bit, where `which` did not (review M-5).
- Untitled windows are now selectable, so without `--title` a visible untitled popup of the same
  app (an open menu, an overlay) in front of the real window would win. None on this box; a later
  tie-break could prefer the first titled match. ⚠️ **Do not add it naively:** on 2026-10-01 the
  Recorder's untitled sign-in prompt sat in front of its titled "Setting Up…" window and the
  untitled one was the right capture; "prefer titled" would have picked the wrong window.
- An existing Windows install with a **non-ASCII** repo path keeps an old-format sidecar that the
  new wrapper rejects; re-run `scripts/install.ps1` once after updating.
- Pre-existing CLI defects, same on both OSes: `CaptureOptions.Parse` sits outside the try/catch
  (a bad flag prints a stack trace); `outcome with { Warnings }` never recomputes `ExitCode`, so a
  `fullscreen-fallback` warning cannot raise it; the BitBlt fallback fires only when `PrintWindow`
  returns false, not on a single-colour result as spec §3.2 says.
- `scripts/check-diagram-templates.sh`: `cloud.d2` failed on D2 0.9 — three GCP
  icons (Container Registry, Cloud SQL, Pub/Sub) use an SVG style D2's importer rejects; fixed on
  branch `fix/d2-cloud-icon` (bundled cleaned copies), not pushed yet.

## Current state (main): ✅ TWO HALVES SHIPPED + PUBLISHED

Everything below is merged to `main` and pushed to the personal GitHub repo
(`purin-tavilsup/Glimpse`), authored under the personal identity. Branch `main`
clean; .NET suite green (78 passed / 2 skipped — pre-existing Avalonia skips).

### 1. Glimpse render-loop harness (the "see" half)
Renderer-agnostic visual-feedback harness: any UI/diagram → PNG → agent Reads it →
critique → improve → repeat. Generic command→PNG renderer contract.
- `Glimpse.Core` — `RendererRegistry` + `BuiltInRenderers` (mermaid/graphviz/d2/web/
  screencapture), `ToolLocator`, `RenderCommandBuilder`, `ProcessRunner` + `RenderEngine`
  (resolve→run→analyse→outcome, exit 0/1/2), `PngAnalysis` (blank/single-color), manifest
  + `SnapshotWriter` (stable-name `PngPath`, `Prune`, `--no-manifest`).
- `tools/Glimpse.Capture` — the CLI (`--renderer --name --out --size --theme --window-id
  --prune --no-manifest`).
- `glimpse` skill — packages the render→Read→judge→iterate loop.
- **Avalonia engine left untouched** (parked, still present as an optional renderer).
- Built subagent-driven: 8 TDD tasks, per-task + whole-branch review (caught a real
  ProcessRunner stdout deadlock). Spec/plan in `docs/superpowers/`.

### 2. diagram-design skill (the "what good looks like" half)
Agent skill that routes a request to the right diagram type and applies a house style
derived from Pond's reference diagrams, rendering + verifying via glimpse.
- Routes: architecture/C4 (mermaid layered + D2 icon-cloud), sequence (auth flows),
  state machine, ER, flowchart. `SKILL.md` + 6 templates + `reference.md` + exemplars.
- House style: soft-fill labeled containers, cylinders for datastores, meaningful
  call/return arrow colors, bundled **person icon** (`assets/user.png`, embedded as a
  portable data URI in mermaid). D2 icons via Terrastruct (GCP/GitHub verified).
- Validated with-skill vs baseline (16/16 vs 13/16; wins on notation discipline +
  house style; baseline already strong at type routing). Brainstorm → spec → 2-lens
  subagent spec review → skill-creator build.

### Dev ergonomics (just added)
- `plugin/bin/glimpse` — CLI wrapper (runs the built DLL, lazy-builds if missing; no
  rebuild env var — delete the DLL to force a fresh build).
- `scripts/check-diagram-templates.sh` — smoke test: renders all 6 templates via
  `plugin/bin/glimpse` (incl. `--check-icons` for D2 icons), fails on any non-clean render.

## Deferred / next actions
1. **diagram-design proof + reach:**
   - ~~ambiguous-routing test~~ — ✅ **DONE.** 3 trap prompts (keywords pulling toward the
     wrong type) × with-skill/baseline. **Result: both arms routed all 3 correctly,
     including baseline.** Negative finding: the router does NOT earn its place on
     type-selection — a capable base model already infers the right type from semantics.
     The skill's real, demonstrated value is **house style + notation discipline**
     (iteration-1: 16/16 vs 13/16), not routing. Consider reframing SKILL.md to lead with
     style/consistency; keep the router as cheap insurance for weaker models / truly
     ambiguous asks.
   - Still open: the *description-optimization* loop for trigger accuracy (20 trigger-eval
     queries already drafted).
2. ~~D2 silent-fail icons~~ — ✅ **DONE (accuracy push).** `scripts/check-d2-icons.sh`
   HTTP-checks every `icon:` URL deterministically (a dead URL renders nothing in D2 with
   no error); wired into the diagram-design skill flow + the template smoke test. Remaining
   nice-to-have: broaden the verified-URL examples in `reference.md` to AWS/Azure.
3. ~~`app` renderer (live-window screenshot)~~ — ✅ **DONE (autonomy push).** `WindowInfo`
   + pure `WindowSelector` + `MacWindowFinder` (CoreGraphics P/Invoke). CLI:
   `--window "Name"` (autonomous frontmost-window lookup by app name, `--title` to
   disambiguate, full-screen fallback), `--list-windows` (discovery), still `--window-id`.
   Verified live (captured a real Chrome window by name). Screen-Recording permission
   caveat documented in the glimpse skill.
4. **Glimpse review minors** (non-blocking): JSON renderer-override merge path untested;
   no integration test over `Program.cs` manifest-write path.
5. Polish: dynamic-view (numbered-badge) exemplar; cloud template labels GCP "Container
   Registry" icon as "Artifact Registry" (closest match).
6. ~~Distribution~~ — ✅ **DONE + verified from Recorder.** Option B (skills-directory plugin
   `plugin/`, symlinked into `~/.claude/skills/glimpse` via `scripts/install.sh`, no
   marketplace). `glimpse --check-icons` folds in the old script. **Verified from
   `~/dev/Recorder`:** `glimpse` on PATH, a diagram rendered to a real PNG, and both skills
   (`glimpse:glimpse`, `glimpse:diagram-design`) available there. macOS-only. Spec/plan:
   `docs/superpowers/{specs,plans}/2026-06-19-glimpse-distribution*`. (Live `--window
   "Recorder"` capture not run — needs the app running + Screen-Recording permission.)
7. ~~**Cross-platform (Windows)**~~ — ✅ **DONE, merged as PR #2.** See the section at the top
   of this file. Scope grew beyond the original sketch: full parity including live-window
   capture, from a single `net10.0` target. Note the original note here was wrong on one
   point — `ToolLocator` did **not** become `which`→`where`; it stopped shelling out at all
   in favour of a managed PATH scan, because npm ships both `mmdc` and `mmdc.cmd` and only
   the latter is launchable, so a naive lookup finds the broken one.
8. **🔭 Option C — publish as a marketplace plugin (later, when stable + sharing).** Add a
   `marketplace.json` so anyone can `/plugin marketplace add purin-tavilsup/Glimpse` →
   `/plugin install glimpse`. Needs a *pre-built, committed* cross-platform binary in the
   plugin `bin/` (marketplace can't build .NET). The Windows port's `PackAsTool` does **not**
   fill that slot: the package is CLI-only (no skills), lands in the gitignored `artifacts/`,
   and `glimpse.cmd` only runs a repo-built DLL. Still open. Detail in the distribution spec §9.

## Environment notes
- **macOS:** `mmdc` (mermaid-cli) + `d2` (`brew install d2`) installed. Chrome present for `web`.
- **Windows (added 2026-08-01):** `mmdc` 11.16.0 (npm, lands as `mmdc.cmd`), `d2` v0.7.1 and
  `dot` 15.1.0 (winget). **Graphviz's `bin\` was appended to the user PATH by hand — winget
  does not do it.** Chrome + Edge present.
- ⚠️ This repo had **no local git identity**, so it was inheriting the Mimica work identity
  into a *public personal* repo. A local override is now set to
  `Purin Tavilsup <19279956+purin-tavilsup@users.noreply.github.com>`, matching every prior
  commit — but **a fresh clone reverts**, so re-set it.
- **Windows install is LIVE on this box (Task 8, 2026-08-03):** `~/.claude/skills/glimpse` is a
  **directory junction** → `C:\personal\Glimpse\plugin`, and `plugin/bin/glimpse.repo` (gitignored)
  holds the resolved repo path the batch wrapper needs. Re-run `pwsh -File scripts/install.ps1`
  after moving the repo, or the sidecar points at a stale path. `where.exe glimpse` still
  unverified — it is Claude Code that puts the plugin `bin/` on PATH, so it needs a session restart.
- Python 3.13 installed (`/opt/homebrew/bin/python3.13`) — needed for the skill-creator
  eval viewer (`generate_review.py`, requires 3.10+).
- `gh` has two accounts; this repo's git identity + push routing use `purin-tavilsup`.

## Artifacts
- Specs/plans: `docs/superpowers/{specs,plans}/`
- SDD ledgers: `.superpowers/sdd/<date-slug>/progress.md` (gitignored)
- Eval workspace (gitignored): `.claude/skills/diagram-design-workspace/`
