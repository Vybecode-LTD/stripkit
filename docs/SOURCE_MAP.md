# SOURCE_MAP — StripKit

> Version 1.3.0 · last-updated 2026-06-18 · last-audit 2026-06-18

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
  build step / another app. Includes the meter, value-arc, `RenderLayers` (base+pointer),
  and **button/toggle state-frame** (`RenderButtonLayers` / `LayerBehavior.Frame`) paths —
  `Toggle` shares the `Button` state-frame path. Keep in sync if the renderer math changes.
- `.gitignore` — .NET/Avalonia/test-output + packaging ignores (bin, obj, IDE,
  tests/**/output, publish/, installer/Output/; tracks only `releases/latest/*.exe`).
- `docs/` — `ARCHITECTURE.md` (deep dive), this map, `ROADMAP.md`, `TESTING.md`,
  `CHANGELOG.md`, `BUGS.md`, `HANDOFF.md`, `AUDIT-LOG.md`, `KICKOFF.md`, `PACKAGING.md`.
- `installer/` — `StripKit.iss` (Inno Setup script) + `wizard-large.bmp` /
  `wizard-small.bmp` (install-wizard art: StripKit brandmark + VybeCode logo).
  `installer/Output/` is the git-ignored ISCC output.
- `scripts/` — `Invoke-Release.ps1`, the Stage-1 local release driver (test-gate →
  **release-integrity guard** (abort on uncommitted tracked source; `-AllowDirty` overrides) →
  bump → publish → sign → ISCC → sign → stage `releases/latest/` → commit + tag + push; optional
  `-WebsiteRepo` runs Stage 3 via **hashtable** splatting). `Publish-WebsiteChangelog.ps1`, the
  **project-agnostic** Stage-3 tool that auto-drafts a version's `updates.json` entry from
  `docs/CHANGELOG.md` and (with `-Push`) publishes it to a forward-facing site's repo (auto-deploys).
  See `docs/PACKAGING.md`.
- `.github/workflows/` — `ci.yml` (build + test on every push/PR to main, windows-latest,
  .NET 9; `actions/checkout@v5` + `actions/setup-dotnet@v5`, Node 24) + `auto-release.yml`
  (Stage-2 CI release creator: VirusTotal scan + the sole `gh release create`, triggered by a
  pushed `releases/latest/*.exe`; `actions/checkout@v5`).
- `.github/ISSUE_TEMPLATE/` — `bug_report.md` + `feature_request.md`.
- `.github/pull_request_template.md` — PR checklist enforcing the house conventions.
- `releases/latest/` — the staged installer that triggers a release (the only tracked
  path under `releases/`).
- `.claude/skills/` — project-scoped skills the agent should use (see below).
- `src/StripKit/` — the application.
- `tests/StripKit.Tests/` — xUnit tests (216): renderer golden-image (with committed
  `baselines/`), the Generate-tab pipeline (`SvgSanitizerTests`, `SecretStoreTests`,
  `AssetGenerationProviderTests` via a fake HTTP handler, `AssetGenerationServiceTests`,
  `GenerateViewModelTests`, `GenerateViewTests`, `GenerateIntegrationTests`, the custom OpenAI-compatible
  endpoint `CustomOpenAiProviderTests`, the vision/`DescribeImage` payloads `VisionProviderTests`),
  `ContentAnalysis` + alignment render, pointer extraction (`PointerExtractorTests`), layered-import
  service + VM + render (`LayeredImportServiceTests` / `LayeredImportViewModelTests` /
  `LayeredImportRenderTests`), onboarding (`TutorialViewModelTests` / `SettingsServiceTests`),
  view-model load-path, the decompression-bomb-capped PNG decode (`ImageLoadServiceTests`), importer
  engine + VM, manifest (incl. multi-control), batch processor + VM (incl. meter), Skin tab VM
  (`SkinViewModelTests`), meter renderer, value-arc renderer (`ValueArcRenderTests`), layered-knob
  renderer (`LayeredKnobRenderTests`), the toggle/button state-frame renderer (`ToggleRenderTests`),
  code-snippet generation (`CodeSnippetServiceTests`), and a headless drop-zone test. See
  `docs/TESTING.md`.

## Application source (`src/StripKit/`)

- `Program.cs` — entry point; builds the Avalonia app (`UsePlatformDetect`,
  `WithInterFont`).
- `App.axaml` / `App.axaml.cs` — the **Obsidian glassmorphism** design tokens (Fluent
  base, `#e8440a` accent, acrylic/glass brushes, Verdana sans-serif) and the
  **composition root**: all DI registrations live here, and the
  `MainWindow` is created with its view model and given to the dialog service.
  `App.axaml.cs` also strips the framework's default data-validator
  (`BindingPlugins.DataValidators.RemoveAt(0)`) so CommunityToolkit + Avalonia don't double-report.
- `app.manifest` — Windows per-monitor-v2 DPI awareness.

### `Models/` — pure data, no UI or Skia dependencies

- `ComponentType.cs` — enum (six types): `RotaryKnob`, `VerticalFader`, `HorizontalSlider`, `Meter`,
  `Button` (a discrete-state button: frame 0 = off, frame 1 = on, +hover/pressed/disabled), and
  `Toggle` (an on/off switch — 2 frames; rendered via the same `Button` state-frame path, but its own
  type so it gets switch/rocker generated art + a latching boolean code-export binding).
- `StackDirection.cs` — enum: `Vertical`, `Horizontal`.
- `MeterFillDirection.cs` — enum: `Up`, `Down`, `LeftToRight`, `RightToLeft` (meters).
- `FrameTransform.cs` — a readonly record struct describing where the source
  layer is drawn in one frame (translate, draw size, rotation, pivot).
- `FilmstripSettings.cs` — the full render contract (frame count/size, angles,
  pivot, content-centre alignment, margins, supersample, stack direction, meter
  fields, the value-arc fields, and the `Layers` stack). Passed to the renderer.
- `StripDetection.cs` — the inferred layout of an *existing* strip (count, frame
  size, orientation, classified kind, low-confidence flag). Output of the importer.
- `SkinManifest.cs` — `SkinManifest` / `ManifestControl` / `ManifestBounds` records:
  the `skin.json` schema that binds an exported strip to a plugin parameter.
- `BatchModels.cs` — `BatchOptions` (inputs/output/template), `BatchProgress`,
  `BatchItemResult`, `BatchResult` for the Batch tab.
- `CodeModels.cs` — `CodeTarget` enum (`Juce` / `Css` / `IPlug2` / `Hise`) +
  `CodeSnippetRequest` record: the inputs for the code-export service.
- `RenderLayer.cs` — `LayerBehavior` enum (`Static` / `Rotate` / `Frame`) + `RenderLayer`
  (behaviour + a normalized per-layer pivot): the ordered layer stack for a layered knob /
  button (`FilmstripSettings.Layers`). `Static` = every frame, `Rotate` = knob pointer,
  `Frame` = shown only on the frame whose index matches the layer's index (button off/on
  state art). Skia-free; the layer's bitmap is passed alongside to the renderer.
- `AppSettings.cs` — the persisted preferences (`HasSeenTutorial`; the Generate tab's last-used
  `GenerateProvider` + per-provider model overrides; the custom-provider `GenerateCustomBaseUrl`; and
  the user's saved prompt seeds `GenerateSeeds`), serialized by `SettingsService`. API keys are
  **not** here — they live encrypted in the secret store.
- `GenerationModels.cs` — the Generate-tab data: `AiProvider` enum (Claude/OpenAI/Gemini/**Custom** —
  any OpenAI-compatible endpoint), `GenerationStyle` enum, the `GenerationRequest` (now incl. an
  `Avoid` negative-direction note + a `MeterHorizontal` flag) / `GenerationResult` records,
  `GenerationSetItem` (one control in a matching set), `ReferenceDescription` (a vision describe-image
  result), and `GenerationSeed` + `GenerationSeedLibrary` (a named style bundle + 5 built-in seeds).
  No deps.
- `TutorialStep.cs` — one Getting Started step (title, body, optional tip, offers-sample flag).

### `Services/` — the engine and I/O

- `IFilmstripRenderer.cs` / `SkiaFilmstripRenderer.cs` — **the heart of the app.**
  `ComputeTransform` holds the per-component math; `RenderFrame` composites one
  frame with supersampling + Mitchell cubic resampling (meters fill segments via
  `RenderMeterFrame` — procedural bars or a layered on/off-art reveal; knobs may get a
  value-tracking fill arc via `RenderValueArc`, or be composited from a base+pointer layer
  stack via `RenderLayers` when `settings.Layers` + the `layerArt` are supplied; **buttons** and
  **toggles** composite discrete state art per frame via `RenderButtonLayers` — `Static` layers on
  every frame, `Frame` layers only on their matching index (`Toggle` shares the `Button` path));
  `RenderStrip` stacks frames into the output PNG. No Avalonia dependency. Do not rewrite this.
- `PointerExtractor.cs` — static: `Extract` splits a flat knob into a static base + a rotating
  pointer via the radial-symmetry residual (★ #3 step 2; auto-fills the layered-knob slots).
  Returns a `PointerExtractionResult` (base, pointer, confidence). No Avalonia dependency;
  app-only (not mirrored in `FilmstripEngine.cs`).
- `ILayeredImportService.cs` / `LayeredImportService.cs` — `Import` parses a layered `.svg`
  (Svg.Skia / MIT) or `.psd`/`.psb` (Magick.NET / Apache-2.0) into named, behaviour-tagged,
  canvas-registered layers (★ #3 step 3) → a `LayeredImportResult` (`ImportedLayer[]` + canvas
  size). `Guess` auto-tags by name (pointer/needle/… → Rotate; exact `off`/`on` → Frame; else
  Static). Runs `SafeXml.Parse` **before** handing the SVG to Svg.Skia (BUG-010 — the hardened
  parse must gate the untrusted document first), and caps SVG/PSD input size. Feeds the renderer's
  existing layer stack; no Avalonia dependency; app-only (NOT in `FilmstripEngine.cs`). The interface
  file holds the `ImportedLayer` / `LayeredImportResult` DTOs.
- `SafeXml.cs` — static: hardened `XDocument` parse for **untrusted** SVG (AI replies + imported
  files): `DtdProcessing.Prohibit`, no `XmlResolver`, `MaxCharactersFromEntities = 0` — closes
  entity-expansion DoS ("billion laughs") + external-entity / SSRF. Used by `SvgSanitizer` and
  `LayeredImportService`. BCL only (`System.Xml`); app-only.
- `IImageLoadService.cs` / `ImageLoadService.cs` — decode a PNG to an `SKBitmap`, capping the decoded
  dimensions via `SKCodec` first (a decompression-bomb guard against a hostile image).
- `IFileDialogService.cs` / `FileDialogService.cs` — open-image / open-layered (SVG/PSD) /
  save-PNG / **save-SVG** / open-folder pickers via Avalonia `StorageProvider`. The concrete class
  holds the `Owner` window, set in `App.axaml.cs` after the window is created.
- `ISettingsService.cs` / `SettingsService.cs` — load/save the small `AppSettings` JSON
  (`%APPDATA%/StripKit/settings.json`); the app's persisted state (the first-run "seen
  tutorial" flag + the Generate tab's last provider/model). Best-effort; constructor-injectable path
  for tests. No Avalonia dependency.
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
  `MapType` lowercases the component type for the manifest (`Button` → "button", `Toggle` → "toggle").
- `ICodeSnippetService.cs` / `CodeSnippetService.cs` — emit ready-to-paste loader code
  (JUCE / CSS-HTML / iPlug2 / HISE) for an exported strip: `Generate` / `FileName` (pure)
  + a thin `SaveAsync`. No Avalonia dependency.
- `IBatchProcessor.cs` / `BatchProcessor.cs` — render a folder of sources into many
  strips off the UI thread (`Task.Run`), with per-item progress and between-item
  cancellation; isolates per-file failures. No Avalonia dependency.
- `IAssetGenerationService.cs` / `AssetGenerationService.cs` — the **Generate tab** orchestrator:
  builds the StripKit-aware SVG prompt (type-aware — knob = `body`+`pointer`, button/toggle = `off`+`on`,
  fader/slider = a single `body` cap; square canvas, ~10% margin, pointer at 12 o'clock), dispatches
  to the chosen provider, then extracts + sanitizes the SVG and returns a `GenerationResult`. Also:
  `GenerateSetAsync` (a matching SET — every chosen type with the same style inputs, run concurrently),
  `GenerateVariationsAsync` (several concurrent takes of one control), `RefineAsync` (revise an existing
  SVG from a plain-language instruction), `DescribeReferenceAsync` (vision — describe a reference image
  for "match this style"), and `BuildPrompts` (the exact (system, user) prompt, for the show-prompt
  preview). Networked + non-deterministic (unlike the rest); app-only, not in `FilmstripEngine.cs`.
- `IAssetGenerationProvider.cs` (+ `GenerationException` + `HttpAssetGenerationProvider` base) and
  `ClaudeProvider.cs` / `OpenAiProvider.cs` / `GeminiProvider.cs` — one provider per AI service
  (Anthropic Messages / OpenAI Chat Completions / Gemini generateContent), each with its own URL,
  per-request auth header, request body, and response parse; non-2xx → a friendly `GenerationException`.
  Each now also implements `DescribeImageAsync` (a **vision** image-description payload; the base default
  reports "unsupported" so a new provider compiles without it). `OpenAiProvider` exposes an overridable
  `EndpointUrl` so a subclass can repoint it. Share one DI-singleton `HttpClient`.
- `CustomOpenAiProvider.cs` — an **OpenAI-compatible custom endpoint** (OpenRouter / Ollama / LM Studio /
  Azure OpenAI) at a user-supplied base URL. Subclasses `OpenAiProvider` and overrides **only** the URL
  (read from `AppSettings.GenerateCustomBaseUrl` at call time, normalised to `…/chat/completions`); no
  built-in default/suggested models (the user types the id their endpoint expects). App-only.
- `SvgSanitizer.cs` — static: carves the `<svg>…</svg>` out of a chatty/fenced model reply and strips
  anything active or external (script / event handlers / `<image>` / `<foreignObject>` / off-document
  `href`) before it reaches the renderer. Parses through `SafeXml` (`System.Xml.Linq`), no Skia.
- `ISecretStore.cs` / `DpapiSecretStore.cs` — per-provider API-key storage encrypted at rest via
  Windows **DPAPI** (`ProtectedData`, CurrentUser) → `%APPDATA%/StripKit/secrets.dat` (ciphertext;
  base64 fallback off-Windows for dev/test). Constructor-injectable path for tests.

### `Helpers/`

- `SkiaImageInterop.cs` — converts an `SKBitmap` to an Avalonia `Bitmap` for the
  preview (PNG round-trip). See the `avalonia-skia-interop` skill for the faster
  `WriteableBitmap` path if preview performance ever matters.
- `HexToColorBrushConverter.cs` — an `IValueConverter` that turns a `#RRGGBB` hex string into an
  Avalonia `IBrush`, backing the Generate tab's body/accent colour swatches (live as you type).

### `Controls/`

- `SectionHeader.cs` — a `TemplatedControl` with one `Text` styled property: a short dark label
  with a 3px accent divider beneath it overhanging ~25% past the text (styled by a `ControlTheme`
  in `App.axaml`). Used throughout the sidebars.

### `ViewModels/`

- `ViewModelBase.cs` — `ObservableObject` base.
- `MainWindowViewModel.cs` — all bound state and commands. A single
  `OnPropertyChanged` funnel recomputes derived readouts and refreshes the
  preview; `_suspendRefresh` guards bulk updates. `BuildSettings()` maps the
  bound properties to a `FilmstripSettings` (and appends the layered-knob `Layers`;
  `BuildLayerArt()` supplies the matching base/pointer bitmaps). See the
  `live-preview-render-loop` skill — this view model is a worked example of that pattern.
  Exposes `Importer`, `Batch`, `Skin`, and `Generate` (the other tabs' view models); wires the
  Generate tab's `UseInCreateRequested` event to jump to Create and import the generated SVG
  (`ImportLayeredFromPathAsync`, shared with the file picker) — **honouring the generated control
  type** (knob → body+pointer layers; button/toggle → off/on Frame layers; meter → background+source
  layers; fader/slider → flattened source). The layered file picker auto-detects an `off`/`on` pair as
  a **toggle**.
- `ImporterViewModel.cs` — backs the **Import** tab: load an existing strip, run
  detection, scrub the detected frames, and extract / re-stack. Same preview-funnel
  pattern; holds no Avalonia UI types beyond the preview bitmap.
- `BatchViewModel.cs` — backs the **Batch** tab: input/output folders + a render
  template (now incl. the meter settings + the layered/backdrop toggle), run a folder export
  off-thread with progress + cancel + a results summary. No Avalonia UI types.
- `SkinViewModel.cs` — backs the **Skin** tab: a multi-control `skin.json` builder (a controls
  list, add-from-strip via `FilmstripImporter.Detect`/add-blank, a per-control detail editor,
  skin metadata, and export-to-folder); its `MapType` maps the component type for the manifest
  (`Button` → "button", `Toggle` → "toggle"). No Avalonia UI types.
- `SkinControlEntry.cs` — the mutable, observable per-control row the Skin list + detail editor
  bind to; mapped to the immutable `ManifestControl` record on export.
- `ImportedLayerRow.cs` — the observable per-layer row for an imported SVG/PSD (name + editable
  `Behavior` (Static / Rotate / Frame) + the canvas-sized art); drives the Create-tab import list (§6.8).
- `GenerateViewModel.cs` — backs the **Generate** tab: provider/model/key/style + the generated
  **control type**, the async cancellable Generate command, preview via `ILayeredImportService.Import`
  (which also validates the layered structure), Save/Copy SVG, a structure warning (knob with no
  pointer / button missing a state), and the `UseInCreateRequested` handoff event. The **model field is
  free text** (an editable `AutoCompleteBox` bound to a per-provider `SuggestedModels` list — a
  custom/just-released model id is sent verbatim, and a pinned-but-delisted model shows as text rather
  than a blank box). The preview is built **off the UI thread** (`BuildPreview` runs the temp-write +
  layered import + composite + PNG-encode inside one `Task.Run`; the UI thread only assigns the
  finished bitmap), and the prior temp SVG is dropped each generation (no temp accumulation). Persists
  the provider/model prefs (`ISettingsService`) and the key (`ISecretStore`). Also drives the newer
  Generate features: a **matching set** (`SetTypeOption` checklist → `GenerateSetAsync` → a
  `GeneratedSetResult` grid), **variations** (several concurrent takes to pick from), **refine** (a
  plain-language revise instruction), **vision** ("match this style" from a reference image via
  `DescribeReferenceAsync`), **prompt seeds** (apply/save built-in + user `GenerationSeed`s), an
  **avoid** negative-direction field, **auto-retry** on a transient failure, **show-prompt** (preview
  the exact prompt via `BuildPrompts`), and the **custom-endpoint** base-URL field (the `Custom`
  provider). No Avalonia UI types beyond the preview bitmap.
- `GenerateSetModels.cs` — two small Generate-tab view types: `SetTypeOption` (a checkable control
  type in the matching-set picker — which types to include) and `GeneratedSetResult` (one generated
  set/variation result: type + label, the preview bitmap, the temp SVG path, the raw SVG, and a
  success flag). Bound by `GenerateViewModel`'s set/variation grids.
- `TutorialViewModel.cs` — backs the Getting Started overlay: the step list, navigation
  (Next/Back/Skip), first-run auto-open via `ISettingsService`, and the `LoadSampleRequested`
  event the host VM wires to the bundled sample knob. No Avalonia UI types.

### `Views/`

- `MainWindow.axaml` — the UI, a `TabControl` with five tabs: **Create** (settings
  panel + preview/scrub/play/export), **Import** (hosts `ImporterView`, bound to
  `MainWindowViewModel.Importer`), **Batch** (hosts `BatchView`, bound to `.Batch`),
  **Skin** (hosts `SkinView`, bound to `.Skin`), and **Generate** (hosts `GenerateView`, bound to
  `.Generate`). Compiled bindings.
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
- `GenerateView.axaml(.cs)` — the Generate tab's `UserControl` (`x:DataType` = `GenerateViewModel`):
  provider/key/model + control type + style/accent/size + Generate/Cancel (left), the SVG preview +
  status + Use-in-Create / Save / Copy + a raw-response expander (right). The **model picker is an
  `AutoCompleteBox`** (free text + suggestions); the provider + control-type + style pickers stay
  `ComboBox`es. Code-behind holds the clipboard copy + the colour-picker flyout handlers (the swatch
  buttons), like the Create tab's snippet copy.
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
- `layer-aware-filmstrip-compositing` — the layered-knob (base + pointer) compositing model.
- `release-source-integrity-guard` — ensure a release tag can rebuild its own artifact (commit feature source before the release script runs; portable).
