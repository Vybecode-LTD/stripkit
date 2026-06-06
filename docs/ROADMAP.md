# ROADMAP — StripKit

> Version 1.0.0 · last-updated 2026-06-06 · last-audit 2026-06-06

The master roadmap for StripKit. Phases 0–8 (the v1 scaffold through the v0.6.0
ship — Inno installer, release pipeline, and website) are **complete**, and three
further releases have shipped since (see **Releases** below). The **vNext — Future
features** section captures the product-brainstorm backlog, grouped by theme and
priority. The three ★ bets were value-arc (✅), code-export (✅ first wave), and
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
  as optional Stage 3 (`-WebsiteRepo`). Hybrid: auto-draft → refine → push.
- ⏳ **Minor: bump `actions/checkout@v4 → v5`** — v4 runs on the soon-deprecated Node 20 (the
  v1.0.0 Auto Release run warned about it). Both `ci.yml` and `auto-release.yml`.

---

## vNext — Future features

The product backlog, grouped by theme. Each item has a 1–2 sentence description
and a priority tag (**P1** highest → **P3** lowest). The three ★ items are the
highest-leverage bets across all groups; pursue them first.

### Close the loop (asset → working control)

- 🔄 **Code / component export** — every export can also emit ready-to-paste loader
  code for the target framework. **Shipped 2026-06-04: JUCE** (`LookAndFeel` filmstrip
  `Slider` / meter `Component`), **CSS/HTML** (`background-position` sprite + value setter),
  **iPlug2** (`IBKnobControl`/`IBSliderControl`/`IBitmapControl`), and **HISE** (`ScriptPanel`
  paint) — a pure `CodeSnippetService` mirroring `ManifestService`, a "CODE EXPORT" panel +
  live preview/copy, +15 tests. **Remaining (P2):** a **React / Web Component** and
  **Unity / Godot** targets. *(was P1, ★ — the second of the three ★ bets; the first two
  targets were the recommended first wave, all four shipped.)*
- ✅ **Multi-control manifests** (Unreleased) — the **Skin tab** surfaces the model's
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
  reproduce prior output; mirrored in `FilmstripEngine.cs`. **Step 2 (Unreleased): auto-pointer
  extraction from flat art** — `PointerExtractor` (radial-symmetry residual) splits a flat knob
  into base + pointer and auto-fills the slots, with a confidence score. **Step 3 (Unreleased):
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
  law (log frequency, dB) instead of a linear divisor. **(P2)**
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

### QA (lock the output)

*(See also the linter + frame-diff above, which doubles as a QA tool.)*

- ✅ **Importer frame-count resampling** (Unreleased) — the Import tab re-times a strip to a
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

- ⏳ **Boolean trigger components** — buttons and toggles with an on/off state:
  momentary vs latching, two-state and multi-state selectors, rendered as
  discrete-state filmstrips from layered art or N source PNGs. **(P2)** *(new —
  explicitly requested.)*
- ⏳ **Meter peak-hold / stereo** — peak-hold indicators, dual / stereo meters, dB
  segment spacing, and per-segment colour ramps. **(P3)** *(carryover — deferred
  from Phase 6.)*

### Onboarding & documentation

- ✅ **Interactive in-app help / tutorial system** (Unreleased) — a re-openable **"Getting
  Started"** guided overlay (`TutorialViewModel` + `TutorialOverlay`) walks a new user through the
  core loop (load art → choose a type → align → export → loader code → layered import). **Auto-opens
  on first launch** (a new minimal `ISettingsService` persists "seen"), re-openable from the header
  **"Getting started"** button, with a **bundled sample knob** (`IAssetService`) and **contextual
  tooltips** on the key controls. +11 tests (suite 112→123). **(was P1)** *(owner-requested.)*
- ⏳ **Website "Getting started" how-to guide** — a `stripkit.pro/getting-started/` page on the
  `Vybecode-LTD/StripKit-Website` repo: a step-by-step illustrated how-to (install → load a knob →
  align → export → drop in the JUCE/CSS/iPlug2/HISE loader), mirroring the in-app tutorial.
  Depends on the website deploy. **(P2)** *(owner-requested.)*

---

## Standing conventions for every phase

Keep the MVVM boundary (no UI types in view models, minimal code-behind), the
design tokens (Obsidian glassmorphism — dark theme, `#e8440a` accent, Verdana-led
sans-serif; see `App.axaml`), compiled bindings, and `python -m pip` style invocation
in any Python helpers. After each phase, build, run, confirm, and update the "Last
completed task" section of `CLAUDE.md`.
