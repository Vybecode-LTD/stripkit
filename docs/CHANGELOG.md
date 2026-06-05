# CHANGELOG — StripKit

> Version 0.7.0 · last-updated 2026-06-04 · last-audit 2026-06-04
>
> Notable changes per doc/feature version. Dates are authoring dates; several
> versions landed on 2026-06-03 across one working stretch.

## [Unreleased]

### Added
- **Skin tab — multi-control `skin.json` builder.** A new fourth tab that binds several
  exported filmstrips to several parameters in one manifest, surfacing what the
  `SkinManifest` model already supported. Add controls **from a strip** (the importer
  auto-detects frame count / size / orientation / kind) or **blank**; edit each in a detail
  panel (id, type, parameter id, asset/@2x, frames, frame size, stack, on-screen **bounds**,
  optional **value range**); set skin-level **name / author / design resolution / window
  background**; **Export skin.json…** writes one combined manifest into a chosen folder. New
  `IManifestService.BuildManifest`, `SkinViewModel` + `SkinControlEntry` + `SkinView`. **+6
  tests, suite 86→92.**
- **Batch-tab meter settings.** The Batch tab now exposes the full meter template (segment
  count, fill direction, continuous/discrete, on/off colours) when the component type is
  **Meter**, plus a **"source is a backdrop"** toggle that switches each file between the
  meter's **lit on-state art** (layered — revealed up to the fill) and a **housing/backdrop**
  with **procedural LED segments** drawn over it. New `BatchOptions.MeterSourceIsBackdrop`;
  `BatchProcessor` routes the source to the on-art or the background slot accordingly.
  Resolves the v0.6.0 carryover where `ComponentType.Meter` was selectable in Batch but had
  no settings. **+2 tests, suite 84→86.**
- **Layer-aware knob animation — base + pointer (★ #3, step 1).** A knob can now be built
  from **two layers**: a **static base** (the body / well, drawn fixed) and a separate
  **pointer** that rotates with the value — so only the pointer moves and the body stays
  crisp and re-renderable at any resolution. A general, ordered **layer model**
  (`RenderLayer` + `LayerBehavior {Static, Rotate}`, with a per-layer normalized pivot) on a
  new `FilmstripSettings.Layers` list; the renderer composites the stack bottom-first via a
  new `RenderLayers` path in `RenderFrame`/`RenderStrip` (both gained an optional
  index-matched `IReadOnlyList<SKBitmap>? layerArt` parameter). The pointer rotates about its
  **own** pivot (independent of the body), seeded from the body's detected content centre and
  adjustable via numeric **Pointer pivot X/Y** + a "Center pointer on body" reset. A value arc
  still composes on top, concentric with the body centre. Create-tab **"LAYERED KNOB (base +
  pointer)"** panel in the rotary section: Load/Clear **Base** + **Pointer** slots and the
  pivot controls. **Gated by defaults — an empty `Layers` list renders the single source
  byte-for-byte as before, so all prior golden baselines are unchanged.** Knob-only for now
  (faders/sliders/meters ignore layers). Mirrored in the standalone `FilmstripEngine.cs`.
  **+12 tests (`LayeredKnobRenderTests`: 3 golden baselines + 6 pixel-logic; 3 VM load-path
  tests in `LoadPathTests`), suite 84 passing.** *(Next layer-aware steps: auto-pointer
  extraction from flat art, then layered PSD/SVG import.)*

## [0.7.0] — 2026-06-04

### Added
- **Code / component export.** Every export can also emit ready-to-paste **loader code** for
  the target framework, so there's no hand-wiring step: **JUCE** (`LookAndFeel` filmstrip
  `Slider` for knob/fader/slider, or a meter `Component`), **CSS/HTML** (a self-contained
  `background-position` sprite + a 0..1 value setter), **iPlug2** (`IBKnobControl` /
  `IBSliderControl` / `IBitmapControl` + `LoadBitmap`), and **HISE** (a `ScriptPanel` paint
  routine). All share the universal rule `frame = clamp(round(value·(N−1)), 0, N−1)` and pick
  the source axis from the stack direction. New `CodeTarget` enum + `CodeSnippetRequest` model
  and a **pure** `CodeSnippetService` (`Generate` / `FileName` / `SaveAsync` — no Skia/Avalonia),
  mirroring `ManifestService`. Create tab: a **"CODE EXPORT"** panel (per-target tick boxes →
  one file each next to the PNG, e.g. `.juce.h` / `.html` / `.iplug2.cpp` / `.hise.js`) plus a
  live **preview / copy-to-clipboard** expander. Identifiers are sanitised per language.
  **+15 tests (`CodeSnippetServiceTests`), suite 72.** *(React / Web Component and Unity / Godot
  targets remain on the roadmap.)*
- **Value arc / fill ring (knobs).** An optional Serum/Vital-style fill arc composited onto
  each knob frame that tracks the value: the lit arc sweeps from the start angle to the
  current frame's angle, concentric with the rotation pivot. Configurable radius, thickness,
  colour, round/butt end caps, an optional dim full-sweep track, an optional sweep gradient
  (two colours), and an optional glow. Eleven Skia-free `FilmstripSettings` fields (packed
  `0xAARRGGBB` colours), gated on `ShowValueArc` (**off by default — existing output is
  byte-identical**); rendered into the oversampled work surface (`RenderValueArc`) so it
  stays crisp; surfaced in a "VALUE ARC" panel in the Create tab's rotary section. The arc
  inherits the knob's rotation sweep (no separate start-angle field) and is knob-only.
  Mirrored in the standalone `FilmstripEngine.cs`. **+8 tests (4 golden baselines incl.
  gradient+glow, 4 pixel-logic), suite 57 passing.**

### Changed
- **Open-sourced under the MIT license.** Added a `LICENSE` (MIT), a public-facing
  `README.md` (badges, screenshot, tech stack, a contributing guide, and release-download
  links), and license/author metadata in `StripKit.csproj`. The website's hero now carries
  a "free & open source" badge linking to the public repo.

## [0.6.0] — 2026-06-03

### Changed
- New **glassmorphism UI** (Obsidian, chosen from two rendered mockups): acrylic
  frosted window (`TransparencyLevelHint="AcrylicBlur"` + `ExperimentalAcrylicBorder`,
  `FallbackColor` for non-acrylic platforms), translucent `Border.card` glass panels
  (hairline borders + soft shadows), the `#e8440a` accent kept, Fluent `accent` primary
  buttons, and a **Verdana-led sans-serif** font (replaces JetBrains Mono). Design
  tokens centralized in `App.axaml`; `MainWindow` / `ImporterView` / `BatchView`
  restyled. **Styling-only** — renderer, view-models, and tests untouched (41/41 green).
  Pending on-screen review + token tuning (translucency/tint/radius). Hover/focus +
  input-state polish added later (custom Button `ControlTheme` with `:pointerover` /
  `:pressed` / `:focus-visible`, brush + box-shadow transitions, rounded inputs, per-tab
  dividers with near-white subtitles). Design confirmed on-screen by the owner.

### Added
- **Alignment tools (pivot-only).** The renderer centres art on its detected *content*
  centre (`SourceCenterX/Y`, normalized; 0.5,0.5 reproduces prior output) and rotates about
  that point, so an off-centre knob spins in place instead of orbiting. A persistent
  **Enable crosshair** toggle marks the spin centre with live drag — the art itself never
  moves — and the preview always renders at **1024 virtual steps** so alignment isn't
  quantized to the export frame count. Plus **Auto-center**, numeric **Center X/Y**,
  **Reset**, and auto-centring of knobs on load. New `ContentAnalysis` auto-detects the
  opaque-content centre. Same content-centring for fader/slider caps. Mirrored in
  `FilmstripEngine.cs`.
- **Preview transport.** Step back / Play–Stop / step forward / **Reset** (returns to the
  centred start); Play keeps working while the crosshair is enabled.
- **Header wordmark + About.** A wordmark logo on the header's right; a **?** About flyout
  showing the brandmark, version, https://stripkit.pro, VybeCode Software, and
  https://vybeco.de.
- **Windows installer + release pipeline.** An **Inno Setup** installer (per-user; choose
  the install dir; optional desktop + Start-Menu shortcuts; a registry-wiping uninstaller;
  both the StripKit brandmark and the VybeCode logo in the wizard). `scripts/Invoke-Release.ps1`
  (Stage 1: bump version across csproj / .iss / CHANGELOG, test-gate, self-contained publish,
  ISCC package, stage under `releases/latest/`, commit + tag + push) and
  `.github/workflows/auto-release.yml` (Stage 2: VirusTotal scan, then the single GitHub
  Release creator). The self-contained `win-x64` publish runs without the .NET SDK. See
  `docs/PACKAGING.md`.
- **Landing-page website.** A separate medium-light themed site (features grid, GitHub-driven
  download + changelog, privacy / terms / contact, VybeCode footer) under `StripKit-Website`.
- **App icon + favicon.** A multi-resolution `.ico` (16–256 px) embedded via
  `<ApplicationIcon>` and used for the window/taskbar; a compact `brand/favicon.ico`
  (16/32/48/64) for the future website. Generated from `stripkiticon02.png` (contain-fit,
  so the non-square source isn't distorted).

### Fixed
- Off-centre knob PNGs no longer "wobble"/orbit during the sweep — the rotation pivot is
  the art's detected content centre, not the image-rectangle centre.

### Removed
- **Velopack in-app auto-update** (`UpdateService`, `VelopackApp.Run()`, the package
  reference) — replaced by the Inno installer + website-download model. An Inno-installed
  app can't use Velopack's updater; updates now come via the website and GitHub Releases.

### Tests
- **49 passing.** `ContentAnalysis` detection, alignment render (off-centre art stays pinned
  to the frame centre across frames; a sanity test confirms it orbits without the fix),
  auto-center-on-load, and crosshair-toggle persistence. Golden baselines unchanged (the
  default 0.5,0.5 centre reproduces prior output).

## [0.5.0] — 2026-06-03 — Meter mode

### Added
- **Phase 6 — Meter mode** (design signed off first). New `ComponentType.Meter`,
  `MeterFillDirection` enum, and meter fields on `FilmstripSettings` (`SegmentCount`,
  `FillDirection`, `ContinuousFill`, `SegmentGap`, `OnColorArgb`/`OffColorArgb` — all
  Skia-free). The renderer's `RenderMeterFrame` renders **procedural** segment bars
  (no art) or a **layered** reveal (source = on art over background off art),
  auto-selected by whether art is loaded. All four fill directions; discrete by
  default with a continuous toggle. Create-tab "Meter" type + METER settings group; a
  procedural meter previews/exports without a source. Manifest maps to `"meter"`.
  Mirrored in the standalone `FilmstripEngine.cs`.
- `RenderFrame`/`RenderStrip` now accept a nullable `source` (null only for a
  procedural meter).

### Tests
- 9 meter renderer tests (5 committed golden baselines + 4 pixel-logic for fill
  direction & layered reveal) + 1 VM test (procedural meter exports without a source).
  Suite: **41 passing**.

## [0.4.0] — 2026-06-03 — Batch processing

### Added
- **Phase 5 — Batch processing (Batch tab).** `BatchProcessor` (SkiaSharp, no
  Avalonia) renders a filmstrip for each source image in a folder, off the UI thread
  (the whole loop runs via `Task.Run`), reporting per-item progress and cancelling
  between items without throwing (returns a result with `Cancelled` set). Failures are
  isolated (one bad file does not abort the run). New `BatchOptions` / `BatchProgress`
  / `BatchItemResult` / `BatchResult` models, `BatchViewModel`, and `BatchView` (third
  tab — **Create** | **Import** | **Batch**). Per-knob frame sizing ("Square knob
  frames to each source"); optional @2x + `skin.json` per strip.
- `IFileDialogService.OpenFolderAsync` (folder picker) for the input/output folders.

### Tests
- 4 batch-processor integration tests (real files: per-file render, failure
  isolation, cancellation, @2x+manifest) + 2 batch-VM gating tests. Suite: **31 passing**.

## [0.3.0] — 2026-06-03 — Importer + manifest

### Added
- **Phase 2 — Filmstrip importer (Import tab).** `FilmstripImporter` (SkiaSharp,
  no Avalonia) detects frame count / orientation / control kind from a strip's
  dimensions (ordered candidate counts + aspect classification), flags ambiguous
  "square + adjacent count" cases, extracts a single frame, and re-stacks to a new
  orientation. New `StripDetection` model, `ImporterViewModel`, and `ImporterView`
  UserControl. `MainWindow` is now a `TabControl` (**Create** | **Import**); each tab
  has its own scoped drop zone.
- **Phase 3 — Manifest export.** A "Also write a skin.json manifest" toggle + a
  parameter-id field on the Create tab; export writes a schema-valid
  `<name>.skin.json` next to the PNG via the new `ManifestService` (System.Text.Json,
  camelCase). New `SkinManifest` / `ManifestControl` / `ManifestBounds` models.

### Tests
- Importer engine tests (detection/classification/ambiguity, extraction, lossless
  re-stack), importer VM tests, manifest mapping + JSON-Schema-conformance tests.
  Suite: **25 passing**.

### Docs
- New `docs/ARCHITECTURE.md`, `docs/TESTING.md`, `docs/CHANGELOG.md`, `docs/BUGS.md`,
  `docs/HANDOFF.md`, `docs/AUDIT-LOG.md`. README rewritten for the two-tab app +
  manifest + tests. `SOURCE_MAP`, `ROADMAP`, `KICKOFF` reconciled to current state.

## [0.2.0] — 2026-06-03 — Rename, Phase 0/1, test project

### Changed
- **Renamed the app FilmstripForge → StripKit** across all docs and code (brand,
  namespaces, `RootNamespace`/`AssemblyName`, `app.manifest`, `avares://` URIs,
  solution/project, folder → `src/StripKit/`). Kept "filmstrip" as the domain noun;
  left VybeForge / VybeCod.ing alone. Deleted the duplicate `skills/FilmstripForge/`
  mirror; left reusable `skills/*` generic.

### Fixed
- **BUG-001:** illegal `--` in a `.csproj` XML comment (build blocker).
- **BUG-002:** missing `Microsoft.Extensions.DependencyInjection` package reference
  (build blocker). See `docs/BUGS.md`. Both pre-existing scaffold defects.

### Added
- **Phase 0 verified** — `dotnet build` 0/0; GUI boots; load → render → export
  round-trip confirmed (valid 80×5120 strip, clean 270° sweep).
- **Phase 1 — drag-and-drop.** The Create preview is a drop zone; the button and the
  drop share `MainWindowViewModel.LoadSourceFromPath` (no duplicated load logic).
- **Phase 4 (brought forward) — test project** `tests/StripKit.Tests`: renderer
  golden-image baselines, VM load-path tests, headless drop-zone test.

## [0.1.0] — 2026-06-02 — Scaffold handoff

- Packaged the v1 scaffold for a Claude Code handoff: `docs/` (`KICKOFF`,
  `SOURCE_MAP`, `ROADMAP`), six project skills under `.claude/skills/`, and the
  initial `CLAUDE.md`. Complete and cross-checked but **not yet compiled** (no SDK in
  the authoring environment).
