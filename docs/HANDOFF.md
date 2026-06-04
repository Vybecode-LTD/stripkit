---
document: HANDOFF
version: 0.6.0
last-updated: 2026-06-04
last-audit: 2026-06-04
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into
animated filmstrip sprite sheets for audio-plugin GUI controls (knobs, faders,
sliders, meters). Stack: .NET 9 / Avalonia 11.3 / SkiaSharp / Inno Setup.

**Phase:** Post-ship (v0.6.0 live). Repo is public, MIT-licensed.

## Last Session (2026-06-04)

### What was done
- Rewrote ARCHITECTURE.md (full source-verified deep-dive), PACKAGING.md
  (~640 lines, exhaustive release-pipeline reference with two DO-NOT-REINTRODUCE
  bug guards), and ROADMAP.md (completed-phases history + vNext feature backlog).
- Added website README.md (site-maintenance guide) to StripKit-Website.
- Open-sourced under MIT: LICENSE, public README, .csproj metadata,
  CONTRIBUTING.md, CI workflow, issue/PR templates, repo About sidebar (commit 41050a5).
- Audit code fixes (commit 0aaa257): BatchViewModel CTS disposal, Meter type added
  to batch ComponentTypes, MainWindow timer cleanup on close, Assets/README.txt
  corrected, KICKOFF.md frame-count corrected, FilmstripEngine.cs MIT header.
- All 7 historical bugs resolved; 0 open bugs.

### Decisions made
- Inno Setup replaces Velopack permanently. No in-app auto-update.
- Website changelog (updates.json) is intentionally decoupled from docs/CHANGELOG.md.
- Meter-specific fields (SegmentCount, FillDirection, colours) render from
  FilmstripSettings defaults in batch until a dedicated batch-meter UI is built.

### What was NOT finished
- Website not deployed (stripkit.pro is still a placeholder).
- Batch-meter UI is unbuilt (meter type exists in dropdown; field exposure is vNext).

## Current State

### Working
- v0.6.0 live: https://github.com/Vybecode-LTD/stripkit/releases/tag/v0.6.0
  (StripKit-Setup-0.6.0-x64.exe, ~33.5 MB, self-contained, no SDK needed).
- Tests: 49/49 green. CI runs on every push/PR.
- 3-stage release pipeline proven end-to-end (scripts/Invoke-Release.ps1 + auto-release.yml).
- Website repo Vybecode-LTD/StripKit-Website: built and pushed, not deployed.

### Known Issues / Limitations (not bugs)
- VirusTotal ~4/71 heuristic FPs on the unsigned installer. Not a real bug.
- Batch tab: meter-specific settings fields not yet exposed in the template UI.
- Importer cannot resample frame *count* (dimension-based detection only).
- Manifest UI emits a single control only.

## How to Ship the Next Release

```powershell
pwsh scripts/Invoke-Release.ps1          # default: patch bump -> 0.6.1
pwsh scripts/Invoke-Release.ps1 -Bump minor
gh run watch                             # CI is the sole release creator
```

Full flow: Stage 1 (local script) -> Stage 2 (CI VirusTotal + gh release create) ->
Stage 3 (website reads live release). Details: docs/PACKAGING.md.

## Next Steps (priority order)

1. Deploy website to stripkit.pro — enable GitHub Pages on StripKit-Website or
   point domain. (User action, no code change.)
2. Code-signing certificate — clears VirusTotal FPs and SmartScreen prompt.
   The .iss has a SignTool hook; see docs/PACKAGING.md section 13.
3. Per-release upkeep — add a plain-language entry to StripKit-Website/updates.json
   alongside each docs/CHANGELOG.md entry. One manual cross-repo step per release.
4. vNext features (see docs/ROADMAP.md). Top three by priority:
   - Code/component export (copy-pasteable JUCE/web LookAndFeel boilerplate)
   - Layer-aware animation + auto-pointer extraction
   - Procedural value-arc / fill-ring generator

## Warnings for Next Agent

- **UTF-8 discipline in release scripts.** PS 5.1 Get-Content without -Encoding UTF8
  corrupts em-dashes. Fixed in f1b68d3, documented in PACKAGING.md section 9.
  Always use `pwsh` (PS 7) and explicit UTF-8.
- **Never inline changelog body into gh release create --notes "..."**. Backticks
  trigger shell command substitution. Always use --notes-file. Fixed in a408bc9.
- **FilmstripEngine.cs (repo root) is a hand-maintained mirror** of
  SkiaFilmstripRenderer + Models. It is NOT compiled by the app. Sync it if
  renderer math changes.
- actions/checkout@v4 runs Node 20 (GitHub deprecation mid-2026) — upgrade to v5
  at next convenience.
- Do NOT rewrite SkiaFilmstripRenderer, change the (N-1) angle divisor, move VM
  logic into code-behind, or reference Avalonia UI types from view models.

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules.
2. `docs/SOURCE_MAP.md` — where everything lives.
3. `docs/ARCHITECTURE.md` — deep design reference (updated this session).
4. `docs/PACKAGING.md` — full release-pipeline reference with bug guards.
