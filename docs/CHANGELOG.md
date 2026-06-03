# CHANGELOG — StripKit

> Version 0.5.0 · last-updated 2026-06-03 · last-audit 2026-06-03
>
> Notable changes per doc/feature version. Dates are authoring dates; several
> versions landed on 2026-06-03 across one working stretch.

## [Unreleased] — Obsidian design system

### Changed
- New **glassmorphism UI** (Obsidian, chosen from two rendered mockups): acrylic
  frosted window (`TransparencyLevelHint="AcrylicBlur"` + `ExperimentalAcrylicBorder`,
  `FallbackColor` for non-acrylic platforms), translucent `Border.card` glass panels
  (hairline borders + soft shadows), the `#e8440a` accent kept, Fluent `accent` primary
  buttons, and a **Verdana-led sans-serif** font (replaces JetBrains Mono). Design
  tokens centralized in `App.axaml`; `MainWindow` / `ImporterView` / `BatchView`
  restyled. **Styling-only** — renderer, view-models, and tests untouched (41/41 green).
  Pending on-screen review + token tuning (translucency/tint/radius).

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
