---
document: HANDOFF
version: 1.3.0 (v1.5.0-dev, unreleased)
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

**Phase:** **v1.5.0-dev — UNRELEASED, on `main` and PUSHED** (`origin/main` = `41fe792`). The app is a
**six-tab** `TabControl` — **Create | Import | Batch | Skin | Generate | Assemble** — plus a per-tab Getting
Started overlay. **288 tests green, build clean, ~79% line coverage, 0 open bugs.** Last release shipped was
**v1.3.0**; `csproj <Version>` / `.iss` are at 1.3.0 and the release script bumps to **1.5.0** at release.

**What the v1.5.0 release will bundle** (all unreleased, all on origin):
1. **Path-tracing pipeline P1–P5** — the Assemble tab (frame-sequence → filmstrip), the render-recipe
   export, render QC on import, crossfade interpolation, an emission/AOV pass, and 16-bit/EXR HDR ingest.
2. **The 2026-07-02 fine-tooth-comb audit** — 11 bugs fixed (2 HIGH: the un-premultiply colour corruption
   and the SVG-import SSRF), plus the Magick.NET-Q16-HDRI swap and a docs reconcile.
3. **The Depth design-system rebrand** (machined-grey dark theme, ember accent).
4. **The 9 v1.5 quality-of-life enhancements** (this session — below).

---

## This Session (2026-07-02) — v1.5 enhancement wave (9 of 12), then a handoff

The owner opted to bundle a batch of small enhancements and cut everything as **v1.5.0**. Twelve were
planned; **9 are done, committed, and PUSHED**; **3 were deferred** for a careful follow-up pass. Suite
**280 → 288 green.** Built safest-first in tested waves, renderer-touching last.

### Shipped (9/12, on origin)
1. **React / web-component export** (`6d6ba07`) — a 5th `CodeTarget.React` → a `.jsx` sprite component
   (value prop 0..1); wired into Create + Assemble + Batch code-export panels. +3 tests.
2. **Dithered HDR de-band** (`18a444b`, finishes path-tracing P3b) — `Helpers/MagickPixels.DitherDownTo8`
   (Bayer 8×8) in `ImageLoadService.LoadHdr`, so EXR/16-bit ingest reduces to 8-bit without banding. +2.
3. **Remember window size + last tab** (`99bdd22`) — `AppSettings.WindowWidth/Height/LastTabIndex`, wired
   at the composition root in `App.axaml.cs`.
4. **Ctrl+O / Ctrl+E shortcuts** (`99bdd22`) — `Window.KeyBindings` (Ctrl-only, so text fields keep plain keys).
5. **Batch tab → loader code** (`94d431f`) — `BatchOptions.CodeTargets`; `BatchProcessor` takes
   `ICodeSnippetService` and emits per-strip loader files (parity with Create & Assemble). +1.
6. **CI coverage gate** (`3c9be86`) — `ci.yml` collects coverage and fails below 70% line (current ~79%).
7. **"Show in folder" after export** (`a295f38`) — `Helpers/ShellHelper.RevealInFolder`, `RevealExportCommand`
   + `LastExportPath`; the button is placed OUTSIDE the `TransportTile` Border (see the warning below).
8. **Arbitrary HiDPI scale @2x/@3x/@4x** (`43a87c9`) — a `HiDpiScale` across Create/Assemble/Batch (the
   `@Nx` suffix, the render/upscale factor, and the manifest hi-res asset all follow it; default 2). +1.
9. **Meter peak-marker** (`41fe792`) — `FilmstripSettings.ShowMeterPeak` + `PeakColorArgb` (mirrored in
   `FilmstripEngine.cs`); `RenderMeterFrame` paints the direction-aware leading segment, gated OFF by
   default so every meter golden is byte-identical. +1 pixel-logic test.

### Deferred (3/12 — the broadest / most golden-sensitive; do as a fresh, careful pass)
- **Sprite-grid layout (R×C)** — rewrites `RenderStrip`'s output layout + the manifest + loader code.
- **Parameter-law frame mapping (log/skew)** — remaps the sweep-`t` in `ComputeTransform` / meter / layers /
  arc; gate to Linear default so goldens hold, keep the `(N−1)` law. Highest-touch, most golden-sensitive.
- **Save / load render presets** — needs `ISettingsService` injected into `MainWindowViewModel` (a
  constructor change that ripples into `TransportTileAlignmentTests` + `LoadPathTests` ctor calls) + a
  `RenderPreset` model + save/apply/delete + Create UI. No renderer, but broad plumbing.

### Docs
Reconciled every managed doc to the v1.5 / 288 state (CHANGELOG / TESTING / ROADMAP / CLAUDE / SOURCE_MAP /
AUDIT-LOG / README + this HANDOFF).

---

## Current State

### Working
- **288/288 tests green, build clean, ~79% coverage, 0 open bugs.** CI runs on every push/PR (now with the
  70% coverage gate). `main` == `origin`.
- Six tabs functional. The renderer + `FilmstripEngine.cs` mirror are in sync (incl. the meter peak-marker).

### Known issues / limitations (not bugs)
- **Nothing has been eyeballed live this session** — the 9 enhancements are unit-tested (288 green) but not
  yet run in the actual app. A live pass (esp. the meter peak marker, the @3x/@4x export, the React output,
  EXR ingest) is worth doing before the release.
- **AI generation** is exercised only through mocks/fakes; meter/toggle/set art quality wants a real key.
- **Magick.NET-Q16-HDRI-x64 14.14.0** — the Q16 swap means `ToByteArray` is 16-bit; any Magick pixel read
  must go through `Helpers/MagickPixels` (never a raw 4-bytes/pixel copy).
- **Custom AI endpoint** is Bearer-only (no Azure OpenAI api-key header). Installer ~58 MB (accepted).

---

## The release pipeline (one creator = CI) — unchanged

**Stage 1** `scripts/Invoke-Release.ps1` (Windows PowerShell 5.1; needs `az login`, signtool + the Trusted
Signing dlib, ISCC, gh auth): test-gate → integrity guard → bump → publish → **sign exe + installer** → ISCC
→ stage `releases/latest/` → commit + tag + push. **Stage 2** `.github/workflows/auto-release.yml`: VirusTotal
→ the sole `gh release create`. **Stage 3** the website changelog.

To cut **v1.5.0**: `powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump minor
-WebsiteRepo ..\StripKit-Website` then `gh run watch`. (This bumps 1.3.0 → 1.5.0.)

---

## Next Steps (priority order)

1. **Finish the 3 deferred v1.5 items** (sprite-grid, parameter-law, presets) — each as a careful pass with
   golden-baseline regen + the `FilmstripEngine.cs` mirror + tests. OR decide to ship v1.5.0 with the 9.
2. **Live-eyeball** the running app (the 9 enhancements + a real path-traced EXR sequence + a real AI key).
3. **Cut the v1.5.0 release** via the pipeline above.
4. Deferred v2s: optical-flow interpolation, multi-layer EXR, stereo/dB meters, runtime AOV toggle, Unity/
   Godot code targets, website getting-started guide.

---

## Warnings for the next agent

- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias aside). **Gate new render
  paths behind defaults so prior goldens stay byte-identical** (as the meter peak-marker does). Any renderer
  math change must be mirrored in `FilmstripEngine.cs`.
- **The `TransportTile` Border must render at an identical height across Create/Import/Assemble** —
  `TransportTileAlignmentTests` asserts it. Do NOT add controls inside a `TransportTile`; put post-export
  affordances OUTSIDE it (a new parent-grid row). It already caught one mistake this session.
- **Untrusted SVG** goes through `SafeXml.Parse` then `SvgSanitizer.Sanitize` before `Svg.Skia.FromSvg`
  (BUG-013 was a live SSRF). **Magick pixels** go through `Helpers/MagickPixels` (Q16 is 16-bit). **When
  hand-building bitmaps, set the correct `SKAlphaType`** (BUG-012 was a Premul-tagged straight-bytes bug).
- **Release tooling:** Windows PowerShell 5.1 only; `az login` first; Trusted Signing via signtool + the
  dlib (not AzureSignTool). CI is the sole release creator; commit feature work before the release script.
- **House design:** Depth machined-grey dark theme, `#f25914` ember accent, Verdana sans + monospace numerics.

---

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (six tabs, all services + the v1.5 helpers).
3. `docs/ARCHITECTURE.md` — deep reference.
4. `docs/ROADMAP.md` — releases + the vNext backlog (incl. the 3 deferred v1.5 items).
5. `docs/BUGS.md` (16 resolved) · `docs/PACKAGING.md` (the release pipeline).
