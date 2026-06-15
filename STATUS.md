# STATUS — UI Snapshot Tool

> Goal: a general-purpose tool so Claude can *see* rendered UI by reading PNGs.
> Last updated: 2026-06-15.

## Current state: 📋 DESIGN COMPLETE — awaiting Pond review (tomorrow)

- Brainstormed end-to-end (scope, hybrid capture, modular core + per-framework modules).
- **Spec written + committed** (`ec4c2bc`):
  `docs/superpowers/specs/2026-06-15-ui-snapshot-tool-design.md`
- **Triumvirate-reviewed** (Architect / .NET-SWE / QA) — feedback folded in:
  - Per-app **preview console** (`dotnet run`) instead of a global reflection-tool
    (kills ALC/version/native-lib risk).
  - Headless flow needs dispatcher + **settle loop** (not just one render tick).
  - Engine returns **pixels**; Core owns all file I/O + manifest.
  - Determinism guards: disable animations, pin culture, font-tofu detection,
    theme **differential** test, manifest with per-entry status/warnings.
  - **DECIDED:** net10.0 + matching Avalonia 11.3.x, enforced fail-fast.

## Next actions

1. **Pond reviews the spec** (open questions in §9): tool name, output location
   (per-repo vs central), repo home (`~/dev/ui-snapshot-tool` vs inside Recorder).
2. After approval → `writing-plans` skill to produce the implementation plan.
3. Then implement in build order (§10): Abstractions → engine → core → scratch
   console + tests → **Recorder preview console (e2e gate)** → screencapture wrapper.

## Notes

- No implementation yet (plan only, by request).
- First real consumer = Recorder Avalonia views (`Mimica.Recorder.UI`, net10.0).
- Future: skill packaging (`snapshot-ui`), web/mobile modules, visual diff.
