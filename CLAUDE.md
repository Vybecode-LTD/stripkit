# CLAUDE.md — StripKit

> Version 0.6.0 · last-updated 2026-06-03 · last-audit 2026-06-03

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
   `docs/AUDIT-LOG.md`.
4. The skills in `.claude/skills/` are scoped to this repo — use them.

## Stack

- .NET 9, Avalonia 11.3, CommunityToolkit.Mvvm 8.4 (source generators),
  SkiaSharp 3.119.
- MVVM + DI (Microsoft.Extensions.DependencyInjection), compiled bindings.
- Tests: xUnit + NSubstitute + FluentAssertions, `Avalonia.Headless` for view
  tests, golden-image regression for the renderer (`tests/StripKit.Tests`).

## Run / build

- `dotnet run --project src/StripKit` (needs the .NET 9 SDK).
- If NuGet warns of a SkiaSharp version conflict with Avalonia, align the
  `SkiaSharp` version in the csproj to Avalonia's transitive one
  (`dotnet list package --include-transitive`).
- Use `python -m pip` style invocation in any Python helper scripts (bare
  `pip`/`python` are not on PATH in this environment).

## Architecture (one idea, four component types) — full detail in `docs/ARCHITECTURE.md`

Every render is: *for each of N frames, place the source art inside a fixed frame
cell under a per-frame transform, then stack the cells into one PNG.* The four
component types are knob, vertical fader, horizontal slider, and **meter**
(progressive segment fill). The app is a `TabControl` with three tabs — **Create**
(make a strip), **Import** (re-use one), and **Batch** (a whole folder at once).

- `Models/` — pure data, no UI/Skia deps: `FilmstripSettings` (render contract),
  `FrameTransform`, `StripDetection` (importer output),
  `SkinManifest`/`ManifestControl`/`ManifestBounds`, `BatchModels`, the
  `ComponentType`/`StackDirection`/`MeterFillDirection` enums.
- `Services/SkiaFilmstripRenderer.cs` — **the heart.** `ComputeTransform` does the
  rotary/linear math; `RenderFrame` composites one frame with supersampling +
  Mitchell cubic resampling (meters fill segments via `RenderMeterFrame` — procedural
  or layered on/off-art reveal); `RenderStrip` blits frames into the stacked PNG.
- `Services/FilmstripImporter.cs` — detect an existing strip's layout from its
  dimensions, extract a frame, re-stack orientation (no Avalonia dep).
- `Services/ManifestService.cs` — build + serialize a `skin.json` (System.Text.Json).
- `Services/BatchProcessor.cs` — render a folder of sources into many strips off the
  UI thread (`Task.Run`), with per-item progress and a working cancel.
- `Services/ImageLoadService.cs` / `ExportService.cs` — decode/encode PNG ↔ `SKBitmap`.
- `Services/FileDialogService.cs` — open/save pickers (app-layer; holds the Window).
- `Helpers/SkiaImageInterop.cs` — `SKBitmap` -> Avalonia `Bitmap` for preview.
- `ViewModels/MainWindowViewModel.cs` — Create-tab state + commands; a single
  `OnPropertyChanged` funnel refreshes the preview. Exposes `Importer` and `Batch`.
- `ViewModels/ImporterViewModel.cs` — Import-tab state + commands (same funnel).
- `ViewModels/BatchViewModel.cs` — Batch-tab state + commands (folders, template,
  run/cancel, progress); no preview funnel.
- `Views/MainWindow.axaml(.cs)` — the `TabControl`; code-behind holds the auto-play
  timer + the Create preview's drag-drop handlers.
- `Views/ImporterView.axaml(.cs)` — the Import tab UserControl + its drop handlers.
- `Views/BatchView.axaml(.cs)` — the Batch tab UserControl (folder pickers, template,
  Run/Cancel, progress bar, results).
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
