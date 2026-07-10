# CLAUDE.md — StripKit

> Version 1.5.1 · last-updated 2026-07-04 · last-audit 2026-07-04

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
- Layered-source import + HDR frame ingest (app-only): **Svg.Skia** 5.0.0 (MIT, SVG layers) +
  **Magick.NET-Q16-HDRI-x64** 14.14.0 (Apache-2.0, PSD/PSB layers + 16-bit/EXR HDR frames — Q16-HDRI
  so an EXR tone-maps before the 8-bit reduction; `Helpers/MagickPixels` downshifts its 16-bit
  `ToByteArray`). Both permissive; not in `FilmstripEngine.cs`.
- AI SVG generation (Generate tab, app-only): OpenAI / Gemini / Claude — plus any **OpenAI-compatible
  custom endpoint** (`AiProvider.Custom`: OpenRouter / Ollama / LM Studio) — over a shared `HttpClient`,
  incl. **vision** (`DescribeImageAsync`) for reference-image matching; user API keys encrypted at rest
  with **System.Security.Cryptography.ProtectedData** 9.0.0 (Windows DPAPI). Not in `FilmstripEngine.cs`.
- MVVM + DI (Microsoft.Extensions.DependencyInjection), compiled bindings.
- Tests: xUnit + NSubstitute + FluentAssertions, `Avalonia.Headless` for view
  tests, golden-image regression for the renderer (`tests/StripKit.Tests`; coverlet.collector
  6.0.4). **346 green.**
- Packaging: self-contained `win-x64` publish → **Inno Setup** installer
  (`installer/StripKit.iss`); distributed as a **GitHub Release download** (no in-app
  auto-update). Release pipeline: `scripts/Invoke-Release.ps1` +
  `.github/workflows/auto-release.yml` — see `docs/PACKAGING.md`.
- CI: `.github/workflows/ci.yml` runs build + full test suite on every push and PR
  (`actions/checkout@v5` + `actions/setup-dotnet@v5` — Node 24); collects coverage and **fails below
  70% line coverage**.

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

- `Models/` — pure data, no UI/Skia deps: `FilmstripSettings` (render contract — incl.
  `ShowMeterPeak` + `PeakColorArgb`, mirrored in `FilmstripEngine.cs`; also **sprite-grid layout**
  `Layout`/`GridColumns` and **parameter-law frame mapping** `MappingCurve`/`MappingSkew`/
  `MappingLogBase` + the `MapT(t)` remap method — both default to the byte-identical Strip/Linear
  path), `StripLayout` ({Strip, **Grid**}) / `FrameMappingCurve` ({Linear, **Skew**, **Logarithmic**})
  enums, `FrameTransform`, `StripDetection` (importer output),
  `SkinManifest`/`ManifestControl`/`ManifestBounds` (`ManifestControl` carries nullable
  `Layout`/`GridColumns`, populated only for a grid strip), `BatchModels`, `CodeModels`
  (`CodeSnippetRequest` carries `Layout`/`GridColumns` too), `RenderLayer` (`LayerBehavior`
  {Static, Rotate, **Frame**} + per-layer pivot — the layered-knob / button stack), `RenderPreset`
  (a named snapshot of the Create tab's full render setup — no loaded art — for save/load presets),
  the `ComponentType` ({RotaryKnob, VerticalFader, HorizontalSlider, Meter,
  **Button**, **Toggle**}) / `StackDirection` / `MeterFillDirection` enums.
- `Services/SkiaFilmstripRenderer.cs` — **the heart.** `ComputeTransform` does the
  rotary/linear math; `RenderFrame` composites one frame with supersampling +
  Mitchell cubic resampling (meters fill segments via `RenderMeterFrame` — procedural
  or layered on/off-art reveal, plus an optional direction-aware **peak marker** on the leading segment
  when `settings.ShowMeterPeak`; knobs may get a value-tracking fill arc via
  `RenderValueArc`, or be composited from a base+pointer layer stack via `RenderLayers`
  when `settings.Layers` + `layerArt` are supplied; **buttons** composite discrete state
  art per frame via `RenderButtonLayers` — `Static` on every frame, `Frame` only on its
  matching index); `RenderStrip` blits frames into the stacked PNG — a single Stack-direction strip
  (default) or, when `Layout == Grid`, a row-major R×C sprite atlas. All four sites that compute the
  linear sweep fraction `t` (`ComputeTransform`, `RenderLayers`, `RenderMeterFrame`,
  `RenderValueArc`) pass it through `settings.MapT(t)` first, so the parameter-law curve applies
  uniformly everywhere; `Linear` (default) is a true no-op.
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
  Clamps `GridColumns` to `Math.Max(1, …)` before serializing (BUG-017) so an unclamped upstream
  value can never violate the `plugin-asset-manifest` schema's `minimum: 1`.
- `Services/KitBuilder.cs` — the Generate tab's **one-click "Build kit"**: takes a generated matching
  set and renders each control to a filmstrip PNG (+@Nx) via the same per-type paths the Create tab
  uses (layered knob / state-frame button-toggle / flattened fader-slider / meter off-on reveal), then
  assembles a multi-control `skin.json` (row layout, reusing `ManifestService`). Orchestration over the
  renderer/exporter/importer/manifest; no renderer-math change → not in `FilmstripEngine.cs`. App-only.
- `Services/CodeSnippetService.cs` — emit ready-to-paste loader code (JUCE / CSS-HTML /
  iPlug2 / HISE / **React** — `CodeTarget.React` → a `.jsx` sprite component driven by a 0..1 `value`
  prop) for an exported strip; pure string-gen mirroring `ManifestService`. Wired into the Create,
  Assemble, and **Batch** code-export panels (Batch emits per-strip snippets via `BatchOptions.CodeTargets`).
  All 5 targets are **grid-layout aware** (real column/row math for JUCE/CSS/HISE/React; iPlug2's
  `IBitmap`/`LoadBitmap` can only read a 1D strip, so its grid path emits an explicit warning
  comment instead of silently mis-reading a 2D atlas).
- `Services/RenderRecipeService.cs` — the **Render recipe** (Create tab; path-tracing P2): emit a
  Blender `bpy` script + `frame,value,angle_deg` CSV/JSON so an offline render matches the runtime
  sweep law. Pure string-gen like `CodeSnippetService`; its static `BuildFrameTable` mirrors
  `SkiaFilmstripRenderer`'s `t = i/(N−1)` / `angle = start + (end−start)·t`, so recipe and renderer
  can't drift. App-only; not in `FilmstripEngine.cs`.
- `Services/BatchProcessor.cs` — render a folder of sources into many strips off the
  UI thread (`Task.Run`), with per-item progress and a working cancel; takes `ICodeSnippetService` and
  emits the JUCE/CSS/iPlug2/HISE/React loader snippets per strip (`BatchOptions.CodeTargets` — parity
  with Create & Assemble).
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
  `Helpers/MagickPixels.cs` — downshift Q16-HDRI's 16-bit `ToByteArray` to 8-bit, with an 8×8 Bayer
  **ordered dither** (`DitherDownTo8`) so EXR/16-bit HDR ingest reduces to 8-bit without banding (used
  by `ImageLoadService.LoadHdr`). `Helpers/ShellHelper.cs` — `RevealInFolder` (the "Show in folder"
  export action).
- `Controls/SectionHeader.cs` — a `TemplatedControl` section label with a 3px accent divider
  (styled by a `ControlTheme` in `App.axaml`); used across the sidebars.
- `ViewModels/MainWindowViewModel.cs` — Create-tab state + commands; a single
  `OnPropertyChanged` funnel refreshes the preview. Holds the layered-knob Base/Pointer slots +
  the auto-extract command + the **layered SVG/PSD import** (an `ImportedLayers` row list with
  per-layer Static/Rotate/Frame dropdowns; `ImportedLayerRow`). Exposes `Importer`, `Batch`, `Skin`,
  and `Generate`; `ImportLayeredFromPathAsync` is shared by the layered file picker and the Generate
  tab's "Use in Create" handoff — which **honours the generated control type** (knob → body+pointer;
  button → off/on Frame layers; fader/slider → flattened single source). Also holds the **sprite
  layout** (`Layout`/`GridColumns`), the **parameter-law** curve fields, and — now taking an
  injected `ISettingsService` — **render presets** (`Presets` + Save/Apply/Delete commands; a
  preset is a named snapshot of the full render setup, no loaded art; delete removes by object
  reference on both the UI collection and the persisted list so duplicate names can't desync them —
  BUG-018).
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
- Ship a HiDPI variant (the Export scale is selectable — `@2x` / `@3x` / `@4x` via the `HiDpiScale`
  property; the export suffix, the render/upscale factor, and the manifest hi-res asset all follow it;
  default 2).
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

- **2026-07-04 (released v1.5.0 then v1.5.1 — Assemble HDR-drop fix + tutorial expansions + full
  website Tutorials reference; shipped end-to-end)** — Two releases and a big website push. **(1)
  Released v1.5.0** (tag `v1.5.0`, commit `dc37f85`) — the previously-unreleased bundle: path-tracing
  P1–P5, the Depth rebrand, the 12-item enhancement wave, and the audit fixes all shipped in one
  version. **(2) Then cut v1.5.1** (patch) — GitHub Release live (tag `v1.5.1`, release commit
  `eeafd22`, installer `StripKit-Setup-1.5.1-x64.exe`, signed via Azure Trusted Signing, VirusTotal
  **0 malicious / 0 suspicious / 66 clean**; website changelog auto-pushed to `updates.json`);
  csproj/`.iss`/CHANGELOG bumped 1.5.0 → 1.5.1 by the release script. **v1.5.1's content** (fix commit
  `f83150e`): **BUG-021 (high)** — the Assemble tab **silently dropped HDR frames** (`.exr` / `.hdr` /
  16-bit `.tif`) on **drag-drop** and in **"Add files…"** (only "Choose folder…" accepted them) because
  three accepted-extension lists had drifted; fixed by promoting
  `FrameSequenceViewModel.AcceptedExtensions` to a single **`public static`** source of truth that
  `AssembleView.axaml.cs`'s drop handler (delegates via a property) and
  `FileDialogService.OpenImagesAsync` both use (the list now carries `.exr`/`.hdr`/`.tif`/`.tiff`).
  Plus **in-app Getting Started walkthrough expansions** across all six tabs in `TutorialViewModel.cs`
  (Ctrl+O/Ctrl+E + Render Recipe on Create; transport controls + resample/slice-count decoupling on
  Import; Button/Toggle-not-batchable on Batch; stack-direction + @2x fields on Skin;
  provider-switch-resets-model + auto-retry + Copy SVG on Generate; frame-list add/reorder/remove on
  Assemble). **Suite 333 → 335 green, build clean.** **(3) StripKit-Website work** (sibling repo, all
  pushed) — a full **Tutorials reference**: a hub `tutorials.html` + 7 standalone per-tab pages
  (`tutorials-create/import/batch/skin/generate/assemble/shortcuts.html`) + a shared `css/docs.css`,
  generated from actual source via an inventory→draft→verify→fix Workflow; refreshed the stale app
  screenshot; and brought `getting-started.html`/`index.html` current (they said "4 control types" →
  now **6**; added Button/Toggle, the React export target, @3x/@4x, and the Generate + Assemble tabs).
  Plus a small nav/hover polish round (missing GitHub link on 4 pages; a `.nav a:hover` specificity bug
  that turned the Get-StripKit button text orange). **The v1.5.1 in-app fixes came from a two-surface
  docs audit** (website tutorials + in-app `TutorialViewModel` vs. actual source), run as a fan-out
  Workflow — server-side rate-limiting repeatedly killed the verify sub-agents, so verification was
  done by the main loop against ground truth. **Next:** a live-eyeball QA pass of the path-traced /
  AI-generation output with real assets (long-standing carryover); a few deliberately-skipped
  low-priority in-app tutorial parity items (documented on the website, not in-app by choice).

- **2026-07-02 (v1.5.0-dev: enhancement wave — 12/12 feature-complete + 4-dimension adversarial
  review; uncommitted)** — Finished the 3 items deferred from the prior session, in priority order.
  **(10) Sprite-grid layout (R×C)** — a `StripLayout` enum (Strip default / **Grid**) +
  `FilmstripSettings.Layout`/`GridColumns`; `RenderStrip` packs frames into a row-major R×C atlas
  when selected (`col = i % cols`, `row = i / cols`), gated so Strip stays byte-identical; mirrored
  in `FilmstripEngine.cs`. `ManifestControl` gained nullable `Layout`/`GridColumns` (omitted unless
  grid) + a `plugin-asset-manifest` schema-doc update. All 5 code-export targets got grid-aware
  column/row math except **iPlug2** — its built-in `IBitmap`/`LoadBitmap` can only read a 1D strip,
  so it emits an explicit warning comment instead of silently mis-emitting. A "Sprite layout" combo +
  conditional "Grid columns" input on the Create tab (Stack-direction hides when Grid is active);
  new golden `knob_grid8x4`. +16 tests. **(11) Parameter-law frame mapping (log/skew)** — a
  `FrameMappingCurve` enum (Linear/**Skew**/**Logarithmic**) + `MappingCurve`/`MappingSkew`/
  `MappingLogBase` on `FilmstripSettings` + a `MapT(t)` remap applied at all 4 renderer sites that
  compute the sweep fraction (`ComputeTransform`, `RenderLayers`, `RenderMeterFrame`,
  `RenderValueArc`), so knobs, layered knobs, meters, and the value arc all honour the curve
  consistently. `Linear` is a true no-op — returns the input completely unchanged, not just
  numerically equal — so every existing golden stayed byte-identical; mirrored in
  `FilmstripEngine.cs`. A "PARAMETER LAW (advanced)" Create-tab section; the preview readout now
  reflects the mapped angle too. New golden `knob_skew_mid`. +12 tests. **(12) Save/load render
  presets** — a `RenderPreset` model (~40 fields: the full Create-tab render setup — type, frames,
  sweep, resolution, sprite layout, parameter-law curve, meter/value-arc settings, export
  preferences — deliberately excluding loaded art) persisted via `AppSettings.RenderPresets`.
  `ISettingsService` is now injected into `MainWindowViewModel`'s constructor (rippled into
  `TransportTileAlignmentTests`/`LoadPathTests`/`LayeredImportViewModelTests` + a new
  `TestFakes.MainVm()` helper). `SavePreset`/`ApplyPreset`/`DeletePreset` commands (save overwrites
  by case-insensitive name; apply bulk-restores everything in one `_suspendRefresh` pass, mirroring
  the existing bulk-assign pattern). A "PRESETS" section atop the Create tab's left panel. +9 tests.
  **Adversarial review:** a 4-dimension Workflow (renderer/golden-compat, VM/MVVM,
  code-export/manifest, XAML/tests — each independently re-verified against the current file
  contents by a second agent) found and fixed 2 real issues before commit: **BUG-017** (medium) —
  `ManifestService` could serialize a non-positive `GridColumns`, violating the manifest schema's
  `minimum: 1` — now clamped; **BUG-018** (low) — `DeletePreset()` removed the UI's `Presets` entry
  by reference but the persisted list's entry by name, so duplicate-named presets (hand-edited-file
  only) could desync the two collections — now reference-based on both sides. **Live-verified** in
  the running dev build (`dotnet run` + computer-use): Presets section, the Sprite-layout
  Strip↔Grid toggle, and the Parameter Law section all render and behave correctly. **Suite
  288 → 331 green, build clean.** **The v1.5 enhancement wave is now 12/12 feature-complete.** Not
  yet committed to git (working-tree changes) or released — csproj/`.iss` `<Version>` still at
  1.3.0; the release script bumps to 1.5.0 at release. **Next:** commit this work (ask the user
  first — not done autonomously per the git-safety rule), then a live path-traced/AI-generation
  eyeball pass (carried over, unrelated to this session), then cut **v1.5.0**.

- **2026-07-02 (v1.5.0-dev: enhancement wave — 9/12 shipped + pushed on `origin/main`; unreleased)** —
  Rather than cut v1.4.0, the owner bundled a batch of small enhancements and **reframed the next release
  as v1.5.0** (the path-tracing P1–P5 work + the fine-tooth-comb audit below are all part of it). **Nine of
  twelve** planned items are done, committed, and **pushed to `origin/main` (tip `41fe792`)**: **(1)** a
  **React / web-component code-export target** — `CodeTarget.React` → a `.jsx` sprite component driven by a
  0..1 `value` prop, wired into the Create + Assemble + **Batch** export panels (`6d6ba07`, +3). **(2)**
  **Dithered HDR de-band** (finishes path-tracing P3b) — `Helpers/MagickPixels.DitherDownTo8` (an 8×8 Bayer
  ordered dither) in `ImageLoadService.LoadHdr`, so EXR/16-bit ingest reduces to 8-bit without banding
  (`18a444b`, +2). **(3)** **Remember window size + last tab** — `AppSettings.WindowWidth/WindowHeight/
  LastTabIndex`, restored/persisted in `App.axaml.cs` (`99bdd22`). **(4)** **Ctrl+O / Ctrl+E shortcuts** —
  `Window.KeyBindings`, Ctrl-modified only (`99bdd22`). **(5)** **Batch tab → loader code** —
  `BatchOptions.CodeTargets`; `BatchProcessor` takes `ICodeSnippetService` and emits per-strip
  JUCE/CSS/iPlug2/HISE/React snippets, parity with Create & Assemble (`94d431f`, +1). **(6)** **CI coverage
  gate** — `ci.yml` collects coverage and fails below **70% line** (`3c9be86`). **(7)** **"Show in folder"
  after export** (Create + Assemble) — `Helpers/ShellHelper.RevealInFolder` + a `RevealExportCommand` /
  `LastExportPath` on the VMs; the button sits **outside** the `TransportTile` Border to preserve the
  transport-tile-height invariant (`a295f38`). **(8)** **Arbitrary HiDPI scale** — a `HiDpiScale` property
  across Create/Assemble/Batch (`@2x`/`@3x`/`@4x`; the suffix + render/upscale factor + manifest hi-res
  asset all follow it; default 2) (`43a87c9`, +1). **(9)** **Meter peak-marker** —
  `FilmstripSettings.ShowMeterPeak` + `PeakColorArgb` (mirrored in `FilmstripEngine.cs`);
  `RenderMeterFrame` paints the direction-aware leading (peak) segment, **gated OFF by default** so every
  meter golden is byte-identical (`41fe792`, +1). **Suite 280 → 288 green, build clean, ~79% coverage.**
  **Deferred to a later careful pass (3 of 12):** sprite-grid layout, parameter-law frame mapping,
  save/load presets. **Still unreleased** — csproj/`.iss` `<Version>` at 1.3.0 (the release script bumps to
  1.5.0 at release). **Next:** the 3 deferred items, then release **v1.5.0**.

- **2026-07-02 (v1.4.0-dev: path-tracing P3b + P4 + P5 — HDR ingest, frame interpolation, AOV pass; on
  `main`, unreleased)** — Finished the remaining offline-3D / path-tracing phases, all on the Assemble
  tab with no renderer change. **P4 — frame interpolation** (`6840c93`): a `FrameInterpolation`
  {Nearest, Crossfade} mode; Crossfade cross-dissolves the two bracketing frames per output frame (the
  `(N−1)/(M−1)` law keeps the endpoints exact) so ~32 expensive frames ship as 64/128. **P5 — AOV /
  emission pass** (`52726cf`): the tab takes a second render pass (an emission/glow AOV) and additively
  composites it over the beauty frames; `FrameSequenceOptions.EmissionFrames` / `EmissionIntensity`, a
  mismatched count warns rather than throws. **P3b — 16-bit / EXR HDR ingest** (`7f5057a`): swapped
  **Magick.NET-Q8 → Q16-HDRI** (holds >8-bit; OpenEXR bundled); `ImageLoadService` routes `.exr` / `.hdr`
  / 16-bit `.tif` through Magick (linear → sRGB + clamp → depth-8 → PNG32 → Skia), and new
  `Helpers/MagickPixels` downshifts Q16's 16-bit `ToByteArray` to 8-bit — the PSD path relies on this and
  was revalidated. +6 tests. **Suite 274 → 280 green, build advisory-clean.** Docs reconciled (ROADMAP
  P3b/P4/P5 → ✅; CLAUDE / HANDOFF / TESTING 280 / CHANGELOG). **Deferred:** optical-flow interpolation
  (P4 v2 — needs a CV dep); a dithered de-band (Magick `OrderedDither` posterizes to 2 levels) +
  multi-layer/deep EXR (P3b); runtime toggle/value-track of the AOV pass (P5 — needs loader + manifest).
  **Next:** push + release v1.4.0.

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
