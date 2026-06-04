---
document: HANDOFF
version: 0.7.0
last-updated: 2026-06-04
last-audit: 2026-06-04
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into
animated filmstrip sprite sheets for audio-plugin GUI controls (knobs, faders,
sliders, meters). Stack: .NET 9 / Avalonia 11.3 / SkiaSharp / Inno Setup.

**Phase:** Post-ship (v0.7.0 live). Repo is public, MIT-licensed. Two of the three
★ vNext bets are now shipped; the third (layer-aware animation) is the next feature.

## Last Session (2026-06-04) — v0.7.0: two ★ features + ship

### What was done
- **★ Value-arc / fill-ring generator** (vNext ★ #1). A Serum/Vital-style fill arc
  composited onto knob frames that tracks the value (lit arc sweeps
  `Start → Start+(End−Start)·t`, concentric with the rotation pivot). Optional dim
  track, sweep gradient, glow, round/butt caps. 11 Skia-free `FilmstripSettings`
  fields gated on `ShowValueArc` (**off by default → existing output byte-identical**;
  all prior golden baselines unchanged). New `RenderValueArc` + `StrokePaint` in
  `SkiaFilmstripRenderer`, mirrored into `FilmstripEngine.cs`; "VALUE ARC" panel in
  the Create tab's rotary section. +8 tests (`ValueArcRenderTests`); baselines visually
  reviewed.
- **★ Code / component export** (vNext ★ #2 — "close the loop"). Every export can emit
  ready-to-paste loader code: **JUCE** (`LookAndFeel` filmstrip `Slider` / meter
  `Component`), **CSS/HTML** (self-contained `background-position` sprite + a 0..1 value
  setter), **iPlug2** (`IBKnobControl`/`IBSliderControl`/`IBitmapControl`), **HISE**
  (`ScriptPanel` paint). Pure `CodeSnippetService` (`Generate`/`FileName`/`SaveAsync`,
  no Skia/Avalonia — a sibling of `ManifestService`); new `CodeTarget` enum +
  `CodeSnippetRequest` record. Create-tab "CODE EXPORT" panel (per-target tick boxes →
  one file each next to the PNG) + a live preview / copy-to-clipboard expander. +15 tests
  (`CodeSnippetServiceTests`); all four generated snippets eyeballed for API correctness.
- **Shipped v0.7.0.** Committed both features (`52f0b4c`), ran `scripts/Invoke-Release.ps1
  -Bump minor` (test gate 72/72 → bump 0.6.0→0.7.0 → publish → Inno installer → commit
  `fe24ca3` + tag `v0.7.0` + push). CI (`auto-release.yml`) VirusTotal-scanned and created
  the **public release** — verified live with `StripKit-Setup-0.7.0-x64.exe` (33.5 MB).
- **Handoff:** reconciled every managed doc to **0.7.0**; this file rewritten; AUDIT-LOG
  entry added.

### Decisions made (these are not derivable from the diff)
- **vNext order = value-arc → code-export → layer-aware** (the three ★ items; the agent
  recommended value-arc first for visible ROI + lowest risk, and that it de-risks the
  overlay pattern for layer-aware — owner agreed).
- **Ship v0.7.0 before starting layer-aware** (the big one deserves a fresh start).
- **Layer-aware MVP scope = all three input modes eventually**, built in order:
  **base+pointer PNGs first** (establishes the multi-layer render model with no new
  parsers), **then auto-extract the pointer from flat art** (CV, seed from
  `ContentAnalysis`), **then layered PSD/SVG import** (biggest dependency lift — no
  PSD/SVG layer reader in the stack today).
- **Code export choices:** the arc/code features are knob-aware where it matters; the
  code preview box deliberately uses the house **sans-serif** font (no monospace) per the
  Obsidian design rule — flag for the owner if a monospace code box is wanted there.

### What was NOT finished (the backlog)
- **Layer-aware animation (★ #3)** — not started. The next feature.
- **Code export targets React / Web Component + Unity / Godot** — deferred (P2); the four
  shipped (JUCE/CSS/iPlug2/HISE) were the agreed first wave.
- **Website not deployed** (stripkit.pro still a placeholder) and its `updates.json` has
  **no v0.7.0 entry yet** (the per-release manual step).
- **Batch-tab meter settings UI** still unbuilt (carryover from v0.6.0 audit).

## Current State

### Working
- **v0.7.0 live:** https://github.com/Vybecode-LTD/stripkit/releases/tag/v0.7.0
  (`StripKit-Setup-0.7.0-x64.exe`, ~33.5 MB, self-contained, no SDK needed).
- Tests: **72/72 green**. CI runs on every push/PR. Working tree clean; `main` == origin.
- 3-stage release pipeline proven again end-to-end this session.

### Known Issues / Limitations (not bugs — 0 open bugs)
- VirusTotal heuristic FPs on the unsigned installer. Not a real bug.
- Code export: React/Web-Component + Unity/Godot targets not built; meter → iPlug2 maps to
  an `IBitmapControl` (no stock filmstrip meter control) and is commented as such.
- Batch tab: meter-specific settings fields not yet exposed.
- Importer cannot resample frame *count*; manifest UI emits a single control.
- `FilmstripEngine.cs` is a hand-maintained mirror (now includes `RenderValueArc`).

## How to Ship the Next Release

```powershell
pwsh scripts/Invoke-Release.ps1            # default: patch bump -> 0.7.1
pwsh scripts/Invoke-Release.ps1 -Bump minor   # -> 0.8.0
gh run watch                               # CI is the sole release creator
```

Commit feature work **first** — the script only stages the version files + installer
(it does not commit your source changes). Full flow: docs/PACKAGING.md.

## Next Steps (priority order)

1. **vNext ★ #3 — layer-aware animation.** Start with the **base+pointer PNG** MVP
   (two layers; only the pointer rotates, body stays crisp), then auto-pointer extraction
   from flat art, then PSD/SVG import. This is a deep renderer/model change — extend
   `RenderFrame` to composite a layer list; keep `FilmstripEngine.cs` in sync; gate behind
   defaults so existing single-source output is unchanged (as value-arc did).
2. **Finish the code-export targets** — React / Web Component, Unity / Godot (just add
   `CodeTarget` cases + generators + tests; the service is built to extend).
3. **Deploy the website to stripkit.pro** and add a plain-language **v0.7.0 entry** to
   `StripKit-Website/updates.json`. (User step + one cross-repo edit.)
4. **Code-signing certificate** — clears VirusTotal FPs and SmartScreen.
5. **Batch-tab meter settings UI**; bump `actions/checkout@v4 → v5` (Node 20 deprecation).

## Warnings for Next Agent

- **UTF-8 discipline in release scripts.** PS 5.1 `Get-Content` without `-Encoding UTF8`
  corrupts em-dashes (BUG-003). Always `pwsh` + explicit UTF-8. Documented in PACKAGING §9.
- **Never inline a changelog body into `gh release create --notes "..."`** — backticks
  trigger shell command substitution (BUG-004). Always `--notes-file`.
- **`FilmstripEngine.cs` (repo root) is a hand-maintained mirror** of the renderer +
  models — now including `RenderValueArc` and the arc fields. Sync it if renderer math
  changes. It is NOT compiled by the app and is NOT under test.
- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM
  logic into code-behind, or reference Avalonia UI types from view models. Extend, don't
  rewrite.
- **House design rule:** sans-serif only (Verdana-led), no monospace, `#e8440a` accent,
  reuse the `App.axaml` tokens. The code-export preview box honors this (no monospace).
- `actions/checkout@v4` runs Node 20 (GitHub deprecation mid-2026) — bump to v5 soon.

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives.
3. `docs/ARCHITECTURE.md` — deep design reference (value-arc §5.5, code export §9.1).
4. `docs/ROADMAP.md` — done phases + the vNext backlog (layer-aware is next).
5. `docs/PACKAGING.md` — full release-pipeline reference with bug guards.
