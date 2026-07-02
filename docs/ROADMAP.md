# ROADMAP — StripKit

> Version 1.3.0 · last-updated 2026-07-02 · last-audit 2026-07-02

The master roadmap for StripKit. Phases 0–8 (the v1 scaffold through the v0.6.0
ship — Inno installer, release pipeline, and website) are **complete**, and several
further releases have shipped since (see **Releases** below). The next release is
**v1.5.0** (unreleased) — it bundles the whole **offline-3D / path-tracing pipeline P1–P5**, the
**Depth** rebrand, a 2026-07-02 audit, and a **v1.5 enhancement wave** (9 of 12 items done + pushed to
origin/main). The **vNext — Future features** section captures the product-brainstorm backlog, grouped by
theme and priority. The three ★ bets were value-arc (✅), code-export (✅ first wave), and
layer-aware animation (✅ all 3 steps done — base+pointer, auto-extract, PSD/SVG import).

**Status icons:** ✅ Done · 🔄 Active · ⏳ Next / queued · 🚫 Blocked · ❌ Cancelled.

## Releases

- **v0.6.0** (2026-06-04) — Inno installer + 3-stage release pipeline + landing-page website
  (Phases 7–8). First GitHub Release.
- **v0.7.0** (2026-06-04) — ★ **value-arc / fill-ring** generator (knobs) + ★ **code / component
  export** (JUCE / CSS-HTML / iPlug2 / HISE loader snippets). Suite 72.
- **v0.8.0** (2026-06-05) — **Batch-tab meter settings** (+ layered/backdrop toggle), **Skin tab**
  (multi-control `skin.json` builder), **importer frame-count resampling**, and ★ **layer-aware
  knob step 1** (base + pointer). Suite 94.
- **v1.0.0** (2026-06-06) — **the 1.0 release.** ★ **layer-aware step 2** (auto-pointer extraction,
  `PointerExtractor`) + ★ **step 3** (layered **PSD/SVG import** via Svg.Skia + Magick.NET) —
  **completing the layer-aware ★ bet** — plus the **interactive Getting Started tutorial** (per-tab,
  auto-open first run, bundled sample knob, tooltips) and a **centered About modal** + live-version
  fix. First **code-signed** release (Azure Trusted Signing — exe + installer). Suite **125**. The
  release pipeline gained an optional **Stage 3** that auto-drafts the website changelog
  (`Publish-WebsiteChangelog.ps1`). All three ★ bets are now done.
- **v1.1.0** (2026-06-07) — **the Generate tab.** A fifth tab that generates a **layered knob SVG**
  from the user's own **OpenAI / Gemini / Claude** key (`IAssetGenerationService` + three providers
  over a shared `HttpClient`; `SvgSanitizer`; **DPAPI-encrypted** keys via `ISecretStore`),
  validated by importing it (preview = the real import) with a **"Use in Create"** handoff. Plus the
  **verified-model dropdown** (fixes a retired-Gemini crash), **body + accent colour** inputs with
  live swatches, **style-effect** checkboxes, and the renderer **content-centering** fix. New dep:
  `System.Security.Cryptography.ProtectedData` 9.0.0. Suite **125→157**.
- **v1.2.0** (2026-06-09) — **buttons + all-control-type generation.** New `ComponentType.Button` +
  `LayerBehavior.Frame` (discrete off/on state frames; mirrored in `FilmstripEngine.cs`); the
  Generate tab supports **all four control types** (knob / fader / slider / button); **colour-picker
  flyouts** (`Avalonia.Controls.ColorPicker` 11.3.0). *(Release-integrity note: the v1.2.0 feature
  **source** was accidentally omitted from the release commit — only version files + the installer
  were staged — and was committed retroactively `2026-06-14` (`b55380f`) so the tag can rebuild its
  own installer.)*
- **v1.2.1** (2026-06-14, shipped + signed) — **fix wave.** The Generate→Create handoff now
  **honours the generated control type** (was hard-forced to `RotaryKnob`, so generated
  faders/sliders/buttons broke); **hardened untrusted-SVG XML parsing** against entity-expansion DoS
  / external-entity via new `SafeXml` (applied to both the AI-reply sanitizer and the layered-file
  import picker); added the missing `BindingPlugins.DataValidators.RemoveAt(0)` and a Generate
  structure warning (knob with no pointer / button missing a state). Suite **157→171**.
- **v1.2.2** (2026-06-14, shipped + signed) — **polish + tooling wave.** The Generate tab's model
  picker is now an **editable `AutoCompleteBox`** (free text + per-provider suggestions — a
  custom/just-released model id is sent verbatim; a pinned-but-delisted model shows as text, not a
  blank box); the **preview build moved off the UI thread** (one `Task.Run` in
  `GenerateViewModel.BuildPreview`; the UI thread only assigns the bitmap) and **generated temp SVGs
  no longer accumulate**. Release tooling: a **release-integrity guard** in `Invoke-Release.ps1`
  (abort if tracked source is uncommitted; untracked strays allowed; `-AllowDirty` override) so
  feature source can't be orphaned from its tag, plus a Stage-3 website-changelog splat fix
  (array→hashtable). CI future-proofing: `actions/checkout@v4→v5` + `actions/setup-dotnet@v4→v5`
  (Node 24) and `coverlet.collector 6.0.2→6.0.4`. New portable skill
  `release-source-integrity-guard`. Suite **171→172**.
- **v1.3.0** (2026-06-18, shipped) — **the AI-generation wave + meters + toggles.** **Generate-tab
  meters** (an off/on layered pair → background + revealed source) plus **horizontal meters**
  (orientation inferred from the art aspect). New first-class **`ComponentType.Toggle`** — an on/off
  toggle across generate / import / create / code-export, distinct from Button but sharing its
  state-frame render path. The full **AI-generation program** on the Generate tab: a **matching-set
  generator** (one prompt → a consistent control family), a **variations grid** (N takes of one
  control), **refine** (revise the current SVG by instruction), **reference-image match / vision**
  ("describe this screenshot"), a **prompt seeds library** (built-in + saved style bundles), an
  **"avoid" field**, **auto-retry** on weak structure, **show-the-prompt**, and an
  **OpenAI-compatible custom endpoint** (OpenRouter / Ollama / LM Studio). Security/hardening:
  **BUG-010** (SVG file-import billion-laughs DoS) fixed + input-size caps (PNG / SVG / PSD). Suite
  **172→216**.
- **v1.4.0-dev → v1.5.0** (2026-07-02, **unreleased** — the whole path-tracing pipeline + a v1.5
  enhancement wave, shipping together as **v1.5.0**) — the **six-tab, path-tracing** build. First the
  full **offline-3D / path-tracing pipeline P1–P5** (the **Assemble** tab + render-recipe export + render
  QC + EXR/HDR ingest + frame interpolation + AOV emission-pass; see the pipeline section below), a
  **Depth design-system rebrand** (machined-grey dark theme, `#f25914` ember; vendored `Depth/Depth.axaml`
  mapped in `App.axaml`), and a **2026-07-02 fine-tooth-comb audit** (11 bugs fixed). Then the owner
  folded in a **v1.5 enhancement wave** — **9 of 12** planned items shipped + pushed to origin/main (tip
  `41fe792`): **(1)** a **React / Web-Component code-export target** (`CodeTarget.React` → a `.jsx` sprite
  component driven by a 0..1 `value` prop; wired into Create + Assemble + Batch); **(2)** **dithered HDR
  de-band** (finishing path-tracing P3b — `Helpers/MagickPixels.DitherDownTo8`, an 8×8 Bayer ordered
  dither, in `ImageLoadService.LoadHdr` so EXR/16-bit reduces to 8-bit without banding); **(3)** **remember
  window size + last tab** (`AppSettings.WindowWidth/WindowHeight/LastTabIndex`, restored/persisted at the
  composition root); **(4)** **Ctrl+O / Ctrl+E shortcuts** (`Window.KeyBindings`); **(5)** **Batch-tab
  loader-code export** (`BatchOptions.CodeTargets`; `BatchProcessor` emits JUCE/CSS/iPlug2/HISE/React per
  strip — parity with Create & Assemble); **(6)** a **CI coverage gate** (`ci.yml` fails below 70% line
  coverage); **(7)** **"Show in folder" after export** (Create + Assemble — `Helpers/ShellHelper.RevealInFolder`
  + `RevealExportCommand`/`LastExportPath`, outside the `TransportTile` Border to keep the transport-tile-height
  invariant); **(8)** **arbitrary HiDPI scale @2x / @3x / @4x** (a `HiDpiScale` property across
  Create/Assemble/Batch — suffix, render factor, and manifest hi-res asset all follow it; default 2); and
  **(9)** a **meter peak-marker** (`FilmstripSettings.ShowMeterPeak` + `PeakColorArgb`, mirrored in
  `FilmstripEngine.cs`; `RenderMeterFrame` paints the direction-aware leading peak segment; gated OFF by
  default so every meter golden is byte-identical). Commits `6d6ba07` / `18a444b` / `99bdd22` / `94d431f` /
  `3c9be86` / `a295f38` / `43a87c9` / `41fe792`. Suite **280→288**, build clean, coverage ~79%.
  **Deferred** to a later careful v1.5 pass: **sprite-grid layout**, **parameter-law frame mapping**, and
  **save/load render presets** (all ⏳ below). Not yet released (csproj/`.iss` `<Version>` still at 1.3.0;
  the release script bumps to 1.5.0 at release).

---

## Done / shipped — Phases 0–8 (v1 scaffold → v0.6.0)

All eight foundational phases are complete. Each was sized to compile and run on
its own, named the guiding skill, and met a done-condition. Condensed history:

- ✅ **Phase 0 — Verify the scaffold** (2026-06-03). `dotnet run --project
  src/StripKit` opens the window; the load → preview → export round-trip works.
  Fixed two pre-existing scaffold build blockers (an illegal `--` in a `.csproj`
  comment; a missing `Microsoft.Extensions.DependencyInjection` reference).
- ✅ **Phase 1 — Drag-and-drop input** (2026-06-03). The preview `Border` is a drop
  zone; the Load button and the drop handler share `MainWindowViewModel.LoadSourceFromPath`
  (no duplicated load logic). Covered by VM load-path tests + a headless drop-zone
  test. *Skill: `avalonia-drag-drop-files`.*
- ✅ **Phase 2 — Filmstrip importer** (2026-06-03), as a second tab (confirmed).
  `FilmstripImporter` (SkiaSharp) detects frame count / orientation / kind from
  dimensions and flags ambiguous cases; the **Import** tab shows the detection, an
  editable count, a frame scrubber, and extract / re-stack-orientation export.
  Verified visually (a 64×6500 strip → 100 frames, sliced + re-stacked correctly).
  *Skill: `filmstrip-importer-engine`.*
- ✅ **Phase 3 — Manifest export** (2026-06-03). The Create-tab export has an "Also
  write a skin.json manifest" toggle + a parameter-id field; on export it writes a
  schema-valid `<name>.skin.json` next to the PNG (one control, relative
  `asset`/`asset2x`, frames, frame size, stack, base-resolution `bounds`) via
  `ManifestService` (System.Text.Json, camelCase). Conformance-tested against the
  skill's JSON Schema. *Skill: `plugin-asset-manifest`.*
- ✅ **Phase 4 — Golden-image tests** (brought forward to 2026-06-03 alongside
  Phase 1). `tests/StripKit.Tests` (xUnit + NSubstitute + FluentAssertions +
  Avalonia.Headless): committed golden baselines, VM tests, headless view tests;
  `ImageAssert` emits expected/actual/diff PNGs on mismatch. *Skill:
  `image-regression-testing`.*
- ✅ **Phase 5 — Batch processing** (2026-06-03), as a third tab (**Batch**).
  `BatchProcessor` (SkiaSharp, no Avalonia) runs the loop via `Task.Run` (off the
  UI thread), reports per-item progress, and cancels between items without
  throwing; per-file failures are isolated. Optional @2x + manifest per strip.
  *Skills: `live-preview-render-loop`, `csharp-mastery`.*
- ✅ **Phase 6 — Meter mode** (2026-06-03), design signed off first. New
  `ComponentType.Meter` + `MeterFillDirection` + meter fields on `FilmstripSettings`.
  `RenderMeterFrame` does procedural segment bars or a layered on/off-art reveal
  (auto-selected); all four fill directions; discrete default + continuous toggle.
  Mirrored in `FilmstripEngine.cs`.
- ✅ **Phase 7 — Packaging and distribution** (2026-06-04). Switched from Velopack to
  an **Inno Setup installer** (`installer/StripKit.iss`: per-user, choose-dir,
  optional shortcuts, registry-wiping uninstaller). A **3-stage release pipeline**
  per `SOFTWARE_RELEASE.md`: `scripts/Invoke-Release.ps1` (Stage 1, local build) +
  `.github/workflows/auto-release.yml` (Stage 2 — VirusTotal scan, the **sole**
  release creator). The self-contained `win-x64` publish runs without the SDK. The
  **first GitHub Release, v0.6.0, is live** (`StripKit-Setup-0.6.0-x64.exe`). Full
  flow: `docs/PACKAGING.md`. *Skill: `dotnet-installer-publishing`.*
- ✅ **Phase 8 — Landing page website** (2026-06-04). Built and pushed as
  **`Vybecode-LTD/StripKit-Website`**: hero, features, a GitHub-driven download
  button, a simplified changelog from `updates.json` (decoupled from the technical
  `docs/CHANGELOG.md`), privacy/terms/contact with a Formspree form, a VybeCode
  footer, and a VirusTotal shield. SEO/GEO per `SEO_OPTIMIZATION.md`.

**Test suite:** 49/49 green at the v0.6.0 ship.

---

## Operational follow-ups (post-v0.6.0 ship)

Open items carried from the v0.6.0 release — small, mostly non-feature:

- ✅ **Deploy the website to `stripkit.pro`** — **live**, hosted on **Railway** (auto-deploys the
  `Vybecode-LTD/StripKit-Website` repo on push). Download button reads the latest GitHub Release
  client-side; the changelog reads `updates.json`.
- ✅ **Code signing** — the app + installer are **code-signed** via **Azure Trusted Signing** (the
  `VybeCode` profile; `signtool` + the Trusted Signing dlib). Live since v0.8.0's signed re-release.
- ✅ **Per-release website maintenance — now automated.** `scripts/Publish-WebsiteChangelog.ps1`
  auto-drafts the `updates.json` entry from `docs/CHANGELOG.md`; wired into `Invoke-Release.ps1`
  as optional Stage 3 (`-WebsiteRepo`, **hashtable** splat so a trailing `-Push` binds). Hybrid:
  auto-draft → refine → push.
- ✅ **Minor: bump `actions/checkout@v4 → v5`** (v1.2.2) — v4 ran on the deprecated Node 20 (the
  v1.0.0 Auto Release run warned about it). Both `ci.yml` and `auto-release.yml` now pin `@v5`, and
  `ci.yml` also bumps `actions/setup-dotnet@v4 → v5` (Node 24, ahead of the June 16 2026 forcing).

---

## vNext — Future features

The product backlog, grouped by theme. Each item has a 1–2 sentence description
and a priority tag (**P1** highest → **P3** lowest). The three ★ items are the
highest-leverage bets across all groups; pursue them first.

### Offline-3D / path-tracing pipeline (render sequence → filmstrip)

The KVR-validated workflow: path-trace a control offline (Blender Cycles / KeyShot / Octane), then
ship the rendered frames as a cheap runtime filmstrip — pay the lighting + anti-aliasing cost **once**,
offline, instead of every frame on the user's GPU. StripKit owns the last mile (assemble the sequence)
and, next, the spec for the render itself. **No renderer change in any phase** (assembly/packing only),
so nothing mirrors into `FilmstripEngine.cs`. *(Origin: a KVR thread on raster vs. WebGL-3D plugin GUIs —
"you're still better off doing offline path-tracing into spritesheets".)*

- ✅ **P1 — Frame-sequence assembler (the "Assemble" tab)** *(unreleased; next: v1.4.0)* — stack a
  folder (or drag-drop) of individually-rendered frames into one filmstrip: natural-sort
  (`frame_2` before `frame_10`), reconcile odd frame sizes (pad-to-largest / crop-to-smallest /
  strict), optional content re-centre (fixes 3D object drift between frames), optional nearest-frame
  re-time to 32/64/128, and the usual @2x / `skin.json` / JUCE-CSS-iPlug2-HISE loader-code exports.
  New `FrameSequenceAssembler` (pure SkiaSharp, injects the importer for the resample),
  `NaturalFileNameComparer`, `FrameSequenceModels`, `FrameSequenceViewModel` (+ `FrameItemRow`),
  `AssembleView`; `IImageLoadService.Probe` + `IFileDialogService.OpenImagesAsync`. +28 tests
  (216 → 244), golden `assemble_knob_mix_4`. **(P1, the headline — done.)**
- ✅ **P2 — Render-recipe export** *(unreleased; v1.4.0)* — emit the spec that makes the offline
  render match StripKit's runtime law (`angle_i = start + (end − start)·i/(N−1)`, the deliberate N−1
  divisor): a Blender `bpy` script (transparent film; a keyframe baked on **every** one of the N
  frames — exact angles, no interpolation drift — plus a 0..1 `value` custom property to drive
  non-rotary rigs via drivers) **plus** an engine-agnostic `frame,value,angle_deg` CSV/JSON table for
  KeyShot / Octane / C4D. Pure `RenderRecipeService` (+ `RenderRecipeModels`) mirroring
  `CodeSnippetService`, with one `BuildFrameTable` that mirrors `ComputeTransform` so the recipe and
  the renderer can never diverge. A "Render recipe" panel on the **Create** tab (Blender/CSV/JSON live
  preview + copy + save) — Create carries every input the recipe needs (type, frame count, sweep,
  resolution). +14 tests (244 → 258). **(P1 — done.)** *(A discoverability entry-point on the Assemble
  tab is an easy follow-on.)*
- ✅ **P3 — Render QC on import** *(unreleased; v1.4.0; EXR/HDR split to P3b)* — catch the path-tracer failure
  modes when frames land on the Assemble tab. **Done:** `FrameSequenceAssembler.AnalyzeQc` reports
  **object drift** (the content-centre spread in px — P1 added the re-centre fix, P3 adds the
  detection + a "tick Re-centre" nudge), frames with **no transparency** (a missing transparent
  background — the exact mistake the render recipe prevents) or **none at all** (a failed render), and
  **premultiplied edges**; plus a **"Check frames"** pre-flight button and a **"Un-premultiply alpha"**
  fix (`UnpremultiplyAlpha` divides RGB by alpha to kill dark edge halos). +7 tests (258 → 265). Also
  landed the **Assemble-tab render-recipe entry-point** (plan a render right where you assemble).
  **Deferred → P3b:** 16-bit / EXR HDR ingest + tone-map + dithered de-band — needs the Magick.NET
  **Q8 → Q16-HDRI** swap (Q8 can't hold >8-bit), so it's its own piece. **(P2)**
- ✅ **P3b — 16-bit / EXR HDR ingest** *(unreleased; v1.4.0)* — the Assemble tab ingests `.exr` / `.hdr` /
  16-bit `.tif` frames (the native output of most path tracers). Swapped **Magick.NET-Q8 → Q16-HDRI**
  (holds >8-bit so an EXR is tone-mapped before the 8-bit reduction; OpenEXR is bundled — no extra
  delegate). `ImageLoadService` routes HDR formats through Magick (linear → sRGB + clamp → depth-8 →
  PNG32 → Skia decode); new `Helpers/MagickPixels` downshifts Q16's 16-bit `ToByteArray` to 8-bit (the
  PSD path relies on this, and was revalidated). +2 tests (278 → 280). *(A dithered de-band — Magick's
  `OrderedDither` posterizes, so it needs error-diffusion — and multi-layer/deep EXR are deferred.)* **(P3)**
- ✅ **P4 — Frame interpolation ("render fewer, ship more")** *(unreleased; v1.4.0)* — **v1 (crossfade)
  done:** a new interpolation mode in the assembler cross-dissolves the two bracketing frames per output
  frame (the `(N−1)/(M−1)` law keeps the real endpoints exact), so ~32 expensive frames can ship as
  64/128 for slow, smooth motion. `FrameInterpolation` {Nearest, Crossfade} + a Method combo on the
  Assemble tab. +2 tests (274 → 276). *(Optical-flow (v2) is deferred — it needs a CV dependency; the
  mode enum leaves room.)* **(P2)**
- ✅ **P5 — AOV / emission-pass ingest** *(unreleased; v1.4.0)* — the Assemble tab takes a second render
  pass (an emission/glow AOV, one frame per beauty frame) and **additively composites** it over the
  beauty frames, so a path-traced glow reads like emitted light instead of being baked flat.
  `FrameSequenceOptions.EmissionFrames` / `EmissionIntensity`; a folder picker + intensity slider on the
  tab; a mismatched count is ignored with a warning. +2 tests (276 → 278). *(Interpreted for the
  frame-sequence workflow — AOVs arrive as sequences; the glow is baked into the strip, and a runtime
  toggle / value-track of the pass needs loader + manifest support, deferred.)* **(P3)**

### Close the loop (asset → working control)

- ✅ **AI asset generation — the Generate tab** (v1.1.0) — closes the loop at the *input* end:
  no starting art needed. The user's own OpenAI / Gemini / Claude key generates a **layered knob
  SVG** (static `body` + rotating `pointer`) that drops into the §6.8 layered-import pipeline, so
  only the pointer rotates. `IAssetGenerationService` (StripKit-aware prompt) + three
  `IAssetGenerationProvider`s over a shared `HttpClient` + `SvgSanitizer` + DPAPI-encrypted keys
  (`ISecretStore`); preview-by-importing + "Use in Create" handoff. App-only; +27 tests. *(v1.2.2:
  the model field is now an **editable `AutoCompleteBox`** — type a custom/just-released id — and
  the preview builds off the UI thread.)*
- ✅ **Generate: all control types** (v1.2.0 → v1.3.0) — the Generate tab now produces **knob /
  fader / slider / button / toggle / meter** art (was knob-only): the "WHAT TO MAKE" combo drives a
  type-aware prompt (knob → `body`+`pointer`, button/toggle → `off`+`on`, fader/slider → a single
  `body` cap, **meter → an off/on layered pair → background + revealed source**, incl. **horizontal
  meters** inferred from art aspect), and the handoff (v1.2.1) maps each to the right renderer path.
  *(v1.3.0 also added the full AI-generation program — see "AI generation: matching sets, variations,
  refine, vision, seeds" below.)* *(fader/slider/meter output paths still want a live eyeball + prompt
  tuning with a real API key — knob is the proven path.)*
- ✅ **AI generation: matching sets, variations, refine, vision, seeds** (v1.3.0) — the Generate tab
  grew a full generation program: a **matching-set generator** (one prompt → a consistent family of
  controls), a **variations grid** (N takes of one control), **refine** (revise the current SVG by
  instruction), **reference-image match / vision** ("describe this screenshot"), a **prompt seeds
  library** (built-in + saved style bundles), an **"avoid" field**, **auto-retry** on weak structure,
  **show-the-prompt**, and an **OpenAI-compatible custom endpoint** (OpenRouter / Ollama / LM Studio).
  App-only. *(Next: a "seeds → matching set → auto-assemble a Skin" end-to-end flow (P2); Azure
  OpenAI auth — api-key header — for the custom endpoint (P3).)*
- 🔄 **Code / component export** — every export can also emit ready-to-paste loader
  code for the target framework. **Shipped 2026-06-04: JUCE** (`LookAndFeel` filmstrip
  `Slider` / meter `Component`), **CSS/HTML** (`background-position` sprite + value setter),
  **iPlug2** (`IBKnobControl`/`IBSliderControl`/`IBitmapControl`), and **HISE** (`ScriptPanel`
  paint) — a pure `CodeSnippetService` mirroring `ManifestService`, a "CODE EXPORT" panel +
  live preview/copy, +15 tests. **Shipped v1.5.0:** a **React / Web-Component** target
  (`CodeTarget.React` → a `.jsx` sprite component driven by a 0..1 `value` prop; wired into the
  Create + Assemble + Batch code-export panels; +3 tests). **Remaining (P2):** **Unity / Godot**
  targets. *(was P1, ★ — the second of the three ★ bets; the first two targets were the recommended
  first wave.)*
- ✅ **Multi-control manifests** (v0.8.0) — the **Skin tab** surfaces the model's
  multi-control capability: bind several strips to several parameters in one `skin.json`, with
  per-control bounds + value range and a skin-level window background. `SkinViewModel` +
  `SkinControlEntry` + `SkinView`; `IManifestService.BuildManifest`; +6 tests. Pairs directly
  with theme/skin variants and code export. **(was P2)**

### Render quality / the first mile

- ✅ ★ **Layer-aware animation + auto-pointer extraction** — accept layered input
  (SVG / PSD or base + pointer) and tag per-layer behavior (rotate / stay) so only the pointer
  rotates while the body stays crisp, re-renderable at any resolution. Plus auto-detect the
  indicator in FLAT legacy art (seed from the existing `ContentAnalysis`). **(P1, ★ — all 3
  steps done.)**
  **Step 1 (v0.8.0): base + pointer PNGs** — a general `RenderLayer`/`LayerBehavior` model +
  `FilmstripSettings.Layers`; `RenderLayers` composites a static body + a rotating pointer (its
  own pivot) in `RenderFrame`/`RenderStrip`; explicit Base/Pointer slots; gated so empty layers
  reproduce prior output; mirrored in `FilmstripEngine.cs`. **Step 2 (v1.0.0): auto-pointer
  extraction from flat art** — `PointerExtractor` (radial-symmetry residual) splits a flat knob
  into base + pointer and auto-fills the slots, with a confidence score. **Step 3 (v1.0.0):
  layered PSD/SVG import** — `LayeredImportService` (app-only) reads SVG groups (Svg.Skia / MIT)
  and PSD layers (Magick.NET / Apache-2.0) into the renderer's existing layer stack, with
  name-guessed Static/Rotate behaviours the user overrides per layer; no renderer change, gated
  so prior output is byte-identical. *(Future: translate / opacity-ramp behaviours — a renderer
  increment — and layer reorder / deep-group flattening.)*
- ✅ **Procedural value-arc / fill-ring generator** (2026-06-04) — a Serum/Vital-style
  fill arc that tracks the value frame-by-frame is composited onto knob frames:
  configurable radius, thickness, colour, round/butt end caps, optional dim track,
  optional sweep gradient, and optional glow. Gated on `ShowValueArc` (off by default →
  existing output unchanged); `RenderValueArc` in the renderer + the `FilmstripEngine.cs`
  mirror; "VALUE ARC" panel in the Create tab. The arc inherits the knob's rotation sweep.
  **+8 tests (suite 57).** *(was P1, ★ — the first of the three ★ bets.)*

### Correctness (the sweep matches reality)

- ⏳ **Parameter-law-aware frame mapping** — map parameter → frame via a curve (log
  / skew / custom easing) so the visual sweep matches the plugin's actual parameter
  law (log frequency, dB) instead of a linear divisor. **(P2)** *(queued for v1.5.0 — one of the
  three v1.5 items deferred to a later careful pass.)*
- ⏳ **Frame-budget optimizer** — perceptually recommend the minimum frame count
  that looks identical to the eye, and show the file-size saving. **(P3)**

### Scale / design systems

- ⏳ **Theme / skin variant batch** — recolor or re-skin a control (or a whole
  folder) into a product's full theme set (light / dark / N skins) in one pass,
  wired into a multi-control manifest. **(P2)**
- ⏳ **Filmstrip design-system linter + frame diff** — audit a folder for
  consistency (frame counts, cell sizes, sweep angles, alignment), diff old vs new
  exports to catch regressions, and flag bad frames with a wobble / jump detector.
  **(P2)**
- ⏳ **Sprite-grid layout** — pack frames into an N-column grid (a 2D sprite atlas)
  instead of a single vertical/horizontal strip, for loaders that expect a grid sheet.
  **(P2)** *(queued for v1.5.0 — one of the three v1.5 items deferred to a later careful pass.)*
- ⏳ **Save / load render presets** — persist a control's full render setup (type,
  frames, sweep, resolution, meter/value-arc/layer settings) as a named preset and
  reload it in one click. **(P2)** *(queued for v1.5.0 — one of the three v1.5 items deferred
  to a later careful pass.)*

### QA (lock the output)

*(See also the linter + frame-diff above, which doubles as a QA tool.)*

- ✅ **Importer frame-count resampling** (v0.8.0) — the Import tab re-times a strip to a
  new frame count (`FilmstripImporter.Resample`, nearest-frame so a moving pointer never
  ghosts), not just re-stack orientation. **(was P3)** *(Interpolated/blended resampling
  remains intentionally unbuilt — nearest is correct for filmstrips.)*

### Reach beyond audio / handoff

- ⏳ **Web & animation exports** — CSS sprite (`steps()`), APNG / WebP / MP4, and
  Lottie (true vector Lottie when the source is layered). Takes StripKit's output
  well past JUCE filmstrips. **(P2)**
- ⏳ **Interactive shareable preview** — export a tiny self-contained HTML of the
  interactive control for client sign-off / docs: hand off a link, not a flat PNG.
  **(P2)**
- ⏳ **In-context mockup preview** — drop a screenshot of the plugin panel, place
  the control on it, and watch it animate in situ before exporting. **(P3)**

### New control types

- ✅ **Boolean trigger components** (v1.2.0 → v1.3.0) — buttons / toggles with discrete on/off (and
  hover / pressed / disabled) states, rendered as discrete-state filmstrips from layered art.
  New `ComponentType.Button` + `LayerBehavior.Frame` (a `Frame` layer shows only on its matching
  frame index; index 0 = off, 1 = on); a **BUTTON STATES** Create-tab section; the importer
  auto-tags `off`/`on` groups as `Frame`; the renderer's `RenderButtonLayers` path (mirrored in
  `FilmstripEngine.cs`); and Generate can produce the off/on SVG. **v1.3.0** added a first-class
  **`ComponentType.Toggle`** — a dedicated on/off toggle across generate / import / create /
  code-export, distinct from Button but sharing its state-frame render path. *(Future: momentary vs
  latching semantics + multi-state selectors are a loader/manifest concern, not a render one.)*
- 🔄 **Meter peak-hold / stereo** — peak-hold indicators, dual / stereo meters, dB
  segment spacing, and per-segment colour ramps. **Shipped v1.5.0:** a **peak-marker**
  (`FilmstripSettings.ShowMeterPeak` + `PeakColorArgb`, mirrored in `FilmstripEngine.cs`;
  `RenderMeterFrame` paints the direction-aware leading peak segment; gated OFF by default so
  existing meter goldens are byte-identical; +1 pixel-logic test). **Remaining (P3):** dual /
  stereo meters, dB segment spacing, per-segment colour ramps. *(carryover — deferred from Phase 6.)*

### Onboarding & documentation

- ✅ **Interactive in-app help / tutorial system** (v1.0.0) — a re-openable **"Getting
  Started"** guided overlay (`TutorialViewModel` + `TutorialOverlay`) walks a new user through the
  core loop (load art → choose a type → align → export → loader code → layered import). **Auto-opens
  on first launch** (a new minimal `ISettingsService` persists "seen"), re-openable from the header
  **"Getting started"** button, with a **bundled sample knob** (`IAssetService`) and **contextual
  tooltips** on the key controls. +11 tests (suite 112→123). A **Generate** walkthrough was added
  with the Generate tab (v1.1.0). **(was P1)** *(owner-requested.)*
- ⏳ **Website "Getting started" how-to guide** — a `stripkit.pro/getting-started/` page on the
  `Vybecode-LTD/StripKit-Website` repo: a step-by-step illustrated how-to (install → load a knob →
  align → export → drop in the JUCE/CSS/iPlug2/HISE loader), mirroring the in-app tutorial.
  Depends on the website deploy. **(P2)** *(owner-requested.)*

---

## Standing conventions for every phase

Keep the MVVM boundary (no UI types in view models, minimal code-behind), the
design tokens (the **Depth** machined-grey dark theme — `#f25914` ember accent, Verdana-led
sans for labels/body + monospace for numerics; vendored `Depth/Depth.axaml`, mapped in `App.axaml`),
compiled bindings, and `python -m pip` style invocation
in any Python helpers. After each phase, build, run, confirm, and update the "Last
completed task" section of `CLAUDE.md`.
