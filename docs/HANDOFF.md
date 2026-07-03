---
document: HANDOFF
version: 1.5.0 (v1.5.0-dev, unreleased — 12/12 feature-complete)
last-updated: 2026-07-02
last-audit: 2026-07-02
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into animated filmstrip sprite
sheets for audio-plugin GUI controls — **knobs, faders, sliders, meters, buttons, and toggles**. Stack:
**.NET 9 / Avalonia 11.3 / SkiaSharp 3.119.2 / Inno Setup**. Public, MIT-licensed. Layered-source import +
HDR frame ingest adds **Svg.Skia** (MIT) + **Magick.NET-Q16-HDRI-x64** (Apache-2.0, PSD + 16-bit/EXR); the
**Generate** tab is an AI SVG-generation studio over the user's own OpenAI / Gemini / Claude key (or any
OpenAI-compatible endpoint), with DPAPI-encrypted keys.

**Phase:** **v1.5.0-dev — UNRELEASED.** The 9-of-12 enhancement wave is on `main` and pushed
(`origin/main` = `41fe792`); the remaining 3 items + 2 review fixes from **this session are
uncommitted** in the working tree. The app is a **six-tab** `TabControl` — **Create | Import | Batch |
Skin | Generate | Assemble** — plus a per-tab Getting Started overlay. **331 tests green, build clean,
0 open bugs.** Last release shipped was **v1.3.0**; `csproj <Version>` / `.iss` are at 1.3.0 and the
release script bumps to **1.5.0** at release.

**What the v1.5.0 release will bundle** (once this session's work is committed):
1. **Path-tracing pipeline P1–P5** — the Assemble tab (frame-sequence → filmstrip), the render-recipe
   export, render QC on import, crossfade interpolation, an emission/AOV pass, and 16-bit/EXR HDR ingest.
2. **The 2026-07-02 fine-tooth-comb audit** — 11 bugs fixed (2 HIGH: the un-premultiply colour corruption
   and the SVG-import SSRF), plus the Magick.NET-Q16-HDRI swap and a docs reconcile.
3. **The Depth design-system rebrand** (machined-grey dark theme, ember accent).
4. **The v1.5 enhancement wave, now 12/12 feature-complete** (see below).

---

## This Session (2026-07-02) — finished the 3 deferred v1.5 items + an adversarial review

The user picked up the prior session's handoff choice: "finish all 3 deferred items" rather than
ship v1.5.0 with 9/12. Built in the same priority order the prior HANDOFF suggested, then ran a
4-dimension adversarial code review before considering the work done. Suite **288 → 331 green.**

### Finished (12/12 now, uncommitted — see "Not yet done" below)
10. **Sprite-grid layout (R×C).** A `StripLayout` enum (`Strip` default / `Grid`) +
    `FilmstripSettings.Layout`/`GridColumns`; `SkiaFilmstripRenderer.RenderStrip` packs frames into a
    row-major R×C atlas when selected (`col = i % cols`, `row = i / cols`), gated so `Strip` stays
    byte-identical; mirrored in `FilmstripEngine.cs`. `ManifestControl` gained nullable
    `Layout`/`GridColumns` (omitted unless grid) + a `plugin-asset-manifest` skill schema-doc update.
    All 5 code-export targets got grid-aware column/row math **except iPlug2** — its built-in
    `IBitmap`/`LoadBitmap` API can only read a 1D strip, so it emits an explicit warning comment
    instead of silently mis-emitting. A "Sprite layout" combo + conditional "Grid columns" input on
    the Create tab (Stack-direction hides when Grid is active). New golden `knob_grid8x4`. +16 tests.
11. **Parameter-law frame mapping (log/skew).** A `FrameMappingCurve` enum
    (`Linear`/`Skew`/`Logarithmic`) + `MappingCurve`/`MappingSkew`/`MappingLogBase` on
    `FilmstripSettings` + a `MapT(t)` remap applied at all 4 renderer sites that compute the sweep
    fraction (`ComputeTransform`, `RenderLayers`, `RenderMeterFrame`, `RenderValueArc`), so knobs,
    layered knobs, meters, and the value arc all honour the curve consistently. `Linear` is a true
    no-op — returns the input completely unchanged — so every existing golden stayed byte-identical;
    mirrored in `FilmstripEngine.cs`. A "PARAMETER LAW (advanced)" Create-tab section; the preview
    readout now reflects the mapped angle too. New golden `knob_skew_mid`. +12 tests.
12. **Save/load render presets.** A `RenderPreset` model (~40 fields: the full Create-tab render
    setup — type, frames, sweep, resolution, sprite layout, parameter-law curve, meter/value-arc
    settings, export preferences — deliberately excluding loaded art) persisted via
    `AppSettings.RenderPresets`. `ISettingsService` is now injected into `MainWindowViewModel`'s
    constructor (rippled into `TransportTileAlignmentTests`/`LoadPathTests`/
    `LayeredImportViewModelTests` + a new `TestFakes.MainVm()` helper). `SavePreset`/`ApplyPreset`/
    `DeletePreset` commands (save overwrites by case-insensitive name; apply bulk-restores everything
    in one `_suspendRefresh` pass). A "PRESETS" section atop the Create tab's left panel. +9 tests.

### Adversarial review (2 real findings, both fixed)
A 4-dimension Workflow (renderer/golden-compat, VM/MVVM, code-export/manifest, XAML/tests) — each
dimension reviewed, then independently re-verified against the actual current file contents by a
second agent before being reported. Renderer and XAML/tests came back clean.
- **BUG-017 (medium):** `ManifestService.BuildSingleControl` could serialize a non-positive
  `GridColumns` into `skin.json`, violating the manifest schema's `minimum: 1`. Fixed with
  `Math.Max(1, …)`, mirroring the renderer's own guard.
- **BUG-018 (low):** `DeletePreset()` removed the UI's `Presets` entry by object reference but the
  persisted `RenderPresets` entry by name, so two duplicate-named presets (only reachable via a
  hand-edited settings file) could desync the two collections. Persisted-side removal is now
  reference-based too.

Both are regression-tested and part of the 331/331 green suite; see `docs/BUGS.md` for full detail.

### Live-verified
Launched the dev build (`dotnet run` + computer-use) and confirmed: the PRESETS section renders with
a working name field and correctly-gated Apply/Delete buttons; the Sprite-layout combo toggles
Strip↔Grid correctly (Grid columns field appears, Stack direction hides); the Parameter Law section
renders with the Linear default. The golden `knob_grid8x4` was also eyeballed directly (an 8-frame
knob sweep packed 4×2, row-major).

### Docs
Reconciled every managed doc to the 12/12 / 331-test / uncommitted state (CHANGELOG / TESTING /
ROADMAP / CLAUDE / SOURCE_MAP / BUGS / AUDIT-LOG + this HANDOFF). Also deleted a stray garbled file
one review sub-agent accidentally wrote into the repo root (untracked, not part of any commit).

---

## Current State

### Working
- **331/331 tests green, build clean, 0 open bugs.** CI runs on every push/PR (70% coverage gate).
- Six tabs functional. The renderer + `FilmstripEngine.cs` mirror are in sync (grid layout +
  parameter-law mapping included).

### Not yet done
- **This session's work is NOT YET COMMITTED to git.** The working tree has the 3 finished features +
  2 review fixes as uncommitted changes (modified + new files; 2 new golden PNGs). Per this repo's
  git-safety rule, commit only when the user explicitly asks — the next agent (or the next turn of
  this session) should confirm with the user before committing/pushing.
- **Nothing from the prior 9-item wave has been eyeballed live either** — carried over from before
  this session (the meter peak marker, the @3x/@4x export, the React output, EXR ingest all still
  want a manual pass with real assets before release).

### Known issues / limitations (not bugs)
- **AI generation** is exercised only through mocks/fakes; meter/toggle/set art quality wants a real key.
- **Magick.NET-Q16-HDRI-x64 14.14.0** — the Q16 swap means `ToByteArray` is 16-bit; any Magick pixel read
  must go through `Helpers/MagickPixels` (never a raw 4-bytes/pixel copy).
- **Custom AI endpoint** is Bearer-only (no Azure OpenAI api-key header). Installer ~58 MB (accepted).
- **iPlug2 code-export for a grid-layout strip** is a documented limitation, not a bug: its built-in
  `IBitmap`/`LoadBitmap` can only read a 1D strip, so grid mode emits a warning comment recommending
  Strip layout instead, rather than a working snippet.

---

## The release pipeline (one creator = CI) — unchanged

**Stage 1** `scripts/Invoke-Release.ps1` (Windows PowerShell 5.1; needs `az login`, signtool + the Trusted
Signing dlib, ISCC, gh auth): test-gate → integrity guard → bump → publish → **sign exe + installer** → ISCC
→ stage `releases/latest/` → commit + tag + push. **Stage 2** `.github/workflows/auto-release.yml`: VirusTotal
→ the sole `gh release create`. **Stage 3** the website changelog.

To cut **v1.5.0**: `powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump minor
-WebsiteRepo ..\StripKit-Website` then `gh run watch`. (This bumps 1.3.0 → 1.5.0.) **Requires this
session's uncommitted work to be committed + pushed first** — the release-integrity guard aborts on
uncommitted tracked source.

---

## Next Steps (priority order)

1. **Commit + push this session's work** (ask the user first, per the git-safety rule — do not commit
   autonomously). The v1.5 enhancement wave is 12/12 feature-complete; nothing further needs building.
2. **Live-eyeball** the running app (all 12 enhancements + a real path-traced EXR sequence + a real AI
   key) — carried over, not specific to this session.
3. **Cut the v1.5.0 release** via the pipeline above.
4. Deferred v2s: optical-flow interpolation, multi-layer EXR, stereo/dB meters, runtime AOV toggle,
   Unity/Godot code targets, website getting-started guide.

---

## Warnings for the next agent

- **This session's changes are uncommitted.** Don't assume `git log` reflects the sprite-grid /
  parameter-law / presets work — check `git status`/`git diff` first. Confirm with the user before
  committing or pushing.
- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias aside). **Gate new render
  paths behind defaults so prior goldens stay byte-identical** (as the meter peak-marker, sprite-grid
  layout, and parameter-law mapping all do). Any renderer math change must be mirrored in
  `FilmstripEngine.cs`.
- **The `TransportTile` Border must render at an identical height across Create/Import/Assemble** —
  `TransportTileAlignmentTests` asserts it. Do NOT add controls inside a `TransportTile`; put post-export
  affordances OUTSIDE it (a new parent-grid row). It already caught one mistake in an earlier session.
- **Untrusted SVG** goes through `SafeXml.Parse` then `SvgSanitizer.Sanitize` before `Svg.Skia.FromSvg`
  (BUG-013 was a live SSRF). **Magick pixels** go through `Helpers/MagickPixels` (Q16 is 16-bit). **When
  hand-building bitmaps, set the correct `SKAlphaType`** (BUG-012 was a Premul-tagged straight-bytes bug).
- **Render presets are a "style" snapshot, not an asset bundle** — `RenderPreset` deliberately excludes
  loaded art (source/background/layers). Don't add art-loading to it without discussing the tradeoff
  (bigger settings.json, broken links if a referenced file moves).
- **Manifest fields that carry a user-controlled count must be clamped before serialization** — the
  renderer clamping internally (`Math.Max(1, …)`) does not protect the manifest; BUG-017 was exactly
  this gap for `GridColumns`. Any new manifest field with a schema `minimum` needs its own clamp at
  the `ManifestService` call site.
- **Release tooling:** Windows PowerShell 5.1 only; `az login` first; Trusted Signing via signtool + the
  dlib (not AzureSignTool). CI is the sole release creator; commit feature work before the release script.
- **House design:** Depth machined-grey dark theme, `#f25914` ember accent, Verdana sans + monospace numerics.

---

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (six tabs, all services + the v1.5 helpers).
3. `docs/ARCHITECTURE.md` — deep reference.
4. `docs/ROADMAP.md` — releases + the vNext backlog (the v1.5 wave is now 12/12 ✅).
5. `docs/BUGS.md` (18 resolved) · `docs/PACKAGING.md` (the release pipeline).
