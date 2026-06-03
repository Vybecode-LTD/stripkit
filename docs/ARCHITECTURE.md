# ARCHITECTURE — StripKit

> Version 0.5.0 · last-updated 2026-06-03 · last-audit 2026-06-03
>
> The deep, file-and-flow reference for how StripKit is built. `docs/SOURCE_MAP.md`
> says *where* things live; this says *how and why* they work. Read alongside
> `CLAUDE.md` (conventions) and the phase docs in `docs/ROADMAP.md`.

---

## 1. What StripKit is

StripKit turns a single transparent PNG into an animated **control filmstrip**
(sprite sheet) for audio-plugin GUIs — rotary knobs, vertical faders, horizontal
sliders — and the reverse: it **imports** an existing strip, detects its layout,
and extracts/re-stacks it. It can also emit a **`skin.json` manifest** that binds a
strip to a plugin parameter for a data-driven skinning engine / JUCE `LookAndFeel`.

### The one idea

Every render reduces to: **for each of N frames, place the source art inside a
fixed-size frame cell under a per-frame transform, then stack the cells into one
PNG.** Knobs rotate the art about a pivot; faders/sliders translate the art (the
"cap") along an axis. Supersampling + a Mitchell cubic resampler keep rotated edges
crisp.

---

## 2. Solution & project structure

```
StripKit.sln                  → one app project + one test project
FilmstripEngine.cs            → standalone portable renderer (NOT in the build; see §12)
src/StripKit/StripKit.csproj  → the app (WinExe, net9.0)
tests/StripKit.Tests/         → xUnit test project (references the app)
```

**`StripKit.csproj` key settings:** `OutputType=WinExe` (no console window on
Windows), `net9.0`, `Nullable=enable`, `ImplicitUsings=enable`,
`AvaloniaUseCompiledBindingsByDefault=true`, `RootNamespace`/`AssemblyName=StripKit`,
`ApplicationManifest=app.manifest` (per-monitor-v2 DPI).

**Packages:** Avalonia 11.3 (+ Desktop, Themes.Fluent, Fonts.Inter, Diagnostics
[Debug-only]), CommunityToolkit.Mvvm 8.4 (source generators),
Microsoft.Extensions.DependencyInjection 9.0, SkiaSharp 3.119.

---

## 3. Layered architecture

```
Views (.axaml + thin code-behind)        ← Avalonia, XAML, compiled bindings
   │  bindings / commands
ViewModels (CommunityToolkit.Mvvm)        ← NO Avalonia UI types (preview Bitmap excepted)
   │  service interfaces (DI)
Services (engine + I/O)                   ← SkiaSharp; renderer/importer/manifest have NO Avalonia dep
   │
Models (pure data)                        ← no UI, no Skia
```

Dependencies point downward only. The renderer, importer, and manifest builder are
pure (SkiaSharp + BCL), so they are unit-testable and host-agnostic. The dialog
service is the one service that touches Avalonia (it owns a `Window` for pickers).

### 3.1 `Models/` — pure data, no UI/Skia deps

| File | Purpose |
|------|---------|
| `ComponentType.cs` | enum `RotaryKnob`, `VerticalFader`, `HorizontalSlider`, `Meter`. |
| `StackDirection.cs` | enum `Vertical`, `Horizontal`. |
| `MeterFillDirection.cs` | enum `Up`, `Down`, `LeftToRight`, `RightToLeft` (meter fill). |
| `FrameTransform.cs` | `readonly record struct`: per-frame placement in 1× frame units — `TranslateX/Y`, `DrawWidth/Height`, `RotateDegrees`, `PivotX/Y`. |
| `FilmstripSettings.cs` | the full render contract: `ComponentType`, `FrameCount`, `FrameWidth/Height`, `Start/EndAngleDegrees`, `PivotOffsetX/Y`, `EdgeMargin`, `CapCrossOffset`, `Supersample`, `StackDirection`, plus meter fields (`SegmentCount`, `FillDirection`, `ContinuousFill`, `SegmentGap`, `OnColorArgb`/`OffColorArgb`). Has `Clone()`. |
| `StripDetection.cs` | `record`: the inferred layout of an *existing* strip — `Vertical`, `FrameCount`, `FrameWidth/Height`, `Kind` (`ComponentType?`), `LowConfidence`, `CandidateCounts`. Helpers `Direction`, `KindLabel`. |
| `SkinManifest.cs` | `SkinManifest` / `ManifestControl` / `ManifestBounds` records — the `skin.json` schema (see §8). |
| `BatchModels.cs` | `BatchOptions`, `BatchProgress`, `BatchItemResult`, `BatchResult` — batch run inputs / progress / outcome (see §8.1). |

### 3.2 `Services/` — the engine and I/O

| File | Purpose | Avalonia? |
|------|---------|-----------|
| `IFilmstripRenderer` / `SkiaFilmstripRenderer` | **the heart**: `ComputeTransform`, `RenderFrame`, `RenderStrip`. | No |
| `IFilmstripImporter` / `FilmstripImporter` | detect layout, `ExtractFrame`, `Restack`. | No |
| `IManifestService` / `ManifestService` | `BuildSingleControl`, `Serialize`, `SaveAsync`. | No |
| `IBatchProcessor` / `BatchProcessor` | render a folder of sources → many strips off-thread, with progress + cancel (see §8.1). | No |
| `IImageLoadService` / `ImageLoadService` | decode a file → `SKBitmap` (premultiplied RGBA). | No |
| `IExportService` / `ExportService` | encode an `SKBitmap` → PNG file. | No |
| `IFileDialogService` / `FileDialogService` | open-image / save-PNG / open-folder pickers via `IStorageProvider`. Holds the `Owner` window. | **Yes** |

### 3.3 `Helpers/`

`SkiaImageInterop.ToAvaloniaBitmap(SKBitmap)` — encodes the bitmap to PNG in memory
and wraps it as an Avalonia `Bitmap` for binding to an `Image`. Fine for
preview-sized frames; a `WriteableBitmap` direct-pixel path is the upgrade if
playback ever needs it (see the `avalonia-skia-interop` skill).

### 3.4 `ViewModels/`

- `ViewModelBase` — `ObservableObject` base; never references Avalonia UI types.
- `MainWindowViewModel` — the **Create** tab (§6). Exposes `Importer` and `Batch`.
- `ImporterViewModel` — the **Import** tab (§7).
- `BatchViewModel` — the **Batch** tab (§8.1). No preview funnel; runs off-thread.

Both are `partial` (CommunityToolkit source generators require it) and use
`[ObservableProperty]` / `[RelayCommand]`. The only Avalonia reference is
`using AvBitmap = Avalonia.Media.Imaging.Bitmap;` for the preview image — a media
type, not a control/visual. Logic, files, and domain state stay out of code-behind.

### 3.5 `Views/`

- `MainWindow.axaml(.cs)` — a `TabControl` with **Create** (inline), **Import** (hosts
  `ImporterView`), and **Batch** (hosts `BatchView`). Code-behind holds only the
  auto-play `DispatcherTimer` and the Create preview's drag-drop handlers.
- `ImporterView.axaml(.cs)` — the Import tab `UserControl` (`x:DataType` =
  `ImporterViewModel`) + its own drag-drop handlers.
- `BatchView.axaml(.cs)` — the Batch tab `UserControl` (`x:DataType` =
  `BatchViewModel`); markup-only code-behind (no drag-drop — it works on folders).

All bindings are compiled (`x:DataType` on every view).

---

## 4. Composition root & dependency injection

`App.axaml.cs > OnFrameworkInitializationCompleted` is the single composition root:

```csharp
services.AddSingleton<IImageLoadService, ImageLoadService>();
services.AddSingleton<IFilmstripRenderer, SkiaFilmstripRenderer>();
services.AddSingleton<IFilmstripImporter, FilmstripImporter>();
services.AddSingleton<IManifestService, ManifestService>();
services.AddSingleton<IBatchProcessor, BatchProcessor>();
services.AddSingleton<IExportService, ExportService>();
services.AddSingleton<FileDialogService>();                                   // concrete, for Owner
services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());
services.AddTransient<ImporterViewModel>();
services.AddTransient<BatchViewModel>();
services.AddTransient<MainWindowViewModel>();                                  // depends on Importer + Batch VMs
```

The stateless engine services are singletons; the view models are transient.
`FileDialogService` is registered concretely *and* behind its interface so the app
can set its `Owner` window after the window exists. The `MainWindow` is created with
`MainWindowViewModel` as `DataContext` only under
`IClassicDesktopStyleApplicationLifetime` (so headless tests, which use a different
lifetime, do not auto-create it — see `docs/TESTING.md`).

---

## 5. The render pipeline (`SkiaFilmstripRenderer`)

### 5.1 `ComputeTransform(settings, source, frameIndex)`

`n = max(1, FrameCount)`. `t = n > 1 ? frameIndex / (n - 1) : 0`. **The `(n - 1)`
divisor is deliberate** — it lands the final frame exactly on the maximum value.
Changing it to `n` makes the last frame fall short. Do not change it.

- **RotaryKnob** — fit the art aspect-preserving and centred into the frame
  (`Contain`), then rotate about the pivot.
  `angle = Start + (End - Start) * t`. `pivot = (fw/2 + PivotOffsetX, fh/2 + PivotOffsetY)`.
- **VerticalFader** — the cap keeps native size and slides vertically: frame 0 (min)
  at the bottom (`yBottom = fh - EdgeMargin - capH`), last (max) at the top
  (`yTop = EdgeMargin`); `y = yBottom + (yTop - yBottom) * t`. x centred + `CapCrossOffset`.
- **HorizontalSlider** — cap slides horizontally: min at left (`xLeft = EdgeMargin`),
  max at right (`xRight = fw - EdgeMargin - capW`); `x = xLeft + (xRight - xLeft) * t`.
  y centred + `CapCrossOffset`.

### 5.2 `RenderFrame(settings, source, background, frameIndex, scale)`

1. `ss = clamp(Supersample, 1, 8)`; `px = scale * ss` (oversampled, export-scaled px).
2. Create a `targetW×targetH` result and a `workW×workH` work surface, both
   `Rgba8888` / **premultiplied** alpha; clear transparent.
3. If a static `background` is supplied, draw it stretched to the work surface (once,
   never transformed — e.g. a knob well or fader track).
4. Apply the transform: translate to `pivot*px`, `RotateDegrees`, translate back;
   draw the source `srcRect → dstRect` (both scaled by `px`) with **Mitchell cubic**
   sampling.
5. Snapshot the work surface and downscale once to the target with Mitchell cubic.
   When `ss == 1` this is a 1:1 copy and costs almost nothing.

Quality comes from rendering rotated/scaled content into an oversampled surface and
resampling **once** at the end — this is what keeps a rotated knob edge smooth rather
than jagged. `SKCubicResampler.Mitchell` is low-ringing for both rotation and
downscale.

### 5.3 `RenderStrip(settings, source, background, scale)`

Computes the stacked dimensions (`vertical → fw × fh*n`, else `fw*n × fh`) and renders
**frame-by-frame**, blitting each finished frame 1:1 into the strip. Only one
oversampled frame is in memory at a time — important for XL strips tens of thousands
of pixels tall. The blit needs no resampling (frame is already at target size).

Output is 32-bit RGBA, transparent background.

### 5.4 Meters (`RenderMeterFrame`)

For `ComponentType.Meter`, `RenderFrame` skips the transform path and calls
`RenderMeterFrame`. The lit fraction for frame *i* is `t = i/(N−1)`, snapped to whole
segments (`round(t·S)/S`) unless `ContinuousFill`. The lit region is a rectangle for
the `FillDirection` (Up fills from the bottom, Down from the top, LeftToRight from the
left, RightToLeft from the right).

- **Layered** (a `source` on-state art is present): the off-state art (the
  `background`, drawn full earlier) shows through, and the on-state art is drawn
  **clipped to the lit region**.
- **Procedural** (`source` is null): `S` segment bars (separated by `SegmentGap`) are
  drawn in `OffColorArgb`, then re-drawn in `OnColorArgb` **clipped to the lit
  region** — so whole segments snap when discrete and the boundary segment is partially
  lit when continuous. Colours are unpacked from `0xAARRGGBB`.

`ComputeTransform` returns an identity full-frame transform for a meter (never used on
the meter path). `RenderFrame`/`RenderStrip` take a **nullable** `source` (null only
for a procedural meter).

---

## 6. The Create tab (`MainWindowViewModel`)

### 6.1 The single-funnel preview loop

`OnPropertyChanged` is one funnel (the `live-preview-render-loop` pattern):

1. **Output / irrelevant properties early-return** (the ignore-list): `PreviewImage`,
   `PreviewReadout`, `StripDimensions`, `StatusMessage`, `SourceInfo`,
   `BackgroundInfo`, `IsRotary`, `IsLinear`, `IsPlaying`, `ExportManifest`,
   `ParameterId`. These never trigger a re-render (avoids feedback loops and needless
   work).
2. `_suspendRefresh` guards bulk updates (e.g. applying type defaults, recomputing
   angles) so the preview refreshes **once** after a batch, not per property.
3. Side-effects: `SweepDegrees`/`RotationClockwise` → `RecomputeAnglesFromSweep`;
   `ComponentType` → `ApplyTypeDefaults`.
4. Then `UpdateReadouts()` (frame readout + strip dimensions) and `RefreshPreview()`.

`BuildSettings()` maps the bound properties to a `FilmstripSettings`.

### 6.2 Shared load path

`LoadSourceFromPath(string)` is **the one** load path: decode via `IImageLoadService`,
set `HasSource`/`SourceInfo`, square the frame for a knob, refresh. Both the
"Load source image…" button (`OpenSourceCommand`) and the drag-drop handler call it —
no duplicated load logic.

### 6.3 Preview rendering

`RefreshPreview()` renders the current frame at **display size** (`PreviewDisplaySize
= 380` on the long edge) for crispness, and caps supersample at 2 so scrubbing/playback
stay responsive (export uses the full supersample). The `SKBitmap` is converted via
`SkiaImageInterop.ToAvaloniaBitmap`.

### 6.4 Export (`ExportAsync`, gated on `HasSource`)

1. Prompt for a save path (suggested `name_<frames>frames.png`).
2. `BuildSettings()`, capture `source`/`background` locally.
3. Render the strip **off the UI thread** (`Task.Run`) and save the PNG.
4. If `ExportAt2x`: render + save `…@2x.png` (scale 2.0).
5. If `ExportManifest`: build a one-control manifest (parameter id defaults to the
   file name) and write `<name>.skin.json` next to the PNG (§8).
6. Status reports `… (+@2x) (+skin.json)` as applicable. All wrapped in try/catch →
   `StatusMessage`.

---

## 7. The Import tab (`ImporterViewModel` + `FilmstripImporter`)

### 7.1 Detection (`FilmstripImporter.Detect`)

A PNG does not store its frame count, so it is **inferred from dimensions** and must
be verified. Algorithm (per the `filmstrip-importer-engine` skill):

1. `vertical = height >= width`; `total = vertical ? height : width`.
2. Test candidate counts in priority order
   `[128, 127, 101, 100, 64, 63, 48, 32, 24, 16, 12, 8, 4, 3, 2]`; collect all that
   are `<= total` and divide evenly. The **first** is the best guess `n`
   (biased to the largest plausible count; odd "+ centre frame" variants 127/101/63
   are included because they appear in real exports).
3. `frameW/H` from `n`; classify by aspect:
   `|fh-fw| <= 0.2·max → knob/button`, `fh > fw·2 → vfader`, `fw > fh·2 → hslider`,
   else unknown (`Kind == null`).
4. **Low confidence** when the frame is square *and* an adjacent count also divides
   (`total % (n±1) == 0`) — the strip may include an extra centre frame (e.g. 64 vs
   63). The UI surfaces a warning and the count is editable.

> The heuristic biases to the largest count, so a "round" strip (e.g. 80×640) is
> read as 128 frames, not 8 — **the detected count is a guess; the UI makes it
> editable and the user verifies the sweep visually.** This is by design.

### 7.2 Extraction & re-stack

- `ExtractFrame(strip, layout, index)` — source rect from `layout.Vertical`, blit 1:1
  (nearest sampling) into a new `frameW×frameH` bitmap.
- `Restack(strip, layout, destination)` — read each frame using the *source*
  orientation, write it using the *destination* orientation; lossless 1:1 blits.

### 7.3 The view model

Same funnel pattern as Create. `LoadStripFromPath` (shared by button + drop) runs
detection, publishes `DetectedInfo`/`LowConfidence`, sets the editable `FrameCount`
(under `_suspendRefresh`), and refreshes. Editing `FrameCount` re-slices live
(`RecomputeFrameSize` + preview). `ExtractCurrentFrameCommand` and
`ExportRestackedCommand` are gated on `HasStrip`; the re-stack runs off-thread.

### 7.4 Hosting (the TabControl)

`MainWindow.axaml` is a `TabControl` (**Create** | **Import**). The Create content is
inline (bound to the window's `MainWindowViewModel`). The Import tab hosts
`<views:ImporterView DataContext="{Binding Importer}"/>` — `Importer` is the
`ImporterViewModel` exposed by `MainWindowViewModel` and resolved via DI.

---

## 8. The manifest (`skin.json`)

`ManifestService.BuildSingleControl(settings, asset, asset2x, controlId, parameterId)`
maps a `FilmstripSettings` + export filenames to a one-control `SkinManifest`:

- `type` ← `ComponentType` (`knob`/`vfader`/`hslider`).
- `asset`/`asset2x` ← bare file names (the manifest is written next to the PNG, so
  paths are relative). `asset2x` is null (and omitted) when no @2x was exported.
- `frames`, `frameWidth`, `frameHeight`, `stack` ← settings.
- `bounds` ← `{0, 0, frameWidth, frameHeight}` (one frame at the origin; the skin
  author repositions; bounds are base-resolution pixels).
- `baseWidth`/`baseHeight` ← frame size; `name` ← `"<id> skin"`.

`Serialize` uses System.Text.Json with `WriteIndented`, `CamelCase` naming, and
`DefaultIgnoreCondition = WhenWritingNull`. The output validates against the JSON
Schema in the `plugin-asset-manifest` skill (top-level + `controls[]` required keys,
`type`/`stack` enums, `bounds` x/y/w/h). The model supports multi-control manifests
and `valueMin/Max/Default`, but the export UI currently emits one control.

---

## 8.1 The Batch tab (`BatchViewModel` + `BatchProcessor`)

Renders a whole **folder** of source images into strips in one run.

- **Folders & template.** `BatchViewModel` collects an input folder (enumerated for
  accepted image extensions → `_inputFiles`), an output folder, and a render template
  (component type, frames, frame size, sweep, supersample, stack, plus
  `MatchKnobFrameToSource`, `ExportAt2x`, `ExportManifest`). `BuildSettings()` turns
  these into a `FilmstripSettings` (sweep → start/end angles, as on the Create tab).
  Folder pickers come from `IFileDialogService.OpenFolderAsync`.
- **Run loop (`BatchProcessor.ProcessAsync`).** The *entire* loop runs on a
  thread-pool thread via `Task.Run`, so every decode/render/encode is off the UI
  thread. For each file: load → (square the frame to the source for knobs when
  `MatchKnobFrameToSource`) → `RenderStrip` → save PNG → optional `@2x` → optional
  `<name>.skin.json`. A per-file failure is caught and recorded as a
  `BatchItemResult` — **the run continues** (failure isolation).
- **Progress & cancel.** After each item the processor calls `IProgress<BatchProgress>`.
  The VM builds that `Progress<T>` on the UI thread, so its callback marshals back to
  the UI thread to update the progress bar + per-file text. The cancellation token is
  checked between items; it is **not** passed to `Task.Run`, so cancelling returns a
  `BatchResult{ Cancelled = true }` rather than throwing. Renders are not interrupted
  mid-strip (between-item granularity).
- **Command gating.** `RunCommand` is enabled only when there are input files, an
  output folder, and no run in progress; `CancelCommand` only while running. The
  settings/folder controls are disabled (`IsEnabled="{Binding !IsRunning}"`) during a run.

---

## 9. Drag-and-drop (the `avalonia-drag-drop-files` pattern)

A drop target is inert until (1) `DragDrop.AllowDrop="True"` (set in XAML on the
preview `Border` of each tab) and (2) `DragOver` sets `e.DragEffects` — **skip
DragOver and `Drop` never fires.** Handlers are attached in code-behind and **scoped
to the tab's own drop border** (`PreviewBorder` for Create, `ImportDropBorder` for
Import) so the two tabs never cross-handle a drop; `OnDrop` sets `e.Handled = true`.
The handler only extracts a local path (`e.Data.GetFiles()` →
`IStorageItem.TryGetLocalPath()`), filters by extension, and calls the view model's
shared load method. A drag-over highlights the border with the accent colour.

---

## 10. Threading model

- **Rendering / re-stacking** (CPU-bound, pure) runs **off the UI thread** via
  `Task.Run` in the export/restack commands; the `await` resumes on the UI context to
  update `StatusMessage`.
- **Batch** runs its *entire* loop off the UI thread (`Task.Run`); progress marshals
  back via a UI-thread-created `Progress<T>` (§8.1).
- **Preview** renders **synchronously on the UI thread** — it is cheap (display-sized,
  supersample capped at 2). The auto-play `DispatcherTimer` (33 ms) nudges
  `PreviewValue` on the UI thread.
- View models touch only their own state on the UI thread; the engine services hold no
  shared mutable state, so off-thread renders are safe with locally-captured inputs.

---

## 11. SkiaSharp ↔ Avalonia interop

The engine works entirely in `SKBitmap` (premultiplied `Rgba8888`). For display,
`SkiaImageInterop.ToAvaloniaBitmap` does a PNG round-trip in memory into an Avalonia
`Bitmap`. Loading uses `SKBitmap.Decode` (premultiplied RGBA by default — the layout
the renderer composites in). Export uses `SKImage.Encode(Png, 100)`.

---

## 12. The standalone engine (`FilmstripEngine.cs`)

The repo-root `FilmstripEngine.cs` is a **single-file, copy-paste-portable** copy of
the renderer + models in namespace `StripKit.Engine`, with only a SkiaSharp
dependency. **It is not part of the build** (it lives outside `src/` and no project
compiles it) — it exists so the engine can be dropped into a CLI, a build step, or
another app unchanged.

> **Maintenance hazard:** it duplicates `Services/SkiaFilmstripRenderer.cs` +
> `Models/{FilmstripSettings,FrameTransform,ComponentType,StackDirection}`. If the
> in-app renderer's math changes, update this file to match (or it silently drifts).
> As of this audit the two are in sync.

---

## 13. Conventions & invariants (do not break)

- **Filmstrip:** frames stack **vertically** by default; frame 0 = min, frame N-1 =
  max. Rotary angle for frame i = `Start + (End-Start)·i/(N-1)` (the `(N-1)` divisor
  is intentional). Default sweep 270° (−135° → +135°). 32-bit RGBA, transparent bg.
  Standard counts 32/64/128. Ship `@2x` for HiDPI. ~10% transparent margin on knob art.
- **MVVM:** view models never reference Avalonia UI types (the preview `Bitmap` alias
  is the one allowed presentation type). Code-behind holds only view concerns.
  Source-generator classes are `partial`.
- **Design tokens (Obsidian glassmorphism):** dark frosted/acrylic, accent `#e8440a`,
  **sans-serif** `Verdana, Segoe UI, Arial` (no monospace); tokens centralized in
  `App.axaml` (`AccentBrush`/`AccentHiBrush`, `Text1/2/3Brush`, `GlassFill/GlassBorder`,
  the `ObsidianAcrylic` material). Window: `TransparencyLevelHint="AcrylicBlur"` +
  `ExperimentalAcrylicBorder` (with `FallbackColor`); panels: `Border.card`; primary
  buttons: Fluent `accent` class. Avalonia has no stock per-panel backdrop blur — the
  frost is the window acrylic + translucent layers. CommunityToolkit source generators,
  compiled bindings.
- **C#:** `Path.Combine` (never raw separators); no `System.Drawing`; no
  `.Result`/`.Wait()`; `async void` only for event handlers (the app has none — all
  commands are `async Task` via `[RelayCommand]`).
- **Do not** rewrite `SkiaFilmstripRenderer` or re-scaffold the project; extend it.

---

## 14. Data-flow walkthroughs

**Export (Create):** user adjusts settings → funnel re-renders preview → clicks
Export → `SavePngAsync` path → `BuildSettings` → `Task.Run(RenderStrip)` → `ExportService`
writes PNG → (@2x) → (manifest via `ManifestService`) → status updated.

**Import:** user drops a strip → `ImporterView` handler → `LoadStripFromPath` →
`FilmstripImporter.Detect` → publishes layout, sets editable count → scrub →
`ExtractFrame` per frame for preview → Extract/Re-stack command → `ExportService`.

---

## 15. Extension points (upcoming phases)

- **Phase 5 — Batch:** ✅ done (§8.1). `BatchProcessor` + `BatchViewModel` + `BatchView`.
- **Phase 6 — Meter mode:** ✅ done (§5.4). `ComponentType.Meter` + `RenderMeterFrame`
  (procedural + layered), four fill directions, discrete/continuous.
- **Phase 7 — Packaging:** signed single-file Windows build (`dotnet-installer-publishing`).
- The manifest model already supports multi-control skins and value ranges — surface
  them when a multi-asset workflow lands.
