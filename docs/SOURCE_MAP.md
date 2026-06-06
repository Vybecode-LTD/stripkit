# SOURCE_MAP — StripKit

> Version 0.8.0 · last-updated 2026-06-05 · last-audit 2026-06-05

A file-by-file map so a coding agent can navigate the repo without reverse-
engineering it. The architecture is described in `CLAUDE.md`; this is the "where
does each thing live" companion.

## Top level

- `StripKit.sln` — the solution; references the app and test projects.
- `CLAUDE.md` — project context: stack, architecture, conventions, last task.
- `README.md` — human-facing build/run/use instructions.
- `KICKOFF.md` — pointer to `docs/KICKOFF.md` (single source of truth for the kickoff).
- `LICENSE` — MIT license (Copyright VybeCode Software 2026).
- `CONTRIBUTING.md` — contributor guide: setup, workflow, house conventions, renderer rules.
- `FilmstripEngine.cs` — standalone, copy-paste-portable renderer (namespace
  `StripKit.Engine`, SkiaSharp-only). **Not compiled by the app** — a hand-maintained
  mirror of `Services/SkiaFilmstripRenderer.cs` + the `Models`, for reuse in a CLI /
  build step / another app. Keep in sync if the renderer math changes.
- `.gitignore` — .NET/Avalonia/test-output + packaging ignores (bin, obj, IDE,
  tests/**/output, publish/, installer/Output/; tracks only `releases/latest/*.exe`).
- `docs/` — `ARCHITECTURE.md` (deep dive), this map, `ROADMAP.md`, `TESTING.md`,
  `CHANGELOG.md`, `BUGS.md`, `HANDOFF.md`, `AUDIT-LOG.md`, `KICKOFF.md`, `PACKAGING.md`.
- `installer/` — `StripKit.iss` (Inno Setup script) + `wizard-large.bmp` /
  `wizard-small.bmp` (install-wizard art: StripKit brandmark + VybeCode logo).
  `installer/Output/` is the git-ignored ISCC output.
- `scripts/` — `Invoke-Release.ps1`, the Stage-1 local release driver (test-gate →
  bump → publish → sign → ISCC → sign → stage `releases/latest/` → commit + tag + push; optional
  `-WebsiteRepo` runs Stage 3). `Publish-WebsiteChangelog.ps1`, the **project-agnostic** Stage-3
  tool that auto-drafts a version's `updates.json` entry from `docs/CHANGELOG.md` and (with `-Push`)
  publishes it to a forward-facing site's repo (auto-deploys). See `docs/PACKAGING.md`.
- `.github/workflows/` — `ci.yml` (build + test on every push/PR to main, windows-latest,
  .NET 9) + `auto-release.yml` (Stage-2 CI release creator: VirusTotal scan + the sole
  `gh release create`, triggered by a pushed `releases/latest/*.exe`).
- `.github/ISSUE_TEMPLATE/` — `bug_report.md` + `feature_request.md`.
- `.github/pull_request_template.md` — PR checklist enforcing the house conventions.
- `releases/latest/` — the staged installer that triggers a release (the only tracked
  path under `releases/`).
- `.claude/skills/` — project-scoped skills the agent should use (see below).
- `src/StripKit/` — the application.
- `tests/StripKit.Tests/` — xUnit tests (123): renderer golden-image (with committed
  `baselines/`), `ContentAnalysis` + alignment render, pointer extraction
  (`PointerExtractorTests`), layered-import service + VM + render
  (`LayeredImportServiceTests` / `LayeredImportViewModelTests` / `LayeredImportRenderTests`),
  onboarding (`TutorialViewModelTests` / `SettingsServiceTests`), view-model load-path, importer
  engine + VM, manifest (incl. multi-control), batch processor + VM (incl. meter), Skin tab VM
  (`SkinViewModelTests`), meter renderer, value-arc renderer (`ValueArcRenderTests`), layered-knob
  renderer (`LayeredKnobRenderTests`), code-snippet generation (`CodeSnippetServiceTests`), and a
  headless drop-zone test. See `docs/TESTING.md`.

## Application source (`src/StripKit/`)

- `Program.cs` — entry point; builds the Avalonia app (`UsePlatformDetect`,
  `WithInterFont`).
- `App.axaml` / `App.axaml.cs` — the **Obsidian glassmorphism** design tokens (Fluent
  base, `#e8440a` accent, acrylic/glass brushes, Verdana sans-serif) and the
  **composition root**: all DI registrations live here, and the
  `MainWindow` is created with its view model and given to the dialog service.
- `app.manifest` — Windows per-monitor-v2 DPI awareness.

### `Models/` — pure data, no UI or Skia dependencies

- `ComponentType.cs` — enum: `RotaryKnob`, `VerticalFader`, `HorizontalSlider`, `Meter`.
- `StackDirection.cs` — enum: `Vertical`, `Horizontal`.
- `MeterFillDirection.cs` — enum: `Up`, `Down`, `LeftToRight`, `RightToLeft` (meters).
- `FrameTransform.cs` — a readonly record struct describing where the source
  layer is drawn in one frame (translate, draw size, rotation, pivot).
- `FilmstripSettings.cs` — the full render contract (frame count/size, angles,
  pivot, content-centre alignment, margins, supersample, stack direction, meter
  fields, and the value-arc fields). Passed to the renderer.
- `StripDetection.cs` — the inferred layout of an *existing* strip (count, frame
  size, orientation, classified kind, low-confidence flag). Output of the importer.
- `SkinManifest.cs` — `SkinManifest` / `ManifestControl` / `ManifestBounds` records:
  the `skin.json` schema that binds an exported strip to a plugin parameter.
- `BatchModels.cs` — `BatchOptions` (inputs/output/template), `BatchProgress`,
  `BatchItemResult`, `BatchResult` for the Batch tab.
- `CodeModels.cs` — `CodeTarget` enum (`Juce` / `Css` / `IPlug2` / `Hise`) +
  `CodeSnippetRequest` record: the inputs for the code-export service.
- `RenderLayer.cs` — `LayerBehavior` enum (`Static` / `Rotate`) + `RenderLayer` (behaviour +
  a normalized per-layer pivot): the ordered layer stack for a layered knob (`FilmstripSettings.Layers`).
  Skia-free; the layer's bitmap is passed alongside to the renderer.
- `AppSettings.cs` — the persisted preferences (currently just `HasSeenTutorial`); the app's only
  saved state, serialized by `SettingsService`.
- `TutorialStep.cs` — one Getting Started step (title, body, optional tip, offers-sample flag).

### `Services/` — the engine and I/O

- `IFilmstripRenderer.cs` / `SkiaFilmstripRenderer.cs` — **the heart of the app.**
  `ComputeTransform` holds the per-component math; `RenderFrame` composites one
  frame with supersampling + Mitchell cubic resampling (meters fill segments via
  `RenderMeterFrame` — procedural bars or a layered on/off-art reveal; knobs may get a
  value-tracking fill arc via `RenderValueArc`, or be composited from a base+pointer layer
  stack via `RenderLayers` when `settings.Layers` + the `layerArt` are supplied); `RenderStrip`
  stacks frames into the output PNG. No Avalonia dependency. Do not rewrite this.
- `PointerExtractor.cs` — static: `Extract` splits a flat knob into a static base + a rotating
  pointer via the radial-symmetry residual (★ #3 step 2; auto-fills the layered-knob slots).
  Returns a `PointerExtractionResult` (base, pointer, confidence). No Avalonia dependency;
  app-only (not mirrored in `FilmstripEngine.cs`).
- `ILayeredImportService.cs` / `LayeredImportService.cs` — `Import` parses a layered `.svg`
  (Svg.Skia / MIT) or `.psd`/`.psb` (Magick.NET / Apache-2.0) into named, behaviour-tagged,
  canvas-registered layers (★ #3 step 3) → a `LayeredImportResult` (`ImportedLayer[]` + canvas
  size). Feeds the renderer's existing layer stack; no Avalonia dependency; app-only (NOT in
  `FilmstripEngine.cs`). The interface file holds the `ImportedLayer` / `LayeredImportResult` DTOs.
- `IImageLoadService.cs` / `ImageLoadService.cs` — decode a PNG to an `SKBitmap`.
- `IFileDialogService.cs` / `FileDialogService.cs` — open-image / open-layered (SVG/PSD) /
  save-PNG / open-folder pickers via Avalonia `StorageProvider`. The concrete class holds the
  `Owner` window, set in `App.axaml.cs` after the window is created.
- `ISettingsService.cs` / `SettingsService.cs` — load/save the small `AppSettings` JSON
  (`%APPDATA%/StripKit/settings.json`); the app's only persisted state (the first-run "seen
  tutorial" flag). Best-effort; constructor-injectable path for tests. No Avalonia dependency.
- `IAssetService.cs` / `AssetService.cs` — extract a bundled avares asset (the tutorial's sample
  knob) to a temp file path. App layer (uses Avalonia's asset loader).
- `IExportService.cs` / `ExportService.cs` — encode an `SKBitmap` to a PNG file.
- `IFilmstripImporter.cs` / `FilmstripImporter.cs` — detect an existing strip's
  layout from its dimensions (ordered candidate counts + aspect classification),
  extract a single frame, re-stack to a new orientation, and **resample** (re-time) to a
  different frame count via nearest-frame mapping. No Avalonia dependency.
- `IManifestService.cs` / `ManifestService.cs` — build + serialize a `skin.json`
  manifest (System.Text.Json, camelCase): `BuildSingleControl` (one control, from the Create
  tab) and `BuildManifest` (multi-control, from the Skin tab) binding strips to parameters.
- `ICodeSnippetService.cs` / `CodeSnippetService.cs` — emit ready-to-paste loader code
  (JUCE / CSS-HTML / iPlug2 / HISE) for an exported strip: `Generate` / `FileName` (pure)
  + a thin `SaveAsync`. No Avalonia dependency.
- `IBatchProcessor.cs` / `BatchProcessor.cs` — render a folder of sources into many
  strips off the UI thread (`Task.Run`), with per-item progress and between-item
  cancellation; isolates per-file failures. No Avalonia dependency.

### `Helpers/`

- `SkiaImageInterop.cs` — converts an `SKBitmap` to an Avalonia `Bitmap` for the
  preview (PNG round-trip). See the `avalonia-skia-interop` skill for the faster
  `WriteableBitmap` path if preview performance ever matters.

### `ViewModels/`

- `ViewModelBase.cs` — `ObservableObject` base.
- `MainWindowViewModel.cs` — all bound state and commands. A single
  `OnPropertyChanged` funnel recomputes derived readouts and refreshes the
  preview; `_suspendRefresh` guards bulk updates. `BuildSettings()` maps the
  bound properties to a `FilmstripSettings` (and appends the layered-knob `Layers`;
  `BuildLayerArt()` supplies the matching base/pointer bitmaps). See the
  `live-preview-render-loop` skill — this view model is a worked example of that pattern.
  Exposes `Importer`, `Batch`, and `Skin` (the Import, Batch, and Skin tab view models).
- `ImporterViewModel.cs` — backs the **Import** tab: load an existing strip, run
  detection, scrub the detected frames, and extract / re-stack. Same preview-funnel
  pattern; holds no Avalonia UI types beyond the preview bitmap.
- `BatchViewModel.cs` — backs the **Batch** tab: input/output folders + a render
  template (now incl. the meter settings + the layered/backdrop toggle), run a folder export
  off-thread with progress + cancel + a results summary. No Avalonia UI types.
- `SkinViewModel.cs` — backs the **Skin** tab: a multi-control `skin.json` builder (a controls
  list, add-from-strip via `FilmstripImporter.Detect`/add-blank, a per-control detail editor,
  skin metadata, and export-to-folder). No Avalonia UI types.
- `SkinControlEntry.cs` — the mutable, observable per-control row the Skin list + detail editor
  bind to; mapped to the immutable `ManifestControl` record on export.
- `ImportedLayerRow.cs` — the observable per-layer row for an imported SVG/PSD (name + editable
  Static/Rotate `Behavior` + the canvas-sized art); drives the Create-tab import list (§6.8).
- `TutorialViewModel.cs` — backs the Getting Started overlay: the step list, navigation
  (Next/Back/Skip), first-run auto-open via `ISettingsService`, and the `LoadSampleRequested`
  event the host VM wires to the bundled sample knob. No Avalonia UI types.

### `Views/`

- `MainWindow.axaml` — the UI, a `TabControl` with four tabs: **Create** (settings
  panel + preview/scrub/play/export), **Import** (hosts `ImporterView`, bound to
  `MainWindowViewModel.Importer`), **Batch** (hosts `BatchView`, bound to `.Batch`), and
  **Skin** (hosts `SkinView`, bound to `.Skin`). Compiled bindings.
- `MainWindow.axaml.cs` — minimal code-behind: the view-side auto-play
  `DispatcherTimer`, plus the Create preview's file drag-and-drop handlers (scoped
  to its drop border so they don't collide with the other tabs).
- `ImporterView.axaml(.cs)` — the Import tab's `UserControl` (`x:DataType` =
  `ImporterViewModel`): detection readout, editable frame count, a frame scrubber,
  extract / re-stack buttons, and its own drop zone.
- `BatchView.axaml(.cs)` — the Batch tab's `UserControl` (`x:DataType` =
  `BatchViewModel`): render template, input/output folder pickers, Run/Cancel, a
  progress bar, and a results summary. Markup-only code-behind.
- `SkinView.axaml(.cs)` — the Skin tab's `UserControl` (`x:DataType` = `SkinViewModel`): skin
  metadata + controls list (left), a per-control detail editor + Export skin.json (right).
  Markup-only code-behind.
- `TutorialOverlay.axaml(.cs)` — the Getting Started guided overlay (`x:DataType` =
  `TutorialViewModel`): a non-blocking bottom-centre glass card over a click-through scrim,
  hosted as the top layer of `MainWindow`'s root `Panel`. Markup-only code-behind.

### `Assets/`

- `README.txt` — where to drop a bundled font/icon. The app uses a **Verdana-led
  sans-serif** fallback chain (Obsidian design; JetBrains Mono was removed) and ships
  `stripkit.ico` / `stripkit.png` for the window / taskbar / installer icon.
- `sample-knob.png` — the bundled sample knob the tutorial's "Load sample knob" shortcut loads
  (extracted to a temp file by `AssetService`).

## Project skills (`.claude/skills/`)

These are scoped to the repo so Claude Code uses them automatically:

- `avalonia-skia-interop` — SkiaSharp-inside-Avalonia rendering and pixel interop.
- `avalonia-drag-drop-files` — file drag-and-drop into an Avalonia view.
- `live-preview-render-loop` — the responsive settings-driven preview pattern.
- `image-regression-testing` — golden-image tests to lock the renderer's output.
- `filmstrip-importer-engine` — detect and re-slice existing filmstrips (Phase 2).
- `plugin-asset-manifest` — the JSON manifest that binds strips to parameters.
