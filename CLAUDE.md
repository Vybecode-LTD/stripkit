# CLAUDE.md — StripKit

> Version 1.5.1 · last-updated 2026-10-15 · last-audit 2026-07-04

## Identity
StripKit turns a single transparent PNG into an animated **filmstrip** (sprite sheet) for audio-plugin GUI
controls (rotary knobs, faders, sliders, meters, buttons and toggles), consumed by a JUCE-style
LookAndFeel filmstrip loader. Stack: .NET 9, Avalonia 11.3, CommunityToolkit.Mvvm, SkiaSharp 3.119.2,
Inno Setup. Public, MIT-licensed. Repo `Vybecode-LTD/StripKit`. A Windows desktop download app,
distributed as a signed GitHub Release.

## Current State
- **Released:** v1.6.0 on 2026-07-11 (`98efb65`): an app-wide style and usability pass and the Generate
  tab's one-click "Build kit". The app has six tabs: Create, Import, Batch, Skin, Generate, Assemble.
- **Docs lag the release:** docs/HANDOFF.md and the session log stop at v1.5.1 (2026-07-04); nothing
  records the v1.6.0 work except the commits and docs/CHANGELOG.md. Reconcile before relying on them.
- **2026-10-15:** this file was leaned. Every section moved verbatim (map below).

Next steps, blockers, and warnings live in docs/HANDOFF.md.

## Commands
| What | Command |
|---|---|
| Run | `dotnet run --project src/StripKit` (needs the .NET 9 SDK) |
| Build | `dotnet build` |
| Test (xUnit, headless Avalonia, golden images) | `dotnet test tests/StripKit.Tests` |
| Release (the only release creator; read docs/PACKAGING.md first) | `scripts/Invoke-Release.ps1` |

## Architecture (brief)
- One idea, six component types: a source image becomes N frames through `SkiaFilmstripRenderer`, stacked
  vertically into one strip, frame 0 = minimum value.
- MVVM with DI and compiled bindings. View models never reference Avalonia UI types, so they're testable.
- Layered-source import (Svg.Skia for SVG, Magick.NET Q16-HDRI for PSD and 16-bit/EXR) and the AI SVG
  generator (OpenAI, Gemini, Claude or any OpenAI-compatible endpoint, keys encrypted with DPAPI) are
  app-only, never in `FilmstripEngine.cs`.
- Release: `Invoke-Release.ps1` builds, signs (Azure Trusted Signing) and commits the installer;
  `auto-release.yml` is the sole `gh release create`; the website reads the live release.
- Full detail: docs/ARCHITECTURE.md (the moved Stack and Architecture sections are appended at its end).

## Critical Gotchas
1. **Rotary frame i sits at `start + (end - start) * i / (N - 1)`.** The `(N - 1)` lands the last frame on
   the max; default sweep 270° (−135° to +135°).
2. **Untrusted SVG** (AI replies, imported files) goes through `SafeXml.Parse` only, never a bare
   `XDocument.Parse`.
3. **Commit the feature work before running the release script.** Its "Release" commit stages only version
   files and the installer; the v1.2.0 source was once orphaned that way (the script now refuses a dirty
   tree).
4. **Sign with signtool and the Trusted Signing dlib,** not AzureSignTool (it returns 403).
5. **Do NOT rewrite `SkiaFilmstripRenderer` or re-scaffold the project;** extend it.
6. **No `System.Drawing`, no `.Result`/`.Wait()`;** `async void` only for event handlers; source-generator
   classes are `partial`; `Path.Combine`, never raw separators.
7. **Use the Depth design-system keys** (`App.axaml`, `src/StripKit/Depth/Depth.axaml`); don't hard-code
   hex, and keep monospace for numbers only.
8. **CI fails below 70% line coverage,** and runs on every push to `main`. Mark a docs-only commit
   `[skip ci]`.
9. **A SkiaSharp version conflict with Avalonia:** align the csproj's SkiaSharp to Avalonia's transitive
   version (`dotnet list package --include-transitive`).

## Conventions
- Frames stack vertically; 32-bit RGBA, transparent; 32/64/128 frames; HiDPI export via `HiDpiScale`
  (`@2x` default); knob art keeps about 10% transparent margin.
- Use the repo's own skills in `.claude/skills/` (list in docs/WORKFLOW.md).
- After delivering something useful, suggest follow-on skills; a new skill ships as a `.skill` file with a
  description under 1,024 characters, linted first.
- Python helpers: `python -m pip`.
- **At the end of every session,** update Current State above and docs/HANDOFF.md. The old "Last completed
  task" log continues in docs/archive/SESSION-LOG.md, newest first.
- **Sections moved on 2026-10-15,** headings unchanged: Stack and Architecture → appended to
  docs/ARCHITECTURE.md; Release → appended to docs/PACKAGING.md; What this is, Start here, OSS, Run/build,
  skills, Filmstrip, House and owner conventions → docs/WORKFLOW.md; Last completed task →
  docs/archive/SESSION-LOG.md. The whole previous file: docs/archive/CLAUDE.pre-lean-2026-10-15.md.

## Project Map
| Topic | File | Read this when... |
|---|---|---|
| Current handoff | docs/HANDOFF.md | starting a session or picking up work |
| Where everything lives | docs/SOURCE_MAP.md | locating code |
| Architecture and stack | docs/ARCHITECTURE.md | changing structure or the renderer |
| Workflow, conventions, skills | docs/WORKFLOW.md | before a first change, or styling the UI |
| Packaging and release | docs/PACKAGING.md | before any release |
| Roadmap | docs/ROADMAP.md | deciding what's next |
| Bugs | docs/BUGS.md | triaging or fixing a defect |
| Testing | docs/TESTING.md | writing or running tests |
| Changelog | docs/CHANGELOG.md | needing recent history of a change |
| Audit log | docs/AUDIT-LOG.md | checking what an audit found |
| New-session prompt | docs/KICKOFF.md | starting cold |
| Session log, the pre-lean brief | docs/archive/ | needing older context |

## Process references
- `C:\DEV\SOFTWARE_RELEASE.md`: read before releasing (CI is the single release creator).
- `C:\DEV\TESTING_PROCEDURES.md`: read when adding test infrastructure.
- `C:\DEV\DEBUG_PROTOCOL.md`: read when a second fix for the same symptom fails.
- `C:\DEV\VERSION_CONTROL.md`: read before rewriting history or cleaning up a secret.
