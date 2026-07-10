# ARCHITECTURE — StripKit

> Version 1.5.1 · last-updated 2026-07-04 · last-audit 2026-07-04
>
> The deep, file-and-flow reference for how the **StripKit desktop app** is built.
> `docs/SOURCE_MAP.md` says *where* things live; this says *how and why* they work.
> Read alongside `CLAUDE.md` (conventions) and `docs/ROADMAP.md` (the phased plan).
> Packaging / release / website are out of scope here — see `docs/PACKAGING.md`.

---

## 1. What StripKit is

StripKit turns a single transparent PNG into an animated **control filmstrip**
(sprite sheet) for audio-plugin GUIs — rotary knobs, vertical faders, horizontal
sliders, **meters**, **buttons**, and **toggles** — and the reverse: it **imports** an existing
strip, detects its layout, and extracts/re-stacks it. It can also emit a **`skin.json` manifest**
that binds a strip to a plugin parameter for a data-driven skinning engine / JUCE
`LookAndFeel`, and it can render a **whole folder** of sources in one batch run. When you have no
source art at all, it can **generate** one with your own OpenAI / Gemini / Claude / OpenAI-compatible
key — a layered control SVG (knob, button, toggle, fader/slider cap, or meter) that flows straight
into the same pipeline; it can also generate a **matching set** or **variations**, **refine** an
existing SVG, and **match a reference image** (§11).

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
Microsoft.Extensions.DependencyInjection 9.0.0, SkiaSharp 3.119.2 (the floor Svg.Skia
5.0.0 requires, a patch above Avalonia 11.3's transitive Skia — see `CLAUDE.md` if NuGet
warns of a conflict), and `Avalonia.Controls.ColorPicker` 11.3.0 (the Generate colour swatches).

**Layered-import / Generate packages (app-only, not in `FilmstripEngine.cs`):** `Svg.Skia` 5.0.0
(MIT, SVG layers), `Magick.NET-Q16-HDRI-x64` 14.14.0 (Apache-2.0, PSD/PSB layers + 16-bit/EXR HDR frames), and
`System.Security.Cryptography.ProtectedData` 9.0.0 (Windows DPAPI for the encrypted AI keys).

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
| `ComponentType.cs` | enum `RotaryKnob`, `VerticalFader`, `HorizontalSlider`, `Meter`, `Button` (discrete on/off state frames), `Toggle` (an on/off switch — distinct control, but rendered via the same discrete state-frame path as `Button`). |
| `StackDirection.cs` | enum `Vertical`, `Horizontal`. |
| `MeterFillDirection.cs` | enum `Up`, `Down`, `LeftToRight`, `RightToLeft` (meter fill axis). |
| `FrameTransform.cs` | `readonly record struct`: per-frame placement in **1× frame units** — `TranslateX/Y`, `DrawWidth/Height`, `RotateDegrees`, `PivotX/Y`. The renderer multiplies these by the working pixel scale. |
| `FilmstripSettings.cs` | the full render contract (see §3.1.1). Has `Clone()` (a `MemberwiseClone`). |
| `StripDetection.cs` | `record`: the inferred layout of an *existing* strip — `Vertical`, `FrameCount`, `FrameWidth/Height`, `Kind` (`ComponentType?`), `LowConfidence`, `CandidateCounts`. Helpers `Direction`, `KindLabel`. |
| `SkinManifest.cs` | `SkinManifest` / `ManifestControl` / `ManifestBounds` records — the `skin.json` schema (see §9). |
| `BatchModels.cs` | `BatchOptions`, `BatchProgress`, `BatchItemResult`, `BatchResult` — batch run inputs / progress / outcome (see §8). |
| `CodeModels.cs` | `CodeTarget` enum + `CodeSnippetRequest` record — inputs for the code-export service (§9.1). |
| `RenderLayer.cs` | `LayerBehavior` enum (`Static`, `Rotate`, `Frame`) + `RenderLayer` (behaviour + a normalized per-layer pivot) — the layered-knob / button / toggle model (§5.6, §5.7). `Frame` = shown only on the frame whose index matches the layer's index (button/toggle off/on state art). Skia-free; the layer's bitmap is passed alongside to the renderer. |

#### 3.1.1 `FilmstripSettings` fields (the render contract)

- **General:** `ComponentType` (default `RotaryKnob`), `FrameCount` (64), `FrameWidth`/`FrameHeight` (80, in 1× px).
- **Rotary:** `StartAngleDegrees` (−135), `EndAngleDegrees` (135) — both clockwise; `PivotOffsetX/Y` (0) — an *advanced nudge* on top of the content centre, for deliberately eccentric rotation.
- **Content alignment (all art types):** `SourceCenterX`/`SourceCenterY` (both 0.5) — the normalized (0..1) visual centre of the art within the source image; `(0.5, 0.5)` reproduces plain rectangle centring. See §7.
- **Linear:** `EdgeMargin` (4) — gap left at each end of the cap's travel; `CapCrossOffset` (0) — offset on the non-travel axis.
- **Quality/output:** `Supersample` (4; clamped 1–8 by the renderer), `StackDirection` (`Vertical`).
- **Meter:** `SegmentCount` (12), `FillDirection` (`Up`), `ContinuousFill` (false), `SegmentGap` (3, px), `OnColorArgb` (`0xFFE8440A`, the house accent), `OffColorArgb` (`0xFF2A2A2A`, dim). Colours are packed `0xAARRGGBB` so the model keeps no Skia dependency.
- **Value arc (knob, §5.5):** `ShowValueArc` (false — the master gate), `ArcRadius` (0.88, fraction of the inscribed radius), `ArcThickness` (4, px), `ArcRoundCaps` (true), `ArcColorArgb` (`0xFFE8440A`), `ArcGradient` (false) + `ArcColor2Argb` (`0xFFFFC107`, amber), `ArcTrack` (true) + `ArcTrackColorArgb` (`0x33FFFFFF`, faint white), `ArcGlow` (false) + `ArcGlowSize` (6, px). All packed `0xAARRGGBB` / primitives — Skia-free.
- **Layers (knob, §5.6):** `Layers` (an empty `List<RenderLayer>` by default — the gate). When non-empty for a rotary knob the renderer composites this ordered stack (a `Static` body + a `Rotate` pointer) instead of rotating the single source; empty reproduces the single-source render byte-for-byte. The layer **bitmaps** are passed alongside to the renderer (see §5.2/§5.6), not stored here. `Clone()` deep-copies the list.

### 3.2 `Services/` — the engine and I/O

| File | Purpose | Avalonia? |
|------|---------|-----------|
| `IFilmstripRenderer` / `SkiaFilmstripRenderer` | **the heart**: `ComputeTransform`, `RenderFrame`, `RenderStrip` (+ private `RenderMeterFrame`). | No |
| `ContentAnalysis` (static) | `DetectContentCenter` — pixel analysis backing the alignment tools (§7). | No |
| `PointerExtractor` (static) | `Extract` — splits a flat knob into a base + pointer via the radial-symmetry residual (§6.7). Returns a `PointerExtractionResult` (base, pointer, confidence). | No |
| `ILayeredImportService` / `LayeredImportService` | `Import` — parse a layered `.svg` (Svg.Skia) / `.psd`/`.psb` (Magick.NET) into named, behaviour-tagged, canvas-registered layers (§6.8). Returns a `LayeredImportResult` (`ImportedLayer[]` + canvas size). Hardened: `SafeXml.Parse` runs **before** `Svg.Skia.FromSvg` (BUG-010), and inputs are capped (SVG text ≤ 20 MB, PSD canvas ≤ 64 MP) so a malicious file can't exhaust memory. | No |
| `IFilmstripImporter` / `FilmstripImporter` | `Detect` (layout from dimensions), `ExtractFrame`, `Restack`, `Resample`. | No |
| `IManifestService` / `ManifestService` | `BuildSingleControl`, `Serialize`, `SaveAsync`. | No |
| `ICodeSnippetService` / `CodeSnippetService` | `Generate` / `FileName` / `SaveAsync` — emit JUCE / CSS / iPlug2 / HISE loader code (§9.1). | No |
| `IBatchProcessor` / `BatchProcessor` | render a folder of sources → many strips off-thread, with progress + cancel (§8). | No |
| `IImageLoadService` / `ImageLoadService` | decode a file → `SKBitmap` (premultiplied RGBA); returns null on a missing/undecodable file. Peeks the header dimensions via `SKCodec` first and **rejects > 64 MP** (≈ 8192×8192) so a decompression-bomb image can't allocate gigabytes before decode. | No |
| `IExportService` / `ExportService` | encode an `SKBitmap` → PNG file (creates the directory). | No |
| `IFileDialogService` / `FileDialogService` | open-image / save-PNG / save-SVG / open-folder / open-layered pickers via `IStorageProvider`. Holds the `Owner` window. | **Yes** |
| `ISettingsService` / `SettingsService` | load/save the small `AppSettings` JSON (`%APPDATA%/StripKit/settings.json`) — the first-run "seen tutorial" flag + the Generate tab's last provider/model prefs. Best-effort (defaults on missing/corrupt). | No |
| `IAssetService` / `AssetService` | extract a bundled avares asset (the tutorial's sample knob) to a temp file path. | **Yes** |
| `IAssetGenerationService` / `AssetGenerationService` | the **Generate** tab orchestrator (§11): build the prompt → dispatch → sanitize → `GenerationResult`. Networked. | No |
| `IAssetGenerationProvider` + `ClaudeProvider` / `OpenAiProvider` / `GeminiProvider` / `CustomOpenAiProvider` | one AI service each (URL + auth + body + parse), over a shared `HttpClient`; non-2xx → `GenerationException`. Each also implements `DescribeImageAsync` for reference-image vision (Claude image block / OpenAI `image_url` data URI / Gemini `inline_data`; `CustomOpenAiProvider` inherits OpenAI's). `CustomOpenAiProvider` subclasses `OpenAiProvider`, overriding only the endpoint URL to hit any OpenAI-compatible chat-completions server (`AppSettings.GenerateCustomBaseUrl`) (§11). | No |
| `SvgSanitizer` (static) | carve the `<svg>` out of a chatty reply + strip script / `<image>` / `<foreignObject>` / `on*` / off-document `href` (§11). Parses via `SafeXml`. | No |
| `SafeXml` (static) | hardened `XDocument` parse for **untrusted** SVG (`DtdProcessing.Prohibit`, no resolver, `MaxCharactersFromEntities = 0`) — closes entity-expansion DoS / external-entity. Used by `SvgSanitizer` + `LayeredImportService` (run **before** `Svg.Skia.FromSvg` — BUG-010, §11). | No |
| `ISecretStore` / `DpapiSecretStore` | per-provider API keys encrypted at rest via Windows DPAPI → `%APPDATA%/StripKit/secrets.dat` (§11). | No |

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
- `MainWindowViewModel` — the **Create** tab (§6). Exposes `Importer`, `Batch`, `Skin`, and `Generate`.
- `ImporterViewModel` — the **Import** tab (§5.7 cross-ref / detailed in §10).
- `BatchViewModel` — the **Batch** tab (§8). No preview funnel; runs off-thread.
- `SkinViewModel` (+ `SkinControlEntry`) — the **Skin** tab (§9.2): a multi-control
  `skin.json` builder. `SkinControlEntry` is the mutable, observable per-control row the list
  and detail editor bind to (mapped to the immutable `ManifestControl` record on export).
- `GenerateViewModel` — the **Generate** tab (§11): provider/key/model/base-URL + control type +
  the async cancellable generate, an off-thread preview-by-importing, and the `UseInCreateRequested`
  handoff to Create. Also drives the **matching-set** generator, the **variations** grid, **refine**,
  **reference-image match**, and the **prompt-seeds** library.
- `TutorialViewModel` — the Getting Started overlay (§6.9); a per-screen walkthrough incl. Generate.

All are `partial` (CommunityToolkit source generators require it) and use
`[ObservableProperty]` / `[RelayCommand]`. The only Avalonia reference is
`using AvBitmap = Avalonia.Media.Imaging.Bitmap;` for the preview image — a **media**
type, not a control/visual. Logic, files, and domain state stay out of code-behind.

### 3.6 `Views/`

- `MainWindow.axaml(.cs)` — a `TabControl` with **Create** (inline), **Import** (hosts
  `ImporterView`), **Batch** (hosts `BatchView`), **Skin** (hosts `SkinView`), and **Generate**
  (hosts `GenerateView`). Code-behind holds the auto-play `DispatcherTimer`, the Create preview's
  drag-drop handlers, the alignment crosshair drag (§7.3), and the About-flyout link handler.
- `ImporterView.axaml(.cs)` — the Import tab `UserControl` (`x:DataType` =
  `ImporterViewModel`) + its own drag-drop handlers.
- `BatchView.axaml(.cs)` — the Batch tab `UserControl` (`x:DataType` =
  `BatchViewModel`); markup-only code-behind (no drag-drop — it works on folders).
- `SkinView.axaml(.cs)` — the Skin tab `UserControl` (`x:DataType` = `SkinViewModel`): the
  skin metadata fields + controls list on the left, a per-control detail editor + export on
  the right; markup-only code-behind.
- `GenerateView.axaml(.cs)` — the Generate tab `UserControl` (`x:DataType` = `GenerateViewModel`):
  provider/key/model (+ a base-URL field shown only for the Custom provider) + control type +
  style/accent/size + "avoid" field on the left, the SVG preview + Use-in-Create / Save / Copy +
  raw-response / show-the-prompt expanders + the matching-set / variations result grids on the right.
  The model picker is an `AutoCompleteBox` (free text + suggestions); provider + control-type + style
  stay `ComboBox`es. Code-behind: clipboard copy + the colour-picker flyout handlers.
  `ViewModels/GenerateSetModels.cs` holds the grid view types (`SetTypeOption`, `GeneratedSetResult`).

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
services.AddTransient<SkinViewModel>();
services.AddTransient<MainWindowViewModel>();                                  // depends on Importer + Batch + Skin VMs
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

`n = max(1, FrameCount)`. The linear position `frameIndex / (n − 1)` (or `0` when
`n == 1`) is then passed through `settings.MapT(linearT)` to get `t`. **The `(n − 1)`
divisor is deliberate** — it lands the final frame exactly on the maximum value.
Changing it to `n` makes the last frame fall short. Do not change it.

**Parameter-law frame mapping (`FilmstripSettings.MapT`).** `MapT` remaps the linear
0..1 strip position through `settings.MappingCurve` (`FrameMappingCurve`: `Linear` /
`Skew` / `Logarithmic`) before it drives any sweep — the frame count and spacing never
change, only the value assigned to a given frame index does. `Linear` (the default)
returns `linearT` **completely unchanged** — no clamp, no arithmetic, a true no-op — so
every existing golden image stayed byte-identical. `Skew` raises the clamped input to
`MappingSkew` (the JUCE `NormalisableRange` skew-factor convention: `t' = t ^
MappingSkew`; skew < 1 front-loads resolution toward the low end, skew > 1 toward the
high end). `Logarithmic` applies `t' = log(1 + t·(MappingLogBase − 1)) /
log(MappingLogBase)` (a concave taper for frequency-style parameters; both curves clamp
their input and fall back to a safe default — `MappingSkew`/`MappingLogBase` ≤ their
valid range — if the parameter itself is invalid). `MapT` is called at all four
`t`-computation sites in the renderer: here in `ComputeTransform` (rotary/linear
position), `RenderLayers` (layered-knob angle, §5.6), `RenderMeterFrame` (meter fill
fraction, §5.4), and `RenderValueArc` (the value-arc sweep, §5.5) — so every
sweep-driven render path honours the curve consistently. Mirrored in
`FilmstripEngine.cs` (§15).

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

### 5.2 `RenderFrame(settings, source, background, frameIndex, scale = 1.0, layerArt = null)`

`source` is **nullable** (null is valid for a procedural meter or a layered knob; any other
case throws `ArgumentNullException`). `layerArt` is an optional `IReadOnlyList<SKBitmap>`
index-matched to `settings.Layers`; when both are non-empty for a knob the layered path
(§5.6) runs instead of the single-source path. It defaults to null, so existing callers and
output are unchanged.

1. `ss = clamp(Supersample, 1, 8)`; `px = scale · ss` (oversampled, export-scaled px).
2. Compute `targetW×targetH` (`FrameWidth·scale`) and the larger `workW×workH`
   (`FrameWidth·px`). Create a `work` `SKSurface` — `Rgba8888` / **premultiplied** alpha
   — and clear it transparent. (A null surface throws "Failed to create the render
   surface (out of memory?)".)
3. If a static `background` is supplied, draw it stretched to the work surface (once,
   never transformed — e.g. a knob well, a fader track, or a meter's off-state art).
4. **Meter path:** call `RenderMeterFrame` (§5.4). **Layered-knob path** (a rotary knob with
   a non-empty `Layers` + `layerArt`): call `RenderLayers` (§5.6). **Otherwise (single
   source):** compute the transform, `canvas.Save()`, translate to `pivot·px` →
   `RotateDegrees` → translate back, draw the source `srcRect → dstRect` (both scaled by `px`)
   with **Mitchell cubic**, `Restore()`. The rotate is a no-op when `RotateDegrees == 0`.
5. Snapshot the work surface and downscale once to the target with Mitchell cubic into a
   fresh transparent `result`. When `ss == 1` (and `scale == 1`) this is a 1:1 copy and
   costs almost nothing.

Quality comes from rendering rotated/scaled content into an oversampled surface and
resampling **once** at the end — this is what keeps a rotated knob edge smooth rather
than jagged.

### 5.3 `RenderStrip(settings, source, background, scale = 1.0)`

Computes the stacked dimensions and renders **frame-by-frame**, blitting each finished
frame 1:1 into the strip with `DrawBitmap` (no resampling — the frame is already at
target size). Only **one** oversampled frame is in memory at a time — important for XL
strips tens of thousands of pixels tall. Output is 32-bit RGBA, transparent background.

**Sprite-grid layout (`StripLayout`).** `settings.Layout` (`StripLayout`: `Strip` default
/ `Grid`) picks the packing:

- **`Strip`** (default) — the classic 1×N (or N×1) strip: `vertical → fw × fh·n`, else
  `fw·n × fh` (`fw/fh` are `scale`-adjusted). Frame *i* blits to `(0, i·fh)` when vertical,
  `(i·fw, 0)` otherwise — byte-identical to every pre-existing export.
- **`Grid`** — an R×C atlas: `cols = max(1, settings.GridColumns)`,
  `rows = ceil(FrameCount / GridColumns)`, strip size `fw·cols × fh·rows`. Frame *i* blits
  row-major to `col = i % cols`, `row = i / cols` → `(col·fw, row·fh)`.

Each frame is still rendered independently via `RenderFrame` regardless of layout — only
the destination blit coordinates change — so a grid strip costs nothing extra per frame.
`Strip` being the default (and the only code path when `Layout != Grid`) is what keeps
every prior golden image byte-for-byte unchanged. Mirrored in `FilmstripEngine.cs` (§15).

### 5.4 Meters (`RenderMeterFrame`)

For `ComponentType.Meter`, `RenderFrame` skips the transform path and calls the private
`RenderMeterFrame(canvas, settings, onArt, frameIndex, px, workW, workH)` after the
background has been drawn full. The lit fraction for frame *i* is `t = settings.MapT(i/(N−1))`
(§5.1 — `Linear`, the default, leaves it as `i/(N−1)`), snapped to whole segments
(`round(t·S)/S`, clamped 0..1) unless `ContinuousFill`. The lit region is a rectangle from
`FillRect(FillDirection, fill, …)`:

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
  `Start → Start + (End − Start)·t` for frame *i* (`t = settings.MapT(i/(N−1))`, §5.1 — the
  same remapped `t` the rotation itself uses), i.e. the same fraction of the sweep the frame
  represents. **Angle convention:** the app's `0°` is 12 o'clock (`+` = clockwise); Skia's
  arc `0°` is 3 o'clock, so the start converts as `StartAngle − 90` (the `−90` cancels in the
  sweep *delta*). Negative sweeps (counter-clockwise knobs) draw correctly because `DrawArc`
  honours a negative sweep.
- **Layers (each optional, drawn back-to-front).** A dim **track** (`ArcTrack`,
  `ArcTrackColorArgb`) at the full sweep shows the unfilled remainder; a **glow**
  (`ArcGlow`, `ArcGlowSize`) is a blurred under-stroke of the lit portion via
  `SKMaskFilter.CreateBlur`; the **lit fill** itself is solid `ArcColorArgb`, or a
  `SKShader.CreateSweepGradient` from `ArcColorArgb → ArcColor2Argb` across the sweep when
  `ArcGradient` is set. Colours are packed `0xAARRGGBB` (the model stays Skia-free, as with
  the meter). Drawn into the oversampled work surface, so the arc downsamples crisp.

The arc bakes into the exported PNG; the `skin.json` manifest is unchanged (the loader just
shows frames). `StrokePaint(color, thickness, cap)` is the shared stroked-paint helper.

### 5.6 Layer-aware knob — base + pointer (`RenderLayers`)

The first step of the layer-aware feature (ROADMAP ★ #3): a knob built from **two layers** —
a **static base** (the body / well) and a separate **pointer** that rotates with the value —
so only the pointer moves and the body stays crisp and re-renderable at any resolution.

- **Model.** `FilmstripSettings.Layers` is an ordered `List<RenderLayer>` (bottom-first), each
  layer a `LayerBehavior` (`Static` / `Rotate`) + a normalized per-layer pivot (`PivotX/Y`).
  It is **Skia-free** — the layer bitmaps are passed alongside as `RenderFrame`/`RenderStrip`'s
  optional `layerArt` (index-matched to `Layers`). **Empty `Layers` ⇒ the single-source path
  runs unchanged**, so every prior golden baseline is byte-identical (the same gating pattern
  as the value arc's `ShowValueArc = false`).
- **Composition (`RenderLayers`).** For a rotary knob with a non-empty stack, `RenderFrame`
  calls `RenderLayers` instead of the single-source draw. Each layer is **contain-fit and
  centred** in the cell exactly like a single knob source (so layers authored at the same
  canvas size overlay pixel-perfectly; a differently-sized one stays undistorted). A `Static`
  layer is drawn fixed; a `Rotate` layer is drawn rotated by the per-frame angle
  (`Start + (End−Start)·t`, where `t = settings.MapT(i/(N−1))` — §5.1, the same remapped `t`
  the single-source knob path uses) about **its own** pivot — independent of the body, which
  is what lets the pointer's rotation axis differ from the body centre. Layers are knob-only;
  every other component type ignores them.
- **Pivot.** Each `Rotate` layer rotates about its own normalized pivot mapped into the cell.
  The app seeds the pointer's pivot from the **body's** detected content centre (the knob
  axis) on load, then exposes it for manual adjustment (§6.6). A same-canvas pointer therefore
  rotates about the body centre by default.
- **Value arc still composes.** `RenderLayers` returns a `FrameTransform` carrying the knob
  centre (the static body's content centre), so an optional value arc (§5.5) draws on top,
  concentric with the body — a layered knob can carry an arc too.

The composite bakes into the exported PNG; the `skin.json` manifest and code export are
unchanged (the loader still just shows frames). Mirrored in `FilmstripEngine.cs` (§14). Next
layer-aware steps (auto-pointer extraction from flat art, then layered PSD/SVG import) reuse
this same `Layers` model and slot UI.

### 5.7 Discrete state frames — buttons & toggles (`RenderButtonLayers`)

`ComponentType.Button` and `ComponentType.Toggle` share **one** render path: discrete
**state frames** rather than a continuous transform. A button is typically 2 frames
(off / on) but can carry more states (hover, pressed, disabled); a toggle is an on/off
switch — 2 frames. Both are surfaced as **distinct** control types (different generated
art and a different code-export binding — see §9.1 and §11) but render identically.

- **Composition (`RenderButtonLayers`).** For a button/toggle with a non-empty `Layers` +
  `layerArt`, `RenderFrame` calls `RenderButtonLayers` instead of the single-source draw.
  Each layer is contain-fit and centred in the cell (no rotation, no translation): a
  `Static` layer is drawn on **every** frame, and a `Frame` layer is drawn **only** on the
  frame whose index matches its position in the stack — so the off-state art shows on
  frame 0, the on-state on frame 1, and so on. There is no interpolation between frames.
- **Mirrored in `FilmstripEngine.cs`** (§14): the Button/Toggle case routes to the same
  `RenderButtonLayers` state-frame path in the standalone engine.

---

## 6. The Create tab (`MainWindowViewModel`)

### 6.1 The single-funnel preview loop

`OnPropertyChanged` is one funnel (the `live-preview-render-loop` pattern):

1. **Output / irrelevant properties early-return** (the ignore-list): `PreviewImage`,
   `PreviewReadout`, `StripDimensions`, `StatusMessage`, `SourceInfo`, `BackgroundInfo`,
   `IsRotary`, `IsLinear`, `IsMeter`, `ShowLoadHint`, `IsPlaying`, `ExportManifest`,
   `ParameterId`, `IsGridLayout`, `IsSkewMapping`, `IsLogMapping`, `NewPresetName`,
   `SelectedPreset`. These never trigger a re-render (avoids feedback loops and needless
   work — `ExportManifest`/`ParameterId` only affect the written file, not the pixels;
   `IsGridLayout`/`IsSkewMapping`/`IsLogMapping` are computed UI-visibility flags whose
   *underlying* property — `Layout`/`MappingCurve` — already triggers the funnel normally;
   `NewPresetName`/`SelectedPreset` are preset-list UI state, not render input).
2. `_suspendRefresh` guards bulk updates (applying type defaults, recomputing angles,
   loading a source, the alignment commands) so the preview refreshes **once** after a
   batch, not per property.
3. Side-effects: `SweepDegrees`/`RotationClockwise` → `RecomputeAnglesFromSweep`
   (sets `Start/EndAngleDegrees` to `∓half` under a nested `_suspendRefresh`);
   `ComponentType` → `ApplyTypeDefaults`.
4. Then `UpdateReadouts()` (frame/angle readout + strip dimensions, including the @2x
   size when `ExportAt2x`) and `RefreshPreview()`.

`BuildSettings()` maps the bound properties to a `FilmstripSettings`, parsing the meter
colour hex via `ParseArgb` (`SKColor.TryParse` → packed ARGB, or a fallback). It also
carries `Layout`/`GridColumns` (sprite-grid packing, §5.3) and `MappingCurve`/
`MappingSkew`/`MappingLogBase` (parameter-law frame mapping, §5.1) straight through.

**Type defaults (`ApplyTypeDefaults`):** knob → square the frame to the source (or 80×80
with no source); vertical fader → 40×128; horizontal slider → 128×32; meter → 48×160 (a
tall vertical meter the user widens for horizontal fills).

**Derived/state properties:** `IsRotary`/`IsLinear`/`IsMeter`/`IsButton`/`IsToggle` (drive panel
visibility), plus `IsStateFrames = IsButton || IsToggle` — button and toggle share the discrete
state-frame render + Create logic (off/on layers), so the state-frame UI and the layer-building
branch on `IsStateFrames` rather than on `Button` alone;
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

### 6.6 Layered knob — base + pointer (Create tab, knob-only)

The Create tab's rotary section has a **"LAYERED KNOB (base + pointer)"** panel (§5.6 is the
render side). The VM holds two extra bitmaps beyond the single `_source` — `_baseLayer`
(static body) and `_pointer` (rotating) — plus `HasBaseLayer`/`HasPointer`/`BaseLayerInfo`/
`PointerInfo` and the independent `PointerPivotX/Y`. `OpenBaseLayer`/`OpenPointer` (and the
shared `LoadBaseLayerFromPath`/`LoadPointerFromPath`, no Avalonia types) + `ClearBaseLayer`/
`ClearPointer` + `CenterPointerOnBody` back the panel. Loading the **base** squares the frame
to it, detects its content centre (the knob axis → `SourceCenterX/Y`), and **seeds the
pointer pivot** to that centre.

- **Render switch.** `IsLayeredKnob` (a knob with a base loaded) makes `BuildSettings` append
  the `Static` (+ optional `Rotate`) `Layers` and `BuildLayerArt` return the matching
  bitmaps; preview/export pass them to the renderer (the non-layered path keeps the exact
  5-arg `RenderFrame` call, so nothing else changes). `CanExport` and `ShowLoadHint` treat a
  base layer like a source (a base-only knob previews/exports).
- **Funnel.** `HasBaseLayer`/`HasPointer`/`BaseLayerInfo`/`PointerInfo` are output-only
  (ignore-list, no re-render — loads call `RefreshPreview` directly); `PointerPivotX/Y` are
  **not** ignored, so editing them re-renders live.

### 6.7 Auto-pointer extraction from flat art (`PointerExtractor`, ★ #3 step 2)

The "Auto-extract from flat knob…" button (`AutoExtractPointerCommand`) turns a single FLAT knob
image (body + indicator baked together) into the two layer slots automatically, so a legacy flat
asset becomes layered without hand-splitting.

- **The principle.** A knob body is rotationally symmetric about its centre, so the indicator is
  whatever **breaks** that symmetry. `PointerExtractor.Extract(flat, cx, cy)` computes the
  per-radius mean colour **robustly** (a second pass drops the indicator outliers so they don't
  pollute the body estimate); that rotational average is the symmetric **base** (the indicator
  erased, same silhouette), and the per-pixel **residual** that deviates from its radial ring —
  a soft 0..1 mask × the original colour — is the **pointer** on a transparent canvas. It returns
  a `PointerExtractionResult` with a **confidence** (a small, concentrated residual scores high; a
  residual spread across the body — an asymmetric/textured knob — scores low and is flagged).
- **The command.** Detects the centre (`ContentAnalysis.DetectContentCenter`), extracts, then fills
  `_baseLayer` + `_pointer`, squares the frame, and seeds `SourceCenterX/Y` + the pointer pivot to
  the centre — exactly the state two manual loads would produce, so it feeds §6.6 / §5.6 unchanged.
  It's a **starting guess the user verifies** via the preview/scrub (importer philosophy), and it
  assumes the art shows the indicator at the minimum (frame-0) position.
- **Boundaries.** Pure SkiaSharp + BCL (like `ContentAnalysis`), runs once on load (not per frame),
  knob-only, and **app-only** — it is *not* mirrored into `FilmstripEngine.cs` (which holds only
  render math). A small central residual is inherent when the needle passes through the pivot.

### 6.8 Layered PSD / SVG import (`ILayeredImportService`, ★ #3 step 3)

The final layer-aware step: instead of hand-loading a base + pointer (§6.6) or auto-extracting them
from flat art (§6.7), import a **real layered source** and let each layer map straight onto the
renderer's stack. The **"Import layered file (SVG / PSD)…"** button (`ImportLayeredFileCommand`)
runs `ILayeredImportService.Import(path)` off the UI thread.

- **Parsing (`LayeredImportService`, app-only).** Dispatch by extension:
  - **`.svg`** — render the whole document once with **Svg.Skia** (MIT, SkiaSharp-native, no Avalonia
    dep) to fix the canonical canvas box, then for each top-level `<g>` build a standalone SVG (the
    root + shared `<defs>` + that one group) and rasterize it through the same transform, so the
    isolated groups register pixel-for-pixel. Names come from `inkscape:label` / `id` / `data-name`;
    document order = paint order = bottom-first.
  - **`.psd` / `.psb`** — read layers with **Magick.NET-Q16-HDRI** (Apache-2.0). ImageMagick returns
    `[merged composite, layer, layer, …]`; the unlabeled composite is dropped, each named layer's
    RGBA pixels are blitted onto a full-canvas bitmap at its page offset (so layers register). At
    Q16-HDRI the pixel bytes are 16-bit, so they're downshifted to 8-bit RGBA via `Helpers/MagickPixels`.
  - Each `ImportedLayer` carries a **name-guessed behaviour** (an indicator-like name —
    pointer/needle/indicator/tick… — → `Rotate`, else `Static`), a starting guess the user overrides.
  - The service is **not** mirrored into `FilmstripEngine.cs` (render math only); it's pure
    SkiaSharp + the two libraries, like `FilmstripImporter` / `PointerExtractor`.
- **Mapping (the view model).** Each parsed layer becomes an `ImportedLayerRow` (name + an editable
  `Behavior` + the canvas-sized art) in `ImportedLayers`. When that list is non-empty (`IsImportedKnob`),
  `BuildSettings` appends one `RenderLayer` per row — every `Rotate` layer pivots about the **shared**
  detected knob centre (`SourceCenterX/Y`, since all imported layers are the same canvas size) — and
  `BuildLayerArt` returns the rows' bitmaps. So the **renderer is untouched**: it already composites an
  N-layer stack (§5.6). Importing **squares the frame to the canvas**, forces the type to knob, seeds the
  axis from the merged layers' content centre, and **replaces the base/pointer slots** (the two layered
  modes are mutually exclusive). Re-tagging a row (the per-layer Static/Rotate dropdown) re-renders live.
- **Gating.** Empty `ImportedLayers` ⇒ nothing changes; the single-source / base-pointer paths and every
  golden baseline are byte-identical. It's a starting point the user verifies via the preview/scrub
  (assumes the art is drawn at the minimum / frame-0 position), like the importer and auto-extract.

MVP boundaries: top-level groups are the layers (no deep flattening / Figma single-root unwrap);
PSD layer order follows the file (no reorder UI); behaviours are limited to the rendered
`Static`/`Rotate` (translate/opacity-ramp remain a future renderer increment).

### 6.9 Onboarding — the Getting Started tutorial (P1)

A re-openable guided overlay that lowers the first-mile barrier, built entirely from existing
controls + design tokens (no renderer/engine change).

- **`TutorialViewModel`** holds an ordered `TutorialStep` list (welcome+sample → pick a type →
  align → frames/export → loader code → layered import), `CurrentIndex` and derived
  `CurrentStep`/`StepProgress`/`IsFirstStep`/`IsLastStep`/`NextLabel`, and the `Open`/`Next`/
  `Previous`/`Skip`/`LoadSample` commands. It holds no Avalonia types.
- **First-run.** `MaybeShowOnFirstRun()` (called once at the end of the `MainWindowViewModel`
  constructor) opens the overlay only when `ISettingsService.Settings.HasSeenTutorial` is false;
  finishing or skipping persists the flag so it never auto-opens again. It stays re-openable from
  the header **"Getting started"** button (`Tutorial.OpenCommand`).
- **Sample knob.** Step 1 offers **"Load sample knob"**, which raises `LoadSampleRequested`; the
  host VM extracts the bundled `Assets/sample-knob.png` via `IAssetService` and runs it through the
  normal `LoadSourceFromPath` (resetting to a single-source knob first), so a new user sees the full
  load → preview → export loop with no art of their own.
- **View (`TutorialOverlay`).** A non-blocking bottom-centre `card` (Depth tokens) over a
  faint, click-through scrim, hosted as the top child of the `MainWindow` root `Panel` and bound to
  `Tutorial`; it hides itself (and stops intercepting input) when `IsOpen` is false. Plus
  `ToolTip.Tip` hints on the key Create-tab controls.
- **Persistence (`SettingsService`).** A tiny System.Text.Json file in the app-data folder — the
  app's only persisted state. The path is constructor-injectable so tests use a temp file.

### 6.10 Save / load render presets

`MainWindowViewModel` now takes an injected `ISettingsService` (a new constructor dependency —
previously it had none), which rippled into every test that constructs the VM directly
(`TransportTileAlignmentTests`/`LoadPathTests`/`LayeredImportViewModelTests`, plus a new
`TestFakes.MainVm(ISettingsService)` helper).

- **`RenderPreset` (the model).** A plain data class (`Models/RenderPreset.cs`, ~40 properties)
  snapshotting the *entire* Create-tab render setup — component type, frame count/size, rotary
  sweep, content-alignment centre, linear-fader margins, supersample, stack direction, the §5.3
  sprite-grid fields, the §5.1 parameter-law fields, meter settings, value-arc settings, and export
  preferences. **Deliberately excludes any loaded art** (source/background/layers) — a preset is a
  reusable *style*, not an asset bundle, so applying one never touches `_source`/`_baseLayer`/
  `_pointer`/`ImportedLayers`.
- **Persistence.** `AppSettings.RenderPresets` (`List<RenderPreset>`) is loaded into the VM's
  observable `Presets` collection at construction, and both are kept in lock-step by object
  reference from then on (not by name — see the delete note below).
- **`SavePreset` (`[RelayCommand]`, gated on a non-blank `NewPresetName`).** Snapshots the current
  VM state into a `RenderPreset` (`ToPreset`); if an existing preset shares the name
  (case-insensitive), it's replaced in place rather than duplicated. Rewrites
  `_settings.Settings.RenderPresets` from `Presets` and calls `Save()`.
  `ApplyPreset`/`DeletePreset` are gated on `SelectedPreset` being non-null.
- **`ApplyPreset`.** Bulk-assigns every field from the selected preset back onto the VM inside one
  `_suspendRefresh = true` block (the same bulk-assign pattern §6.6/§6.8 use) — this matters because
  assigning `ComponentType` mid-restore would otherwise fire `ApplyTypeDefaults()` (§6.1) and
  overwrite the frame size the preset is about to set anyway. After the block, one manual
  `UpdateReadouts`/`UpdateCodePreview`/`UpdateRecipePreview`/`RefreshPreview` pass brings the preview
  in sync.
- **`DeletePreset`.** Removes the selected preset from **both** `Presets` and
  `_settings.Settings.RenderPresets` **by object reference**, not by name. This was a deliberate fix
  during a 2026-07-02 adversarial review (BUG-018, see `docs/BUGS.md`): removing by name on the
  persisted side while the UI side removed by reference meant two presets that happened to share a
  name (only reachable via a hand-edited settings file) could desync — deleting one from the UI
  silently deleted both from disk. Reference-based removal on both sides closes that.
- **View.** A "PRESETS" section sits atop the Create tab's left panel: a `ListBox` bound to
  `Presets` (a `DataTemplate` over `Models.RenderPreset` showing `Name`) + a name `TextBox` +
  Apply/Delete/Save buttons.

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

Renders a whole **folder** of source images into strips in one run. All four component
types are available, **meters included**: the template exposes the meter settings (segments,
fill direction, continuous, on/off colours) plus a `MeterSourceIsBackdrop` toggle — each file
is either the meter's **lit on-state art** (layered) or a **housing/backdrop** with procedural
LED segments drawn over it (`BatchProcessor` routes the loaded bitmap to the `source` or the
`background` slot of `RenderStrip` accordingly).

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

- `type` ← `ComponentType` (`knob`/`vfader`/`hslider`/`meter`/`button`/`toggle`, via `MapType`).
- `asset`/`asset2x` ← bare file names (the manifest is written next to the PNG, so paths
  are relative). `asset2x` is null (and omitted on serialize) when no @2x was exported.
- `frames`, `frameWidth`, `frameHeight`, `stack` (`"vertical"`/`"horizontal"`) ← settings.
- `layout`/`gridColumns` ← **nullable**, populated only when `settings.Layout == StripLayout.Grid`
  (§5.3) — `"grid"` + the clamped column count (`Math.Max(1, settings.GridColumns)`, BUG-017: an
  unclamped upstream value must never violate the manifest schema's `gridColumns` `minimum: 1`).
  Absent entirely for the default `Strip` layout, so a non-grid manifest is byte-identical to
  before this feature existed.
- `bounds` ← `{0, 0, frameWidth, frameHeight}` (one frame at the origin; the skin author
  repositions; bounds are base-resolution pixels).
- `baseWidth`/`baseHeight` ← frame size; `name` ← `"<id> skin"`; `manifestVersion = 1`.

`Serialize` uses System.Text.Json with `WriteIndented`, `CamelCase` naming, and
`DefaultIgnoreCondition = WhenWritingNull`. `SaveAsync` creates the directory and writes
the file. The output validates against the JSON Schema in the `plugin-asset-manifest`
skill (top-level + `controls[]` required keys, `type`/`stack` enums, `layout` enum,
`gridColumns` minimum, `bounds` x/y/w/h).
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
  for a knob, `drawLinearSlider` for a fader/slider), a meter `Component` with `setLevel`, or a
  filmstrip `juce::Button` for a **button/toggle** (a latching toggle via `setClickingTogglesState(true)`
  + `getToggleState()` frame select; drive from `isButtonDown` for a momentary button);
  `Css` — a self-contained HTML/`<style>`/`<script>` sprite driven by `background-position` + a
  0..1 value setter; `IPlug2` — `IBKnobControl` / `IBSliderControl` / `IBitmapControl`, or an
  `IBSwitchControl` for a **button/toggle**, with `LoadBitmap(nStates, framesAreHorizontal)`;
  `Hise` — a `ScriptPanel` with a filmstrip paint routine; `React` — a `.jsx` sprite component
  driven by a `value` prop (0..1). The five files use extensions
  `.juce.h` / `.html` / `.iplug2.cpp` / `.hise.js` / `.jsx`.
- **The universal rule.** Every snippet selects the frame with
  `frame = clamp(round(value·(N−1)), 0, N−1)` and reads the source cell from the stack axis
  (`frame·frameH` down a vertical strip, `frame·frameW` along a horizontal one) — the same
  `(N−1)` law as the renderer. Identifiers are sanitised per language (`Pascal` for C++ class /
  param names, a CSS-class form, JUCE `BinaryData` mangling for the embedded asset name).
- **Grid-layout awareness (§5.3).** `CodeSnippetRequest` carries trailing `Layout`/`GridColumns`
  params (defaulted, so pre-existing positional call sites still compile unchanged). When the
  asset is a grid, JUCE/CSS/HISE/React swap the single-axis `frame·frameW`/`frame·frameH` source
  read for real column/row math (`col = frame % cols`, `row = frame / cols`) — CSS additionally
  swaps its single `--frame` custom property for `--col`/`--row`. **iPlug2 is the one exception:**
  its built-in `IBitmap`/`LoadBitmap(nStates, framesAreHorizontal)` can only read a 1D strip, so
  its grid path emits an explicit warning comment recommending Strip layout instead of silently
  mis-reading a 2D atlas as a 1D one.
- **Wiring.** `MainWindowViewModel` exposes `ExportCode` + five per-target toggles
  (`EmitCodeJuce/Css/IPlug2/Hise/React`), a `CodePreviewTarget`, and a live `GeneratedCode` string;
  the funnel refreshes the snippet when a code-relevant input (incl. `ParameterId`) changes —
  **without** re-rendering the image. On export, `SaveAsync` writes one file per ticked target
  next to the PNG; the Create tab also has a **preview / copy-to-clipboard** expander (clipboard
  access lives in `MainWindow.axaml.cs`, a view concern). The snippet is generated from the
  baked PNG; the renderer and manifest are untouched. Also wired into the **Batch** and
  **Assemble** tabs (`BatchOptions.CodeTargets`) for parity.

### 9.2 Multi-control skin builder (the Skin tab)

The Create-tab export writes a **one-control** manifest next to its strip (§6.4). The **Skin
tab** (`SkinViewModel` + `SkinView`) surfaces the `SkinManifest` model's **multi-control**
capability: bind several already-exported strips to several parameters in one `skin.json`.

- **Controls.** An `ObservableCollection<SkinControlEntry>` — each row added **from a strip**
  (`FilmstripImporter.Detect` auto-fills frames / frame size / orientation / kind from the PNG,
  flagging a low-confidence count) or **blank**. The selected row is edited in a detail panel
  (id, type, parameter id, asset/`@2x`, frames, frame size, stack, on-screen `bounds`, optional
  value range). `SkinControlEntry` is the mutable observable mirror of the immutable
  `ManifestControl` record; `SkinViewModel.ToManifestControl` maps each on export (value-range
  strings parse to `double?`, blank ⇒ omitted; `ComponentType` → the schema's type string).
- **Skin metadata.** Name, optional author, the design resolution (seeded from the first
  control), and an optional whole-window `background` (a relative file name).
- **Export.** `IManifestService.BuildManifest(controls, name, author, baseW, baseH, background)`
  assembles the `SkinManifest`; the Skin tab writes `<name>.skin.json` into a **chosen folder**
  (next to the assets, so the relative `asset`/`background` names resolve). The strips
  themselves are produced on the Create / Batch tabs — the Skin tab only assembles the bindings.

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

### 10.2 Extraction, re-stack & resample

- `ExtractFrame(strip, layout, index)` — clamp the index, source rect from
  `layout.Vertical`, blit 1:1 (`Nearest`/no-mip sampling — exact and cheap) into a new
  `frameW×frameH` bitmap.
- `Restack(strip, layout, destination)` — read each frame using the *source* orientation,
  write it using the *destination* orientation; lossless 1:1 blits.
- `Resample(strip, layout, destinationCount)` — re-time the strip to a new frame count: output
  frame *j* takes source frame `round(j·(N−1)/(M−1))` (clamped), so the endpoints land exactly
  on the source min/max (the same `(N−1)` law as the renderer) and intermediate frames pick the
  **nearest** source frame — a 1:1 `Nearest` blit, never a blend (a blended moving pointer would
  ghost). Output keeps the source orientation + frame size; downsampling is lossy. Surfaced on
  the Import tab as a "Resample frame count" target + Export resampled (off-thread, like re-stack).

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

`MainWindow.axaml` is a `TabControl` (**Create** | **Import** | **Batch** | **Skin** |
**Generate**). The Create content is inline (bound to the window's `MainWindowViewModel`). The other
four tabs host `<views:ImporterView DataContext="{Binding Importer}"/>`,
`<views:BatchView DataContext="{Binding Batch}"/>`, `<views:SkinView DataContext="{Binding Skin}"/>`,
and `<views:GenerateView DataContext="{Binding Generate}"/>` — `Importer`/`Batch`/`Skin`/`Generate`
are the child VMs exposed by `MainWindowViewModel` and resolved via DI (see §11 for Generate).

---

## 11. The Generate tab (`GenerateViewModel` + `AssetGenerationService`)

The newest tab **generates** source art instead of importing it: it uses the **user's own** OpenAI /
Gemini / Claude / OpenAI-compatible API key to produce a **layered control SVG**, which feeds the
*same* layered-import pipeline as §6.8 — so there is **no renderer change** and nothing is mirrored
into `FilmstripEngine.cs`. It is also the only **networked, non-deterministic** part of the app, so
it brings the first secret (an API key) and the first HTTP I/O.

**Providers.** Four `IAssetGenerationProvider`s sit behind `AiProvider` (`Claude` / `OpenAI` /
`Gemini` / `Custom`): `ClaudeProvider`, `OpenAiProvider`, `GeminiProvider`, and `CustomOpenAiProvider`
— which **subclasses `OpenAiProvider`** and overrides only the endpoint URL to call **any
OpenAI-compatible chat-completions server** at a user-supplied base URL (`AppSettings.GenerateCustomBaseUrl`
— e.g. OpenRouter, Ollama at `http://localhost:11434/v1`, or LM Studio). The base-URL field is shown
only when the Custom provider is selected.

**Flow.** `GenerateViewModel.GenerateAsync` → `IAssetGenerationService.GenerateAsync(request,
provider, key, model, ct)` (a thin wrapper over the shared `RunAsync` core that all the generation
modes below share):

1. **Prompt.** `AssetGenerationService` builds a StripKit-aware prompt — a square `viewBox`, ~10%
   transparent rotation margin, a static `<g id="body">` plus a separate `<g id="pointer">` drawn at
   **12 o'clock**. (The renderer applies the start→end sweep to the pointer, so a 12-o'clock rest
   pose with the default −135…+135° lands frame 0 at 7 o'clock as usual — §15 invariants.) The group
   names match the importer's Rotate hints, so the import auto-tags `pointer`→Rotate, `body`→Static.
2. **Dispatch.** It calls the chosen **`IAssetGenerationProvider`** — `ClaudeProvider` (Messages API,
   `x-api-key`), `OpenAiProvider` (Chat Completions, Bearer), or `GeminiProvider` (generateContent,
   `x-goog-api-key`) — over a shared DI `HttpClient`. The `HttpAssetGenerationProvider` base turns any
   non-2xx / transport fault into a friendly `GenerationException` carrying the API's own
   `error.message`.
3. **Sanitize.** `SvgSanitizer.TryClean` carves the `<svg>…</svg>` out of the (often fenced/chatty)
   reply, parses it, and strips anything active or external — `<script>`, `<image>`,
   `<foreignObject>`, `on*` handlers, and any `href` that isn't a local `#id`. Pure
   `System.Xml.Linq`; Svg.Skia (via the importer) is the final validator.

**Editable model (v1.2.2).** The model field is **free text** — an `AutoCompleteBox` bound to a
per-provider `SuggestedModels` list, *not* a fixed dropdown. A custom or just-released model id can be
typed and is sent to the provider **verbatim** (so you can use a model the moment it ships), and a
pinned-but-delisted model displays as the typed text rather than collapsing to a blank box. Switching
provider still re-seeds the suggestions for that provider.

**Preview = validation, built off-thread (v1.2.2).** The VM writes the SVG to a temp file and runs it
through the *real* `ILayeredImportService.Import` — the preview is the flattened import, so a visible
preview **guarantees** the Create tab can import it. The whole preview build (temp-write + layered
import + composite + PNG-encode) runs inside **one `Task.Run`** (`BuildPreview`); the UI thread only
assigns the finished bitmap, so a large canvas no longer hitches the dispatcher. Generated temp SVGs
**no longer accumulate** — the prior one is dropped (`TryDelete`) each generation. **"Use in Create"**
raises `UseInCreateRequested(path)`; the host VM switches to the Create tab and calls the shared
`ImportLayeredFromPathAsync` (§6.8). **Save SVG** / **Copy SVG** persist it for reuse anywhere.

**Keys & persistence.** `ISecretStore`/`DpapiSecretStore` encrypts each provider's key at rest with
Windows DPAPI (`ProtectedData`, CurrentUser) in `%APPDATA%/StripKit/secrets.dat` — ciphertext only
(a base64 passthrough off-Windows keeps dev/test round-tripping). The last-used provider + a
per-provider model override persist in `AppSettings` (never the key). DI: the providers, the service,
and the secret store are singletons (with the `HttpClient`); `GenerateViewModel` is transient.

**All six control types (v1.3.0).** The `GenerationRequest` carries the component type, and the prompt is
**type-aware**: a **knob** asks for `<g id="body">` + `<g id="pointer">`; a **button** asks for `<g id="off">` +
`<g id="on">`; a **toggle** asks for off/on switch/rocker art; a **fader/slider** asks for a single `<g id="body">`
cap/handle shape; a **meter** asks for an unlit `<g id="off">` + a fully-lit `<g id="on">` pair (**vertical or
horizontal** — the orientation is inferred from the generated art's aspect). The importer's name hints map each on
import (`pointer`->Rotate, `off`/`on`->Frame, else Static), and the **Use-in-Create handoff honours the generated
type** (v1.2.1): knob -> the body+pointer `Layers` stack; button/toggle -> `off`/`on` as `LayerBehavior.Frame`
state layers; fader/slider -> flattened to the single source the linear renderer expects; **meter** -> `off`
becomes the meter background (drawn full) and `on` becomes the source the renderer reveals up to the value
(reusing the existing layered-meter reveal path — no renderer change). (Before v1.2.1 the handoff hard-forced
`RotaryKnob`, so generated faders/sliders rotated and buttons stacked both states.) The fader/slider/meter output
paths still want a live eyeball + prompt tuning — knob is the proven path.

**Beyond one control (v1.3.0).** The tab now generates more than a single SVG, all sharing the `RunAsync` core:

- **Matching set.** Pick which control types to generate, and `IAssetGenerationService.GenerateSetAsync(baseRequest,
  types, …)` produces the whole family **concurrently** from **one shared style** → an `IReadOnlyList<GenerationSetItem>`
  (each a `ComponentType` + its `GenerationResult`). The UI shows a results grid with per-item Use-in-Create / Save /
  Regenerate, plus Save-set-to-folder — a head start toward a full Skin.
- **Variations.** `GenerateVariationsAsync(request, count, …)` runs N concurrent takes (2 / 4 / 6 / 8) of one control,
  shown in a grid to pick from.
- **Refine.** `RefineAsync(request, currentSvg, instruction, …)` revises the current SVG from a plain-language
  instruction.
- **Reference-image match (vision).** Each provider implements `IAssetGenerationProvider.DescribeImageAsync` (Claude
  image block / OpenAI `image_url` data URI / Gemini `inline_data`; Custom inherits OpenAI's); `DescribeReferenceAsync`
  turns a dropped reference image into a `ReferenceDescription` whose style text is folded into the **Extra-direction**
  box, so the next generation matches the look.
- **Prompt seeds.** `GenerationSeed` + `GenerationSeedLibrary` (5 built-ins) are named style bundles; user seeds persist
  in `AppSettings.GenerateSeeds`.
- **Quality knobs.** `GenerationRequest.Avoid` (an "avoid" field), an **auto-retry** once on a structurally weak first
  take, and a **show-the-prompt** expander backed by `IAssetGenerationService.BuildPrompts`.

---

## 12. Drag-and-drop (the `avalonia-drag-drop-files` pattern)

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

## 13. Threading model

- **Rendering / re-stacking** (CPU-bound, pure) runs **off the UI thread** via `Task.Run`
  in the export/restack commands; the `await` resumes on the UI context to update
  `StatusMessage`.
- **Batch** runs its *entire* loop off the UI thread (`Task.Run`); progress marshals back
  via a UI-thread-created `Progress<T>` (§8).
- **Generate** builds its preview off the UI thread in one `Task.Run` (`BuildPreview`, §11);
  only the finished bitmap assignment returns to the UI context.
- **Preview** (Create) renders **synchronously on the UI thread** — it is cheap (display-sized,
  supersample capped at 2, or 1 while the crosshair is on). The auto-play `DispatcherTimer`
  (33 ms) nudges `PreviewValue` on the UI thread; the crosshair drag coalesces renders to
  one per UI cycle (§7.3).
- View models touch only their own state on the UI thread; the engine services hold no
  shared mutable state, so off-thread renders are safe with locally-captured inputs.
- No `async void` except event handlers; all `[RelayCommand]` async methods return `Task`;
  no `.Result`/`.Wait()`.

---

## 14. SkiaSharp ↔ Avalonia interop

The engine works entirely in `SKBitmap` (premultiplied `Rgba8888`). Loading uses
`SKBitmap.Decode` (premultiplied RGBA by default — exactly the layout the renderer
composites in). Export uses `SKImage.Encode(Png, 100)`. For display,
`SkiaImageInterop.ToAvaloniaBitmap` does a PNG round-trip in memory into an Avalonia
`Bitmap`. The preview `Image` uses `RenderOptions.BitmapInterpolationMode="HighQuality"`
and `Stretch="Uniform"`.

---

## 15. The standalone engine (`FilmstripEngine.cs`)

The repo-root `FilmstripEngine.cs` is a **single-file, copy-paste-portable** copy of the
core renderer in namespace `StripKit.Engine`, with only a SkiaSharp dependency. **It is
not part of the build** (it lives outside `src/` and no project compiles it) — it exists
so the engine can be dropped into a CLI, a build step, a web backend, or another app
unchanged.

It contains exactly the **render math**: the `ComponentType` (incl. `Button` and `Toggle`), `MeterFillDirection`,
`StackDirection`, **`StripLayout`** (§5.3), **`FrameMappingCurve`** (§5.1), and `LayerBehavior` (incl. `Frame`)
enums; the `FrameTransform` struct and the `RenderLayer`
class; `FilmstripSettings` (including the `SourceCenterX/Y` alignment fields, the meter fields,
the value-arc fields, the `Layers` list with its deep `Clone`, the **`Layout`/`GridColumns`** sprite-grid
fields, and the **`MappingCurve`/`MappingSkew`/`MappingLogBase`** parameter-law fields + the **`MapT`**
remap method itself); and `IFilmstripRenderer` +
`SkiaFilmstripRenderer` (including `ComputeTransform`, `RenderFrame`, `RenderStrip` — with its **R×C
grid-packing branch** — the full
`RenderMeterFrame` procedural/layered path, `RenderValueArc`, the `RenderLayers`
base+pointer path, and the `RenderButtonLayers` button/toggle state-frame path — the renderer's
`Button` case also handles `Toggle`; all four `t`-computation sites route through `MapT`). It does **not**
include the
app-only services — `ContentAnalysis`, `FilmstripImporter`, `ManifestService`,
`BatchProcessor`, the I/O services, or any view-model/view — by design.

> **Maintenance hazard:** it duplicates `Services/SkiaFilmstripRenderer.cs` +
> `Models/{FilmstripSettings, FrameTransform, ComponentType, StackDirection,
> MeterFillDirection, RenderLayer, StripLayout, FrameMappingCurve}`. If the in-app renderer's math
> changes (transform,
> supersampling, meter fill, alignment, layers, grid packing, parameter-law mapping), update this file to
> match (or it silently
> drifts). As of this audit the two are in sync — the alignment `SourceCenterX/Y` pivot, the
> meter path, the `RenderValueArc` value-arc path (with its arc fields), the `RenderLayers`
> base+pointer path, the `RenderButtonLayers` button/toggle state-frame path (with the `RenderLayer` /
> `LayerBehavior` Static/Rotate/Frame types and the `Layers` field), the **sprite-grid R×C packing**, and
> the **parameter-law `MapT` remap** are
> all present here.

---

## 16. Conventions & invariants (do not break)

- **Filmstrip:** frames stack **vertically** by default; frame 0 = min, frame N−1 = max.
  Rotary angle for frame i = `Start + (End − Start)·i/(N−1)` (the `(N−1)` divisor is
  intentional). Default sweep 270° (−135° → +135°). 32-bit RGBA, transparent bg. Standard
  counts 32/64/128. Ship `@2x` for HiDPI. ~10% transparent margin on knob art so corners
  don't clip on rotation.
- **New render paths gate behind a byte-identical default.** Sprite-grid layout defaults to
  `StripLayout.Strip` (§5.3) and parameter-law mapping defaults to `FrameMappingCurve.Linear`
  (§5.1) — `Linear` is a **true no-op** (returns the input completely unchanged, not just
  numerically equal), so every prior golden image stays byte-for-byte identical until a user
  opts in. This is the same discipline the meter peak-marker (`ShowMeterPeak`) and the value
  arc (`ShowValueArc`) already follow — extend it to any future render-path addition.
- **A manifest field with a JSON-Schema `minimum` needs its own clamp at the `ManifestService`
  call site.** The renderer clamping a value internally (e.g. `Math.Max(1, settings.GridColumns)`
  in `RenderStrip`) does **not** protect the exported manifest — `ManifestService` must clamp
  independently before serializing. BUG-017 was exactly this gap for `GridColumns`; see
  `docs/BUGS.md`.
- **Alignment:** rotation/centring is about the art's **content centre** (`SourceCenterX/Y`);
  `(0.5, 0.5)` reproduces classic rectangle-centred output. The bounding-box centre (not a
  centroid) is the detection method. Keep the defaults to preserve existing output.
- **MVVM:** view models never reference Avalonia UI types (the preview `Bitmap` alias is the
  one allowed presentation type). Code-behind holds only view concerns (timers, drag-drop,
  the crosshair drag, opening About links). Source-generator classes are `partial`.
- **Design tokens (Depth machined-grey, dark; rebranded from Obsidian glass in v1.4.0):** ember accent
  `#f25914`; **sans for labels/body** (`Verdana, Segoe UI, Arial`) and **monospace for numerics only**
  (`JetBrains Mono, Consolas, …` — `NumericUpDown` + numeric readouts). The Depth tokens are vendored in
  `src/StripKit/Depth/Depth.axaml` (`DepthBg`/`DepthChrome*`/`DepthInset`/`DepthLine*`, `DepthInk*`,
  `DepthEmber*`, `DepthRadius*`, `DepthRaise*`) and **mapped** onto StripKit's keys in `App.axaml`:
  `AccentBrush`/`AccentHiBrush`, `Text1/2/3Brush`, the `GlassFill*`/`GlassBorder*` surface keys (now
  solid greys), `AccentGradient(Hover)` (ember face), recessed input wells (`ComboBox*`/`TextControl*`
  background+border keys → `DepthInset`/`DepthLine`, focused border = ember) + the dropdown/menu +
  checkbox/slider keys, `SectionTextBrush` (→ `DepthInkDim`, light on the dark panel), `ControlCornerRadius`
  6 / `OverlayCornerRadius` 8, and `DialogFillGradient`. A full `Button` `ControlTheme` plus global
  `Button:not(.accent)` styles give neutral **raised "keycap"** buttons (a `DepthButtonFace` + bevel +
  drop shadow, lighter on hover, `translateY` on press) and an `.accent` (ember-face) primary variant.
  Style classes: `Border.card`/`.tile`/`.divider`, `TextBlock.section`/`.label`, plus the `SectionHeader`
  control. The window is a **solid `DepthBg`** base — the old acrylic (`ObsidianAcrylic`
  `ExperimentalAcrylicMaterial`, `TransparencyLevelHint="AcrylicBlur"`, the `ExperimentalAcrylicBorder`
  frost + the warm radial glow) was removed, so every tab reads uniform. Re-use these mapped keys / Depth
  tokens; don't hard-code hex, and keep monospace to numerics. *(The renderer's procedural meter/arc
  default colours, §5, are still packed `0xFFE8440A` in `FilmstripSettings` — a model default, not a UI
  token, so the rebrand left them unchanged to keep the goldens byte-identical.)*
- **C#:** `Path.Combine` (never raw separators); no `System.Drawing`; no `.Result`/`.Wait()`;
  `async void` only for event handlers (all commands are `async Task` via `[RelayCommand]`).
- **Do not** rewrite `SkiaFilmstripRenderer` or re-scaffold the project; extend it.

---

## 17. Data-flow walkthroughs

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

## 18. Tests (`tests/StripKit.Tests`)

xUnit + NSubstitute + FluentAssertions + `Avalonia.Headless` (coverlet.collector `6.0.4` for
coverage; CI on `actions/checkout@v5` + `actions/setup-dotnet@v5`, Node 24). Coverage spans:
golden-image renderer baselines (`RendererGoldenTests`), alignment renders (`AlignmentRenderTests`),
meter renders (`MeterRenderTests`), value-arc renders (`ValueArcRenderTests` — golden
baselines incl. gradient+glow, plus pixel-logic for the lit-sweep growth and the off /
non-knob no-ops), layered-knob renders (`LayeredKnobRenderTests` — base+pointer golden
baselines plus pixel-logic for the rotating pointer / static body, the empty-layers fallback,
the pointer pivot, and the non-knob no-op), button/toggle state-frame renders (`ToggleRenderTests`),
layered import (`LayeredImportServiceTests`, `LayeredImportRenderTests`, `LayeredImportViewModelTests` —
incl. the BUG-010 `SafeXml`-before-`FromSvg` hardening + the SVG/PSD size caps), image-load caps
(`ImageLoadServiceTests` — the > 64 MP rejection), code-snippet generation (`CodeSnippetServiceTests` —
per-target control class / draw method incl. the button/toggle latching binding, the frame math,
stack-axis source, identifier sanitisation, file names, `SaveAsync`), `ContentAnalysisTests`,
`PointerExtractorTests`, importer engine + VM (`FilmstripImporterTests`, `ImporterViewModelTests`),
manifest mapping/JSON-Schema (`ManifestServiceTests`), the Skin tab (`SkinViewModelTests`), batch
processor + VM (`BatchProcessorTests`, `BatchViewModelTests`), tutorial + settings
(`TutorialViewModelTests`, `SettingsServiceTests`), the Generate-tab pipeline (`SvgSanitizerTests`,
`SecretStoreTests`, `AssetGenerationProviderTests`, `CustomOpenAiProviderTests`, `VisionProviderTests`
— the per-provider `DescribeImageAsync`, `AssetGenerationServiceTests` — incl. set / variations /
refine, `GenerateViewModelTests` — incl. the editable/delisted-model test, `GenerateViewTests`,
`GenerateIntegrationTests`), the Create-tab load path (`LoadPathTests`), and a headless
`DropZoneViewTests` (a synthetic OS drag gesture isn't constructable headlessly, so drop is
covered by the VM load-path + `AllowDrop`-wiring assertions). `TestAppBuilder`/`TestImages`/`ImageAssert`/
`TestFakes` (incl. the `MainVm(ISettingsService)` helper) are the harness. Also: parameter-law
frame-mapping math + renderer integration (`ParameterLawMappingTests` — golden `knob_skew_mid`)
and save/load render presets (`RenderPresetTests` — JSON round-trip + VM command behaviour), plus
grid-layout additions to `RendererGoldenTests` (golden `knob_grid8x4`), `ManifestServiceTests`, and
`CodeSnippetServiceTests`; and the one-click **Build kit** builder (`KitBuilderTests` — real
render + skin.json assembly for a mixed kit). **346 green.** See `docs/TESTING.md` for the methodology and the current count.

---

## 19. Extension points

- The **manifest** model already supports multi-control skins, per-control/window
  backgrounds, and value ranges — surface them when a multi-asset workflow lands.
- **Importer** frame-count *resampling* (re-timing a strip to a different count) and meter
  **peak-hold / stereo** are noted as unbuilt in `docs/ROADMAP.md`.
- Preview interop could move to a reused `WriteableBitmap` if very high-frame-rate playback
  is ever needed (§13).
- Phases 5–7 (batch, meter, packaging) are **done**; packaging detail lives in
  `docs/PACKAGING.md`, intentionally out of this app-internals reference.
