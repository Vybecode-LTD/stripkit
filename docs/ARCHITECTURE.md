# ARCHITECTURE — StripKit

> Version 0.7.0 · last-updated 2026-06-04 · last-audit 2026-06-04
>
> The deep, file-and-flow reference for how the **StripKit desktop app** is built.
> `docs/SOURCE_MAP.md` says *where* things live; this says *how and why* they work.
> Read alongside `CLAUDE.md` (conventions) and `docs/ROADMAP.md` (the phased plan).
> Packaging / release / website are out of scope here — see `docs/PACKAGING.md`.

---

## 1. What StripKit is

StripKit turns a single transparent PNG into an animated **control filmstrip**
(sprite sheet) for audio-plugin GUIs — rotary knobs, vertical faders, horizontal
sliders, and **meters** — and the reverse: it **imports** an existing strip, detects
its layout, and extracts/re-stacks it. It can also emit a **`skin.json` manifest**
that binds a strip to a plugin parameter for a data-driven skinning engine / JUCE
`LookAndFeel`, and it can render a **whole folder** of sources in one batch run.

The output PNG is consumed by a filmstrip loader that shows **one frame per parameter
value** (frame 0 = minimum, frame N−1 = maximum).

### The one idea

Every render reduces to: **for each of N frames, place the source art inside a
fixed-size frame cell under a per-frame transform, then stack the cells into one
PNG.** Knobs rotate the art about a pivot; faders/sliders translate the art (the
"cap") along an axis; meters fill segments progressively. Supersampling + a Mitchell
cubic resampler keep rotated/scaled edges crisp.

---

## 2. Solution & project structure

```
StripKit.sln                  → one app project + one test project
FilmstripEngine.cs            → standalone portable renderer (NOT in the build; see §13)
src/StripKit/StripKit.csproj  → the app (WinExe, net9.0)
tests/StripKit.Tests/         → xUnit test project (references the app)
```

**`StripKit.csproj` key settings:** `OutputType=WinExe` (no console window on
Windows; a normal GUI exe on macOS/Linux), `net9.0`, `Nullable=enable`,
`ImplicitUsings=enable`, `LangVersion=latest`, `AvaloniaUseCompiledBindingsByDefault=true`,
`RootNamespace`/`AssemblyName=StripKit`, `ApplicationManifest=app.manifest`
(per-monitor-v2 DPI), `ApplicationIcon=Assets\stripkit.ico`, `<Version>` (the single
source of truth the release script bumps), `PackageLicenseExpression=MIT`.
`AvaloniaResource Include="Assets/**"` bundles the icons/logos.

**Packages:** Avalonia 11.3.0 (+ `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
`Avalonia.Fonts.Inter`, and `Avalonia.Diagnostics` — F12 dev tools, **Debug-only** via
`IncludeAssets=None` outside Debug), CommunityToolkit.Mvvm 8.4.0 (source generators),
Microsoft.Extensions.DependencyInjection 9.0.0, SkiaSharp 3.119.0 (pinned to align
with Avalonia 11.3's transitive Skia — see `CLAUDE.md` if NuGet warns of a conflict).

---

## 3. Layered architecture

```
Views (.axaml + thin code-behind)        ← Avalonia, XAML, compiled bindings
   │  bindings / commands
ViewModels (CommunityToolkit.Mvvm)        ← NO Avalonia UI types (preview Bitmap alias excepted)
   │  service interfaces (DI)
Services (engine + I/O)                   ← SkiaSharp; renderer/importer/manifest/batch have NO Avalonia dep
   │
Models (pure data)                        ← no UI, no Skia
```

Dependencies point downward only. The renderer, content analysis, importer, manifest
builder, and batch processor are **pure** (SkiaSharp + BCL), so they are unit-testable
and host-agnostic. `FileDialogService` is the one service that touches Avalonia (it
owns a `Window` for the storage pickers).

### 3.1 `Models/` — pure data, no UI/Skia deps

| File | Purpose |
|------|---------|
| `ComponentType.cs` | enum `RotaryKnob`, `VerticalFader`, `HorizontalSlider`, `Meter`. |
| `StackDirection.cs` | enum `Vertical`, `Horizontal`. |
| `MeterFillDirection.cs` | enum `Up`, `Down`, `LeftToRight`, `RightToLeft` (meter fill axis). |
| `FrameTransform.cs` | `readonly record struct`: per-frame placement in **1× frame units** — `TranslateX/Y`, `DrawWidth/Height`, `RotateDegrees`, `PivotX/Y`. The renderer multiplies these by the working pixel scale. |
| `FilmstripSettings.cs` | the full render contract (see §3.1.1). Has `Clone()` (a `MemberwiseClone`). |
| `StripDetection.cs` | `record`: the inferred layout of an *existing* strip — `Vertical`, `FrameCount`, `FrameWidth/Height`, `Kind` (`ComponentType?`), `LowConfidence`, `CandidateCounts`. Helpers `Direction`, `KindLabel`. |
| `SkinManifest.cs` | `SkinManifest` / `ManifestControl` / `ManifestBounds` records — the `skin.json` schema (see §9). |
| `BatchModels.cs` | `BatchOptions`, `BatchProgress`, `BatchItemResult`, `BatchResult` — batch run inputs / progress / outcome (see §8). |

#### 3.1.1 `FilmstripSettings` fields (the render contract)

- **General:** `ComponentType` (default `RotaryKnob`), `FrameCount` (64), `FrameWidth`/`FrameHeight` (80, in 1× px).
- **Rotary:** `StartAngleDegrees` (−135), `EndAngleDegrees` (135) — both clockwise; `PivotOffsetX/Y` (0) — an *advanced nudge* on top of the content centre, for deliberately eccentric rotation.
- **Content alignment (all art types):** `SourceCenterX`/`SourceCenterY` (both 0.5) — the normalized (0..1) visual centre of the art within the source image; `(0.5, 0.5)` reproduces plain rectangle centring. See §7.
- **Linear:** `EdgeMargin` (4) — gap left at each end of the cap's travel; `CapCrossOffset` (0) — offset on the non-travel axis.
- **Quality/output:** `Supersample` (4; clamped 1–8 by the renderer), `StackDirection` (`Vertical`).
- **Meter:** `SegmentCount` (12), `FillDirection` (`Up`), `ContinuousFill` (false), `SegmentGap` (3, px), `OnColorArgb` (`0xFFE8440A`, the house accent), `OffColorArgb` (`0xFF2A2A2A`, dim). Colours are packed `0xAARRGGBB` so the model keeps no Skia dependency.
- **Value arc (knob, §5.5):** `ShowValueArc` (false — the master gate), `ArcRadius` (0.88, fraction of the inscribed radius), `ArcThickness` (4, px), `ArcRoundCaps` (true), `ArcColorArgb` (`0xFFE8440A`), `ArcGradient` (false) + `ArcColor2Argb` (`0xFFFFC107`, amber), `ArcTrack` (true) + `ArcTrackColorArgb` (`0x33FFFFFF`, faint white), `ArcGlow` (false) + `ArcGlowSize` (6, px). All packed `0xAARRGGBB` / primitives — Skia-free.

### 3.2 `Services/` — the engine and I/O

| File | Purpose | Avalonia? |
|------|---------|-----------|
| `IFilmstripRenderer` / `SkiaFilmstripRenderer` | **the heart**: `ComputeTransform`, `RenderFrame`, `RenderStrip` (+ private `RenderMeterFrame`). | No |
| `ContentAnalysis` (static) | `DetectContentCenter` — pixel analysis backing the alignment tools (§7). | No |
| `IFilmstripImporter` / `FilmstripImporter` | `Detect` (layout from dimensions), `ExtractFrame`, `Restack`. | No |
| `IManifestService` / `ManifestService` | `BuildSingleControl`, `Serialize`, `SaveAsync`. | No |
| `ICodeSnippetService` / `CodeSnippetService` | `Generate` / `FileName` / `SaveAsync` — emit JUCE / CSS / iPlug2 / HISE loader code (§9.1). | No |
| `IBatchProcessor` / `BatchProcessor` | render a folder of sources → many strips off-thread, with progress + cancel (§8). | No |
| `IImageLoadService` / `ImageLoadService` | decode a file → `SKBitmap` (premultiplied RGBA); returns null on a missing/undecodable file. | No |
| `IExportService` / `ExportService` | encode an `SKBitmap` → PNG file (creates the directory). | No |
| `IFileDialogService` / `FileDialogService` | open-image / save-PNG / open-folder pickers via `IStorageProvider`. Holds the `Owner` window. | **Yes** |

### 3.3 `Helpers/`

`SkiaImageInterop.ToAvaloniaBitmap(SKBitmap)` — encodes the bitmap to PNG in memory
and wraps it as an Avalonia `Bitmap` for binding to an `Image`. Fine for
preview-sized frames; a reused `WriteableBitmap` direct-pixel path is the documented
upgrade if high-frame-rate playback ever needs it (see the `avalonia-skia-interop` skill).

### 3.4 `Controls/`

`SectionHeader` — a `TemplatedControl` with one `Text` styled property. Renders a
short dark label with a 3px accent divider beneath it that overhangs ~25% past the
text (a `ScaleTransform` in the template, styled by a `ControlTheme` in `App.axaml`).
A small artistic flourish used throughout the sidebars.

### 3.5 `ViewModels/`

- `ViewModelBase` — `ObservableObject` base; never references Avalonia UI types.
- `MainWindowViewModel` — the **Create** tab (§6). Exposes `Importer` and `Batch`.
- `ImporterViewModel` — the **Import** tab (§5.7 cross-ref / detailed in §10).
- `BatchViewModel` — the **Batch** tab (§8). No preview funnel; runs off-thread.

All three are `partial` (CommunityToolkit source generators require it) and use
`[ObservableProperty]` / `[RelayCommand]`. The only Avalonia reference is
`using AvBitmap = Avalonia.Media.Imaging.Bitmap;` for the preview image — a **media**
type, not a control/visual. Logic, files, and domain state stay out of code-behind.

### 3.6 `Views/`

- `MainWindow.axaml(.cs)` — a `TabControl` with **Create** (inline), **Import** (hosts
  `ImporterView`), and **Batch** (hosts `BatchView`). Code-behind holds the auto-play
  `DispatcherTimer`, the Create preview's drag-drop handlers, the alignment crosshair
  drag (§7.3), and the About-flyout link handler.
- `ImporterView.axaml(.cs)` — the Import tab `UserControl` (`x:DataType` =
  `ImporterViewModel`) + its own drag-drop handlers.
- `BatchView.axaml(.cs)` — the Batch tab `UserControl` (`x:DataType` =
  `BatchViewModel`); markup-only code-behind (no drag-drop — it works on folders).

All bindings are compiled (`x:DataType` on every view; `AvaloniaUseCompiledBindingsByDefault`).

---

## 4. Composition root & dependency injection

`App.axaml.cs > OnFrameworkInitializationCompleted` is the single composition root:

```csharp
services.AddSingleton<IImageLoadService, ImageLoadService>();
services.AddSingleton<IFilmstripRenderer, SkiaFilmstripRenderer>();
services.AddSingleton<IFilmstripImporter, FilmstripImporter>();
services.AddSingleton<IManifestService, ManifestService>();
services.AddSingleton<ICodeSnippetService, CodeSnippetService>();
services.AddSingleton<IBatchProcessor, BatchProcessor>();
services.AddSingleton<IExportService, ExportService>();
services.AddSingleton<FileDialogService>();                                   // concrete, for Owner
services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());
services.AddTransient<ImporterViewModel>();
services.AddTransient<BatchViewModel>();
services.AddTransient<MainWindowViewModel>();                                  // depends on Importer + Batch VMs
```

The stateless engine services are singletons; the view models are transient.
`ContentAnalysis` is a static class (no registration). `FileDialogService` is
registered concretely *and* behind its interface so the app can set its `Owner` window
after the window exists. `Program.Main` is `[STAThread]` and calls
`BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` (`UsePlatformDetect`,
`WithInterFont`, `LogToTrace`). The `MainWindow` is created with a resolved
`MainWindowViewModel` as `DataContext` **only** under
`IClassicDesktopStyleApplicationLifetime` — so headless tests, which use a different
lifetime, do not auto-create it (see `docs/TESTING.md`).

---

## 5. The render pipeline (`SkiaFilmstripRenderer`)

The renderer is `sealed`, stateless, and holds a single shared
`SKSamplingOptions Cubic = new(SKCubicResampler.Mitchell)` — Mitchell is low-ringing
for both rotation and downscale.

### 5.1 `ComputeTransform(settings, source, frameIndex)`

`n = max(1, FrameCount)`. `t = n > 1 ? frameIndex / (n − 1) : 0`. **The `(n − 1)`
divisor is deliberate** — it lands the final frame exactly on the maximum value.
Changing it to `n` makes the last frame fall short. Do not change it.

- **RotaryKnob** — fit the art aspect-preserving and rectangle-centred into the frame
  (`Contain`); the art is **not moved**. Only the rotation happens, about the art's
  *content* centre:
  - `(drawW, drawH) = Contain(srcW, srcH, fw, fh)`; `drawX = (fw − drawW)/2`, `drawY = (fh − drawH)/2`.
  - `angle = Start + (End − Start)·t`.
  - `pivot = (drawX + SourceCenterX·drawW + PivotOffsetX, drawY + SourceCenterY·drawH + PivotOffsetY)`.
  - With `SourceCenterX/Y = 0.5` the pivot is the frame centre — the classic behaviour. An off-centre value spins the knob **in place** instead of orbiting (§7).
- **VerticalFader** — the cap keeps native size and slides vertically: frame 0 (min) at
  the bottom (`yBottom = fh − EdgeMargin − capH`), last (max) at the top
  (`yTop = EdgeMargin`); `y = yBottom + (yTop − yBottom)·t`. Cross axis:
  `x = fw/2 − SourceCenterX·capW + CapCrossOffset` (so the art's content centre lands on
  the track centre; `RotateDegrees = 0`).
- **HorizontalSlider** — cap slides horizontally: min at left (`xLeft = EdgeMargin`),
  max at right (`xRight = fw − EdgeMargin − capW`); `x = xLeft + (xRight − xLeft)·t`.
  Cross axis: `y = fh/2 − SourceCenterY·capH + CapCrossOffset`.
- **Meter** — returns an **identity full-frame transform** (`0,0,fw,fh`, pivot at the
  centre). It is never used: `RenderFrame` routes meters to `RenderMeterFrame` instead.

### 5.2 `RenderFrame(settings, source, background, frameIndex, scale = 1.0)`

`source` is **nullable** (null is valid only for a procedural meter; any other type
throws `ArgumentNullException`).

1. `ss = clamp(Supersample, 1, 8)`; `px = scale · ss` (oversampled, export-scaled px).
2. Compute `targetW×targetH` (`FrameWidth·scale`) and the larger `workW×workH`
   (`FrameWidth·px`). Create a `work` `SKSurface` — `Rgba8888` / **premultiplied** alpha
   — and clear it transparent. (A null surface throws "Failed to create the render
   surface (out of memory?)".)
3. If a static `background` is supplied, draw it stretched to the work surface (once,
   never transformed — e.g. a knob well, a fader track, or a meter's off-state art).
4. **Meter path:** call `RenderMeterFrame` (§5.4). **Otherwise:** compute the transform,
   `canvas.Save()`, translate to `pivot·px` → `RotateDegrees` → translate back, draw the
   source `srcRect → dstRect` (both scaled by `px`) with **Mitchell cubic**, `Restore()`.
   The rotate is a no-op when `RotateDegrees == 0`.
5. Snapshot the work surface and downscale once to the target with Mitchell cubic into a
   fresh transparent `result`. When `ss == 1` (and `scale == 1`) this is a 1:1 copy and
   costs almost nothing.

Quality comes from rendering rotated/scaled content into an oversampled surface and
resampling **once** at the end — this is what keeps a rotated knob edge smooth rather
than jagged.

### 5.3 `RenderStrip(settings, source, background, scale = 1.0)`

Computes the stacked dimensions (`vertical → fw × fh·n`, else `fw·n × fh`, where
`fw/fh` are `scale`-adjusted) and renders **frame-by-frame**, blitting each finished
frame 1:1 into the strip with `DrawBitmap` (no resampling — the frame is already at
target size). Only **one** oversampled frame is in memory at a time — important for XL
strips tens of thousands of pixels tall. Output is 32-bit RGBA, transparent background.

### 5.4 Meters (`RenderMeterFrame`)

For `ComponentType.Meter`, `RenderFrame` skips the transform path and calls the private
`RenderMeterFrame(canvas, settings, onArt, frameIndex, px, workW, workH)` after the
background has been drawn full. The lit fraction for frame *i* is `t = i/(N−1)`, snapped
to whole segments (`round(t·S)/S`, clamped 0..1) unless `ContinuousFill`. The lit region
is a rectangle from `FillRect(FillDirection, fill, …)`:

- `Up` → fills from the **bottom** up; `Down` → from the **top** down;
  `LeftToRight` → from the **left**; `RightToLeft` → from the **right**.

Two modes, auto-selected by whether a `source` (the on-state art) is present:

- **Layered** (`onArt` present): the off-state art — drawn earlier as the `background` —
  shows through, and the on-state art is drawn **clipped to the lit region**
  (`canvas.ClipRect(fillRect)`).
- **Procedural** (`source` null): `S` segment bars (vertical if `FillDirection` is
  `Up`/`Down`, else horizontal) separated by `SegmentGap·px` are drawn in `OffColorArgb`,
  then the same bars are re-drawn in `OnColorArgb` **clipped to the lit region** — so
  whole segments snap when discrete and the boundary segment is partially lit when
  continuous. Gaps stay transparent. `SegmentRect(k, …)` lays segment `k` at
  `k·(segLen + gap)` along the axis; `FromArgb` unpacks `0xAARRGGBB` → `SKColor`.

### 5.5 Value arc (`RenderValueArc`)

For a **rotary knob** with `ShowValueArc` set, `RenderFrame` composites a value-tracking
fill arc **on top of** the rotated art (after `canvas.Restore()`, before the downsample) —
the modern Serum/Vital look. It is purely additive and **knob-only**: every other component
type, and `ShowValueArc = false` (the default), skip it entirely, so existing output is
byte-identical.

- **Geometry.** The arc is concentric with the knob's rotation pivot (`tf.PivotX/Y · px`),
  so it follows an off-centre / nudged knob. Its radius is `ArcRadius · ½·min(workW, workH)`
  (a fraction of the frame's inscribed radius; ~0.88 sits just outside a typical body). The
  stroke is `ArcThickness · px` with round or butt caps (`ArcRoundCaps`).
- **Sweep = value.** The lit arc inherits the knob's rotation range: it spans
  `Start → Start + (End − Start)·t` for frame *i* (`t = i/(N−1)`), i.e. the same fraction of
  the sweep the frame represents. **Angle convention:** the app's `0°` is 12 o'clock (`+` =
  clockwise); Skia's arc `0°` is 3 o'clock, so the start converts as `StartAngle − 90` (the
  `−90` cancels in the sweep *delta*). Negative sweeps (counter-clockwise knobs) draw
  correctly because `DrawArc` honours a negative sweep.
- **Layers (each optional, drawn back-to-front).** A dim **track** (`ArcTrack`,
  `ArcTrackColorArgb`) at the full sweep shows the unfilled remainder; a **glow**
  (`ArcGlow`, `ArcGlowSize`) is a blurred under-stroke of the lit portion via
  `SKMaskFilter.CreateBlur`; the **lit fill** itself is solid `ArcColorArgb`, or a
  `SKShader.CreateSweepGradient` from `ArcColorArgb → ArcColor2Argb` across the sweep when
  `ArcGradient` is set. Colours are packed `0xAARRGGBB` (the model stays Skia-free, as with
  the meter). Drawn into the oversampled work surface, so the arc downsamples crisp.

The arc bakes into the exported PNG; the `skin.json` manifest is unchanged (the loader just
shows frames). `StrokePaint(color, thickness, cap)` is the shared stroked-paint helper.

---

## 6. The Create tab (`MainWindowViewModel`)

### 6.1 The single-funnel preview loop

`OnPropertyChanged` is one funnel (the `live-preview-render-loop` pattern):

1. **Output / irrelevant properties early-return** (the ignore-list): `PreviewImage`,
   `PreviewReadout`, `StripDimensions`, `StatusMessage`, `SourceInfo`, `BackgroundInfo`,
   `IsRotary`, `IsLinear`, `IsMeter`, `ShowLoadHint`, `IsPlaying`, `ExportManifest`,
   `ParameterId`. These never trigger a re-render (avoids feedback loops and needless
   work — `ExportManifest`/`ParameterId` only affect the written file, not the pixels).
2. `_suspendRefresh` guards bulk updates (applying type defaults, recomputing angles,
   loading a source, the alignment commands) so the preview refreshes **once** after a
   batch, not per property.
3. Side-effects: `SweepDegrees`/`RotationClockwise` → `RecomputeAnglesFromSweep`
   (sets `Start/EndAngleDegrees` to `∓half` under a nested `_suspendRefresh`);
   `ComponentType` → `ApplyTypeDefaults`.
4. Then `UpdateReadouts()` (frame/angle readout + strip dimensions, including the @2x
   size when `ExportAt2x`) and `RefreshPreview()`.

`BuildSettings()` maps the bound properties to a `FilmstripSettings`, parsing the meter
colour hex via `ParseArgb` (`SKColor.TryParse` → packed ARGB, or a fallback).

**Type defaults (`ApplyTypeDefaults`):** knob → square the frame to the source (or 80×80
with no source); vertical fader → 40×128; horizontal slider → 128×32; meter → 48×160 (a
tall vertical meter the user widens for horizontal fills).

**Derived/state properties:** `IsRotary`/`IsLinear`/`IsMeter` (drive panel visibility);
`ShowLoadHint = !HasSource && !IsMeter` (a procedural meter previews with no source);
`SourcePixelWidth`/`SourcePixelHeight` (the loaded source's pixel size — the alignment
overlay maps the crosshair onto the drawn art with these). `[NotifyCanExecuteChangedFor]`
on `HasSource`/`ComponentType` keeps the export command's enablement live.

### 6.2 Shared load path

`LoadSourceFromPath(string)` is **the one** load path: decode via `IImageLoadService`,
dispose the old source, set `HasSource`/`SourceInfo`. For a **knob** it squares the
frame to the art **and auto-detects the content centre** (`ContentAnalysis.DetectContentCenter`,
§7) so an off-centre knob spins in place out of the box; for other types it resets the
centre to `(0.5, 0.5)`. All under `_suspendRefresh`, then one `RefreshPreview()`. Both the
"Load source image…" button (`OpenSourceCommand`) and the drag-drop handler call it — no
duplicated load logic.

### 6.3 Preview rendering (`RefreshPreview`)

Renders the current frame at **display size** for crispness and stays responsive:

- `scale = PreviewDisplaySize (380) / max(FrameWidth, FrameHeight)` — the preview frame
  is ~380px on its long edge regardless of the (often small) real frame size.
- **Supersample** is dropped to 1 while the crosshair guide is on (interactive drag) and
  otherwise capped at 2 (`min(Supersample, 2)`); **export always uses the full setting**.
- **Continuous preview:** the preview renders at a fixed fine virtual frame resolution
  `previewN = max(FrameCount, 1024)`, and the frame index is `round(PreviewValue·(previewN−1))`.
  This makes scrubbing and the aligned position smooth and **not** quantised to the
  (possibly coarse) export frame steps. Export still uses the sidebar `FrameCount`.
- The `SKBitmap` is converted via `SkiaImageInterop.ToAvaloniaBitmap`. With no source and
  not a meter, `PreviewImage` is set null (the load hint shows).

### 6.4 Export (`ExportAsync`, gated on `CanExport = HasSource || IsMeter`)

1. Prompt for a save path (suggested `<name>_<frames>frames.png`).
2. `BuildSettings()`, capture `source`/`background` locally for the background thread.
3. Render the strip **off the UI thread** (`Task.Run(RenderStrip … 1.0)`) and save the PNG.
4. If `ExportAt2x`: render + save `…@2x.png` at `scale = 2.0` (filename via `AppendSuffix`).
5. If `ExportManifest`: build a one-control manifest (`controlId` = file name without
   extension; `parameterId` = the trimmed `ParameterId` field, defaulting to `controlId`)
   and write `<controlId>.skin.json` next to the PNG (§9).
6. Status reports `Exported … (+@2x) (+skin.json)` as applicable. All wrapped in try/catch
   → `StatusMessage` (`"Error during export: …"`).

### 6.5 Other Create commands

`OpenBackgroundCommand`/`ClearBackgroundCommand` (the static layer), `SetFrames32/64/128`,
`Sweep270/300/360`, `MatchFrameToSourceCommand` (knob → square; vfader → `srcW+8 × srcH·6`;
hslider → `srcW·6 × srcH+8`), and the alignment commands `AutoCenterCommand`,
`ResetCenterCommand` (§7).

---

## 7. The alignment system (content-centre / pivot)

A knob's art often is not centred in its own PNG (a bright indicator on one side, an
asymmetric body). Rotating about the **image-rectangle** centre then makes the knob
*orbit* — the "wobble" bug. StripKit fixes this by rotating about the art's detected
**content centre** and exposing it for manual correction.

### 7.1 The model & math

`SourceCenterX`/`SourceCenterY` on `FilmstripSettings` are the normalized (0..1 per
axis) visual centre of the art **within the source image**. They feed `ComputeTransform`
(§5.1): for a knob they place the **rotation pivot** at that point inside the drawn art;
for a fader/slider cap they set the **cross-axis centring** so the art's middle sits on
the track centre. `(0.5, 0.5)` reproduces plain rectangle centring — i.e. the classic
output — so the default is backward-compatible. `PivotOffsetX/Y` remain an extra manual
nudge (in px) layered on top, for deliberately eccentric rotation.

### 7.2 Detection (`ContentAnalysis.DetectContentCenter`)

Pure, static, Avalonia-free. Returns the normalized centre of the **bounding box** of
pixels whose alpha exceeds a threshold (default 8). A bounding-box centre is more stable
than an alpha-weighted centroid for knobs that carry a bright indicator on one side. It
fast-paths the raw 4-byte pixel buffer for `Rgba8888`/`Bgra8888` (the alpha byte is 4th
either way), and falls back to `GetPixel` otherwise. A null/empty/fully-transparent image
returns `(0.5, 0.5)`. Called automatically when a knob source loads (§6.2) and by the
`AutoCenter` command.

### 7.3 The crosshair guide (view-side, `MainWindow.axaml.cs`)

`ShowCenterGuide` is a persistent toggle (the "Enable crosshair" checkbox). When on, a
`GuideCanvas` overlays the preview with a full-frame crosshair (`CrossH`/`CrossV` lines +
a `CrossRing`). The knob is **never moved** by toggling it (Play keeps animating) — only
the overlay appears/disappears — which resolves the owner's "it reverts when I remove the
crosshair" report: the centre value already persisted; the gap was a missing live apply.

The drag mechanics:

- `OnGuidePressed`/`OnGuideMoved` capture the pointer on `GuideCanvas` and convert the
  pointer position into a normalized point **within the drawn art** (not the whole cell)
  via `ArtRectOnScreen()` — which reproduces the renderer's `Contain`-fit + centre so the
  mark lands on the knob. `LetterboxRect()` gives the displayed (Uniform-stretched) rect of
  the preview image inside the overlay.
- **Render coalescing:** the crosshair follows the pointer **instantly** (`PlaceCrosshair`),
  but the expensive preview re-render is applied **at most once per UI cycle** — the latest
  normalized point is stashed (`_pendingX/Y`) and a single `Dispatcher.UIThread.Post`
  (`ApplyPendingCenter` → `Vm.SetSourceCenter`) does one batched render. A fast drag thus
  triggers one render per UI cycle, not hundreds.
- `SetSourceCenter(x, y)` on the VM clamps to 0..1, sets `SourceCenterX/Y` under
  `_suspendRefresh`, and does one `RefreshPreview()`.
- The code-behind re-positions the crosshair after layout settles when `SourceCenterX/Y`,
  `ShowCenterGuide`, or `PreviewImage` change (a background-priority `Post`).

### 7.4 Commands

- `AutoCenterCommand` — `DetectContentCenter(_source)` → set the centre (status reports the %).
- `ResetCenterCommand` — back to `(0.5, 0.5)`.
- `OnShowCenterGuideChanged` — updates the status hint when the toggle flips.

The same auto-detect + crosshair apply to fader/slider caps (cross-axis centring); the
Create sidebar shows an "Auto-center on content" / "Auto-center cap on content" button,
the crosshair toggle, numeric **Center X/Y** (0–1), and **Reset** under the rotary/linear
panels.

---

## 8. The Batch tab (`BatchViewModel` + `BatchProcessor`)

Renders a whole **folder** of source images into strips in one run. (`BatchViewModel`
offers only knob/fader/slider — no meter — since a procedural meter needs no source.)

- **Folders & template.** `BatchViewModel` collects an input folder
  (`Directory.EnumerateFiles` filtered to `.png/.webp/.bmp/.jpg/.jpeg`, ordered, →
  `_inputFiles`), an output folder, and a render template (component type, frames, frame
  size, sweep + clockwise, supersample, stack, plus `MatchKnobFrameToSource`, `ExportAt2x`,
  `ExportManifest`). `BuildSettings()` turns these into a `FilmstripSettings` (sweep →
  `∓half` start/end angles, as on the Create tab). Folder pickers come from
  `IFileDialogService.OpenFolderAsync`.
- **Run loop (`BatchProcessor.ProcessAsync`).** The *entire* loop runs on a thread-pool
  thread via `Task.Run`, so every decode/render/encode is off the UI thread. It creates the
  output dir, then for each file: load → (for knobs when `MatchKnobFrameToSource`, clone the
  settings and square `FrameWidth/Height` to that source's larger side) → `RenderStrip` →
  save `<name>_<frames>frames.png` → optional `…@2x.png` → optional `<name>.skin.json`. A
  per-file failure is caught and recorded as a `BatchItemResult{ Success = false, Error }` —
  **the run continues** (failure isolation).
- **Progress & cancel.** After each item the processor calls `IProgress<BatchProgress>`.
  The VM builds that `Progress<T>` **on the UI thread**, so its callback marshals back to
  the UI thread to update the `ProgressValue` (0..100) + `ProgressText`. The cancellation
  token is checked **between items**; it is deliberately **not** passed to `Task.Run`, so
  cancelling returns a `BatchResult{ Cancelled = true }` rather than throwing. Renders are
  not interrupted mid-strip (between-item granularity).
- **Command gating.** `RunCommand` is enabled only when there are input files, an output
  folder, and no run in progress; `CancelCommand` only while `IsRunning`. Settings/folder
  controls bind `IsEnabled="{Binding !IsRunning}"`. `BuildSummary` lists up to 8 failures.

---

## 9. The manifest (`skin.json`)

`ManifestService.BuildSingleControl(settings, assetName, asset2xName, controlId, parameterId)`
maps a `FilmstripSettings` + export filenames to a one-control `SkinManifest`:

- `type` ← `ComponentType` (`knob`/`vfader`/`hslider`/`meter`, via `MapType`).
- `asset`/`asset2x` ← bare file names (the manifest is written next to the PNG, so paths
  are relative). `asset2x` is null (and omitted on serialize) when no @2x was exported.
- `frames`, `frameWidth`, `frameHeight`, `stack` (`"vertical"`/`"horizontal"`) ← settings.
- `bounds` ← `{0, 0, frameWidth, frameHeight}` (one frame at the origin; the skin author
  repositions; bounds are base-resolution pixels).
- `baseWidth`/`baseHeight` ← frame size; `name` ← `"<id> skin"`; `manifestVersion = 1`.

`Serialize` uses System.Text.Json with `WriteIndented`, `CamelCase` naming, and
`DefaultIgnoreCondition = WhenWritingNull`. `SaveAsync` creates the directory and writes
the file. The output validates against the JSON Schema in the `plugin-asset-manifest`
skill (top-level + `controls[]` required keys, `type`/`stack` enums, `bounds` x/y/w/h).
The model already supports **multi-control** manifests, an optional whole-window/per-control
`background`, `author`/`skinVersion`, and `valueMin/Max/Default` — the export UI currently
emits a single control.

### 9.1 Code export (`ICodeSnippetService` / `CodeSnippetService`)

To close the loop from *asset* to *working control*, an export can also emit **ready-to-paste
loader code** for a target framework — so the user doesn't hand-write the filmstrip boilerplate.
The service is **pure** (BCL only — no Skia, no Avalonia), a direct sibling of `ManifestService`:
`Generate(target, request)` → a string, `FileName(target, controlId)` → the on-disk name, and
the thin `SaveAsync(target, request, directory)`. Input is a `CodeSnippetRequest` record
(component type, frame count/size, stack, asset/`@2x` file names, control id, parameter id).

- **Targets (`CodeTarget`).** `Juce` — a `LookAndFeel_V4` filmstrip `Slider` (`drawRotarySlider`
  for a knob, `drawLinearSlider` for a fader/slider) or a meter `Component` with `setLevel`;
  `Css` — a self-contained HTML/`<style>`/`<script>` sprite driven by `background-position` + a
  0..1 value setter; `IPlug2` — `IBKnobControl` / `IBSliderControl` / `IBitmapControl` with
  `LoadBitmap(nStates, framesAreHorizontal)`; `Hise` — a `ScriptPanel` with a filmstrip paint
  routine. The four files use extensions `.juce.h` / `.html` / `.iplug2.cpp` / `.hise.js`.
- **The universal rule.** Every snippet selects the frame with
  `frame = clamp(round(value·(N−1)), 0, N−1)` and reads the source cell from the stack axis
  (`frame·frameH` down a vertical strip, `frame·frameW` along a horizontal one) — the same
  `(N−1)` law as the renderer. Identifiers are sanitised per language (`Pascal` for C++ class /
  param names, a CSS-class form, JUCE `BinaryData` mangling for the embedded asset name).
- **Wiring.** `MainWindowViewModel` exposes `ExportCode` + four per-target toggles
  (`EmitCodeJuce/Css/IPlug2/Hise`), a `CodePreviewTarget`, and a live `GeneratedCode` string;
  the funnel refreshes the snippet when a code-relevant input (incl. `ParameterId`) changes —
  **without** re-rendering the image. On export, `SaveAsync` writes one file per ticked target
  next to the PNG; the Create tab also has a **preview / copy-to-clipboard** expander (clipboard
  access lives in `MainWindow.axaml.cs`, a view concern). The snippet is generated from the
  baked PNG; the renderer and manifest are untouched.

---

## 10. The Import tab (`ImporterViewModel` + `FilmstripImporter`)

### 10.1 Detection (`FilmstripImporter.Detect`)

A PNG does not store its frame count, so it is **inferred from dimensions** and must be
verified. Algorithm (per the `filmstrip-importer-engine` skill):

1. `vertical = height >= width`; `total = vertical ? height : width`.
2. Test candidate counts in priority order
   `[128, 127, 101, 100, 64, 63, 48, 32, 24, 16, 12, 8, 4, 3, 2]`; collect all that are
   `<= total` and divide it evenly. The **first** is the best guess `n` (biased to the
   largest plausible count; odd "+ centre frame" variants 127/101/63 are included because
   they appear in real exports). With none dividing, `n = 1`.
3. `frameW/H` from `n`; classify by aspect (`Classify`):
   `|fh − fw| <= 0.2·max → RotaryKnob` (or a square button), `fh > fw·2 → VerticalFader`,
   `fw > fh·2 → HorizontalSlider`, else `null` (unknown).
4. **Low confidence** when the frame is square *and* an adjacent count also divides the
   total (`total % (n−1) == 0` for `n > 1`, or `total % (n+1) == 0`) — the strip may
   include an extra centre frame (e.g. 64 vs 63). Surfaced as a warning; the count is editable.

> The heuristic biases to the largest count, so a "round" strip (e.g. 80×640) is read as
> 128 frames, not 8 — **the detected count is a guess; the UI makes it editable and the
> user verifies the sweep visually.** This is by design.

### 10.2 Extraction & re-stack

- `ExtractFrame(strip, layout, index)` — clamp the index, source rect from
  `layout.Vertical`, blit 1:1 (`Nearest`/no-mip sampling — exact and cheap) into a new
  `frameW×frameH` bitmap.
- `Restack(strip, layout, destination)` — read each frame using the *source* orientation,
  write it using the *destination* orientation; lossless 1:1 blits.

### 10.3 The view model

Same funnel pattern as Create (with its own output ignore-list). `LoadStripFromPath`
(shared by button + drop) runs detection, publishes `DetectedInfo`/`LowConfidence`, sets
the editable `FrameCount` under `_suspendRefresh`, then `RecomputeFrameSize` +
`RefreshPreview`. Editing `FrameCount` re-slices live (`RecomputeFrameSize` recomputes the
frame size and warns if the total doesn't divide evenly; preview re-extracts).
`CurrentLayout()` builds a `StripDetection` from the current editable state for extraction.
`ExtractCurrentFrameCommand` and `ExportRestackedCommand` are gated on `HasStrip`; the
re-stack toggles to the opposite orientation and runs off-thread (`Task.Run`).

### 10.4 Hosting (the TabControl)

`MainWindow.axaml` is a `TabControl` (**Create** | **Import** | **Batch**). The Create
content is inline (bound to the window's `MainWindowViewModel`). The Import/Batch tabs host
`<views:ImporterView DataContext="{Binding Importer}"/>` and
`<views:BatchView DataContext="{Binding Batch}"/>` — `Importer`/`Batch` are the child VMs
exposed by `MainWindowViewModel` and resolved via DI.

---

## 11. Drag-and-drop (the `avalonia-drag-drop-files` pattern)

A drop target is inert until (1) `DragDrop.AllowDrop="True"` (set in XAML on the preview
`Border` of each tab) and (2) `DragOver` sets `e.DragEffects` — **skip DragOver and `Drop`
never fires.** Handlers are attached in code-behind and **scoped to the tab's own drop
border** (`PreviewBorder` for Create, the Import view's border for Import) so the two tabs
never cross-handle a drop; `OnDrop` sets `e.Handled = true`. The handler only extracts a
local path (`e.Data.GetFiles()` → `IStorageItem.TryGetLocalPath()`), filters by extension
(`.png/.webp/.bmp/.jpg/.jpeg`, mirroring the file picker), and calls the view model's
shared load method. A drag-over flips the border to the accent colour (a `BrushTransition`);
drag-leave/drop restores it to transparent. The Batch tab has no drop zone (it works on
folders).

---

## 12. Threading model

- **Rendering / re-stacking** (CPU-bound, pure) runs **off the UI thread** via `Task.Run`
  in the export/restack commands; the `await` resumes on the UI context to update
  `StatusMessage`.
- **Batch** runs its *entire* loop off the UI thread (`Task.Run`); progress marshals back
  via a UI-thread-created `Progress<T>` (§8).
- **Preview** renders **synchronously on the UI thread** — it is cheap (display-sized,
  supersample capped at 2, or 1 while the crosshair is on). The auto-play `DispatcherTimer`
  (33 ms) nudges `PreviewValue` on the UI thread; the crosshair drag coalesces renders to
  one per UI cycle (§7.3).
- View models touch only their own state on the UI thread; the engine services hold no
  shared mutable state, so off-thread renders are safe with locally-captured inputs.
- No `async void` except event handlers; all `[RelayCommand]` async methods return `Task`;
  no `.Result`/`.Wait()`.

---

## 13. SkiaSharp ↔ Avalonia interop

The engine works entirely in `SKBitmap` (premultiplied `Rgba8888`). Loading uses
`SKBitmap.Decode` (premultiplied RGBA by default — exactly the layout the renderer
composites in). Export uses `SKImage.Encode(Png, 100)`. For display,
`SkiaImageInterop.ToAvaloniaBitmap` does a PNG round-trip in memory into an Avalonia
`Bitmap`. The preview `Image` uses `RenderOptions.BitmapInterpolationMode="HighQuality"`
and `Stretch="Uniform"`.

---

## 14. The standalone engine (`FilmstripEngine.cs`)

The repo-root `FilmstripEngine.cs` is a **single-file, copy-paste-portable** copy of the
core renderer in namespace `StripKit.Engine`, with only a SkiaSharp dependency. **It is
not part of the build** (it lives outside `src/` and no project compiles it) — it exists
so the engine can be dropped into a CLI, a build step, a web backend, or another app
unchanged.

It contains exactly the **render math**: the `ComponentType`, `MeterFillDirection`, and
`StackDirection` enums; the `FrameTransform` struct; `FilmstripSettings` (including the
`SourceCenterX/Y` alignment fields, the meter fields, and the value-arc fields); and
`IFilmstripRenderer` + `SkiaFilmstripRenderer` (including `ComputeTransform`, `RenderFrame`,
`RenderStrip`, the full `RenderMeterFrame` procedural/layered path, and `RenderValueArc`).
It does **not** include the
app-only services — `ContentAnalysis`, `FilmstripImporter`, `ManifestService`,
`BatchProcessor`, the I/O services, or any view-model/view — by design.

> **Maintenance hazard:** it duplicates `Services/SkiaFilmstripRenderer.cs` +
> `Models/{FilmstripSettings, FrameTransform, ComponentType, StackDirection,
> MeterFillDirection}`. If the in-app renderer's math changes (transform, supersampling,
> meter fill, alignment), update this file to match (or it silently drifts). As of this
> audit the two are in sync — the alignment `SourceCenterX/Y` pivot, the meter path, and the
> `RenderValueArc` value-arc path (with its `FilmstripSettings` arc fields) are all present here.

---

## 15. Conventions & invariants (do not break)

- **Filmstrip:** frames stack **vertically** by default; frame 0 = min, frame N−1 = max.
  Rotary angle for frame i = `Start + (End − Start)·i/(N−1)` (the `(N−1)` divisor is
  intentional). Default sweep 270° (−135° → +135°). 32-bit RGBA, transparent bg. Standard
  counts 32/64/128. Ship `@2x` for HiDPI. ~10% transparent margin on knob art so corners
  don't clip on rotation.
- **Alignment:** rotation/centring is about the art's **content centre** (`SourceCenterX/Y`);
  `(0.5, 0.5)` reproduces classic rectangle-centred output. The bounding-box centre (not a
  centroid) is the detection method. Keep the defaults to preserve existing output.
- **MVVM:** view models never reference Avalonia UI types (the preview `Bitmap` alias is the
  one allowed presentation type). Code-behind holds only view concerns (timers, drag-drop,
  the crosshair drag, opening About links). Source-generator classes are `partial`.
- **Design tokens (Obsidian glassmorphism, dark):** accent `#e8440a`; **sans-serif**
  `Verdana, Segoe UI, Arial` (no monospace). Tokens centralized in `App.axaml`:
  `AccentBrush`/`AccentHiBrush`, `Text1/2/3Brush`, `GlassFill*`/`GlassBorder*` brushes and
  gradients, `AccentGradient(Hover)`, recessed input wells (`ComboBox*`/`TextControl*`
  background+border keys, focused border = accent), `SectionTextBrush`, `ControlCornerRadius`
  6 / `OverlayCornerRadius` 8, and the `ObsidianAcrylic` `ExperimentalAcrylicMaterial`
  (`FallbackColor` opaque for non-acrylic platforms). A full `Button` `ControlTheme` provides
  glass + `:pointerover`/`:pressed`/`:disabled`/`:focus-visible` states with brush/box-shadow
  transitions and an `.accent` (Fluent primary) variant. Style classes: `Border.card`/`.tile`/
  `.divider`, `TextBlock.section`/`.label`, plus the `SectionHeader` control. The window uses
  `TransparencyLevelHint="AcrylicBlur"` + an `ExperimentalAcrylicBorder` frosted base and a
  warm radial accent glow. Avalonia has no stock per-panel backdrop blur — the frost is the
  window acrylic + translucent layers. Re-use these tokens; don't hard-code hex or
  reintroduce monospace.
- **C#:** `Path.Combine` (never raw separators); no `System.Drawing`; no `.Result`/`.Wait()`;
  `async void` only for event handlers (all commands are `async Task` via `[RelayCommand]`).
- **Do not** rewrite `SkiaFilmstripRenderer` or re-scaffold the project; extend it.

---

## 16. Data-flow walkthroughs

**Export (Create):** adjust settings → funnel recomputes derived values + re-renders the
(continuous, ≥1024-step, display-sized) preview → click Export → `SavePngAsync` path →
`BuildSettings` → `Task.Run(RenderStrip)` → `ExportService` writes the PNG → (`@2x`) →
(`skin.json` via `ManifestService`) → status updated.

**Align a wobbly knob:** load a knob → content centre auto-detected & frame squared →
enable the crosshair → drag onto the true centre (crosshair instant, render coalesced) →
`SetSourceCenter` commits `SourceCenterX/Y` → toggle the crosshair off (art does not shift) →
export.

**Import:** drop a strip → `ImporterView` handler → `LoadStripFromPath` →
`FilmstripImporter.Detect` → publishes layout, sets the editable count → scrub →
`ExtractFrame` per frame for preview → Extract / Re-stack command → `ExportService`.

**Batch:** choose input + output folders (input enumerated/filtered) → set the template →
Run → `BatchProcessor.ProcessAsync` on a pool thread loops load → render → save (+@2x,
+manifest) per file, reporting progress to a UI-thread `Progress<T>`; Cancel stops between
items → result summary.

---

## 17. Tests (`tests/StripKit.Tests`)

xUnit + NSubstitute + FluentAssertions + `Avalonia.Headless`. Coverage spans: golden-image
renderer baselines (`RendererGoldenTests`), alignment renders (`AlignmentRenderTests`),
meter renders (`MeterRenderTests`), value-arc renders (`ValueArcRenderTests` — golden
baselines incl. gradient+glow, plus pixel-logic for the lit-sweep growth and the off /
non-knob no-ops), code-snippet generation (`CodeSnippetServiceTests` — per-target control
class / draw method, the frame math, stack-axis source, identifier sanitisation, file names,
`SaveAsync`), `ContentAnalysisTests`, importer engine + VM
(`FilmstripImporterTests`, `ImporterViewModelTests`), manifest mapping/JSON-Schema
(`ManifestServiceTests`), batch processor + VM (`BatchProcessorTests`, `BatchViewModelTests`),
the Create-tab load path (`LoadPathTests`), and a headless `DropZoneViewTests` (a synthetic
OS drag gesture isn't constructable headlessly, so drop is covered by the VM load-path +
`AllowDrop`-wiring assertions). `TestAppBuilder`/`TestImages`/`ImageAssert` are the harness.
See `docs/TESTING.md` for the methodology and the current count.

---

## 18. Extension points

- The **manifest** model already supports multi-control skins, per-control/window
  backgrounds, and value ranges — surface them when a multi-asset workflow lands.
- **Importer** frame-count *resampling* (re-timing a strip to a different count) and meter
  **peak-hold / stereo** are noted as unbuilt in `docs/ROADMAP.md`.
- Preview interop could move to a reused `WriteableBitmap` if very high-frame-rate playback
  is ever needed (§13).
- Phases 5–7 (batch, meter, packaging) are **done**; packaging detail lives in
  `docs/PACKAGING.md`, intentionally out of this app-internals reference.
