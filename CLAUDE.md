# CLAUDE.md — StripKit

> Version 1.2.1 · last-updated 2026-06-14 · last-audit 2026-06-14

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
  14.13.1 (Apache-2.0, PSD/PSB layers). Both permissive; not in `FilmstripEngine.cs`.
- AI SVG generation (Generate tab, app-only): OpenAI / Gemini / Claude via their text APIs over a
  shared `HttpClient`; user API keys encrypted at rest with **System.Security.Cryptography.ProtectedData**
  9.0.0 (Windows DPAPI). Not in `FilmstripEngine.cs`.
- MVVM + DI (Microsoft.Extensions.DependencyInjection), compiled bindings.
- Tests: xUnit + NSubstitute + FluentAssertions, `Avalonia.Headless` for view
  tests, golden-image regression for the renderer (`tests/StripKit.Tests`). **171 green.**
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
`installer/StripKit.iss`, and `docs/CHANGELOG.md` (promote `## [Unreleased]` → `## [X.Y.Z]`)
→ `dotnet publish` → **sign the exe + installer** (Azure Trusted Signing via signtool + the
`Microsoft.Trusted.Signing.Client` dlib — NOT AzureSignTool, which 403s) → `ISCC` builds
`releases/latest/StripKit-Setup-<ver>-x64.exe` → commit + tag `vX.Y.Z` + push. **Stage
2** `.github/workflows/auto-release.yml` (triggered by the tracked `releases/latest/*.exe`,
or `workflow_dispatch`): VirusTotal scan (`VT_API_KEY` secret) → the **sole**
`gh release create` (notes via `--notes-file`, never inline `--notes`). **Stage 3** the
website reads the live GitHub Release (refine `updates.json` via `Publish-WebsiteChangelog.ps1`).
**Release integrity:** the "Release" commit stages only version files + the installer by design —
so **commit the feature work first** (the v1.2.0 source was once orphaned because it wasn't).

## Architecture (one idea, five component types) — full detail in `docs/ARCHITECTURE.md`

Every render is: *for each of N frames, place the source art inside a fixed frame
cell under a per-frame transform, then stack the cells into one PNG.* The five
component types are knob, vertical fader, horizontal slider, **meter**
(progressive segment fill), and **button** (discrete state frames — off/on/…). The app is a
`TabControl` with **five** tabs — **Create**
(make a strip), **Import** (re-use / re-slice / resample one), **Batch** (a whole folder at
once), **Skin** (assemble a multi-control `skin.json`), and **Generate** (AI-generate layered
control art from your own OpenAI / Gemini / Claude key, then hand it to Create).

- `Models/` — pure data, no UI/Skia deps: `FilmstripSettings` (render contract),
  `FrameTransform`, `StripDetection` (importer output),
  `SkinManifest`/`ManifestControl`/`ManifestBounds`, `BatchModels`, `CodeModels`,
  `RenderLayer` (`LayerBehavior` {Static, Rotate, **Frame**} + per-layer pivot — the layered-knob /
  button stack), the `ComponentType` ({RotaryKnob, VerticalFader, HorizontalSlider, Meter,
  **Button**}) / `StackDirection` / `MeterFillDirection` enums.
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
- `Services/BatchProcessor.cs` — render a folder of sources into many strips off the
  UI thread (`Task.Run`), with per-item progress and a working cancel.
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
  `UseInCreateRequested` handoff. Persists provider/model prefs + the encrypted key.
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
  raw-response expander on the right). Code-behind: clipboard copy + colour-picker flyout handlers.
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

- **Obsidian design system** (glassmorphism, dark): accent `#e8440a`, **sans-serif
  only** — `Verdana, Segoe UI, Arial, sans-serif` (no monospace). Design tokens live
  in `App.axaml` (`AccentBrush`/`AccentHiBrush`, `Text1/2/3Brush`, `GlassFill/GlassBorder`,
  the `ObsidianAcrylic` material, the `SectionHeader` control theme). The window uses
  `TransparencyLevelHint="AcrylicBlur"`
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

- **2026-06-14 (audit + orphaned-v1.2.0-source recovery + the 1.2.1 fix wave + full doc reconcile)** —
  A correctness + release-integrity session, two commits on `main`. **(1) Recovered an orphaned
  release.** The "Release v1.2.0" commit (`70cf259`) staged **only** the version files + the installer
  — the v1.2.0 **feature source was never committed**, so the live `v1.2.0` tag could not rebuild its
  own installer. Committed that source as-is (matching the shipped binary) in **`b55380f`** before
  fixing forward (`ComponentType.Button` + `LayerBehavior.Frame`; Generate's four control types + the
  button off/on prompt + colour-picker flyouts; the renderer + `FilmstripEngine.cs` button state-frame
  path; the importer's `off`/`on`→`Frame` mapping; supporting wiring). **(2) The 1.2.1 fix wave**
  (`80dc1b5`, staged under CHANGELOG `## [Unreleased]`): the **Generate→Create handoff now honours the
  generated control type** (was hard-forced to `RotaryKnob`, so generated faders/sliders rotated and
  buttons stacked both states) — knob → body+pointer, button → off/on `Frame` layers, fader/slider →
  flattened single source; **hardened untrusted-SVG XML parsing** via new `Services/SafeXml.cs`
  (`DtdProcessing.Prohibit`, no resolver, `MaxCharactersFromEntities = 0`) applied to **both**
  `SvgSanitizer` and the layered-file import picker (closes billion-laughs DoS + external-entity); added
  the missing `BindingPlugins.DataValidators.RemoveAt(0)` + a Generate structure warning (knob with no
  pointer / button missing a state). New files: `SafeXml.cs`, `Helpers/HexToColorBrushConverter.cs`,
  `Controls/SectionHeader.cs`, `tests/StripKit.Tests/GenerateIntegrationTests.cs`. **Suite 157→171
  green; build 0/0.** **(3) Full doc reconcile** — every managed doc stamped 1.2.1 / 2026-06-14;
  CHANGELOG `[Unreleased]`; HANDOFF rewritten (five tabs, 171, the recovery, ship 1.2.1); AUDIT-LOG
  entry; BUGS-008/009 retro-logged; SOURCE_MAP/ARCHITECTURE/README/TESTING/ROADMAP/KICKOFF updated for
  five tabs + Button/Frame + the new files + the test count. **csproj `<Version>` is still 1.2.0 — the
  release script bumps it to 1.2.1; do not hand-edit.** Working tree's only untracked strays:
  `docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json` (not ours). **Next:** ship v1.2.1; then the
  website P2 getting-started guide; Generate fader/slider/meter polish; `checkout@v4→v5`.
- **2026-06-07 (Generate tab — AI-generated SVG control art)** — Built a new **fifth tab** that uses
  the user's **own** OpenAI / Gemini / Claude API key to generate a **layered knob SVG** (a static
  `<g id="body">` + a separate `<g id="pointer">`) as filmstrip source art — "exactly like the
  starter knob," but generated. Scoped the forks with the owner first (all four recommended taken):
  **all three providers** behind one interface, **layered** body+pointer SVG, **DPAPI-encrypted**
  keys, **knob-first**. The key insight: a generated SVG drops straight into the **existing**
  `LayeredImportService` → renderer layer stack, so **no renderer change** and **nothing mirrored
  into `FilmstripEngine.cs`**. New app-only pieces: `IAssetGenerationService`/`AssetGenerationService`
  (builds the StripKit-aware prompt — square canvas, ~10% rotation margin, body+pointer groups,
  pointer at 12 o'clock — then dispatches + sanitizes); `IAssetGenerationProvider` + `ClaudeProvider`
  (Messages) / `OpenAiProvider` (Chat Completions) / `GeminiProvider` (generateContent) over a shared
  DI `HttpClient`, each with friendly non-2xx errors; `SvgSanitizer` (carves the `<svg>` out of a
  chatty/fenced reply, strips script/`<image>`/`<foreignObject>`/event-handlers/off-document href —
  pure `System.Xml.Linq`); `ISecretStore`/`DpapiSecretStore` (per-provider keys encrypted at rest via
  Windows DPAPI → `%APPDATA%/StripKit/secrets.dat`, ciphertext only). `GenerateViewModel` +
  `GenerateView` (Obsidian-styled, mirrors Create/Skin) **validate by importing the SVG** — the
  preview is the real imported result, so what you see imports — then **"Use in Create"** jumps to
  Create and runs the shared `ImportLayeredFromPathAsync`; **Save SVG** / **Copy SVG** too. New dep:
  `System.Security.Cryptography.ProtectedData` 9.0.0. DI: providers + service + secret store as
  singletons (+ the `HttpClient`), `GenerateViewModel` transient; a **Generate** tutorial walkthrough
  added. **+27 tests, suite 125→152 green; build 0/0; app boots clean.** *(Shipped as v1.1.0; the
  centering fix took the suite to 157. Faders/sliders/buttons + colour pickers followed in v1.2.0.)*
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
  script-BOM guard), SOURCE_MAP.
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
  **No renderer/engine changes** (not in `FilmstripEngine.cs`). DI: `ISettingsService`/`IAssetService`
  singletons + `TutorialViewModel` transient. **+11 tests, suite 112→123 green.**
- **2026-06-06 (vNext ★ #3, step 3 — layered PSD/SVG import; completes the layer-aware bet)** —
  The final ★ piece, scoped with the owner before building. A real layered source is now imported
  and mapped onto the renderer's existing layer stack: **"Import layered file (SVG / PSD)…"** in the
  Create-tab layered panel. New **app-only** `Services/LayeredImportService.cs` (`ILayeredImportService`)
  parses **SVG** groups via **Svg.Skia** (MIT) and **PSD/PSB** layers via **Magick.NET-Q8-x64**
  (Apache-2.0). Each `ImportedLayer` carries a **name-guessed behaviour** (pointer/needle/indicator…
  → Rotate, else Static) the user overrides per layer. **No renderer change** (it already composites
  an N-layer stack) → **NOT mirrored in `FilmstripEngine.cs`**; gated behind defaults so every prior
  golden is byte-identical. Deps added: `Svg.Skia` 5.0.0, `Magick.NET-Q8-x64` 14.13.1 (SkiaSharp
  3.119.0→**3.119.2** for Svg.Skia's floor); the win-x64 installer grows ~22 MB. **+14 tests, suite
  98→112 green.**
- **2026-06-05 (vNext ★ #3, step 2 — auto-pointer extraction; + session handoff)** — Built the
  second of the three layer-aware steps. An **"Auto-extract from flat knob…"** button (Create-tab
  layered panel) splits a single FLAT knob image into the base + pointer slots automatically. New
  `Services/PointerExtractor.cs` uses the **radial-symmetry residual**. Pure SkiaSharp like
  `ContentAnalysis`; **app-only — NOT mirrored in `FilmstripEngine.cs`**. +4 tests, suite 94→98.
- **2026-06-05 (v0.8.0 shipped — 3 finish-the-gaps features + layer-aware step 1)** — Cut **v0.8.0**:
  **(1) Batch-tab meter settings** (`e126daf`) + a **"source is a backdrop"** toggle. **(2) Skin tab**
  (`4a9e2ac`) — a multi-control `skin.json` builder; new `IManifestService.BuildManifest` +
  `SkinViewModel`/`SkinControlEntry`/`SkinView`. **(3) Importer resampling** (`322a80d`) — re-time a
  strip to a new frame count (`FilmstripImporter.Resample`, nearest-frame). Plus ★ layer-aware step 1
  (base + pointer; `RenderLayer`/`LayerBehavior`/`RenderLayers`, mirrored in `FilmstripEngine.cs`).
  Suite 72→94; build 0/0.
- **2026-06-04 (vNext ★ #1 + #2 + v0.7.0)** — ★ **value-arc / fill-ring** (knob `RenderValueArc`, 11
  Skia-free arc fields gated on `ShowValueArc`, mirrored in `FilmstripEngine.cs`) + ★ **code / component
  export** (pure `CodeSnippetService` → JUCE / CSS-HTML / iPlug2 / HISE). Shipped as v0.7.0. Suite 49→72.
- **2026-06-04 (documentation overhaul + audit + OSS hardening)** — Rewrote ARCHITECTURE / PACKAGING /
  ROADMAP from source-verified content; open-sourced under **MIT** (LICENSE, README badges, CONTRIBUTING,
  `ci.yml`, issue/PR templates). Audit fixes: `BatchViewModel` CTS disposal + `ComponentType.Meter` in the
  batch list (BUG-005/007); `MainWindow` `_playTimer` stop on `Closed` (BUG-006). 49/49 green.
- **2026-06-03 — earlier sessions** — alignment tools (content-centre pivot, crosshair); the Obsidian
  glassmorphism design; v0.6.0 (Inno pipeline + website, replacing Velopack); Phases 0–6 (verify,
  drag-drop, importer, manifest, tests, batch, meter); and the FilmstripForge → StripKit rename. See
  `docs/CHANGELOG.md` and `docs/AUDIT-LOG.md` for the full history.
- **2026-06-02** — Packaged the v1 scaffold for a Claude Code handoff (`docs/`, six project skills,
  initial `CLAUDE.md`); not yet compiled at that point.
