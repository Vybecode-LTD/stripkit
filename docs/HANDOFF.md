---
document: HANDOFF
version: 1.4.0-dev
last-updated: 2026-07-02
last-audit: 2026-07-02
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into animated filmstrip sprite
sheets for audio-plugin GUI controls — **knobs, faders, sliders, meters, buttons, and toggles**. Stack:
**.NET 9 / Avalonia 11.3 / SkiaSharp 3.119.2 / Inno Setup**. Public, MIT-licensed. Layered-source import
adds **Svg.Skia** (MIT) + **Magick.NET-Q8-x64** (Apache-2.0); the **Generate** tab is an AI SVG-generation
studio over the user's own OpenAI / Gemini / Claude key — or any **OpenAI-compatible custom endpoint** —
with **DPAPI-encrypted** keys.

**Phase:** **v1.4.0-dev — UNRELEASED, on `main`.** The app is a **six-tab** `TabControl` —
**Create | Import | Batch | Skin | Generate | Assemble** — plus a re-openable, per-tab **Getting Started**
overlay. `main` is **several commits ahead of `origin/main`** (the whole v1.4.0 line is unpushed). **274
tests green, build clean, 0 open bugs.** Last release shipped was **v1.3.0** (signed, live on GitHub Releases);
`csproj <Version>` / `.iss` / CHANGELOG are still at **1.3.0** and bump to 1.4.0 only when the release
script runs.

---

## This Session (2026-07-02) — full audit + 11 code/test fixes + docs reconcile

A fine-tooth-comb audit of the unreleased v1.4.0 work (a 10-dimension, adversarially-verified multi-agent
sweep) found 18 real items. The **11 code + test** findings were fixed and committed (`301b2b4`); the
**7 docs/release** items were reconciled (this doc set). Suite **265 → 274 green**.

### Fixed (commit `301b2b4`)
- **BUG-012 (HIGH)** — `FrameSequenceAssembler.UnpremultiplyAlpha` returned a **Premul-tagged** bitmap
  holding straight bytes, so the P3 "Un-premultiply alpha" halo fix **corrupted colours** on export. Now
  returns an Unpremul-tagged bitmap. (The old test only read raw bytes, so it was green while broken.)
- **BUG-013 (HIGH, security)** — the layered **SVG file-import** handed raw text to Svg.Skia without
  `SvgSanitizer`, so an external `<image xlink:href="http://…">` (or `file://`) **fired an outbound
  request** during rasterization — an SSRF / file-existence oracle on file open. **Verified live.**
  `SvgSanitizer.Sanitize` is now public and runs before `FromSvg` on the import path. (AI-reply path was
  already safe.)
- **BUG-014 (MED)** — `RenderButtonLayers` matched `Frame` layers by absolute stack index, so a leading
  `Static` border/shadow shifted and blanked button/toggle states. Now matches by **ordinal** among Frame
  layers (renderer + `FilmstripEngine.cs` mirror), and the state-frame count ignores Static layers.
- **BUG-015 (MED)** — `RegenerateSetItemAsync` reused a possibly-cancelled CTS (`??=`); fresh CTS now.
- **BUG-016 (LOW batch)** — QC drift measured in absolute px (no phantom drift on mixed-size sequences);
  three resource leaks closed (set/variation preview bitmaps, an auto-retry temp SVG, the provider
  `HttpResponseMessage`); the tutorial tip box's undefined `GlassFill`/`GlassBorder` keys → `*Brush`.
- **Tests:** +8 (266 → 274). 6 carry fail-before/pass-after regression guards (proven by stashing the
  fixes and watching them go red); 2 are coverage-gap tests for already-correct code.

### Repo hygiene + docs
Deleted the redundant merged `feat/v1.4.0` branch; gitignored the long-standing strays
(`.claude/launch.json`, `press/`, `docs/PRESS-RELEASE.md`). Reconciled CLAUDE / README / TESTING /
CHANGELOG / BUGS / AUDIT-LOG (all now say **274**, six tabs, "on main").

### The refuted findings (don't re-chase)
The verifier killed 4: a hallucinated `ZZ_AuditProbeTests.cs`, a `<style>`/`@import` SSRF that did **not**
reproduce under `FromSvg(string)` (proven by a live probe), and two that depended on the phantom file.

---

## Prior v1.4.0-dev work already on `main` (context)

- **Path-tracing pipeline P1–P3** — the **Assemble** tab (stack a pre-rendered frame sequence into a
  strip: natural-sort, size reconcile, re-centre, re-time, QC), the **render-recipe** export (Blender
  `bpy` + CSV/JSON matching the `(N−1)` law) on Create *and* Assemble, and **render QC on import**.
- **Depth design-system rebrand** — the whole app moved to the vendored `Depth/Depth.axaml` tokens:
  machined-grey surfaces, ember `#f25914` accent, recessed monospace-numeral wells, raised keycap
  buttons, solid window (acrylic + glow removed). Verdana for labels/body; **monospace for numerics only**.
- **Uniform preview transport** across tabs + the crosshair placement fix.

---

## Current State

### Working
- **274/274 tests green, build clean, 0 open bugs.** CI runs on every push/PR.
- Six tabs all functional. The renderer + `FilmstripEngine.cs` mirror are in sync (incl. the BUG-014
  ordinal fix).

### Known issues / limitations (not bugs)
- **AI generation can't be live-verified here.** Every Generate feature is unit-tested with a mocked
  service + real importer + fake provider/network; the meter/toggle/set **art quality** still wants a
  live eyeball with a real key — knob is the proven path.
- **`FilmstripEngine.cs`** (repo root) mirrors the render math (incl. the Button/Toggle state-frame path
  and the BUG-014 ordinal fix); app-only services stay out — by design.
- **Magick.NET-Q8-x64** was bumped **14.13.1 → 14.14.0** this session (app + test projects), which
  **cleared** the prior known HIGH/moderate NuGet advisories (NU1903/NU1902) — build is now advisory-clean
  and the suite stays 274 green. (The PSD import path processes untrusted files, so keeping this current
  matters.)
- **Custom endpoint:** Bearer auth only (OpenRouter / Ollama / LM Studio); Azure OpenAI (api-key header)
  unsupported. Vision needs a vision-capable model.
- **Installer ~58 MB** (> GitHub's *recommended* 50 MB, under the 100 MB hard limit) — the ImageMagick
  native, accepted for PSD support.

---

## The release pipeline (one creator = CI) — unchanged

**Stage 1** `scripts/Invoke-Release.ps1` (Windows PowerShell 5.1; `az login`, signtool + Trusted Signing
dlib, ISCC, gh auth): test-gate → integrity guard → bump → publish → **sign exe + installer** → ISCC →
stage under `releases/latest/` → commit + tag + push. **Stage 2** `.github/workflows/auto-release.yml`
(triggered by the tracked installer push): VirusTotal → the **sole** `gh release create`. **Stage 3** the
website: `Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\StripKit-Website -Version X.Y.Z -Push`.

To ship v1.4.0: **push `main` first** (the 6 commits), then
`powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump minor -WebsiteRepo ..\StripKit-Website`
then `gh run watch`.

---

## Next Steps (priority order)

1. **Push `main` to origin** (the v1.4.0 line is unpushed) and **release v1.4.0** via the pipeline above.
2. **Live-eyeball the Generate output** with a real key (meters / toggles / matching-set art quality).
3. **P3b — 16-bit / EXR HDR ingest** (needs the Magick.NET Q8 → Q16-HDRI swap) · **P4 — frame
   interpolation** (render fewer, ship more).
4. Website `stripkit.pro/getting-started/` guide · seeds → matching-set → auto-assemble a Skin · Azure
   OpenAI auth · more code-export targets.

---

## Warnings for the next agent

- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias is the exception). Gate
  new render paths behind defaults so prior goldens hold. **Toggle reuses Button's state-frame path** —
  keep them together (both mirrored in `FilmstripEngine.cs`, incl. the ordinal Frame-matching fix).
- **Untrusted SVG must go through `SafeXml.Parse` FIRST, then `SvgSanitizer.Sanitize`, before
  `Svg.Skia.FromSvg`** — never a bare parse, and never skip the sanitizer on the file-import path
  (BUG-013 was a live SSRF). Both callers now do this; keep it that way.
- **When un-premultiplying / hand-building bitmaps, set the correct `SKAlphaType`** — a Premul tag on
  straight bytes silently corrupts colour downstream (BUG-012). Round-trip through encode/decode in tests,
  not just raw bytes.
- **Release tooling:** Windows PowerShell 5.1 only; `az login` before a release; Trusted Signing via
  signtool + the dlib (not AzureSignTool). CI is the **sole** release creator; commit feature work
  **before** the release script (it stages only version files + the installer).
- **House design:** **Depth** machined-grey dark theme, `#f25914` ember accent, Verdana-led sans + monospace
  for numerics only. Reuse the mapped `*Brush` keys / Depth tokens — a typo'd `DynamicResource` key renders
  blank without erroring (BUG-016 tip box).

---

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (six tabs, all services).
3. `docs/ARCHITECTURE.md` — deep reference.
4. `docs/BUGS.md` — the 16 resolved defects (BUG-012…016 are this session's).
5. `docs/ROADMAP.md` — releases + the vNext backlog. `docs/PACKAGING.md` — the release pipeline.
