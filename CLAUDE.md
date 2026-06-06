# CLAUDE.md — StripKit

> Version 1.0.0 · last-updated 2026-06-06 · last-audit 2026-06-06

Context for any Claude Code / agent session working on this repo. Keep this file
short, current, and instruction-shaped. Update the **Last completed task** section
at the end of every working session.

## What this is

StripKit is a C#/Avalonia desktop tool that turns a single transparent PNG
into an animated **filmstrip** (sprite sheet) for audio-plugin GUI controls:
rotary knobs, vertical faders, and horizontal sliders. The output PNG is consumed
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
  SkiaSharp 3.119.2.
- Layered-source import (app-only): **Svg.Skia** 5.0.0 (MIT, SVG layers) + **Magick.NET-Q8-x64**
  14.13.1 (Apache-2.0, PSD/PSB layers). Both permissive; not in `FilmstripEngine.cs`.
- MVVM + DI (Microsoft.Extensions.DependencyInjection), compiled bindings.
- Tests: xUnit + NSubstitute + FluentAssertions, `Avalonia.Headless` for view
  tests, golden-image regression for the renderer (`tests/StripKit.Tests`).
- Packaging: self-contained `win-x64` publish → **Inno Setup** installer
  (`installer/StripKit.iss`); distributed as a **GitHub Release download** (no in-app
  auto-update). Release pipeline: `scripts/Invoke-Release.ps1` +
  `.github/workflows/auto-release.yml` — see `docs/PACKAGING.md`.
- CI: `.github/workflows/ci.yml` runs build + full test suite on every push and PR.

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
test-gate → bump `Version` in `src/StripKit/StripKit.csproj`, `MyAppVersion` in
`installer/StripKit.iss`, and `docs/CHANGELOG.md` → `dotnet publish` → `ISCC` builds
`releases/latest/StripKit-Setup-<ver>-x64.exe` → commit + tag `vX.Y.Z` + push. **Stage
2** `.github/workflows/auto-release.yml` (triggered by the tracked `releases/latest/*.exe`,
or `workflow_dispatch`): VirusTotal scan (`VT_API_KEY` secret) → the **sole**
`gh release create` (notes via `--notes-file`, never inline `--notes`). **Stage 3** the
website reads the live GitHub Release. Unsigned for now (SmartScreen / VirusTotal
heuristic FPs until a code-signing cert is added).

## Architecture (one idea, four component types) — full detail in `docs/ARCHITECTURE.md`

Every render is: *for each of N frames, place the source art inside a fixed frame
cell under a per-frame transform, then stack the cells into one PNG.* The four
component types are knob, vertical fader, horizontal slider, and **meter**
(progressive segment fill). The app is a `TabControl` with **four** tabs — **Create**
(make a strip), **Import** (re-use / re-slice / resample one), **Batch** (a whole folder at
once), and **Skin** (assemble a multi-control `skin.json`).

- `Models/` — pure data, no UI/Skia deps: `FilmstripSettings` (render contract),
  `FrameTransform`, `StripDetection` (importer output),
  `SkinManifest`/`ManifestControl`/`ManifestBounds`, `BatchModels`, `CodeModels`,
  `RenderLayer` (`LayerBehavior` + per-layer pivot — the layered-knob stack), the
  `ComponentType`/`StackDirection`/`MeterFillDirection` enums.
- `Services/SkiaFilmstripRenderer.cs` — **the heart.** `ComputeTransform` does the
  rotary/linear math; `RenderFrame` composites one frame with supersampling +
  Mitchell cubic resampling (meters fill segments via `RenderMeterFrame` — procedural
  or layered on/off-art reveal; knobs may get a value-tracking fill arc via
  `RenderValueArc`, or be composited from a base+pointer layer stack via `RenderLayers`
  when `settings.Layers` + `layerArt` are supplied); `RenderStrip` blits frames into the
  stacked PNG.
- `Services/FilmstripImporter.cs` — detect an existing strip's layout from its
  dimensions, extract a frame, re-stack orientation, and **resample** (re-time) to a new
  frame count via nearest-frame mapping (no Avalonia dep).
- `Services/PointerExtractor.cs` — split a flat knob into a static base + a rotating pointer
  via the **radial-symmetry residual** (auto-fills the layered-knob slots; ★ #3 step 2). Static,
  pure SkiaSharp like `ContentAnalysis`; app-only (not in `FilmstripEngine.cs`).
- `Services/LayeredImportService.cs` — import a real layered **SVG** (Svg.Skia) / **PSD/PSB**
  (Magick.NET) into named, behaviour-tagged, canvas-registered layers (★ #3 step 3 — the final
  layer-aware piece) → a `LayeredImportResult`. Feeds the renderer's existing layer stack with
  no renderer change; app-only (not in `FilmstripEngine.cs`).
- `Services/ManifestService.cs` — build + serialize a `skin.json` (System.Text.Json):
  `BuildSingleControl` (Create tab) and `BuildManifest` (the Skin tab's multi-control export).
- `Services/CodeSnippetService.cs` — emit ready-to-paste loader code (JUCE / CSS-HTML /
  iPlug2 / HISE) for an exported strip; pure string-gen mirroring `ManifestService`.
- `Services/BatchProcessor.cs` — render a folder of sources into many strips off the
  UI thread (`Task.Run`), with per-item progress and a working cancel.
- `Services/ImageLoadService.cs` / `ExportService.cs` — decode/encode PNG ↔ `SKBitmap`.
- `Services/FileDialogService.cs` — open/save/open-layered pickers (app-layer; holds the Window).
- `Services/SettingsService.cs` — persist `AppSettings` (the first-run "seen tutorial" flag) to
  `%APPDATA%/StripKit/settings.json`; the app's only saved state. `Services/AssetService.cs` —
  extract the bundled sample knob to a temp path for the tutorial (app-layer).
- `Helpers/SkiaImageInterop.cs` — `SKBitmap` -> Avalonia `Bitmap` for preview.
- `ViewModels/MainWindowViewModel.cs` — Create-tab state + commands; a single
  `OnPropertyChanged` funnel refreshes the preview. Holds the layered-knob Base/Pointer slots +
  the auto-extract command + the **layered SVG/PSD import** (an `ImportedLayers` row list with
  per-layer Static/Rotate dropdowns; `ImportedLayerRow`). Exposes `Importer`, `Batch`, and `Skin`.
- `ViewModels/ImporterViewModel.cs` — Import-tab state + commands (detect / scrub / extract /
  re-stack / resample; same funnel).
- `ViewModels/BatchViewModel.cs` — Batch-tab state + commands (folders, template incl. the meter
  settings + layered/backdrop toggle, run/cancel, progress); no preview funnel.
- `ViewModels/SkinViewModel.cs` (+ `SkinControlEntry.cs`) — Skin-tab state + commands: a
  multi-control `skin.json` builder (controls list, add-from-strip / blank, detail editor, export).
- `ViewModels/TutorialViewModel.cs` — the Getting Started overlay: step list + navigation +
  first-run auto-open (via `ISettingsService`) + the `LoadSampleRequested` event (sample knob).
- `Views/MainWindow.axaml(.cs)` — the `TabControl` (+ header "Getting started" button + the
  `TutorialOverlay` as the top layer); code-behind holds the auto-play timer + the Create
  preview's drag-drop handlers.
- `Views/TutorialOverlay.axaml(.cs)` — the Getting Started guided overlay (bound to `TutorialViewModel`).
- `Views/ImporterView.axaml(.cs)` — the Import tab UserControl + its drop handlers.
- `Views/BatchView.axaml(.cs)` — the Batch tab UserControl (folder pickers, template,
  Run/Cancel, progress bar, results).
- `Views/SkinView.axaml(.cs)` — the Skin tab UserControl (skin metadata + controls list;
  per-control detail editor + Export skin.json).
- Repo-root `FilmstripEngine.cs` — standalone portable copy of the renderer (NOT in
  the build); keep in sync with `SkiaFilmstripRenderer` if the math changes.

## Project skills (`.claude/skills/`)

- `avalonia-skia-interop` — SkiaSharp rendering inside Avalonia; pixel interop.
- `avalonia-drag-drop-files` — file drag-and-drop into an Avalonia view (Phase 1).
- `live-preview-render-loop` — the responsive preview pattern this VM embodies.
- `image-regression-testing` — golden-image tests to lock renderer output (Phase 4).
- `filmstrip-importer-engine` — detect/re-slice existing strips (Phase 2).
- `plugin-asset-manifest` — JSON manifest binding strips to parameters (Phase 3).

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

## House conventions

- **Obsidian design system** (glassmorphism, dark): accent `#e8440a`, **sans-serif
  only** — `Verdana, Segoe UI, Arial, sans-serif` (no monospace). Design tokens live
  in `App.axaml` (`AccentBrush`/`AccentHiBrush`, `Text1/2/3Brush`, `GlassFill/GlassBorder`,
  the `ObsidianAcrylic` material). The window uses `TransparencyLevelHint="AcrylicBlur"`
  + an `ExperimentalAcrylicBorder` frosted base (`FallbackColor` for non-acrylic
  platforms); panels are `Border.card` glass; primary buttons use Fluent's `accent`
  class. Re-use these tokens — don't hard-code hex or reintroduce JetBrains Mono.
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

- **2026-06-06 (v1.0.0 shipped + reusable website-changelog automation)** — Cut **v1.0.0** (the
  major release: layered PSD/SVG import + the in-app tutorial + the About fix). Hit two release-tooling
  snags first, both fixed: signing needed the **Trusted Signing** path (signtool + `Microsoft.Trusted.
  Signing.Client` dlib; AzureSignTool 403s against Trusted Signing endpoints), and the `.ps1` lost its
  UTF-8 BOM (PS 5.1 mojibake → parse fail; re-added). Release pipeline ran clean: 125 tests → bump →
  publish → **sign exe + installer** → Inno → push → CI VirusTotal + `gh release create`. **Live + signed:**
  `github.com/Vybecode-LTD/stripkit/releases/tag/v1.0.0` (58.3 MB). Then closed the website gap: the app
  release doesn't touch the **website** repo, so stripkit.pro's changelog only moves on a `updates.json`
  commit (Railway auto-deploys the `StripKit-Website` repo on push). Added the v1.0.0 entry (live,
  verified) AND built a **project-agnostic** `scripts/Publish-WebsiteChangelog.ps1` (ASCII-only, no BOM
  trap): auto-drafts a version's plain-language entry from `docs/CHANGELOG.md` (Added→new/Fixed→fix/
  else→improved; strips test/build bookkeeping), prepends to a site's `updates.json`, and with `-Push`
  publishes (→ auto-deploy). **Hybrid:** auto-draft → refine → push. Wired into `Invoke-Release.ps1` as
  an optional Stage 3 (`-WebsiteRepo <path>`). Reusable on any desktop app + download site (same Azure
  Trusted Signing profile signs all). Docs: PACKAGING §8.4 (Stage-3 automation + reuse) + §9A (the
  script-BOM guard), SOURCE_MAP. **Next:** **handoff is overdue** — reconcile all managed docs to 1.0.0
  (`HANDOFF.md` + `AUDIT-LOG.md` + version headers). Then the standing queue (website P2 getting-started
  guide, more code-export targets, `actions/checkout@v4→v5`).
- **2026-06-06 (onboarding P1 — interactive in-app Getting Started tutorial)** — Built the first
  onboarding item after scoping the forks with the owner (all four recommended options taken). A
  re-openable **"Getting Started"** guided overlay (`Views/TutorialOverlay.axaml` + `ViewModels/
  TutorialViewModel.cs`) walks a new user through the core loop (load → pick a type → align → frames/
  export → loader code → layered import) as an on-brand bottom-centre glass card over a click-through
  scrim (non-blocking — the app stays usable while the guide is open). It **auto-opens on first
  launch** (a new minimal `ISettingsService`/`SettingsService` persists `HasSeenTutorial` to
  `%APPDATA%/StripKit/settings.json` — the app's only saved state) and is re-openable from a header
  **"Getting started"** button; finishing/skipping persists "seen". Step 1 offers **"Load sample
  knob"** — a **bundled `Assets/sample-knob.png`** (extracted to temp by a new `IAssetService`/
  `AssetService`, then run through the normal `LoadSourceFromPath`) so a brand-new user sees the whole
  flow with no art. Plus **contextual tooltips** on the key controls (load / type / frames / export).
  `MainWindowViewModel` injects `TutorialViewModel` + `IAssetService`, subscribes to the VM's
  `LoadSampleRequested` event, and calls `Tutorial.MaybeShowOnFirstRun()`. **No renderer/engine
  changes** (not in `FilmstripEngine.cs`). DI: `ISettingsService`/`IAssetService` singletons +
  `TutorialViewModel` transient. **+11 tests** (`SettingsServiceTests` 3, `TutorialViewModelTests` 7,
  + 1 sample-load integration in `LoadPathTests`), suite **112→123 green**; build 0/0; app boots
  clean with the overlay auto-opening on first run (compiled bindings type-checked; final visual
  polish best eyeballed on the GUI, like the Obsidian design sign-off). Owner-confirmed forks: guided
  overlay (not coach-marks / static); auto-open first run + re-openable; bundle a sample knob; and
  checkpoint-commit the layered import first (done — `03b441a`). **Unreleased on the working tree
  (toward 0.9.0); not yet committed.** **Next:** P2 — the website `stripkit.pro/getting-started/`
  guide (separate `StripKit-Website` repo; depends on the website deploy); plus the standing queue
  (more code-export targets, website deploy + `updates.json`, code-signing cert, checkout@v4→v5).
- **2026-06-06 (vNext ★ #3, step 3 — layered PSD/SVG import; completes the layer-aware bet)** —
  The final ★ piece, scoped with the owner before building. A real layered source is now imported
  and mapped onto the renderer's existing layer stack: **"Import layered file (SVG / PSD)…"** in the
  Create-tab layered panel. New **app-only** `Services/LayeredImportService.cs` (`ILayeredImportService`)
  parses **SVG** groups via **Svg.Skia** (MIT — render the doc once for the canonical canvas, then
  rasterize each top-level `<g>` as a standalone SVG so groups register pixel-for-pixel) and **PSD/PSB**
  layers via **Magick.NET-Q8-x64** (Apache-2.0 — drop the unlabeled merged composite, blit each named
  layer onto the canvas at its page offset). Each `ImportedLayer` carries a **name-guessed behaviour**
  (pointer/needle/indicator… → Rotate, else Static) the user overrides per layer. VM: an
  `ObservableCollection<ImportedLayerRow>` (`ImportedLayerRow` = name + editable Static/Rotate + art)
  drives `BuildSettings().Layers` + `BuildLayerArt()` when non-empty (`IsImportedKnob`), each Rotate
  layer pivoting about the shared detected centre; importing squares the frame, forces the knob type,
  seeds the axis, and **replaces** the base/pointer slots (mutually exclusive). **No renderer change**
  (it already composites an N-layer stack) → **NOT mirrored in `FilmstripEngine.cs`**; gated behind
  defaults so the single-source + base/pointer paths and **every prior golden are byte-identical**.
  Owner-confirmed forks: **both** SVG+PSD in one increment; Svg.Skia + Magick.NET (both permissive,
  no copyleft/paid); map to the existing **Static/Rotate** only (translate/opacity-ramp deferred to a
  later renderer increment); **auto-guess by name + manual override**. Deps added: `Svg.Skia` 5.0.0,
  `Magick.NET-Q8-x64` 14.13.1 (SkiaSharp 3.119.0→**3.119.2** for Svg.Skia's floor — no baseline shift);
  the win-x64 installer grows ~22 MB (ImageMagick native — accepted Magick.NET cost). **+14 tests**
  (`LayeredImportServiceTests` 8 SVG+PSD round-trips incl. PSD synthesized via Magick.NET write,
  `LayeredImportViewModelTests` 4 command/gating/wiring, `LayeredImportRenderTests` 2 — golden
  `imported_svg_knob_mid` **eyeballed** + pixel-logic), suite **98→112 green**; build 0/0; app boots
  clean. **Unreleased on `main` (toward 0.9.0); not yet committed.** MVP boundaries (noted in
  ARCHITECTURE §6.8): top-level groups = layers (no Figma single-root unwrap), PSD file order (no
  reorder UI), Static/Rotate only. **Next:** the two onboarding items — interactive in-app
  help/tutorial (P1) and the website `stripkit.pro/getting-started/` guide (P2); plus more code-export
  targets, website deploy + `updates.json`, code-signing cert, `actions/checkout@v4→v5`.
- **2026-06-05 (vNext ★ #3, step 2 — auto-pointer extraction; + session handoff)** — Built the
  second of the three layer-aware steps and performed a full handoff. An **"Auto-extract from flat
  knob…"** button (Create-tab layered panel) splits a single FLAT knob image into the base + pointer
  slots automatically. New `Services/PointerExtractor.cs` uses the **radial-symmetry residual**: a
  knob body is rotationally symmetric, so the indicator is whatever breaks that symmetry — the
  robust per-radius mean is the symmetric **base**, the residual is the **pointer**; returns a
  **confidence** (low for asymmetric bodies, flagged). A starting guess the user verifies via the
  preview/scrub (assumes the art is the frame-0 position). Pure SkiaSharp like `ContentAnalysis`;
  **app-only — NOT mirrored in `FilmstripEngine.cs`**. +4 tests (`PointerExtractorTests` 3 + 1 VM),
  suite **94→98**; build 0/0, app boots clean, **eyeballed** (clean symmetric base; crisp needle
  rotating about a static body; minor central pivot dot). Committed `afca651`, **pushed**
  (unreleased — toward 0.9.0). Owner-confirmed forks: radial-symmetry method, auto-fill-and-verify
  workflow, frame-0 rest-angle assumption. Full handoff: HANDOFF/AUDIT-LOG/CLAUDE + all doc headers
  reconciled to 0.8.0; ROADMAP updated with two new owner-requested items (interactive in-app
  help/tutorial system; website `stripkit.pro/getting-started/` how-to guide). **Next:** ★ step 3 —
  layered **PSD/SVG import** (the big dependency lift — needs a library + license decision; scope first).
- **2026-06-05 (v0.8.0 shipped — 3 finish-the-gaps features + layer-aware step 1)** — Cut **v0.8.0**
  (test gate 94/94 → CI VirusTotal-scanned + created the public release; live, 33.5 MB installer).
  Committed the layer-aware step-1 work + the new **`layer-aware-filmstrip-compositing` project
  skill** (`5fa2ba4`; skill-authoring-linter 0/0; `.skill` under git-ignored `dist/`) as clean
  checkpoint commits, then built three carryover gaps, each its own commit: **(1) Batch-tab meter
  settings** (`e126daf`) — the Batch template now exposes the meter panel + a **"source is a
  backdrop"** toggle (each file = lit on-art → layered, or housing → procedural LEDs);
  `BatchOptions.MeterSourceIsBackdrop`. **(2) Skin tab** (`4a9e2ac`) — a new **fourth tab** that
  binds several strips to several parameters in one `skin.json` (add-from-strip auto-detect / blank;
  per-control id/type/param/asset/frames/size/stack/**bounds**/**value range**; skin name/author/
  base-res/window background; export to a folder); new `IManifestService.BuildManifest` +
  `SkinViewModel`/`SkinControlEntry`/`SkinView`, DI-registered. **(3) Importer resampling**
  (`322a80d`) — re-time a strip to a new frame count (`FilmstripImporter.Resample`, **nearest-frame**,
  endpoints land on min/max, no ghosting). Suite 72→94 across the four; build 0/0; app boots clean;
  per-feature docs reconciled. The v0.8.0 release commit (`65a9c4f`) staged only the version files +
  installer (the two stray untracked files — `docs/PRESS-RELEASE.md`, `press/` — were excluded).
- **2026-06-04 (vNext ★ #3, step 1 — layer-aware knob: base + pointer)** — First of the three
  build-order steps for the last ★ bet. A knob can now be composited from **two layers**: a
  **static base** (body/well, drawn fixed) + a separate **pointer** that rotates with the value,
  so only the pointer moves and the body stays crisp/re-renderable. Built the **general layer
  model** (`Models/RenderLayer.cs`: `LayerBehavior {Static, Rotate}` + `RenderLayer` with a
  normalized per-layer pivot) on a new `FilmstripSettings.Layers` list (deep `Clone`); the
  renderer composites the stack bottom-first via a new **`RenderLayers`** path, and
  `RenderFrame`/`RenderStrip` gained an optional index-matched `IReadOnlyList<SKBitmap>? layerArt`
  param. The **pointer rotates about its own pivot** (independent of the body), seeded from the
  body's detected content centre and adjustable (numeric Pointer pivot X/Y + "Center pointer on
  body"). A value arc still composes on top. Create-tab **"LAYERED KNOB (base + pointer)"** panel
  (knob-only, in the rotary section): Load/Clear **Base** + **Pointer** slots + pivot controls.
  **Gated by defaults — empty `Layers` renders the single source byte-identical, so all prior
  goldens are unchanged.** Owner-confirmed forks first: general layer-list model, explicit
  Base/Pointer slots, separate pointer pivot, knob-only. Mirrored in `FilmstripEngine.cs`.
  **+12 tests (`LayeredKnobRenderTests` 3 golden + 6 pixel-logic; 3 VM load-path), suite 72→84
  green;** baselines eyeballed (body static, only the needle rotates), app boots clean, build
  0/0. Docs reconciled (ARCHITECTURE §5.6/§6.6, SOURCE_MAP, TESTING, CHANGELOG [Unreleased],
  ROADMAP). **Not yet shipped/committed** — feature is on `main` working tree. **Next:** step 2 —
  auto-pointer extraction from flat art (CV, seed from `ContentAnalysis`); then step 3 — layered
  PSD/SVG import. Still pending: React/Web-Component + Unity/Godot code targets, website →
  stripkit.pro + v0.7.0 `updates.json`, code-signing cert, Batch-tab meter UI.
- **2026-06-04 (v0.7.0 shipped + handoff)** — Cut **v0.7.0** with the two ★ features below
  (value-arc + code-export). `Invoke-Release.ps1 -Bump minor`: test gate **72/72** → bump
  0.6.0→0.7.0 → publish → Inno installer → commit `fe24ca3` + tag `v0.7.0` + push; CI
  VirusTotal-scanned and created the **public release** (verified live, 33.5 MB installer).
  All managed docs reconciled to 0.7.0 (HANDOFF rewritten, AUDIT-LOG entry added, test count
  49→72). Working tree clean; `main` == origin; 0 open bugs. **Next: vNext ★ #3 —
  layer-aware animation** (owner wants all three input modes eventually; build order:
  **base+pointer PNGs → auto-pointer-extract from flat art → PSD/SVG import**). Also pending:
  React/Web-Component + Unity/Godot code targets (P2), deploy website to stripkit.pro +
  add its v0.7.0 `updates.json` entry, code-signing cert, Batch-tab meter UI.
- **2026-06-04 (vNext ★ #2 — code / component export)** — Second of the three ★ bets, the
  "close the loop" feature: an export can also emit **ready-to-paste loader code** so there's
  no hand-wiring step. New **pure** `CodeSnippetService` (`Generate`/`FileName`/`SaveAsync`,
  no Skia/Avalonia — a direct sibling of `ManifestService`) + `CodeTarget` enum and
  `CodeSnippetRequest` record (`Models/CodeModels.cs`). **Four targets shipped:** **JUCE**
  (`LookAndFeel_V4` `drawRotarySlider`/`drawLinearSlider`, or a meter `Component` w/ `setLevel`),
  **CSS/HTML** (self-contained `<style>`+`<script>` `background-position` sprite + a 0..1 setter),
  **iPlug2** (`IBKnobControl`/`IBSliderControl`/`IBitmapControl` + `LoadBitmap`), **HISE**
  (`ScriptPanel` paint routine). Every snippet uses the universal `frame = clamp(round(value·(N−1)),
  0, N−1)` and the stack-direction source axis; identifiers sanitised per language. VM: `ExportCode`
  + 4 `EmitCode*` toggles + `CodePreviewTarget` + live `GeneratedCode` (funnel refreshes the
  snippet on code-relevant input incl. `ParameterId`, **without** re-rendering the image); export
  writes one file per ticked target next to the PNG (`.juce.h`/`.html`/`.iplug2.cpp`/`.hise.js`).
  Create-tab **"CODE EXPORT"** panel + a **preview / copy-to-clipboard** expander (clipboard in
  `MainWindow.axaml.cs`). DI-registered. Renderer/manifest untouched; generated snippets visually
  reviewed for correctness. +15 tests (`CodeSnippetServiceTests`). Build **0/0**, **72/72 green**,
  boots clean. **Next:** vNext ★ #3 — **layer-aware animation + auto-pointer extraction** (the big
  one: a deep renderer/model change — accept base+pointer layers so only the pointer rotates, plus
  flat-art indicator auto-detect; scope the input format first). React/Web-Component + Unity/Godot
  code targets remain (P2). Still pending: website → stripkit.pro, code-signing cert, Batch-tab
  meter UI.
- **2026-06-04 (vNext ★ #1 — value-arc / fill-ring generator)** — First of the three
  ★ roadmap bets. A Serum/Vital-style **value arc** is composited onto knob frames: the
  lit arc sweeps from the start angle to the current frame's angle (`Start + (End−Start)·t`),
  concentric with the rotation pivot, baked into the exported PNG. New private
  `RenderValueArc` in `SkiaFilmstripRenderer` (called from `RenderFrame` **only for
  `RotaryKnob` when `ShowValueArc`** — so all existing goldens are byte-identical), plus
  the `StrokePaint` helper. **11 new Skia-free `FilmstripSettings` fields** (packed
  `0xAARRGGBB`): `ShowValueArc`, `ArcRadius` (frac of inscribed radius), `ArcThickness`,
  `ArcRoundCaps`, `ArcColorArgb`, `ArcGradient`+`ArcColor2Argb`, `ArcTrack`+`ArcTrackColorArgb`,
  `ArcGlow`+`ArcGlowSize`. Optional **dim track**, **sweep gradient** (`CreateSweepGradient`),
  and **glow** (`CreateBlur` under-stroke); round/butt caps. The arc **inherits the knob's
  rotation sweep** (no redundant start-angle field) and is **knob-only**; the `skin.json`
  manifest is unchanged (loader just shows frames). Angle convention handled: app 0° = 12
  o'clock, Skia arc 0° = 3 o'clock → `StartAngle − 90` (the −90 cancels in the sweep delta).
  VM: 11 `[ObservableProperty]` fields (hex colours via `ParseArgb`) on the funnel + a
  **"VALUE ARC" panel** in the Create-tab rotary section (radius/thickness/colour/caps/track,
  gradient+glow in an expander). **Mirrored in `FilmstripEngine.cs`.** +8 tests
  (`ValueArcRenderTests`: 4 golden baselines incl. gradient+glow — visually reviewed — and
  4 pixel-logic for lit-sweep growth + the off / non-knob no-ops). Build **0/0**, **57/57
  green**, app boots clean. **Next steps:** vNext ★ #2 — **code/component export**
  (JUCE/iPlug2/CSS loader snippets; a pure `CodeExportService` mirroring `ManifestService`,
  zero renderer risk), then ★ #3 — layer-aware animation. Still pending from before: deploy
  website to **stripkit.pro**, code-signing cert, Batch-tab meter settings UI.
- **2026-06-04 (documentation overhaul + audit + OSS hardening)** — Three doc sets
  fully rewritten from source-verified content: **`docs/ARCHITECTURE.md`** (complete
  deep-dive: alignment system + `SourceCenterX/Y`/`ContentAnalysis`, meter renderer —
  procedural vs. layered modes, all four fill directions, MVVM `OnPropertyChanged`
  funnel, Obsidian design tokens, DI wiring, compiled bindings, test infrastructure);
  **`docs/PACKAGING.md`** (exhaustive ~640-line agent-facing release-pipeline reference:
  every script flag, every CI step, two DO-NOT-REINTRODUCE bug guards for the
  mojibake + shell-injection bugs, full runbook for cutting a release, signing notes,
  VirusTotal false-positive context); **`docs/ROADMAP.md`** (master roadmap with phases
  0-8 confirmed done, full prioritised vNext feature set — starred items: code export
  to JUCE/CLAP snippets, layer-aware animation, value-arc generator, boolean triggers
  P2, design-system linter, frame-budget optimizer, web/WASM export). Open-sourced
  under **MIT**: `LICENSE`, public `README.md` with shields.io badges, `CONTRIBUTING.md`,
  `.github/workflows/ci.yml` (build + test on every push/PR), `.github/ISSUE_TEMPLATE/`
  (`bug_report.md` + `feature_request.md`), `.github/pull_request_template.md`; GitHub
  repo About sidebar set. Website: `StripKit-Website/README.md` added (site maintenance
  guide); OSS badge repositioned below/centred under hero screenshot. Audit code fixes:
  `BatchViewModel` `CancellationTokenSource` disposal fixed + `ComponentType.Meter` added
  to the type list (BUG-005/007); `MainWindow` `_playTimer` stopped on `Closed` event
  (BUG-006); `FilmstripEngine.cs` MIT header added; `Assets/README.txt` corrected
  (JetBrains Mono reference removed); `docs/KICKOFF.md` root test count corrected
  41→49. **49/49 green throughout.** **Next steps:** deploy website to **stripkit.pro**;
  add **code-signing cert** (clears VirusTotal FPs + SmartScreen); expose Meter
  settings in the Batch tab template UI (`ComponentType.Meter` is registered but the
  template fields are not yet surfaced); vNext features per `docs/ROADMAP.md`.
- **2026-06-04 (v0.6.0 shipped — Inno pipeline + website)** — Replaced **Velopack** with
  an **Inno Setup** installer (`installer/StripKit.iss`: per-user — `PrivilegesRequired=lowest`,
  `{autopf}`; choose-dir; optional desktop + Start-Menu shortcuts; registry-wiping
  uninstaller; both logos) and built the **3-stage release pipeline** per
  `@SOFTWARE_RELEASE.md`. **Stage 1** `scripts/Invoke-Release.ps1`: test-gate → bump
  `csproj`/`.iss`/`CHANGELOG` → publish → `ISCC` → stage `releases/latest/` → commit + tag
  + push. **Stage 2** `.github/workflows/auto-release.yml`: VirusTotal scan via the
  `VT_API_KEY` secret → the **sole** `gh release create`. `releases/latest/*.exe` is
  git-tracked (the CI trigger). **Shipped the first GitHub Release, v0.6.0**
  (`StripKit-Setup-0.6.0-x64.exe`, ~33.5 MB self-contained; live at
  `github.com/Vybecode-LTD/stripkit/releases/tag/v0.6.0`; VirusTotal ~4/71 heuristic
  false-positives — unsigned). Created + pushed the landing-page repo
  **`Vybecode-LTD/StripKit-Website`** (hero, features, GitHub-driven download, a
  **simplified** changelog in `updates.json` decoupled from the technical
  `docs/CHANGELOG.md`, Formspree contact, a VirusTotal shield) — **not yet deployed to
  stripkit.pro**. Fixed two release-tooling bugs: PS 5.1 `Get-Content` read the UTF-8
  CHANGELOG as ANSI → mojibake (fixed with `-Encoding UTF8`, `f1b68d3`); CI
  `gh release create --notes "..."` ran the changelog's backticks as shell
  command-substitution (fixed via env vars + `--notes-file` + `workflow_dispatch`,
  `a408bc9`). **49/49 green.** **Next step:** deploy the website to **stripkit.pro**; add a
  **code-signing cert** (clears the VirusTotal FPs + SmartScreen); per release, add a
  plain-language entry to the website's `updates.json` alongside `docs/CHANGELOG.md`.
- **2026-06-03 (alignment: Pin centre)** — Added a **"Pin centre to image"** button to
  the alignment guide (in the preview overlay): drag the crosshair to mark the knob
  centre, click **Pin** → it commits `SourceCenterX/Y` and exits guide mode so the
  preview flips from the raw source to the **re-centred result**. This resolves the
  owner's "as soon as I remove the crosshair it reverts" report — the centre value
  already persisted (a new VM test proves it survives toggling the guide); the gap was a
  missing explicit *apply* + no live feedback while marking on the raw source. +2 VM
  tests (`PinCenterCommand` keeps the mark & exits; centre persists across guide toggle).
  **50/50 green.** **Next step:** none pending on alignment; resume packaging/Release or
  Phase 8 when ready.
- **2026-06-03 (alignment tools + icon refresh)** — Fixed a reported off-centre knob
  **"wobble"**: the renderer now centres art on its detected **content centre** and
  rotates about that point (new `SourceCenterX/Y`; 0.5,0.5 preserves old output) instead
  of the image-rectangle centre. New `ContentAnalysis` auto-detects the opaque-content
  centre; the Create tab gains **Auto-center**, a **draggable crosshair guide** (over the
  raw source, in `ShowCenterGuide` mode), numeric **Center X/Y**, **Reset**, and
  **auto-centres knobs on load**; fader/slider caps get the same cross-centring. Mirrored
  in `FilmstripEngine.cs`. **48/48 green** (+7; existing golden baselines unchanged).
  Also swapped the app icon to the new **filmstrip-fader** art (`stripkiticon02.png` →
  contain-fit multi-res `Assets/stripkit.ico` + `stripkit.png` for window/taskbar/exe;
  installer picks it up via `vpk --icon`) and added **`brand/favicon.ico`** (16/32/48/64)
  for the future website. No system-tray icon exists (StripKit is a normal windowed app).
  **Next step:** publish the GitHub Release when ready (still unsigned → SmartScreen
  until a cert is added); Phase 8 landing page is on the roadmap.
- **2026-06-03 (packaging + icon + repo — v0.6.0)** — The Obsidian design is now
  **visually confirmed by the owner** (the prior "not yet confirmed by the agent"
  caveat is resolved) following the hover/focus + input-state polish (custom Button
  `ControlTheme` with `:pointerover` / `:pressed` / `:focus-visible`, brush + box-shadow
  transitions, slightly-rounded inputs, per-tab dividers with near-white subtitles).
  **Phase 7 packaging** under way: integrated **Velopack 1.1.1** (`VelopackApp.Run()`
  is the first line of `Main`); a **self-contained `win-x64` publish runs without the
  .NET SDK**; `vpk pack` builds an **unsigned installer** (`StripKit-win-Setup.exe`) +
  an update feed (`*-full.nupkg` + `RELEASES`). The app **icon** is wired from the
  supplied PNG → a hand-assembled **multi-resolution `.ico`** (16–256 px, 32-bit DIB —
  Skia can't encode ICO) into `<ApplicationIcon>` and the window (`Assets/stripkit.png`).
  In-app **GitHub auto-update** added (`UpdateService` → `github.com/Vybecode-LTD/stripkit`;
  checks on launch, applies on exit; **no-ops in dev/portable/tests**). New
  `docs/PACKAGING.md` documents the publish → `vpk pack` → GitHub Release → sign flow.
  The **git repo was initialized and pushed** to `https://github.com/Vybecode-LTD/stripkit`.
  Builds clean, **41/41 green**. **Next step:** publish a GitHub Release from the `vpk`
  output (`releases/`) so auto-update goes live; sign with `signtool` once a cert exists
  (shipping unsigned for now); then Phase 8 — the landing-page website (now on the roadmap).
  *(Superseded 2026-06-04: Velopack/`vpk` + in-app auto-update were replaced by the Inno
  Setup installer + GitHub-Release download pipeline; the website did ship.)*
- **2026-06-03 (design system)** — Applied the **Obsidian glassmorphism** design
  (the chosen one of two presented as rendered mockups): acrylic frosted window
  (`TransparencyLevelHint="AcrylicBlur"` + `ExperimentalAcrylicBorder`, with
  `FallbackColor` for non-acrylic platforms), translucent `Border.card` glass panels
  (hairline borders + soft shadows), the `#e8440a` accent kept, Fluent `accent`
  primary buttons, and **Verdana-led sans-serif** (replacing JetBrains Mono). Tokens
  centralized in `App.axaml`; restyled `MainWindow` / `ImporterView` / `BatchView`.
  **Styling-only** — renderer, view-models, and tests untouched. Builds 0/0, **41/41
  green**, boots clean. Glassmorphism confirmed legitimately applicable in Avalonia 11
  (one caveat: no stock per-panel backdrop blur — the frost is the window acrylic +
  translucent layers, identical-looking for this flat UI). **Not yet visually
  confirmed by the agent** (no GUI capture here) — run it to review and tune tokens
  (translucency/tint/radius in `App.axaml`).
- **2026-06-03** — Three things this session: **(1) Rename** FilmstripForge →
  **StripKit** across every doc and code file (brand text, namespaces,
  `RootNamespace`/`AssemblyName`, `app.manifest` identity, `avares://` URIs, the
  solution/project, and the folder → `src/StripKit/`, `StripKit.sln`,
  `StripKit.csproj`). Kept **"filmstrip"** as the domain noun (`FilmstripSettings`,
  `IFilmstripRenderer`, …) and left **VybeForge / VybeCod.ing** alone. Deleted the
  redundant `skills/FilmstripForge/` mirror; left the reusable `skills/*` generic.
  **(2) Phase 0 verified.** Fixed two *pre-existing* scaffold build blockers
  (unrelated to the rename): an illegal `--` in a `.csproj` XML comment, and a
  missing `Microsoft.Extensions.DependencyInjection` package reference. `dotnet
  build` now succeeds (0/0). The GUI boots, and a headless drive of the real
  `StripKit.Engine` confirmed the load → render → export round-trip (valid
  `80×5120` 64-frame strip, clean 270° sweep). **(3) Phase 1 done — drag-and-drop.**
  The preview `Border` is now a drop zone (`DragDrop.AllowDrop`, accent highlight on
  drag-over) per the `avalonia-drag-drop-files` skill; the code-behind handler only
  extracts the path and calls the new shared `MainWindowViewModel.LoadSourceFromPath`,
  which the "Load source image…" button now also uses (no duplicated load logic).
  Accepts the same image types as the file picker. Builds 0/0 and boots clean.
  **(4) Test project — Phase 4 brought forward.** `tests/StripKit.Tests` (xUnit +
  NSubstitute + FluentAssertions + Avalonia.Headless): 6 golden-image baselines
  (knob min/mid/max, an 8-frame strip, fader + slider mids — all visually
  reviewed), view-model load-path tests, and a headless test asserting the preview
  opts into drops. **11/11 green**, closing the Phase-1 drop-test gap (a synthetic
  OS drag gesture isn't constructable headlessly, so the drop is covered by the VM
  load-path + AllowDrop-wiring tests). **(5) Phase 2 done — filmstrip importer
  (second tab, confirmed).** New `FilmstripImporter` (SkiaSharp, no Avalonia)
  detects frame count/orientation/kind from a strip's dimensions per the
  `filmstrip-importer-engine` skill, flags ambiguous (square + adjacent-count)
  cases, extracts a frame, and re-stacks to a new orientation. New `ImporterView`
  UserControl + `ImporterViewModel` (exposed as `MainWindowViewModel.Importer`),
  hosted in a `TabControl` (**Create** | **Import**), each tab with its own drop
  zone. Verified: 19/19 tests green (added importer-engine + importer-VM suites),
  the app boots with both tabs, and a headless drive detected a 64×6500 strip as
  100 frames and sliced + re-stacked it correctly. **(6) Phase 3 done — manifest
  export.** The Create-tab export has an "Also write a skin.json manifest" toggle +
  a parameter-id field; on export it writes a schema-valid `<name>.skin.json` next
  to the PNG via the new `ManifestService` (System.Text.Json, camelCase) per the
  `plugin-asset-manifest` skill. New `SkinManifest`/`ManifestControl`/`ManifestBounds`
  models. Verified: **25/25 tests green** (added manifest mapping + JSON-Schema
  conformance tests), app boots with the manifest UI, and a headless drive emitted a
  valid one-control `skin.json` (knob, relative `asset`/`asset2x`, base-resolution
  `bounds`). **(7) Full doc reconciliation + codebase audit (v0.3.0).** Rewrote README
  for the two-tab app + manifest + tests; created `docs/ARCHITECTURE.md` (deep dive),
  `TESTING.md`, `CHANGELOG.md`, `BUGS.md`, `HANDOFF.md`, `AUDIT-LOG.md`; made the root
  `KICKOFF.md` a pointer to `docs/KICKOFF.md`; documented the standalone
  `FilmstripEngine.cs`. Audit (naming, MVVM boundary, conventions, DI, compiled
  bindings, engine sync) clean; 0 open bugs. **(8) Phase 5 done — batch processing
  (third tab, v0.4.0).** `BatchProcessor` (SkiaSharp, no Avalonia) runs the whole loop
  via `Task.Run` (off the UI thread), reports per-item progress, and cancels between
  items without throwing; per-file failures are isolated. New `BatchModels`,
  `BatchViewModel`, `BatchView`; added `IFileDialogService.OpenFolderAsync`. Verified:
  **31/31 tests green**, app boots with three tabs. **(9) Phase 6 done — meter mode
  (v0.5.0, design signed off first).** New `ComponentType.Meter` + `MeterFillDirection`
  + meter fields on `FilmstripSettings`. `RenderMeterFrame` does procedural segment
  bars (On/Off colour + gap) when no art is loaded, or a layered reveal (source = on
  art over the background off art) when art is present — auto-selected; all four fill
  directions; discrete default + continuous toggle. Create-tab "Meter" type + METER
  settings; a procedural meter previews/exports without a source; manifest maps
  `"meter"`. Mirrored in `FilmstripEngine.cs`. Verified: **41/41 green** (9 renderer
  tests incl. 5 reviewed golden baselines + 1 VM test), app boots with the meter UI.
  **Next step:** Phase 7 — packaging (signed single-file Windows build); confirm the
  distribution target first. (Importer frame-count *resampling*, multi-control
  manifests, and meter peak-hold/stereo remain unbuilt.)
- **2026-06-02** — Packaged the repo for Claude Code handoff: added `docs/`
  (`KICKOFF.md`, `SOURCE_MAP.md`, `ROADMAP.md`), embedded six project skills in
  `.claude/skills/`, and refreshed this file. The v1 scaffold is complete and
  cross-checked but **not yet compiled on a real machine** (no .NET SDK in the
  authoring environment). **Next step:** Phase 0 — verify `dotnet run` builds and
  runs; then Phase 1 — drag-and-drop input per `docs/KICKOFF.md`. Not yet built:
  drag-drop, importer, manifest export, tests, batch, meter mode, packaging.
