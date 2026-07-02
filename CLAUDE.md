# CLAUDE.md — StripKit

> Version 1.3.0 · last-updated 2026-06-30 · last-audit 2026-06-18

Context for any Claude Code / agent session working on this repo. Keep this file
short, current, and instruction-shaped. Update the **Last completed task** section
at the end of every working session.

## What this is

StripKit is a C#/Avalonia desktop tool that turns a single transparent PNG
into an animated **filmstrip** (sprite sheet) for audio-plugin GUI controls:
rotary knobs, vertical faders, horizontal sliders, **meters**, and **buttons**
(discrete on/off state frames). The output PNG is consumed
by a JUCE-style LookAndFeel filmstrip loader (one frame shown per parameter value).
It is the asset-production companion to the GUI skinning system / VybeForge.

## Start here

1. Read `docs/SOURCE_MAP.md` (where everything lives) and this file.
2. `docs/ARCHITECTURE.md` is the deep design reference; `docs/HANDOFF.md` is the
   current state + next task; `docs/ROADMAP.md` is the full phased plan;
   `docs/KICKOFF.md` is the paste-in prompt for a new session.
3. Other managed docs: `docs/TESTING.md`, `docs/CHANGELOG.md`, `docs/BUGS.md`,
   `docs/AUDIT-LOG.md`. Packaging + release flow: `docs/PACKAGING.md`.
4. The skills in `.claude/skills/` are scoped to this repo — use them.

## Stack

- .NET 9, Avalonia 11.3, CommunityToolkit.Mvvm 8.4 (source generators),
  SkiaSharp 3.119.2. `Avalonia.Controls.ColorPicker` 11.3.0 (Generate colour swatches).
- Layered-source import (app-only): **Svg.Skia** 5.0.0 (MIT, SVG layers) + **Magick.NET-Q8-x64**
  14.14.0 (Apache-2.0, PSD/PSB layers). Both permissive; not in `FilmstripEngine.cs`.
- AI SVG generation (Generate tab, app-only): OpenAI / Gemini / Claude — plus any **OpenAI-compatible
  custom endpoint** (`AiProvider.Custom`: OpenRouter / Ollama / LM Studio) — over a shared `HttpClient`,
  incl. **vision** (`DescribeImageAsync`) for reference-image matching; user API keys encrypted at rest
  with **System.Security.Cryptography.ProtectedData** 9.0.0 (Windows DPAPI). Not in `FilmstripEngine.cs`.
- MVVM + DI (Microsoft.Extensions.DependencyInjection), compiled bindings.
- Tests: xUnit + NSubstitute + FluentAssertions, `Avalonia.Headless` for view
  tests, golden-image regression for the renderer (`tests/StripKit.Tests`; coverlet.collector
  6.0.4). **274 green.**
- Packaging: self-contained `win-x64` publish → **Inno Setup** installer
  (`installer/StripKit.iss`); distributed as a **GitHub Release download** (no in-app
  auto-update). Release pipeline: `scripts/Invoke-Release.ps1` +
  `.github/workflows/auto-release.yml` — see `docs/PACKAGING.md`.
- CI: `.github/workflows/ci.yml` runs build + full test suite on every push and PR
  (`actions/checkout@v5` + `actions/setup-dotnet@v5` — Node 24).

## OSS / Contributing

- License: **MIT** (`LICENSE` at repo root).
- Public README with badges (`README.md`).
- `CONTRIBUTING.md` — contribution guide.
- `.github/ISSUE_TEMPLATE/` — `bug_report.md` + `feature_request.md`.
- `.github/pull_request_template.md` — PR checklist.

## Run / build

- `dotnet run --project src/StripKit` (needs the .NET 9 SDK).
- If NuGet warns of a SkiaSharp version conflict with Avalonia, align the
  `SkiaSharp` version in the csproj to Avalonia's transitive one
  (`dotnet list package --include-transitive`).
- Use `python -m pip` style invocation in any Python helper scripts (bare
  `pip`/`python` are not on PATH in this environment).

## Release (Inno Setup + GitHub) — full detail in `docs/PACKAGING.md`

Three stages, **one release creator**. **Stage 1** `scripts/Invoke-Release.ps1`:
test-gate → **release-integrity guard** (abort if tracked source is uncommitted; untracked strays
allowed; `-AllowDirty` overrides) → bump `Version` in `src/StripKit/StripKit.csproj`, `MyAppVersion`
in `installer/StripKit.iss`, and `docs/CHANGELOG.md` (promote `## [Unreleased]` → `## [X.Y.Z]`)
→ `dotnet publish` → **sign the exe + installer** (Azure Trusted Signing via signtool + the
`Microsoft.Trusted.Signing.Client` dlib — NOT AzureSignTool, which 403s) → `ISCC` builds
`releases/latest/StripKit-Setup-<ver>-x64.exe` → commit + tag `vX.Y.Z` + push. **Stage
2** `.github/workflows/auto-release.yml` (triggered by the tracked `releases/latest/*.exe`,
or `workflow_dispatch`): VirusTotal scan (`VT_API_KEY` secret) → the **sole**
`gh release create` (notes via `--notes-file`, never inline `--notes`). **Stage 3** the
website reads the live GitHub Release (auto-draft + refine `updates.json` via
`Publish-WebsiteChangelog.ps1` — invoked with **hashtable** splatting so a trailing `-Push` binds).
**Release integrity:** the "Release" commit stages only version files + the installer by design —
so **commit the feature work first** (the v1.2.0 source was once orphaned because it wasn't; the
Stage-1 guard now enforces this).

## Architecture (one idea, six component types) — full detail in `docs/ARCHITECTURE.md`

Every render is: *for each of N frames, place the source art inside a fixed frame
cell under a per-frame transform, then stack the cells into one PNG.* The six
component types are knob, vertical fader, horizontal slider, **meter**
(progressive segment fill), **button** (discrete state frames — off/on/…), and **toggle**
(an on/off pair; rendered exactly like a 2-state button, with switch-style generated art and a
latching code-export binding). The app is a
`TabControl` with **six** tabs — **Create**
(make a strip), **Import** (re-use / re-slice / resample one), **Batch** (a whole folder at
once), **Skin** (assemble a multi-control `skin.json`), **Generate** (AI-generate layered
control art from your own OpenAI / Gemini / Claude key, then hand it to Create), and **Assemble**
(stack a folder of pre-rendered frames — e.g. a path-traced PNG sequence — into one filmstrip).

- `Models/` — pure data, no UI/Skia deps: `FilmstripSettings` (render contract),
  `FrameTransform`, `StripDetection` (importer output),
  `SkinManifest`/`ManifestControl`/`ManifestBounds`, `BatchModels`, `CodeModels`,
  `RenderLayer` (`LayerBehavior` {Static, Rotate, **Frame**} + per-layer pivot — the layered-knob /
  button stack), the `ComponentType` ({RotaryKnob, VerticalFader, HorizontalSlider, Meter,
  **Button**, **Toggle**}) / `StackDirection` / `MeterFillDirection` enums.
- `Services/SkiaFilmstripRenderer.cs` — **the heart.** `ComputeTransform` does the
  rotary/linear math; `RenderFrame` composites one frame with supersampling +
  Mitchell cubic resampling (meters fill segments via `RenderMeterFrame` — procedural
  or layered on/off-art reveal; knobs may get a value-tracking fill arc via
  `RenderValueArc`, or be composited from a base+pointer layer stack via `RenderLayers`
  when `settings.Layers` + `layerArt` are supplied; **buttons** composite discrete state
  art per frame via `RenderButtonLayers` — `Static` on every frame, `Frame` only on its
  matching index); `RenderStrip` blits frames into the stacked PNG.
- `Services/FilmstripImporter.cs` — detect an existing strip's layout from its
  dimensions, extract a frame, re-stack orientation, and **resample** (re-time) to a new
  frame count via nearest-frame mapping (no Avalonia dep).
- `Services/PointerExtractor.cs` — split a flat knob into a static base + a rotating pointer
  via the **radial-symmetry residual** (auto-fills the layered-knob slots; ★ #3 step 2). Static,
  pure SkiaSharp like `ContentAnalysis`; app-only (not in `FilmstripEngine.cs`).
- `Services/LayeredImportService.cs` — import a real layered **SVG** (Svg.Skia) / **PSD/PSB**
  (Magick.NET) into named, behaviour-tagged, canvas-registered layers (★ #3 step 3 — the final
  layer-aware piece) → a `LayeredImportResult`. Auto-tags `off`/`on` groups → `Frame` (button
  states); parses SVG through `SafeXml`. Feeds the renderer's existing layer stack with
  no renderer change; app-only (not in `FilmstripEngine.cs`).
- `Services/SafeXml.cs` — hardened `XDocument` parse for **untrusted** SVG (AI replies + imported
  files): `DtdProcessing.Prohibit`, no resolver, `MaxCharactersFromEntities = 0` — closes
  entity-expansion DoS / external-entity. Used by `SvgSanitizer` + `LayeredImportService`. App-only.
- `Services/ManifestService.cs` — build + serialize a `skin.json` (System.Text.Json):
  `BuildSingleControl` (Create tab) and `BuildManifest` (the Skin tab's multi-control export).
- `Services/CodeSnippetService.cs` — emit ready-to-paste loader code (JUCE / CSS-HTML /
  iPlug2 / HISE) for an exported strip; pure string-gen mirroring `ManifestService`.
- `Services/RenderRecipeService.cs` — the **Render recipe** (Create tab; path-tracing P2): emit a
  Blender `bpy` script + `frame,value,angle_deg` CSV/JSON so an offline render matches the runtime
  sweep law. Pure string-gen like `CodeSnippetService`; its static `BuildFrameTable` mirrors
  `SkiaFilmstripRenderer`'s `t = i/(N−1)` / `angle = start + (end−start)·t`, so recipe and renderer
  can't drift. App-only; not in `FilmstripEngine.cs`.
- `Services/BatchProcessor.cs` — render a folder of sources into many strips off the
  UI thread (`Task.Run`), with per-item progress and a working cancel.
- `Services/FrameSequenceAssembler.cs` (+ `NaturalFileNameComparer`) — the **Assemble** tab:
  natural-sort a folder of pre-rendered frames (`frame_2` before `frame_10`) and pack them into one
  stacked strip (reconcile odd sizes via `CellFit`, optional content re-centre, optional **un-premultiply**,
  optional nearest-frame resample by delegating to `FilmstripImporter`). Two static **render-QC** helpers
  (path-tracing P3): `AnalyzeQc` → a `RenderQcReport` (drift / missing-transparency / blank / premultiplied
  frames; surfaced as assemble warnings + the "Check frames" pre-flight) and `UnpremultiplyAlpha`. Pure
  SkiaSharp; no renderer change → not in `FilmstripEngine.cs`. The import-side bridge for offline-3D /
  path-traced art.
- `Services/AssetGenerationService.cs` (+ `IAssetGenerationProvider` → `ClaudeProvider` /
  `OpenAiProvider` / `GeminiProvider`, `SvgSanitizer`, `ISecretStore`/`DpapiSecretStore`) — the
  **Generate** tab: build a type-aware StripKit-aware SVG prompt (knob = `body`+`pointer`, button =
  `off`+`on`, fader/slider = a single `body` cap) → call the user's chosen AI over a shared
  `HttpClient` → sanitize the reply to a clean **layered** SVG → feed the
  existing layered-import pipeline. Keys DPAPI-encrypted. App-only; not in `FilmstripEngine.cs`.
- `Services/ImageLoadService.cs` / `ExportService.cs` — decode/encode PNG ↔ `SKBitmap`.
- `Services/FileDialogService.cs` — open/save/open-layered pickers (app-layer; holds the Window).
- `Services/SettingsService.cs` — persist `AppSettings` (the first-run "seen tutorial" flag + the
  Generate provider/model prefs) to `%APPDATA%/StripKit/settings.json`; the app's saved state.
  `Services/AssetService.cs` — extract the bundled sample knob to a temp path for the tutorial (app-layer).
- `Helpers/SkiaImageInterop.cs` — `SKBitmap` -> Avalonia `Bitmap` for preview.
  `Helpers/HexToColorBrushConverter.cs` — `#RRGGBB` → `IBrush` for the Generate colour swatches.
- `Controls/SectionHeader.cs` — a `TemplatedControl` section label with a 3px accent divider
  (styled by a `ControlTheme` in `App.axaml`); used across the sidebars.
- `ViewModels/MainWindowViewModel.cs` — Create-tab state + commands; a single
  `OnPropertyChanged` funnel refreshes the preview. Holds the layered-knob Base/Pointer slots +
  the auto-extract command + the **layered SVG/PSD import** (an `ImportedLayers` row list with
  per-layer Static/Rotate/Frame dropdowns; `ImportedLayerRow`). Exposes `Importer`, `Batch`, `Skin`,
  and `Generate`; `ImportLayeredFromPathAsync` is shared by the layered file picker and the Generate
  tab's "Use in Create" handoff — which **honours the generated control type** (knob → body+pointer;
  button → off/on Frame layers; fader/slider → flattened single source).
- `ViewModels/ImporterViewModel.cs` — Import-tab state + commands (detect / scrub / extract /
  re-stack / resample; same funnel).
- `ViewModels/BatchViewModel.cs` — Batch-tab state + commands (folders, template incl. the meter
  settings + layered/backdrop toggle, run/cancel, progress); no preview funnel.
- `ViewModels/SkinViewModel.cs` (+ `SkinControlEntry.cs`) — Skin-tab state + commands: a
  multi-control `skin.json` builder (controls list, add-from-strip / blank, detail editor, export).
- `ViewModels/GenerateViewModel.cs` — Generate-tab state + commands: provider/model/key/style + the
  generated control type, the async cancellable generate, preview-by-importing (validates the layered
  SVG), Save/Copy SVG, a structure warning (knob with no pointer / button missing a state), and the
  `UseInCreateRequested` handoff. The **model field is an editable `AutoCompleteBox`** (free text +
  per-provider suggestions — a custom/just-released id can be typed and is sent verbatim; a
  pinned-but-delisted model shows as text, not a blank box). The preview is built **off the UI thread**
  in one `Task.Run` (`BuildPreview` — temp-write + layered import + composite + PNG-encode; the UI
  thread only assigns the bitmap), and the prior temp SVG is dropped each generation (no temp
  accumulation). Persists provider/model prefs + the encrypted key.
- `ViewModels/TutorialViewModel.cs` — the Getting Started overlay: step list + navigation +
  first-run auto-open (via `ISettingsService`) + the `LoadSampleRequested` event (sample knob).
  Also has a **Generate** walkthrough.
- `ViewModels/ImportedLayerRow.cs` — the observable per-layer row for an imported/generated SVG/PSD
  (name + editable Static/Rotate/Frame `Behavior` + the canvas-sized art).
- `Views/MainWindow.axaml(.cs)` — the `TabControl` (+ header "Getting started" button + the
  `TutorialOverlay` as the top layer); code-behind holds the auto-play timer + the Create
  preview's drag-drop handlers.
- `Views/TutorialOverlay.axaml(.cs)` — the Getting Started guided overlay (bound to `TutorialViewModel`).
- `Views/ImporterView.axaml(.cs)` — the Import tab UserControl + its drop handlers.
- `Views/BatchView.axaml(.cs)` — the Batch tab UserControl (folder pickers, template,
  Run/Cancel, progress bar, results).
- `Views/SkinView.axaml(.cs)` — the Skin tab UserControl (skin metadata + controls list;
  per-control detail editor + Export skin.json).
- `Views/GenerateView.axaml(.cs)` — the Generate tab UserControl (provider/key/model + control type +
  style/accent/size + Generate/Cancel on the left; SVG preview + Use-in-Create / Save / Copy +
  raw-response expander on the right). The model picker is an `AutoCompleteBox` (provider + style stay
  `ComboBox`es). Code-behind: clipboard copy + colour-picker flyout handlers.
- Repo-root `FilmstripEngine.cs` — standalone portable copy of the renderer (NOT in
  the build); keep in sync with `SkiaFilmstripRenderer` if the math changes (incl. the button
  state-frame path; the Generate providers + sanitizer are app-only and not mirrored).

## Project skills (`.claude/skills/`)

- `avalonia-skia-interop` — SkiaSharp rendering inside Avalonia; pixel interop.
- `avalonia-drag-drop-files` — file drag-and-drop into an Avalonia view (Phase 1).
- `live-preview-render-loop` — the responsive preview pattern this VM embodies.
- `image-regression-testing` — golden-image tests to lock renderer output (Phase 4).
- `filmstrip-importer-engine` — detect/re-slice existing strips (Phase 2).
- `plugin-asset-manifest` — JSON manifest binding strips to parameters (Phase 3).
- `layer-aware-filmstrip-compositing` — the layered-knob (base + pointer) compositing model.
- `release-source-integrity-guard` — ensure a release tag can rebuild its own artifact (commit feature source before the release script runs; portable).

## Globally-installed skills to lean on

`csharp-mastery`, `avalonia-mvvm-patterns`, `avalonia-mvvm-app-scaffold`,
`avalonia-layout-patterns`, `cc-regression-tester-pro`, `dotnet-installer-publishing`,
`debug-protocol`, `code-review-discipline`.

## Filmstrip conventions (do not change without reason)

- Frames stack **vertically** by default; frame 0 = minimum value, frame N-1 = max.
- Rotary angle for frame i: `start + (end - start) * i / (N - 1)`. The `(N-1)`
  divisor is deliberate — it lands the last frame exactly on the max. Not `N`.
- Default sweep 270° (frame 0 at -135° / 7 o'clock, last at +135° / 5 o'clock).
- 32-bit RGBA, transparent background. Standard frame counts: 32 / 64 / 128.
- Ship `@2x` for HiDPI (the Export "Also export @2x" toggle).
- Knob art should carry ~10% transparent margin so corners don't clip on rotation.
- **Untrusted SVG** (AI replies, imported files) is parsed only through `SafeXml.Parse`
  (DTD prohibited) — never a bare `XDocument.Parse`.

## House conventions

- **Depth design system** (machined-grey, dark; rebranded from Obsidian glass in v1.4.0): ember accent
  `#f25914`, **sans for labels/body** (`Verdana, Segoe UI, Arial, sans-serif`) and **monospace for
  numerics only** (`JetBrains Mono, Consolas, …` — `NumericUpDown` + numeric readouts). The Depth tokens
  are vendored in `src/StripKit/Depth/Depth.axaml` (`DepthBg`/`DepthChrome*`/`DepthInset`/`DepthLine*`,
  `DepthInk*`, `DepthEmber*`, `DepthRadius*`, `DepthRaise*`) and **mapped** onto StripKit's existing keys
  in `App.axaml` (`AccentBrush`/`AccentHiBrush`, `Text1/2/3Brush`, the `GlassFill*`/`GlassBorder*` surface
  keys — now solid greys, the input-well + dropdown + checkbox/slider keys, `SectionTextBrush`,
  `DialogFillGradient`), promoted **global** so all six tabs + dialogs share one look. The window is a
  **solid `DepthBg`** base (no acrylic / no glow — the old `ObsidianAcrylic` material is gone). Panels are
  `Border.card`; **neutral buttons** are dark raised "keycaps" (`Button:not(.accent)` — bevel + drop
  shadow, lighter on hover), **primary** buttons use the `.accent` class (ember face). Re-use these
  mapped keys / Depth tokens — don't hard-code hex, and keep monospace to numerics (not labels/body).
- View models never reference Avalonia UI types (testability). Source-generator
  classes must be `partial`. Use `Path.Combine`, never raw separators.
- No `System.Drawing`; no `.Result`/`.Wait()`; `async void` only for event handlers.
- Do NOT rewrite `SkiaFilmstripRenderer` or re-scaffold the project; extend it.

## Conventions for working with the owner (Big Haas)

- After delivering something useful, suggest follow-on **skills** (even loosely
  related) and ask whether to save reusable outputs to artifacts.
- When creating a skill, emit a `.skill` file with a description field < 1024
  chars so it is one-click installable; run the skill-authoring-linter first.

## Last completed task

- **2026-07-02 (v1.4.0-dev: full fine-tooth-comb audit + 11 code/test fixes; on `main`, unreleased)** —
  A 10-dimension, adversarially-verified audit of the unreleased v1.4.0 work found 18 real items; the 11
  **code + test** findings were fixed here (commit `301b2b4`) with fail-before/pass-after regression
  tests. **Suite 265 → 274 green, build clean.** **Two HIGH bugs, both live in the v1.4.0 work:** (1) the
  P3 **`UnpremultiplyAlpha`** returned a *premultiplied-tagged* bitmap holding straight bytes → the
  "un-premultiply alpha" halo fix **corrupted colours** on export (now returns an Unpremul-tagged bitmap);
  (2) the layered **SVG file-import** handed raw text to Svg.Skia without `SvgSanitizer`, so an external
  `<image xlink:href="http://…">` **fired an outbound request** (SSRF / file-existence oracle) on file
  open — verified live (`SvgSanitizer.Sanitize` is now public and runs before `FromSvg` on the import
  path too; AI-reply path was already safe). **Medium:** button/toggle `Frame` layers now match by
  **ordinal** not absolute index (a leading Static border no longer shifts/blanks states — renderer +
  `FilmstripEngine.cs` mirror + state-frame count); Regenerate uses a fresh CTS. **Low:** QC drift in
  absolute px; 3 resource leaks (set preview bitmaps, auto-retry temp SVG, `HttpResponseMessage`);
  TutorialOverlay's undefined `GlassFill`/`GlassBorder` keys → `*Brush`. Also deleted the redundant merged
  `feat/v1.4.0` branch; gitignored the long-standing strays (`.claude/launch.json`, `press/`,
  `docs/PRESS-RELEASE.md`). Also **bumped Magick.NET-Q8-x64 14.13.1 → 14.14.0** (both projects), which
  **clears the known HIGH/moderate NuGet advisories** (NU1903/NU1902) with the suite still 274 green.
  **Next:** push `main` to origin + release v1.4.0; then P3b (EXR/HDR) / P4 (frame interpolation).

- **2026-06-30 (v1.4.0-dev: path-tracing P3 — Render QC on import + the Assemble-tab recipe
  entry-point; on `main`, unreleased)** — Continued the path-tracing chain. **(1) P3 —
  Render QC.** The Assemble tab catches the path-tracer failure modes: new static
  `FrameSequenceAssembler.AnalyzeQc` → a `Models.RenderQcReport` (object **drift** in px via the
  content-centre spread; frames with **no transparency** = a missing transparent background, or **none**
  = a failed render; **premultiplied** edges) — surfaced both as assemble-result warnings and via a new
  **"Check frames"** pre-flight button. Plus an **"Un-premultiply alpha"** fix
  (`FrameSequenceAssembler.UnpremultiplyAlpha` divides RGB by alpha to kill dark edge halos), wired into
  `FrameSequenceOptions.UnpremultiplyAlpha`. Pure SkiaSharp. New `RenderQcTests` (+7). **(2) Assemble-tab
  render-recipe entry-point** — the same Blender/CSV/JSON recipe export (P2) now lives on the Assemble
  tab too, driven by its component type + frames/sweep/size inputs (injected `IRenderRecipeService` into
  `FrameSequenceViewModel`; updated the 3 test-fake call sites). **Suite 258→265 green, build 0/0;
  live-verified (both new Assemble sections render + the recipe preview).** Docs reconciled
  (ROADMAP P3→✅ + new P3b for the deferred 16-bit/EXR-HDR ingest — needs a Magick.NET Q8→Q16-HDRI swap;
  CHANGELOG[Unreleased] / SOURCE_MAP / TESTING 265 / CLAUDE). **Next:** the Obsidian→Depth doc reconcile
  (House conventions + ARCHITECTURE), then merge `feat/v1.4.0` → main; then P3b / P4 (frame interpolation).

- **2026-06-30 (v1.4.0-dev: Depth UI rebrand + crosshair fix committed, then path-tracing P2 —
  render-recipe export; on `main`, unreleased)** — Two things this session (originally staged on a
  `feat/v1.4.0` branch, since folded into `main` and the branch deleted). **(1) Committed the prior batch** (`4793b50`): the P1 Assemble tab + a
  full **Depth design-system rebrand** (vendored `src/StripKit/Depth/Depth.axaml` from
  `C:\DEV\depth-design-system`; `App.axaml` maps StripKit's keys → Depth tokens, **promoted global** so
  all six tabs + dialogs share one machined-grey / ember / recessed-mono-well / raised-keycap look;
  solid Depth window — acrylic + glow removed; VYBECODE DSP brandmark header) + the **crosshair fix**
  (image stays still while the crosshair is dragged; playback orbits the mark on release —
  `_placingCrosshair` gate in `MainWindowViewModel.RefreshPreview`). The three concerns share the
  MainWindow/App files so they couldn't be split into buildable commits; landed as one verified commit.
  **(2) Built path-tracing P2 — render-recipe export.** New app-only `Services/RenderRecipeService` (+
  `IRenderRecipeService`, `Models/RenderRecipeModels`) emits a Blender `bpy` script (transparent film;
  a keyframe baked on **every** frame — exact angles, no interpolation drift — plus a 0..1 `value`
  custom property to drive non-rotary rigs) and an engine-agnostic `frame,value,angle_deg` CSV/JSON
  table. Pure string-gen mirroring `CodeSnippetService`; one static `BuildFrameTable` mirrors the
  renderer's `(N−1)` law so recipe and runtime can't diverge. A **"Render recipe" panel on the Create
  tab** (Blender/CSV/JSON live preview + copy + save) — Create carries every input the recipe needs
  (type, frame count, sweep, resolution); the Assemble tab has none of the sweep/resolution inputs, so
  it's the natural home. Wired DI + `MainWindowViewModel` (funnel + `SaveRecipeCommand`) +
  `MainWindow.axaml`/`.cs` (panel + copy handler). New `RenderRecipeServiceTests` (+14). **Suite 244→258
  green, build 0/0; live-verified (Blender + CSV preview + dropdown).** Docs reconciled
  (ROADMAP P2→✅ / CHANGELOG[Unreleased] / SOURCE_MAP / TESTING 258 / CLAUDE). **Still pending:** the
  CLAUDE.md *House conventions* + ARCHITECTURE still describe the old "Obsidian glassmorphism / no
  monospace" system — reconcile to Depth before the v1.4.0 release. **Next (P3):** 3D-render QC +
  alpha/EXR-HDR ingest; optional: an Assemble-tab recipe entry-point; the `frame-sequence-assembler` skill.

- **2026-06-30 (path-tracing pipeline P1 — the "Assemble" tab; unreleased, next: v1.4.0)** — Built the
  first phase of the offline-3D / path-tracing program (origin: a KVR thread on raster vs. WebGL-3D
  plugin GUIs — "you're better off offline path-tracing into spritesheets"). A new **sixth tab**
  (**Assemble**) stacks a folder (or drag-drop) of individually-rendered frames — a path-traced PNG
  sequence from Blender / KeyShot / Octane, or any pre-rendered set — into one filmstrip:
  **natural-sort** (`frame_2` before `frame_10`, padded or not), reconcile odd frame sizes
  (pad-to-largest / crop-to-smallest / strict), optional **content re-centre** (fixes a 3D object that
  drifts between frames), optional **nearest-frame resample** to 32/64/128, and the full Create-tab
  export set (@2x + `skin.json` + JUCE/CSS/iPlug2/HISE loader code). Assembly runs off the UI thread
  with progress + cancel; preview decodes one frame on demand. **No renderer change** → nothing
  mirrored into `FilmstripEngine.cs`. New: `Models/FrameSequenceModels.cs`,
  `Services/NaturalFileNameComparer.cs`, `Services/IFrameSequenceAssembler` + `FrameSequenceAssembler`
  (injects the importer for the resample), `ViewModels/FrameSequenceViewModel` + `FrameItemRow`,
  `Views/AssembleView`; added `IImageLoadService.Probe` (header-only dim peek) + `IFileDialogService.OpenImagesAsync`
  (multi-select); wired DI + `MainWindowViewModel` + the tab + a Tutorial walkthrough
  (`TutorialScreen.Assemble`). New tests `NaturalFileNameComparerTests` / `FrameSequenceAssemblerTests`
  / `FrameSequenceProbeTests` / `FrameSequenceViewModelTests` / `FrameSequenceAssemblerGoldenTests`
  (golden `assemble_knob_mix_4`) / `AssembleViewTests` (headless markup smoke). **Suite 216→244 green, build 0/0.** Docs reconciled
  (SOURCE_MAP/TESTING/ROADMAP/CHANGELOG[Unreleased]/CLAUDE). **Next (P2):** the **render-recipe export**
  (a Blender `bpy` script + `frame,value,angle` CSV/JSON) so the offline render matches StripKit's
  `(N−1)` sweep law — see `docs/ROADMAP.md` → "Offline-3D / path-tracing pipeline". Not yet released
  (would be v1.4.0); not yet eyeballed live in the running app.

- **2026-06-18 (v1.3.0 — AI-generation program + meters/toggles + security hardening; full reconcile +
  release)** — A large feature wave, shipped end-to-end. **(1) Security/quality fixes:** **BUG-010** —
  the SVG file-import path ran `SafeXml.Parse` *after* `Svg.Skia.FromSvg`, so a billion-laughs entity
  bomb opened via the layered-file picker was expanded first (local DoS); reordered the gate to the top
  (`940b60f`). Input-size caps (`97fb22d`): `ImageLoadService` peeks header dims via `SKCodec` (rejects
  >64 MP), `LayeredImportService` caps SVG text (20 MB) + PSD canvas (64 MP). `ManifestService` /
  `SkinViewModel` `MapType` now map Button→"button", Toggle→"toggle" (were silently "knob"). **(2)
  Meters in Generate** (`d846686`, `72dcc46`): the Generate tab now produces meters — an unlit `off` +
  fully-lit `on` pair; the handoff adopts `off`→meter background, `on`→the source the renderer reveals
  up to the value; **vertical or horizontal** (fill direction inferred from the art's aspect). No
  renderer change. **(3) `ComponentType.Toggle`** (`c0a60af`): a first-class on/off toggle, distinct
  from Button but sharing its discrete state-frame render path (mirrored in `FilmstripEngine.cs`);
  generate / layered-import (auto-detected from off/on names) / create / code-export (JUCE latching
  toggle, iPlug2 `IBSwitchControl`) all honour it. **(4) The full AI-generation program** —
  **matching-set generator** (`5d07923`: one prompt → a whole family of controls, generated
  concurrently from one shared style, `GenerateSetAsync`); **variations grid** (`bfcbba5`:
  `GenerateVariationsAsync`); **OpenAI-compatible custom endpoint** (`ef13091`: `AiProvider.Custom` /
  `CustomOpenAiProvider` for OpenRouter / Ollama / LM Studio); **refine** (`70cedce`: revise the current
  SVG by instruction, `RefineAsync`); **reference-image match / vision** (`b4dd7e1`: per-provider
  `DescribeImageAsync` → `DescribeReferenceAsync` folds a style description into the prompt); **auto-retry
  on a weak first take + show-the-prompt** (`6e3f800`); **"avoid" field** (part of `5d07923`); and the
  **prompt seeds library** (`f3c0f4a`: `GenerationSeed`/`GenerationSeedLibrary`, 5 built-ins + user
  saves). New files: `Services/CustomOpenAiProvider.cs`, `ViewModels/GenerateSetModels.cs`; new tests
  `ToggleRenderTests` / `ImageLoadServiceTests` / `CustomOpenAiProviderTests` / `VisionProviderTests`.
  **Suite 172→216 green, build 0/0.** **(5) Full doc reconcile** — every managed doc stamped 1.3.0 /
  2026-06-18 (ARCHITECTURE/SOURCE_MAP/ROADMAP/KICKOFF/TESTING/BUGS/AUDIT-LOG/CHANGELOG). **(6) Released
  v1.3.0** — signed installer via the standard pipeline; see `docs/HANDOFF.md` for the release outcome.
  Untracked strays (still not ours): `docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json`.
  **Next:** website P2 getting-started guide; a live-eyeball pass on the fader/slider/meter Generate
  output with a real key; seeds→matching-set→Skin auto-assembly; more code-export targets.

- **2026-06-14 (v1.2.2 polish wave shipped + full doc reconcile)** — A small quality + tooling
  release, shipped end-to-end. **(1) Generate-tab polish** (`cdc466e`): the model picker is now an
  **editable `AutoCompleteBox`** (free text + per-provider suggestions) instead of a fixed dropdown —
  a custom/just-released model id can be typed (and is sent verbatim) and a pinned-but-delisted model
  shows as text rather than a blank box; the **preview build moved off the UI thread** into one
  `Task.Run` (`GenerateViewModel.BuildPreview` — temp-write + layered import + composite + PNG-encode;
  the UI thread only assigns the finished bitmap, so a large canvas no longer hitches the dispatcher),
  and **generated temp SVGs no longer accumulate** (the prior one is dropped each generation). **(2)
  Release tooling**: a **release-integrity guard** in `Invoke-Release.ps1` (`e124e47`) now **aborts the
  release if the tracked working tree has uncommitted source** — untracked strays (`??`) are allowed —
  with a `-AllowDirty` override, so feature source can't be orphaned from its tag (the v1.2.0 failure
  mode, now enforced not just documented); and Stage 3's website-changelog push was fixed (a trailing
  `-Push` mis-bound under **array** splatting → switched to **hashtable** splatting). **(3) CI
  future-proofing**: `actions/checkout@v4→v5` (`0fc64db`) and `actions/setup-dotnet@v4→v5` (`33fc522`)
  for the Node 24 runtime (ahead of the June 16 2026 forcing); `coverlet.collector 6.0.2→6.0.4`. **(4)
  New portable skill** `.claude/skills/release-source-integrity-guard/SKILL.md` (`114f8e5`,
  linter 0/0) — commit-source-before-release guard, reusable on any release pipeline. **+1 test**
  (`GenerateViewModelTests.A_custom_model_id_not_in_the_suggestions_is_honored` — a typed/delisted
  model id is sent verbatim); **suite 171→172 green, build 0/0.** **Shipped:** **v1.2.1 AND v1.2.2 both
  released 2026-06-14** (tags `v1.2.1`, `v1.2.2` live + signed; CI VirusTotal-scanned the GitHub
  Releases; website changelog auto-pushed). csproj `<Version>`/`.iss`/CHANGELOG are at 1.2.2 from the
  release script. **(5) Full doc reconcile** — every managed doc stamped 1.2.2 / 2026-06-14;
  SOURCE_MAP/TESTING/ARCHITECTURE/ROADMAP/KICKOFF/PACKAGING/BUGS/AUDIT-LOG updated for the editable
  model + off-thread preview + the release-integrity guard + CI v5 + coverlet 6.0.4 + the new skill +
  the 172 count. Untracked strays (not ours): `docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json`.
  **Next:** website P2 getting-started guide; Generate fader/slider/meter polish; more code-export
  targets; translate/opacity-ramp layer behaviours.
- **2026-06-14 (audit + orphaned-v1.2.0-source recovery + the 1.2.1 fix wave + full doc reconcile)** —
  A correctness + release-integrity session, two commits on `main`. **(1) Recovered an orphaned
  release.** The "Release v1.2.0" commit (`70cf259`) staged **only** the version files + the installer
  — the v1.2.0 **feature source was never committed**, so the live `v1.2.0` tag could not rebuild its
  own installer. Committed that source as-is (matching the shipped binary) in **`b55380f`** before
  fixing forward (`ComponentType.Button` + `LayerBehavior.Frame`; Generate's four control types + the
  button off/on prompt + colour-picker flyouts; the renderer + `FilmstripEngine.cs` button state-frame
  path; the importer's `off`/`on`→`Frame` mapping; supporting wiring). **(2) The 1.2.1 fix wave**
  (`80dc1b5`): the **Generate→Create handoff now honours the generated control type** (was hard-forced
  to `RotaryKnob`, so generated faders/sliders rotated and buttons stacked both states) — knob →
  body+pointer, button → off/on `Frame` layers, fader/slider → flattened single source; **hardened
  untrusted-SVG XML parsing** via new `Services/SafeXml.cs` (`DtdProcessing.Prohibit`, no resolver,
  `MaxCharactersFromEntities = 0`) applied to **both** `SvgSanitizer` and the layered-file import
  picker (closes billion-laughs DoS + external-entity); added the missing
  `BindingPlugins.DataValidators.RemoveAt(0)` + a Generate structure warning. New files: `SafeXml.cs`,
  `Helpers/HexToColorBrushConverter.cs`, `Controls/SectionHeader.cs`,
  `tests/StripKit.Tests/GenerateIntegrationTests.cs`. **Suite 157→171 green; build 0/0.** **(3) Full
  doc reconcile** — every managed doc stamped 1.2.1 / 2026-06-14; HANDOFF rewritten (five tabs, the
  recovery); AUDIT-LOG entry; BUGS-008/009 retro-logged.
- **2026-06-07 (Generate tab — AI-generated SVG control art)** — Built a new **fifth tab** that uses
  the user's **own** OpenAI / Gemini / Claude API key to generate a **layered knob SVG** (a static
  `<g id="body">` + a separate `<g id="pointer">`) as filmstrip source art. **All three providers**
  behind one interface, **layered** body+pointer SVG, **DPAPI-encrypted** keys, **knob-first**. A
  generated SVG drops straight into the **existing** `LayeredImportService` → renderer layer stack, so
  **no renderer change** and **nothing mirrored into `FilmstripEngine.cs`**. New app-only pieces:
  `IAssetGenerationService`/`AssetGenerationService`, `IAssetGenerationProvider` + `ClaudeProvider` /
  `OpenAiProvider` / `GeminiProvider` over a shared DI `HttpClient`, `SvgSanitizer`,
  `ISecretStore`/`DpapiSecretStore` (→ `%APPDATA%/StripKit/secrets.dat`, ciphertext only).
  `GenerateViewModel` + `GenerateView` **validate by importing the SVG** then **"Use in Create"** jumps
  to Create; **Save SVG** / **Copy SVG** too. New dep:
  `System.Security.Cryptography.ProtectedData` 9.0.0. **+27 tests, suite 125→152 green.** *(Shipped as
  v1.1.0; the centering fix took the suite to 157. Faders/sliders/buttons + colour pickers followed in
  v1.2.0.)*
- **2026-06-06 (v1.0.0 shipped + reusable website-changelog automation)** — Cut **v1.0.0** (the
  major release: layered PSD/SVG import + the in-app tutorial + the About fix), first **signed** via the
  **Trusted Signing** path (signtool + `Microsoft.Trusted.Signing.Client` dlib; AzureSignTool 403s
  against Trusted Signing endpoints). **Live + signed:**
  `github.com/Vybecode-LTD/stripkit/releases/tag/v1.0.0` (58.3 MB). Then built a **project-agnostic**
  `scripts/Publish-WebsiteChangelog.ps1` (ASCII-only, no BOM trap): auto-drafts a version's
  plain-language `updates.json` entry from `docs/CHANGELOG.md`, prepends it, and with `-Push` publishes
  (→ Railway auto-deploy). Wired into `Invoke-Release.ps1` as an optional Stage 3 (`-WebsiteRepo`).
  Docs: PACKAGING §8.4 (Stage-3 automation + reuse) + §9A (the script-BOM guard), SOURCE_MAP.
- **2026-06-06 (onboarding P1 — interactive in-app Getting Started tutorial)** — Built a re-openable
  **"Getting Started"** guided overlay (`Views/TutorialOverlay.axaml` + `ViewModels/
  TutorialViewModel.cs`) walking a new user through the core loop as an on-brand bottom-centre glass
  card over a click-through scrim. It **auto-opens on first launch** (a new minimal
  `ISettingsService`/`SettingsService` persists `HasSeenTutorial`) and is re-openable from a header
  button. Step 1 offers **"Load sample knob"** — a **bundled `Assets/sample-knob.png`** (extracted by a
  new `IAssetService`/`AssetService`). Plus **contextual tooltips**. **No renderer/engine changes.**
  **+11 tests, suite 112→123 green.**
- **2026-06-06 (vNext ★ #3, step 3 — layered PSD/SVG import; completes the layer-aware bet)** —
  **"Import layered file (SVG / PSD)…"** in the Create-tab layered panel. New **app-only**
  `Services/LayeredImportService.cs` parses **SVG** groups via **Svg.Skia** (MIT) and **PSD/PSB**
  layers via **Magick.NET-Q8-x64** (Apache-2.0); each `ImportedLayer` carries a **name-guessed
  behaviour** the user overrides per layer. **No renderer change** → **NOT mirrored in
  `FilmstripEngine.cs`**; gated behind defaults so every prior golden is byte-identical. Deps added:
  `Svg.Skia` 5.0.0, `Magick.NET-Q8-x64` 14.13.1 (SkiaSharp 3.119.0→**3.119.2**); the win-x64 installer
  grows ~22 MB. **+14 tests, suite 98→112 green.**
- **2026-06-05 — earlier sessions** — ★ #3 step 2 (auto-pointer extraction, `PointerExtractor`,
  radial-symmetry residual, +4); v0.8.0 (Batch-tab meter settings + backdrop toggle; the Skin tab
  multi-control `skin.json` builder; importer resampling; ★ layer-aware step 1 base+pointer); ★ #1
  value-arc / fill-ring + ★ #2 code/component export (v0.7.0); the documentation overhaul + MIT
  open-sourcing + audit fixes (BUG-005/006/007); alignment tools + the Obsidian glassmorphism design;
  v0.6.0 (Inno pipeline + website, replacing Velopack); Phases 0–6; and the FilmstripForge → StripKit
  rename. See `docs/CHANGELOG.md` and `docs/AUDIT-LOG.md` for the full history.
