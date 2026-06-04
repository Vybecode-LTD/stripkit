# StripKit

A C#/Avalonia 11 desktop tool that turns a single transparent PNG into an animated
**filmstrip** (sprite sheet) for audio-plugin GUI controls — rotary knobs, vertical
faders, horizontal sliders, and segment meters. Feed it your knob/fader art and it produces a
stacked PNG that a JUCE-style `LookAndFeel` reads one frame at a time per parameter
value. It also **imports** existing strips (KnobMan exports, purchased packs) and can
emit a **`skin.json` manifest** that binds a strip to a plugin parameter.

It is the asset-production companion to the GUI skinning system / VybeForge.

## Requirements

- **.NET 9 SDK** — https://dotnet.microsoft.com/download
- Windows 11 (primary target). Builds and runs on macOS/Linux too (Avalonia).

## Run

```bash
dotnet run --project src/StripKit
```

First run restores NuGet packages (Avalonia 11.3, CommunityToolkit.Mvvm 8.4,
SkiaSharp 3.119, Microsoft.Extensions.DependencyInjection 9.0).

> If NuGet warns about a **SkiaSharp version conflict** with Avalonia, run
> `dotnet list package --include-transitive`, find the SkiaSharp version Avalonia
> pulls, and set the `SkiaSharp` `Version` in `src/StripKit/StripKit.csproj` to match.

## Build a release / single-file exe (Windows)

```bash
dotnet publish src/StripKit -c Release -r win-x64 ^
  -p:PublishSingleFile=true -p:PublishSelfContained=true
```

The exe lands in `src/StripKit/bin/Release/net9.0/win-x64/publish/`.

## Run the tests

```bash
dotnet test                       # 49 tests: renderer golden-image, VM, importer, manifest, batch, meter, alignment
UPDATE_BASELINES=1 dotnet test    # regenerate golden-image baselines (review before committing)
```

See [`docs/TESTING.md`](docs/TESTING.md) for the full test inventory and the
golden-image baseline workflow.

## The app: three tabs

### Create — single image → animated strip

1. **Load source image** (button, or **drag-and-drop a PNG onto the preview**) —
   your transparent PNG. For a knob this is the cap/pointer art drawn pointing
   straight up (12 o'clock); for a fader/slider it is the moving cap.
2. **Component type** — RotaryKnob, VerticalFader, HorizontalSlider, or **Meter**.
3. **Frames** — 32 / 64 / 128 quick buttons, or any value. 64 is standard.
4. **Frame size** — the size of one cell. "Match frame to source size" squares the
   frame to a knob's art; for a fader/slider the frame is the full track area.
5. **Rotary:** sweep (270° default), clockwise/counter-clockwise, optional pivot
   nudge. **Linear:** edge margin and cap cross-axis offset. **Meter:** segment count,
   fill direction (up/down/left/right), a continuous toggle, and On/Off colours
   (procedural) — or load a source (on-state) and a background (off-state) for a
   layered meter. A procedural meter needs no source image.
6. **Quality & output:** Supersample (1/2/4/8 — 4 is a good default), vertical or
   horizontal stacking, optional `@2x` HiDPI export.
7. **Manifest (optional):** tick "Also write a skin.json manifest" and set a
   **Parameter id**; export then writes `<name>.skin.json` next to the PNG.
8. **Preview** — drag the slider (or press Play) to watch the control animate.
9. **Export** — writes `name_64frames.png`, plus `name_64frames@2x.png` and
   `name_64frames.skin.json` when those toggles are on.

### Import — re-use an existing strip

1. **Load filmstrip** (button or drop) — an existing strip you did not make.
2. StripKit **detects** the frame count, frame size, orientation, and control kind
   from the image dimensions, and flags ambiguous cases. The detected count is
   **editable** (detection is a guess — verify it).
3. **Scrub** to confirm the control sweeps min → max.
4. **Extract current frame** to a PNG, or **Re-stack (flip orientation)** to write
   the whole strip vertical↔horizontal.

### Batch — a whole folder at once

1. **Choose input folder** — a folder of source images (PNG/WebP/BMP/JPG).
2. **Choose output folder** — where the strips are written.
3. Set the **render template** (component type, frames, frame size, rotary sweep,
   supersample, stacking) plus "Square knob frames to each source", `@2x`, and a
   per-strip `skin.json` toggle.
4. **Run batch** — exports `name_<frames>frames.png` (+ `@2x` / `.skin.json`) for each
   source, off the UI thread, with a progress bar and a per-file readout. A bad file is
   skipped and reported; the run continues. **Cancel** stops cleanly between files.

## Notes on quality

- Rotation aliases hard edges, so each frame is rendered into an oversampled surface
  and downsampled once with a Mitchell cubic resampler — that is what keeps a rotated
  knob crisp at every angle.
- Give knob art roughly a 10% transparent margin so its corners don't clip as it
  rotates within the square frame.
- Exported PNGs are 32-bit RGBA with a transparent background — the plugin's
  `paint()` draws the control's background; the filmstrip draws only the control.

## Project layout

```
StripKit.sln
FilmstripEngine.cs                     standalone, copy-paste portable renderer (not compiled by the app)
src/StripKit/
  Program.cs, App.axaml(.cs)           entry + composition root (DI)
  Models/                              FilmstripSettings, FrameTransform, StripDetection,
                                       SkinManifest, BatchModels, ComponentType,
                                       StackDirection, MeterFillDirection
  Services/                            renderer, importer, manifest, batch, image load, file dialog, export
  Helpers/SkiaImageInterop.cs          SKBitmap -> Avalonia Bitmap (preview)
  ViewModels/                          MainWindowViewModel (Create) + ImporterViewModel (Import)
                                       + BatchViewModel (Batch)
  Views/                               MainWindow (TabControl) + ImporterView + BatchView
tests/StripKit.Tests/                  xUnit: renderer golden-image, VM, importer, manifest, batch, meter
docs/                                  ARCHITECTURE, SOURCE_MAP, ROADMAP, TESTING, CHANGELOG,
                                       BUGS, HANDOFF, AUDIT-LOG, KICKOFF
```

The renderer (`Services/SkiaFilmstripRenderer.cs`), importer (`FilmstripImporter`),
and manifest (`ManifestService`) have **no Avalonia dependency**, so they can be
reused in a CLI, a build step, or another app unchanged.

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — how every piece fits together (deep dive).
- [`docs/SOURCE_MAP.md`](docs/SOURCE_MAP.md) — file-by-file map of the repo.
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — phased plan and status.
- [`docs/TESTING.md`](docs/TESTING.md) — test inventory, frameworks, baselines.
- [`docs/CHANGELOG.md`](docs/CHANGELOG.md) · [`docs/BUGS.md`](docs/BUGS.md) ·
  [`docs/HANDOFF.md`](docs/HANDOFF.md) · [`docs/AUDIT-LOG.md`](docs/AUDIT-LOG.md).
- `CLAUDE.md` — context and conventions for AI coding sessions.
