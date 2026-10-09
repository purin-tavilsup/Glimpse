# Glimpse Marketplace Distribution (install anywhere, Windows + macOS) — Design

> Status: **Design / spec** (agreed 2026-10-09)
> Author: Pond + Claude
> Goal: anyone, on Windows or macOS, installs Glimpse with one `/plugin install`, and every
> machine runs the same released CLI. Realises "Option C" from
> [`2026-06-19-glimpse-distribution-design.md` §9](2026-06-19-glimpse-distribution-design.md).

## 1. Problem & Motivation

Today the plugin works only on a machine with a clone: `plugin/bin/glimpse` (and `glimpse.cmd`) run the CLI built
from this repo's source, and `install.sh` / `install.ps1` link `~/.claude/skills/glimpse` to `plugin/`. A
marketplace install copies just the plugin, so there is no source to build and `glimpse` fails.

Anyone driving and checking UIs with an agent should get this out of the box on both OSes, with every machine seeing the same
Glimpse. It should depend on a released open-source tool, not on someone's clone.

## 2. Goals / Non-Goals

### Goals
- A marketplace install of the `glimpse` plugin works on Windows and macOS with no clone and no build.
- Every install runs **one pinned, released** CLI version; upgrading is a deliberate version bump.
- A clone keeps today's behaviour: edits to the CLI go live without packing anything.
- Releases are published without a stored API key.

### Non-Goals
- Moving the repo to an organisation (an owner decision; §6 notes the one thing it touches).
- Listing Glimpse in any third-party marketplace — consumers do that on their side.
- Removing `install.sh` / `install.ps1`; they stay the developer path.

## 3. Decision: NuGet tool + `dotnet dnx`

The CLI is already packable as a .NET tool (`PackAsTool`, command `glimpse`, ID `Glimpse.Capture`). Releases publish
it to nuget.org; the plugin's wrappers run the pinned version with `dotnet dnx`, which downloads a tool package on
first use and runs it without installing it.

Chosen over a pre-built binary committed to the repo (the June spec's first idea) because it:
- keeps binaries out of git, and needs no per-platform build matrix — one package runs on both OSes;
- pins exactly: the wrapper names the version, and NuGet versions are immutable;
- caches after the first run (about 1 s afterwards, measured on Windows), and works offline from then on.

Proven on Windows before writing this: a locally packed `Glimpse.Capture` 0.1.0, run through
`dotnet dnx Glimpse.Capture@0.1.0 --yes --add-source <folder>`, listed windows and rendered a Mermaid diagram.

**Prerequisite for users:** the .NET 10 SDK (`dnx` ships with it). The wrapper says so when `dotnet` is missing.

**Package ID:** `Glimpse.Capture` is free on nuget.org. The unrelated, older ASP.NET `Glimpse.*` packages do not
reserve the `Glimpse.` prefix, so nothing blocks it; search results are noisier, which the README can address.

## 4. Design

### 4.1 Wrappers (`plugin/bin/glimpse`, `plugin/bin/glimpse.cmd`)

```
repo found (sidecar or lexical ../..) and has tools/Glimpse.Capture?  →  today: build once, run the in-repo DLL
otherwise (a marketplace install)                                        →  dotnet dnx Glimpse.Capture@<pin> --yes -- <args>
```

The pin is the single line in `plugin/bin/glimpse.version`. `--yes` skips `dnx`'s confirmation prompt, which would
hang an agent.

### 4.2 One version, checked at release

`<Version>` in `Glimpse.Capture.csproj`, `version` in `plugin/.claude-plugin/plugin.json` and
`plugin/bin/glimpse.version` must all equal the release tag. The release workflow fails on any mismatch, so a plugin
can never pin a version that was not published with it.

### 4.3 `--help` and `--version`

Today any unknown argument (`--help` included) crashes with an unhandled `ArgumentException`. The CLI gains `--help`
(usage, exit 0) and `--version` (the assembly version, exit 0); other bad arguments print the error and usage and
exit 2. The release smoke test uses `--version` to prove the right package ran.

### 4.4 Release workflow (`.github/workflows/release.yml`, on `v*` tags)

Modelled on Nokpirab's:
1. Verify tag == the three versions (§4.2).
2. Restore, build, test (as CI), pack to `artifacts/`.
3. **Smoke test on `windows-latest` and `macos-latest`:** the plugin's own wrapper, run from a copy of `plugin/` with
   NuGet pointed at the packed feed, prints exactly `<v>` for `--version` on its first and second run. Desktop
   capture is not tested here (CI runners have no interactive desktop).
4. `NuGet/login@v1` (Trusted Publishing, `id-token: write`, user `Exconeer`) immediately before
   `dotnet nuget push --skip-duplicate`.

### 4.5 Marketplace manifest

`.claude-plugin/marketplace.json` at the repo root lists the `glimpse` plugin with `"source": "./plugin"`, so
`/plugin marketplace add <owner>/Glimpse` then `/plugin install glimpse@<marketplace-name>` works. Another marketplace
can list the plugin with a `git-subdir` source, `"path": "plugin"`, pinned by `ref`/`sha`.

## 5. Delivery — one PR each

1. **docs:** this spec.
2. **feat(cli):** `--help`, `--version`, clean bad-argument errors (tests first).
3. **feat(plugin):** wrappers fall back to `dnx`; `glimpse.version`; versions aligned.
4. **ci:** release workflow (§4.4).
5. **feat:** marketplace manifest + README "Install" section for both paths.

Then, by the owner: create the Trusted Publishing policy on nuget.org (owner `Exconeer`, this repo,
`release.yml`) and push tag `v0.1.0`.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A user without the .NET 10 SDK | The wrapper's clear message; the README lists the prerequisite |
| The repo moves to an organisation after the policy exists | Trusted Publishing is tied to owner/repo/workflow — update the policy right after the transfer |
| First run with no network | `dnx` needs NuGet once; afterwards it runs from the cache |
| macOS `dnx` behaviour differs | Covered by the macOS smoke job before every publish |
| On macOS the run that downloads the package prints `Skipping NuGet package signature verification.` on stdout, before the output (also for the nuget.org package) | The bash wrapper downloads the package in a silent run first. The release smoke test runs the real wrappers from a plugin-only copy and requires every run, the first included, to print only the version |
