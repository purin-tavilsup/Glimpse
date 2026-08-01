# STATUS — Glimpse

> Goal: tools so an agent can *see* rendered UI/diagrams (read PNGs) and iterate.
> Repo: https://github.com/purin-tavilsup/Glimpse (public, MIT). Last updated: 2026-08-01.

## 🚧 IN FLIGHT: cross-platform (macOS + Windows) — branch `feat/cross-platform-windows`

**Parked 2026-08-01 after Task 6 of 10. Nothing pushed; 13 commits local.**
Closes deferred item 7 below and unblocks item 8.

- Spec: `docs/superpowers/specs/2026-08-01-cross-platform-windows-design.md`
- Plan: `docs/superpowers/plans/2026-08-01-cross-platform-windows.md`
- **Authoritative checkpoint (gitignored):** `.superpowers/sdd/2026-08-01-cross-platform-windows/progress.md`
  — every task's commits, all deferred minors, and the rulings made. **Read it first on resume.**

**Done (Tasks 1-6), each TDD + task-reviewed:** `.gitattributes` line-ending guard ·
`ToolLocator` resolves tools by managed PATH scan instead of `/usr/bin/which` ·
`WindowId` widened to `long` for HWND · `IAppCapturer` seam with macOS behaviour unchanged ·
`WindowsWindowFinder` via `EnumWindows` · `WindowsAppCapturer` via `PrintWindow` + GDI,
encoding through a new cross-platform `BgraPngEncoder`.

**Suite: 126 passed / 0 failed / 2 skipped on Windows** (baseline was 7 failures, all from
one hardcoded `/usr/bin/which`). `WindowSelector` never touched — Windows maps onto its
existing contract.

**Proven for real on Windows:** a mermaid diagram rendered (1124x993, via `mmdc.cmd`), and a
live Chrome window captured with correct content, orientation and edges.

**Remaining: Tasks 7-10** — `PlatformSupport` + de-branch `Program.cs`; distribution
(`glimpse.cmd`, `install.ps1`, `PackAsTool`); CI matrix; docs. Then a final whole-branch review.

⚠️ **Task 7 must verify the window→capture path end to end through the CLI** (`--list-windows`,
then `--window "chrome"`, then Read the PNG). That is the first exercise of the wired path, and
Task 5's mapping has no automated coverage.

⚠️ **Known residual gap:** real-capture *orientation* has no automated guard — flipping the
DIB's `biHeight` sign today leaves every test green. A deterministic test needs a controlled
fixture window; judged not worth a flaky test in the suite that gates the remaining tasks.

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
7. **🚧 Cross-platform (Windows) — IN FLIGHT, 6 of 10 tasks done.** See the section at the top
   of this file. Scope grew beyond the original sketch: full parity including live-window
   capture, from a single `net10.0` target. Note the original note here was wrong on one
   point — `ToolLocator` did **not** become `which`→`where`; it stopped shelling out at all
   in favour of a managed PATH scan, because npm ships both `mmdc` and `mmdc.cmd` and only
   the latter is launchable, so a naive lookup finds the broken one.
8. **🔭 Option C — publish as a marketplace plugin (later, when stable + sharing).** Add a
   `marketplace.json` so anyone can `/plugin marketplace add purin-tavilsup/Glimpse` →
   `/plugin install glimpse`. Needs a *pre-built, committed* cross-platform binary in the
   plugin `bin/` (marketplace can't build .NET) — same slot the Windows port (item 7)
   fills, so do them together. Detail in the distribution spec §9.

## Environment notes
- **macOS:** `mmdc` (mermaid-cli) + `d2` (`brew install d2`) installed. Chrome present for `web`.
- **Windows (added 2026-08-01):** `mmdc` 11.16.0 (npm, lands as `mmdc.cmd`), `d2` v0.7.1 and
  `dot` 15.1.0 (winget). **Graphviz's `bin\` was appended to the user PATH by hand — winget
  does not do it.** Chrome + Edge present.
- ⚠️ This repo had **no local git identity**, so it was inheriting the Mimica work identity
  into a *public personal* repo. A local override is now set to
  `Purin Tavilsup <19279956+purin-tavilsup@users.noreply.github.com>`, matching every prior
  commit — but **a fresh clone reverts**, so re-set it.
- Python 3.13 installed (`/opt/homebrew/bin/python3.13`) — needed for the skill-creator
  eval viewer (`generate_review.py`, requires 3.10+).
- `gh` has two accounts; this repo's git identity + push routing use `purin-tavilsup`.

## Artifacts
- Specs/plans: `docs/superpowers/{specs,plans}/`
- SDD ledgers: `.git/sdd/progress.md`
- Eval workspace (gitignored): `.claude/skills/diagram-design-workspace/`
