# CHANGELOG — StripKit

> Version 1.5.1 · last-updated 2026-07-04 · last-audit 2026-07-04
>
> Notable changes per doc/feature version. Dates are authoring dates; several
> versions landed on 2026-06-03 across one working stretch.

## [Unreleased]

### Added
- **One-click "Build kit" on the Generate tab.** After generating a matching set of controls, a new
  **Build kit…** button renders the whole family to filmstrip PNGs (+ @2x) and writes a multi-control
  `skin.json` binding them together — from a single folder pick. Each control is prepared through the
  same per-type path the Create tab uses (layered knob body+pointer, button/toggle state frames, a
  flattened fader/slider cap, a meter's off/on reveal), so the kit output matches what Create would
  export; the skin lays the controls out in a non-overlapping row. New app-only `Services/KitBuilder.cs`
  (`IKitBuilder`) + `Models/KitModels.cs`; no renderer-math change, so nothing mirrored in
  `FilmstripEngine.cs`. Adversarially reviewed before landing — fixed a `CanExecuteChanged` gap that
  left the button greyed out on the happy path, a shared-CTS race where Regenerate could cancel a build,
  and two exception-path bitmap leaks. +11 tests (suite 335 → 346).
- **Matching-kit polish.** The Generate tab's set section is now **MATCHING KIT** with a **Select all /
  Clear** shortcut for the type picker, a **Show in folder** button that opens the built kit with its
  `skin.json` selected, and copy that walks you from Generate set → Build kit; the in-app Getting
  Started walkthrough covers the Build-kit flow. +1 test (suite 346 → 347).

## [1.5.1] — 2026-07-04

### Fixed
- **Assemble tab now ingests HDR frames on every load path.** Dragging a `.exr` / `.hdr` / 16-bit
  `.tif` sequence onto the preview — or picking it with "Add files…" — silently dropped the frames;
  only "Choose folder…" accepted them. Three accepted-extension lists had drifted apart; they now share
  one, so drag-drop and the file picker accept exactly what "Choose folder…" does (BUG-021, +2 tests).

### Changed
- **In-app Getting Started walkthroughs expanded to match the current app** (a docs-audit pass): the
  Create walkthrough now surfaces the Ctrl+O / Ctrl+E shortcuts and the Render Recipe panel; Import
  covers the transport controls and that resample's Target frames is separate from the slice Frame
  Count; Batch notes buttons/toggles aren't batchable there; Skin mentions the stack-direction and
  @2x-file fields; Generate warns that switching provider resets the model and that a weak first take is
  auto-retried, and points at Copy SVG; Assemble mentions the frame-list add/reorder/remove controls.

## [1.5.0] — 2026-07-03

Shipping as **v1.5.0**. The headline work is the **offline-3D / path-tracing pipeline** (P1–P5) and a
full **Depth UI rebrand** — P1 adds a sixth tab that turns a pre-rendered frame sequence into a
plugin-ready filmstrip; P2 exports the render *recipe* that makes the offline frames line up with
StripKit's frame math; P3–P5 add render QC, HDR ingest, frame interpolation and an emission pass; and
the rebrand gives every tab one machined-grey, ember-accent look. On top of that, a **batch of twelve
quality-of-life enhancements** (a React export target, dithered HDR de-band, window/tab persistence,
keyboard shortcuts, Batch-tab loader code, a CI coverage gate, "show in folder", arbitrary @Nx HiDPI,
a meter peak-marker, sprite-grid layout, parameter-law frame mapping, and save/load render presets)
rounds out the release.

### Website
- **Path-traced (or any pre-rendered) frames → a filmstrip, in one step.** Rendered your knob as a
  sequence of images (Blender, KeyShot, Octane, an export from another tool)? The new **Assemble** tab
  stacks the whole folder into a single filmstrip — drop it in, check the order, export. Pay the
  expensive lighting/anti-aliasing cost once, offline, then ship a strip that runs anywhere.
- **A render recipe that matches StripKit's frame math.** Path-tracing a knob? Export a Blender script
  (or a CSV/JSON table) so your offline render lands each frame on exactly the right angle — then stack
  the result on the Assemble tab. No guesswork, no drift.
- **Catches common render mistakes.** Assembling a rendered sequence now flags object drift, a
  missing transparent background, blank frames, or dark edge halos — and offers one-click fixes.
- **A new look.** A refined dark, precision-instrument interface — consistent across every tab.
- **Reliability + polish.** The playback transport now looks and behaves the same on every tab, and a
  pre-release audit hardened file import (a crafted SVG can no longer make the app phone home), fixed a
  colour shift in the un-premultiply cleanup, and squared away layered button/toggle state art.
- **More of the path-tracing pipeline.** Assemble now reads **EXR / 16-bit HDR** frames straight from
  Blender / KeyShot / Octane (tone-mapped for you), can **blend in-between frames** to turn ~32 rendered
  frames into a smooth 64/128, and can **add an emission / glow pass** so lit parts read like real light.
- **A batch of quality-of-life upgrades.** More export targets (now including **React** web components),
  sharper HDR imports (banding cleaned up), StripKit **remembers your window size and last tab**,
  **@3x / @4x** exports for the sharpest displays, a **meter peak marker**, keyboard shortcuts, loader code
  straight from the Batch tab, and a **"Show in folder"** button after every export.
- **Pack frames into a grid instead of a strip.** A new "Sprite layout" option stacks frames into an
  R×C grid sheet — for loaders that expect a 2D atlas instead of a single long strip.
- **Match your plugin's real parameter feel.** A new "Parameter law" curve (linear / skew /
  logarithmic) lets the visual sweep track a log-frequency or dB-style parameter instead of a
  straight-line divisor.
- **Save your setup as a preset.** Save the current render setup — type, frames, sweep, layout,
  meter/arc styling, export options — as a named preset and reload it in one click next time.

### Added
- **Assemble tab.** Choose a folder (or drag-drop) of individually-rendered frames; StripKit
  natural-sorts them (`frame_2` before `frame_10`, padded or not) and packs them into one stacked
  filmstrip. Reconcile odd-sized frames (pad to largest / crop to smallest / strict), optionally
  re-centre each frame on its content (fixes a 3D object that drifts between frames), optionally
  re-time to a standard 32 / 64 / 128 count (nearest-frame, the importer's law), and export @2x +
  `skin.json` + JUCE/CSS/iPlug2/HISE loader code — the same outputs as the Create tab. Assembly runs
  off the UI thread with progress + cancel.
- New `Services/FrameSequenceAssembler` (pure SkiaSharp) + `NaturalFileNameComparer`,
  `Models/FrameSequenceModels`, `ViewModels/FrameSequenceViewModel` (+ `FrameItemRow`),
  `Views/AssembleView`; `IImageLoadService.Probe` (header-only dimension peek) and
  `IFileDialogService.OpenImagesAsync` (multi-select). The procedural renderer is untouched — no
  `FilmstripEngine.cs` change. **+28 tests (suite 216 → 244), build 0/0.**
- **Render-recipe export (path-tracing P2).** A "Render recipe" panel on the Create tab emits a
  Blender `bpy` script (transparent film; a keyframe baked on **every** frame so the angles are exact,
  plus a 0..1 `value` custom property to drive non-rotary rigs) and an engine-agnostic
  `frame,value,angle_deg` CSV/JSON table for KeyShot / Octane / C4D. New `Services/RenderRecipeService`
  + `Models/RenderRecipeModels`, mirroring `CodeSnippetService`; one `BuildFrameTable` shares the
  renderer's deliberate `(N−1)` law so the recipe and the runtime can never diverge. **+14 tests
  (244 → 258), build 0/0.**
- **Render QC on import (path-tracing P3).** The Assemble tab now catches the path-tracer failure
  modes: a **"Check frames"** pre-flight (and the assemble result) reports object **drift** between
  frames (with a "tick Re-centre" nudge), frames with **no transparency** (a missing transparent
  background) or **none at all** (a failed render), and **premultiplied edges**; an **"Un-premultiply
  alpha"** toggle divides RGB by alpha to remove dark edge halos. New `FrameSequenceAssembler.AnalyzeQc`
  / `UnpremultiplyAlpha` + `Models.RenderQcReport`, pure SkiaSharp. **+7 tests (258 → 265).**
- **Render-recipe entry-point on the Assemble tab.** Plan a render where you assemble it — the same
  Blender/CSV/JSON recipe export, driven by the Assemble component type + frames/sweep/size inputs.
- **Depth design-system rebrand.** The whole app moved onto the Depth design tokens (vendored
  `src/StripKit/Depth/Depth.axaml`): solid machined-grey surfaces, an ember accent, recessed
  monospace-numeral input wells, raised neutral "keycap" buttons, and a solid window base (the acrylic
  glass + warm glow removed) — uniform across all six tabs and the dialogs, plus a new VYBECODE DSP
  brandmark + wordmark header.
- **Crosshair fix.** Enabling the alignment crosshair keeps the image stationary while you drag the
  mark onto the knob's true centre; on release, playback rotates about that point.
- **Frame interpolation — "render fewer, ship more" (path-tracing P4).** A new re-time method on the
  Assemble tab: **Crossfade** cross-dissolves the two bracketing frames to synthesise in-betweens (the
  `(N−1)/(M−1)` law keeps the real endpoints), so ~32 expensive path-traced frames can ship as 64/128 for
  slow, smooth motion. `FrameInterpolation` {Nearest, Crossfade} + a Method combo. +2 tests (274 → 276).
  *(Optical-flow interpolation is a future v2.)*
- **Emission / glow AOV pass (path-tracing P5).** The Assemble tab can take a second render pass — an
  emission/glow AOV, one frame per beauty frame — and additively composite it over the beauty frames, so
  a path-traced glow (a knob LED, a lit screen) reads like emitted light instead of baked-flat. A folder
  picker + intensity slider; a mismatched frame count is ignored with a warning. +2 tests (276 → 278).
- **16-bit / EXR HDR frame ingest (path-tracing P3b).** The Assemble tab ingests `.exr` / `.hdr` / 16-bit
  `.tif` frames — the native output of most path tracers. Swapped **Magick.NET-Q8 → Q16-HDRI** so an EXR
  is tone-mapped (linear → sRGB + clamp) before the 8-bit reduction; OpenEXR is bundled (no extra
  install). New `Helpers/MagickPixels` downshifts Magick's 16-bit pixels to 8-bit (also fixes the PSD
  reader under the new quantum depth). +2 tests (278 → 280). *(A dithered de-band and multi-layer EXR are
  future work.)*

### Fixed
- **Uniform preview transport across every tab** (Create / Import / Assemble render the play/scrub
  controls identically), plus the inactive-tab hover colour and header/nav alignment, and a new
  "Extract current frame" action. **+1 test (`TransportTileAlignmentTests`; 265 → 266).**
- **Pre-release audit hardening (11 fixes).** A fine-tooth-comb audit of the v1.4.0 work fixed:
  - **`UnpremultiplyAlpha` corrupted colours** — it returned a premultiplied-tagged bitmap holding
    straight bytes, so the P3 "Un-premultiply alpha" halo fix shifted colour on assemble/export. It now
    returns an un-premultiplied bitmap.
  - **SVG file-import SSRF** — the layered-file picker handed raw SVG to Svg.Skia without the sanitizer,
    so an external `<image xlink:href="http://…">` (or `file://`) fired an outbound request during
    rasterization. `SvgSanitizer.Sanitize` now runs on the import path before `FromSvg`.
  - **Button/toggle state art** — a shared `Static` border/shadow layer shifted (and blanked) the off/on
    frames because `Frame` layers matched by absolute stack index. They now match by ordinal among Frame
    layers (renderer + `FilmstripEngine.cs`), and the state-frame count ignores Static layers.
  - Regenerating one matching-set item after a cancel works again; QC drift is measured in absolute
    pixels (no phantom drift on mixed-size sequences); three resource leaks closed (set/variation preview
    bitmaps, an auto-retry temp SVG, the provider `HttpResponseMessage`); and the Getting-Started tip box
    renders (it referenced `GlassFill`/`GlassBorder`, undefined after the Depth rebrand → `*Brush`).
  - **+8 tests (266 → 274), build clean.** See `docs/BUGS.md` (BUG-012…015).

### v1.5 enhancements (twelve quality-of-life items)

A batch of twelve small enhancements bundled into the v1.5.0 release. The first nine landed on
`origin/main` (suite 280 → 288); a further session finished the remaining three (sprite-grid
layout, parameter-law frame mapping, save/load render presets) plus a 4-dimension adversarial
review that caught and fixed 2 more issues (BUG-017/018 — see `docs/BUGS.md`). **Suite
288 → 331 green, build clean.** All twelve items shipped in v1.5.0 (the suite has since grown to
**335** with the v1.5.1 HDR-drop fix).

#### Added
- **React / web-component code-export target.** A new `CodeTarget.React` emits a `.jsx` sprite
  component driven by a `value` prop (0..1), following the same universal
  `frame = clamp(round(value·(N−1)), 0, N−1)` rule as the other targets. Wired into the Create,
  Assemble, **and** Batch code-export panels. **+3 tests (280 → 283).**
- **Arbitrary HiDPI export scale (@2x / @3x / @4x).** A `HiDpiScale` property across the
  Create / Assemble / Batch tabs (default 2) drives the export-filename suffix `@Nx`, the
  render/upscale factor, and the manifest hi-res asset in lockstep — no longer fixed at @2x.
  **+1 test.**
- **Meter peak-marker.** A new `FilmstripSettings.ShowMeterPeak` + `PeakColorArgb` (mirrored in
  `FilmstripEngine.cs`); `RenderMeterFrame` paints the direction-aware leading (peak) segment.
  **Gated OFF by default, so every existing meter golden is byte-identical.** **+1 pixel-logic test
  (→ 288).**
- **Batch tab → loader code.** `BatchOptions.CodeTargets`; `BatchProcessor` takes
  `ICodeSnippetService` and emits the JUCE / CSS / iPlug2 / HISE / React snippet(s) per strip —
  parity with the Create and Assemble tabs. **+1 test.**
- **"Show in folder" after export (Create + Assemble).** A new `Helpers/ShellHelper.RevealInFolder`
  behind a `RevealExportCommand` + `LastExportPath` on the VMs. The button sits **outside** the
  `TransportTile` Border to preserve the transport-tile-height invariant.
- **Keyboard shortcuts.** `Ctrl+O` (open) / `Ctrl+E` (export) via `Window.KeyBindings`
  (Ctrl-modified only).
- **Sprite-grid layout (R×C).** A new `StripLayout` enum (`Strip` default / `Grid`) +
  `FilmstripSettings.Layout`/`GridColumns`; `RenderStrip` packs frames into a row-major R×C sprite
  atlas when selected (mirrored in `FilmstripEngine.cs`), gated so `Strip` stays byte-identical.
  `ManifestControl` gained nullable `Layout`/`GridColumns` (omitted unless grid — schema doc updated
  in the `plugin-asset-manifest` skill). All 5 code-export targets got grid-aware column/row math
  except iPlug2 — its built-in `IBitmap`/`LoadBitmap` can only read a 1D strip, so it emits an
  explicit warning comment instead of silently mis-reading a 2D atlas. A "Sprite layout" combo +
  conditional "Grid columns" input on the Create tab. **+16 tests, new golden `knob_grid8x4`.**
- **Parameter-law frame mapping (log/skew).** A new `FrameMappingCurve` enum
  (`Linear`/`Skew`/`Logarithmic`) + `MappingCurve`/`MappingSkew`/`MappingLogBase` on
  `FilmstripSettings`, plus a `MapT(t)` remap applied at all four renderer sites that compute the
  sweep fraction (`ComputeTransform`, `RenderLayers`, `RenderMeterFrame`, `RenderValueArc`), so
  knobs, layered knobs, meters, and the value arc all honour the curve consistently. `Linear` is a
  true no-op — returns the input completely unchanged — so every existing golden stays
  byte-identical; mirrored in `FilmstripEngine.cs`. A "PARAMETER LAW (advanced)" Create-tab section;
  the preview readout now reflects the mapped angle too. **+12 tests, new golden `knob_skew_mid`.**
- **Save / load render presets.** A new `RenderPreset` model (the full Create-tab render setup —
  type, frames, sweep, resolution, sprite layout, parameter-law curve, meter/value-arc settings,
  export preferences — deliberately excluding loaded art) persisted via `AppSettings.RenderPresets`.
  `ISettingsService` is now injected into `MainWindowViewModel`'s constructor (rippled into
  `TransportTileAlignmentTests`/`LoadPathTests`/`LayeredImportViewModelTests` + a new
  `TestFakes.MainVm()` helper). `SavePreset`/`ApplyPreset`/`DeletePreset` commands (save overwrites
  by case-insensitive name; apply bulk-restores everything in one suspended-refresh pass). A
  "PRESETS" section atop the Create tab's left panel. **+9 tests.**

#### Fixed
- **BUG-017 (medium):** `ManifestService.BuildSingleControl` could serialize a non-positive
  `GridColumns` into `skin.json`, violating the `plugin-asset-manifest` schema's `minimum: 1`. Now
  clamped with `Math.Max(1, …)`, mirroring the renderer's own defensive clamp.
- **BUG-018 (low):** `DeletePreset()` removed the selected preset from the UI's `Presets` collection
  by object reference but from the persisted `RenderPresets` list by name, so two duplicate-named
  presets (only reachable via a hand-edited settings file) could desync the two collections. The
  persisted-side removal is now reference-based too.
- Both found by a 4-dimension adversarial review of this session's diff before commit; both fixed
  and covered by regression tests in the same pass. See `docs/BUGS.md` for full detail.

### Live-testing fixes (post v1.5 wave)
- **BUG-019 (low):** the Import tab was missing the "Show in folder" button the Create and Assemble
  tabs already had, which looked inconsistent switching between tabs. Added the same
  `RevealExportCommand`/`LastExportPath` pattern to `ImporterViewModel` (set after extract / re-stack
  / resample) and the matching button to `ImporterView.axaml`.
- **BUG-020 (low):** the Getting Started overlay's 💡 tip text overflowed the dialog card instead of
  wrapping — a horizontal `StackPanel` gives its children unconstrained width along its axis, so
  `TextWrapping="Wrap"` had nothing to wrap against. Replaced it with a `Grid ColumnDefinitions="Auto,*"`.
- **Tutorial content revamp.** Every walkthrough reviewed for completeness against the current UI:
  Create grew from 5 to 7 steps (component types incl. Button/Toggle, sprite-grid layout &
  parameter-law mapping, render presets, HiDPI + "show in folder"); Generate grew from 4 to 6 steps
  (custom endpoints, matching sets & variations, refine & reference-image matching); Assemble grew
  from 3 to 7 steps (Render QC, crossfade interpolation, the emission/glow pass, the render-recipe
  planner, HiDPI + code export). Import and Batch got minor wording touch-ups for parity.

#### Changed
- **Dithered HDR de-band (finishes path-tracing P3b).** A new `Helpers/MagickPixels.DitherDownTo8`
  (an 8×8 Bayer ordered dither) runs in `ImageLoadService.LoadHdr`, so EXR / 16-bit ingest reduces to
  8-bit without banding. **+2 tests.**
- **Remember window size + last tab.** New `AppSettings.WindowWidth` / `WindowHeight` /
  `LastTabIndex`, restored and persisted in `App.axaml.cs` at the composition root.
- **CI coverage gate.** `.github/workflows/ci.yml` now collects coverage and **fails the build below
  70% line coverage.**

## [1.3.0] — 2026-06-18

The biggest Generate-tab release yet: a full AI-generation workflow, plus meters and on/off toggles
as first-class controls — and a security fix for SVG import.

### Website
- **Design a whole control set from one prompt.** The new **matching-set generator** makes a
  consistent family — knob, fader, slider, meter, button, toggle — in a single click, all sharing your
  style, colours and effects. A head start on a complete plugin skin.
- **More ways to get the art you want.** **Variations** (several takes at once, pick the best),
  **Refine** ("thicker pointer, warmer accent"), **Match a reference image** (describe a screenshot you
  like), a **seeds library** of reusable style presets, and an **"avoid"** box.
- **Use your own AI service.** A new **Custom** provider points StripKit at any OpenAI-compatible
  endpoint — OpenRouter, or a local **Ollama / LM Studio** server — alongside Claude, OpenAI and Gemini.
- **Generate meters and toggles too.** Meters (vertical *or* horizontal) and on/off toggle switches now
  generate, import, and export just like knobs and buttons.

### Added
- **Matching-set generator.** Pick the control types and **Generate set** produces the whole family at
  once — every control generated concurrently from the *same* style/colours/effects/avoid-list, in a
  results grid with per-item **Use in Create**, **Save**, **Regenerate**, plus **Save set…** to a folder.
- **Variations grid.** Generate several takes (2/4/6/8) of the selected control at once and pick the best.
- **Refine.** Revise the current result with a plain-language instruction; the SVG goes back to the
  model keeping its structure.
- **Reference-image match (vision).** "Describe a reference image…" runs a screenshot through a vision
  model (Claude / OpenAI / Gemini / compatible) and folds the style description into Extra direction.
- **Prompt seeds library.** Reusable named style bundles — 5 built-ins (Vital/Serum modern, Vintage
  hardware, Minimal flat, Skeuomorphic metal, Neon glow); save and reuse your own.
- **Custom AI endpoint.** A "Custom" provider for any OpenAI-compatible chat-completions endpoint
  (OpenRouter / Ollama / LM Studio) with Bearer auth and a saved base URL.
- **"Avoid" field**, **auto-retry** on a structurally weak first take (knob with no pointer; button /
  toggle / meter missing a state), and a **"Prompt to be sent"** expander showing the exact prompt.
- **Toggle control type.** A first-class on/off toggle — distinct from the button — that generates
  (switch-style off/on art), imports (auto-detected from off/on layer names), builds in the Create tab,
  and code-exports a latching toggle (JUCE `setClickingTogglesState`, iPlug2 `IBSwitchControl`).
- **Meter generation + horizontal meters.** The Generate tab makes meters as an unlit `off` + fully-lit
  `on` pair (the handoff wires `off` → background, `on` → the revealed source); a "Horizontal meter"
  option produces a wide left→right meter, with the fill direction inferred from the art's aspect.

### Fixed
- **SVG file-import hardening (BUG-010).** A crafted `.svg` opened via the layered-file picker could
  trigger a "billion-laughs" entity-expansion DoS — the BUG-009 hardening ran *after* Svg.Skia had
  already parsed the raw text. `SafeXml.Parse` now runs first, so a DTD is rejected before the
  renderer's parser ever sees it (the AI-reply path was never affected).
- **Input-size guards.** Decompression-bomb images are rejected on load (header dimensions peeked via
  `SKCodec`, capped at 64 MP); imported SVG text is capped at 20 MB and PSD canvases at 64 MP.
- **Control-type manifest mapping.** Buttons and toggles now map to `"button"` / `"toggle"` in
  `skin.json` instead of silently falling through to `"knob"`.

## [1.2.2] — 2026-06-14

### Website
- **Type any model ID in the Generate tab.** The model field now accepts free text *and* the
  suggested list, so you can use a brand-new or private model the moment your provider ships it —
  and if a listed model is ever retired, just type the replacement instead of being stuck.
- **Snappier generation.** Building the preview no longer briefly freezes the window on large
  canvases — the work now happens in the background.

### Added
- **Editable model input.** The Generate tab's model picker is now an `AutoCompleteBox` (free text +
  suggestions) instead of a fixed dropdown, so a custom/just-released model id can be typed and a
  pinned-but-delisted model displays as text rather than a blank box.

### Changed
- **Preview build moved off the UI thread.** The temp-write + layered import + composite + PNG-encode
  now run in one `Task.Run`; the UI thread only assigns the finished bitmap, so a large canvas no
  longer hitches the dispatcher. Generated temp SVGs no longer accumulate (the prior one is dropped
  each generation).

### Fixed
- **Release tooling.** The release script now aborts if tracked source is uncommitted (`-AllowDirty`
  to override) so feature source can't be orphaned from its tag; Stage 3's website-changelog push
  used array splatting that mis-bound `-Push` — switched to hashtable splatting.
- **CI future-proofing.** `actions/checkout` and `actions/setup-dotnet` bumped to `v5` (Node 24,
  ahead of the June 16 2026 forcing); `coverlet.collector` `6.0.2 → 6.0.4`.

## [1.2.1] — 2026-06-14

### Website
- **Generated faders, sliders & buttons now work end-to-end.** Picking a non-knob type in the Generate
  tab and clicking "Use in Create" now builds the right control — sliders slide, buttons toggle between
  states — instead of being treated as a knob.
- **Safer SVG handling.** Imported and AI-generated SVGs are now parsed defensively, so a malformed or
  hostile file can't hang the app.
- **Generation warnings.** The Generate tab tells you when a knob came back without a moving pointer (or a
  button without both on/off states) so you can regenerate before building the filmstrip.

### Fixed
- **Generate → Create handoff now honours the generated control type.** Previously the handoff
  always forced `RotaryKnob`, so a generated **fader/slider/button** produced broken output —
  faders and sliders *rotated* instead of sliding, and buttons stacked both states on top of each
  other. The handoff now branches on the generated type: a **knob** maps to the body + pointer
  layer stack; a **button** maps its `off`/`on` groups to `LayerBehavior.Frame` state layers; a
  **fader/slider** is flattened to the single source the linear renderer expects.
- **Security: hardened untrusted-SVG XML parsing.** Both the AI-reply sanitizer (`SvgSanitizer`)
  and the layered-file import picker now parse SVG through a new `SafeXml.Parse`
  (`DtdProcessing.Prohibit`, no `XmlResolver`, `MaxCharactersFromEntities = 0`), closing an
  entity-expansion denial-of-service ("billion laughs") and external-entity / SSRF probes on
  attacker-influenced input. Legitimate generated control art has no DTD, so the happy path is
  unaffected (a DTD now throws, which both callers already treat as "malformed SVG").
- **Removed the CommunityToolkit + Avalonia double-validation.** Added the missing
  `BindingPlugins.DataValidators.RemoveAt(0)` in `App.axaml.cs` so validation errors are not
  reported twice. The Generate tab now also **warns** when a generated knob has no rotating
  pointer, or a button is missing one of its on/off states.

## [1.2.0] — 2026-06-09

### Website
- **Button filmstrips.** Create multi-state button art — off, on, hover, and pressed — the same way you make knobs and faders. Import a layered file with groups named `off` and `on` and StripKit assigns the states automatically.
- **AI generates every control type.** The Generate tab now works for faders, sliders, and buttons as well as knobs — pick the type, describe your style, and get ready-to-use layered art.
- **Fixed: colour pickers.** The body and accent colour swatches in the Generate tab now open a real colour picker. Before this release they were display-only and clicking them did nothing.

### Added
- **Button component type.** A new `ComponentType.Button` renders discrete-state filmstrips: frame 0 = off,
  frame 1 = on, and any additional frames for hover, pressed, or disabled states. The Create tab shows a
  **BUTTON STATES** section when this type is active, explaining the frame-state mapping and providing the
  layered SVG/PSD import. Import a file with groups named `off` and `on` — the importer auto-assigns
  `LayerBehavior.Frame` to those layers (shown only on their matching frame index). A `Static` layer on a
  button renders on every frame (useful for shared borders or shadows). The layer dropdown now offers
  **Static / Rotate / Frame**. Mirrored in `FilmstripEngine.cs`.
- **Generate tab: all control types.** The "WHAT TO MAKE" section is now a `ComboBox` (Rotary Knob /
  Vertical Fader / Horizontal Slider / Button). Knobs produce a layered `<g id="body">` + `<g id="pointer">`
  SVG; buttons produce `<g id="off">` + `<g id="on">`; faders and sliders produce a single `<g id="body">`
  cap/handle shape. The generation prompt is type-aware for each case.
- **`LayerBehavior.Frame`.** A layer tagged `Frame` is rendered only when its list index equals the current
  frame index — the mechanism behind button state layers. Pairs with the existing `Static` (all frames) and
  `Rotate` (knob pointer) behaviors. `LayeredImportService.Guess` now auto-assigns `Frame` to layers whose
  exact name is `"off"` or `"on"`.

### Fixed
- **Generate tab colour pickers now open a live colour picker on click.** The body and accent colour swatches
  were passive display-only `Border` elements — clicking them did nothing. They are now `Button` controls that
  open a `ColorView` flyout (Avalonia's built-in colour picker) pre-initialised from the current hex value;
  the VM property updates live as you drag the picker. The hex `TextBox` still works for manual entry.
  Added `Avalonia.Controls.ColorPicker` 11.3.0 as a direct project reference.

## [1.1.0] — 2026-06-07

### Website
- **AI-generated control art (use your own API key).** Paste in an OpenAI, Google Gemini, or Anthropic Claude key and StripKit generates a layered knob SVG for you — a static body plus a rotating pointer — ready to drop straight into your filmstrip. Your key is stored encrypted on your own machine, never sent anywhere else.
- **One-click handoff.** Preview the generated art inside the app, then click "Use in Create" to jump straight to the filmstrip builder with the generated layers already loaded.
- **Updated model list.** The Generate tab now shows a curated list of current, working model IDs for each provider. Also fixes a crash caused by a retired Gemini model.
- **Body & accent colour controls.** Set the knob's base colour and the accent highlight before generating, using the new colour swatches in the Generate tab.
- **Style effects.** Tick drop shadow, outer glow, bevel, or metallic sheen to include those effects in the generation prompt.

### Added
- **Generate tab — AI-generated SVG control art (your own API key).** A new **fifth tab** uses your
  own **OpenAI**, **Google Gemini**, or **Anthropic Claude** key to generate a **layered knob SVG** —
  a static `<g id="body">` plus a separate `<g id="pointer">` — which drops straight into the existing
  layered-import pipeline so only the pointer rotates (crisp at any resolution). One
  `IAssetGenerationService` builds a StripKit-aware prompt (square canvas, ~10% rotation margin,
  body+pointer groups, pointer drawn at 12 o'clock) and dispatches to one of three
  `IAssetGenerationProvider`s (Claude Messages / OpenAI Chat Completions / Gemini generateContent),
  all behind a shared `HttpClient`. The reply is carved down to a clean SVG by `SvgSanitizer` (strips
  scripts, event handlers, embedded raster, and off-document references) and **validated by importing
  it** — the preview is the real imported result, so what you see will import. **"Use in Create"**
  hands it to the Create tab (switches tabs + runs the layered import); **Save SVG** / **Copy SVG**
  reuse it anywhere. API keys are stored **encrypted per-user via Windows DPAPI** (`ISecretStore` /
  `DpapiSecretStore` → `%APPDATA%/StripKit/secrets.dat`, ciphertext only — never plaintext).
  Knob-first (the layered path is knob-only today; faders/sliders/meters follow). **App-only** — no
  renderer change, and **not** mirrored into `FilmstripEngine.cs`. New dependency:
  `System.Security.Cryptography.ProtectedData` 9.0.0. **+27 tests, suite 125→152.**
- **Generate tab: verified model dropdown.** Each provider now exposes a `SuggestedModels` list of
  current, validated model IDs; the model TextBox is replaced by a `ComboBox` that resets to the new
  provider's defaults when the user switches providers. Verified IDs: Claude (`claude-sonnet-4-6`,
  `claude-opus-4-8`, `claude-haiku-4-5-20251001`), OpenAI (`gpt-4o`, `gpt-4.1`, `gpt-4.1-mini`),
  Gemini (`gemini-2.5-flash`, `gemini-2.5-pro`, `gemini-2.5-flash-lite`). **Also fixes the Gemini
  crash**: `gemini-2.0-flash` was shut down 2026-06-01 — replaced with `gemini-2.5-flash`.
- **Generate tab: body colour + accent colour inputs with live swatches.** Two hex TextBoxes (body and
  accent) in a side-by-side layout, each with a `HexToColorBrushConverter`-backed swatch `Border` that
  updates as you type. Body colour folds into the generation prompt; an empty body field lets the model
  choose. `AccentColorHex` was already sent; now displayed next to the swatch too.
- **Generate tab: style effects checkboxes.** Four checkboxes — **Drop shadow**, **Outer glow**,
  **Bevel / 3D**, **Metallic sheen** — fold into the generation prompt as effect directives.

### Fixed
- **Off-centre knob wobble (content-centred placement).** The renderer now places the knob so its
  *content centre* lands at the frame centre, not the image-rectangle centre. An off-centre PNG (e.g.
  the bundled sample knob where the disc is off to one side) is now centred in the frame rather than
  left at its raw pixel position; the knob spins in place instead of orbiting. `ComputeTransform` uses
  `drawX = fw/2 − SourceCenterX·drawW` (identical to the old formula when `SourceCenterX = 0.5`, so
  all prior golden baselines are byte-identical). Mirrored in `FilmstripEngine.cs`. **+1 test**
  (`Centering_on_content_places_an_offcenter_knob_at_the_frame_centre`, suite →157).

## [1.0.0] — 2026-06-06

### Added
- **Interactive in-app help / tutorial (onboarding, P1).** A re-openable **"Getting Started"**
  guided overlay walks a new user through the core loop — load art → pick a type → align → frames
  & export → loader code → layered import — as an on-brand glass card (`TutorialOverlay` +
  `TutorialViewModel`). It **auto-opens on first launch** (remembered via a new minimal
  `ISettingsService` writing `%APPDATA%/StripKit/settings.json`) and is re-openable any time from a
  **"Getting started"** button in the header. Step 1 offers **"Load sample knob"** — a bundled
  `Assets/sample-knob.png` (via `IAssetService`) so a brand-new user sees the whole flow instantly.
  Plus **contextual tooltips** on the key controls (load / type / frames / export). No
  renderer/engine changes. **+11 tests, suite 112→123.**
- **Layered PSD / SVG import (★ #3, step 3 — completes the layer-aware bet).** A new
  **"Import layered file (SVG / PSD)…"** button in the Create tab's layered panel reads a real
  layered source and maps each layer onto the renderer's existing layer stack, so a designer
  drops a layered knob and gets a layered filmstrip with no hand-splitting. **SVG** groups are
  rasterized per-layer via **Svg.Skia** (MIT, SkiaSharp-native); **PSD/PSB** layers are read via
  **Magick.NET-Q8** (Apache-2.0). Each parsed layer becomes a row with a name and a behaviour the
  user can override (**Static** / **Rotate**), auto-guessed from the layer name
  (pointer/needle/indicator… → Rotate). The new `ILayeredImportService` is **app-only** (like
  `FilmstripImporter` / `PointerExtractor`) and **not** mirrored into `FilmstripEngine.cs` — the
  renderer math is unchanged, so it's gated behind defaults and every prior golden is byte-identical.
  Replaces the manual base/pointer slots when active (the two layered modes are mutually exclusive).
  **+14 tests, suite 98→112.** New dependencies: `Svg.Skia` 5.0.0, `Magick.NET-Q8-x64` 14.13.1
  (SkiaSharp pinned 3.119.0→3.119.2 to meet Svg.Skia's floor); the self-contained win-x64 installer
  grows by the ImageMagick native (~22 MB).
- **Auto-pointer extraction from flat art (★ #3, step 2).** An **"Auto-extract from flat
  knob…"** button in the Create tab's layered panel splits a single FLAT knob image (body +
  indicator baked together) into a static **base** + a rotating **pointer**, filling both
  step-1 slots automatically. The method is **radial-symmetry residual** (`PointerExtractor`):
  a knob body is rotationally symmetric, so the indicator is whatever breaks that symmetry —
  the rotational average per radius (computed robustly so the indicator doesn't pollute it) is
  the symmetric body, and the residual that deviates from each radial ring is the pointer. It
  reports a **confidence** (small concentrated residual → high; spread-out → low, flagged so
  the user falls back to manual). It's a starting guess the user verifies via the
  preview/scrub (assumes the art shows the indicator at the minimum/frame-0 position). Pure
  SkiaSharp, like `ContentAnalysis`; app-only (not in `FilmstripEngine.cs`). **+4 tests, suite
  94→98.** *(Step 3 — layered PSD/SVG import — landed above, completing the layer-aware bet.)*

### Fixed
- **About box version is no longer hardcoded.** It bound a stale literal ("v0.6.0"); it now binds
  the live assembly version (`MainWindowViewModel.AppVersion`, driven by the csproj `<Version>`), so
  it tracks every release bump automatically. **+1 test.**

## [0.8.0] — 2026-06-05

### Added
- **Importer frame-count resampling.** The Import tab can now **re-time** a strip to a
  different frame count (not just re-stack orientation): a "Resample frame count" target +
  **Export resampled…**. `FilmstripImporter.Resample` picks the **nearest** source frame for
  each output frame (`round(j·(N−1)/(M−1))`, so the first/last frames land exactly on the
  source min/max) — no blending, so a moving pointer never ghosts. Downsampling is lossy by
  nature (a note says so). **+2 tests, suite 92→94.**
- **Skin tab — multi-control `skin.json` builder.** A new fourth tab that binds several
  exported filmstrips to several parameters in one manifest, surfacing what the
  `SkinManifest` model already supported. Add controls **from a strip** (the importer
  auto-detects frame count / size / orientation / kind) or **blank**; edit each in a detail
  panel (id, type, parameter id, asset/@2x, frames, frame size, stack, on-screen **bounds**,
  optional **value range**); set skin-level **name / author / design resolution / window
  background**; **Export skin.json…** writes one combined manifest into a chosen folder. New
  `IManifestService.BuildManifest`, `SkinViewModel` + `SkinControlEntry` + `SkinView`. **+6
  tests, suite 86→92.**
- **Batch-tab meter settings.** The Batch tab now exposes the full meter template (segment
  count, fill direction, continuous/discrete, on/off colours) when the component type is
  **Meter**, plus a **"source is a backdrop"** toggle that switches each file between the
  meter's **lit on-state art** (layered — revealed up to the fill) and a **housing/backdrop**
  with **procedural LED segments** drawn over it. New `BatchOptions.MeterSourceIsBackdrop`;
  `BatchProcessor` routes the source to the on-art or the background slot accordingly.
  Resolves the v0.6.0 carryover where `ComponentType.Meter` was selectable in Batch but had
  no settings. **+2 tests, suite 84→86.**
- **Layer-aware knob animation — base + pointer (★ #3, step 1).** A knob can now be built
  from **two layers**: a **static base** (the body / well, drawn fixed) and a separate
  **pointer** that rotates with the value — so only the pointer moves and the body stays
  crisp and re-renderable at any resolution. A general, ordered **layer model**
  (`RenderLayer` + `LayerBehavior {Static, Rotate}`, with a per-layer normalized pivot) on a
  new `FilmstripSettings.Layers` list; the renderer composites the stack bottom-first via a
  new `RenderLayers` path in `RenderFrame`/`RenderStrip` (both gained an optional
  index-matched `IReadOnlyList<SKBitmap>? layerArt` parameter). The pointer rotates about its
  **own** pivot (independent of the body), seeded from the body's detected content centre and
  adjustable via numeric **Pointer pivot X/Y** + a "Center pointer on body" reset. A value arc
  still composes on top, concentric with the body centre. Create-tab **"LAYERED KNOB (base +
  pointer)"** panel in the rotary section: Load/Clear **Base** + **Pointer** slots and the
  pivot controls. **Gated by defaults — an empty `Layers` list renders the single source
  byte-for-byte as before, so all prior golden baselines are unchanged.** Knob-only for now
  (faders/sliders/meters ignore layers). Mirrored in the standalone `FilmstripEngine.cs`.
  **+12 tests (`LayeredKnobRenderTests`: 3 golden baselines + 6 pixel-logic; 3 VM load-path
  tests in `LoadPathTests`), suite 84 passing.** *(Next layer-aware steps: auto-pointer
  extraction from flat art, then layered PSD/SVG import.)*

## [0.7.0] — 2026-06-04

### Added
- **Code / component export.** Every export can also emit ready-to-paste **loader code** for
  the target framework, so there's no hand-wiring step: **JUCE** (`LookAndFeel` filmstrip
  `Slider` for knob/fader/slider, or a meter `Component`), **CSS/HTML** (a self-contained
  `background-position` sprite + a 0..1 value setter), **iPlug2** (`IBKnobControl` /
  `IBSliderControl` / `IBitmapControl` + `LoadBitmap`), and **HISE** (a `ScriptPanel` paint
  routine). All share the universal rule `frame = clamp(round(value·(N−1)), 0, N−1)` and pick
  the source axis from the stack direction. New `CodeTarget` enum + `CodeSnippetRequest` model
  and a **pure** `CodeSnippetService` (`Generate` / `FileName` / `SaveAsync` — no Skia/Avalonia),
  mirroring `ManifestService`. Create tab: a **"CODE EXPORT"** panel (per-target tick boxes →
  one file each next to the PNG, e.g. `.juce.h` / `.html` / `.iplug2.cpp` / `.hise.js`) plus a
  live **preview / copy-to-clipboard** expander. Identifiers are sanitised per language.
  **+15 tests (`CodeSnippetServiceTests`), suite 72.** *(React / Web Component and Unity / Godot
  targets remain on the roadmap.)*
- **Value arc / fill ring (knobs).** An optional Serum/Vital-style fill arc composited onto
  each knob frame that tracks the value: the lit arc sweeps from the start angle to the
  current frame's angle, concentric with the rotation pivot. Configurable radius, thickness,
  colour, round/butt end caps, an optional dim full-sweep track, an optional sweep gradient
  (two colours), and an optional glow. Eleven Skia-free `FilmstripSettings` fields (packed
  `0xAARRGGBB` colours), gated on `ShowValueArc` (**off by default — existing output is
  byte-identical**); rendered into the oversampled work surface (`RenderValueArc`) so it
  stays crisp; surfaced in a "VALUE ARC" panel in the Create tab's rotary section. The arc
  inherits the knob's rotation sweep (no separate start-angle field) and is knob-only.
  Mirrored in the standalone `FilmstripEngine.cs`. **+8 tests (4 golden baselines incl.
  gradient+glow, 4 pixel-logic), suite 57 passing.**

### Changed
- **Open-sourced under the MIT license.** Added a `LICENSE` (MIT), a public-facing
  `README.md` (badges, screenshot, tech stack, a contributing guide, and release-download
  links), and license/author metadata in `StripKit.csproj`. The website's hero now carries
  a "free & open source" badge linking to the public repo.

## [0.6.0] — 2026-06-03

### Changed
- New **glassmorphism UI** (Obsidian, chosen from two rendered mockups): acrylic
  frosted window (`TransparencyLevelHint="AcrylicBlur"` + `ExperimentalAcrylicBorder`,
  `FallbackColor` for non-acrylic platforms), translucent `Border.card` glass panels
  (hairline borders + soft shadows), the `#e8440a` accent kept, Fluent `accent` primary
  buttons, and a **Verdana-led sans-serif** font (replaces JetBrains Mono). Design
  tokens centralized in `App.axaml`; `MainWindow` / `ImporterView` / `BatchView`
  restyled. **Styling-only** — renderer, view-models, and tests untouched (41/41 green).
  Pending on-screen review + token tuning (translucency/tint/radius). Hover/focus +
  input-state polish added later (custom Button `ControlTheme` with `:pointerover` /
  `:pressed` / `:focus-visible`, brush + box-shadow transitions, rounded inputs, per-tab
  dividers with near-white subtitles). Design confirmed on-screen by the owner.

### Added
- **Alignment tools (pivot-only).** The renderer centres art on its detected *content*
  centre (`SourceCenterX/Y`, normalized; 0.5,0.5 reproduces prior output) and rotates about
  that point, so an off-centre knob spins in place instead of orbiting. A persistent
  **Enable crosshair** toggle marks the spin centre with live drag — the art itself never
  moves — and the preview always renders at **1024 virtual steps** so alignment isn't
  quantized to the export frame count. Plus **Auto-center**, numeric **Center X/Y**,
  **Reset**, and auto-centring of knobs on load. New `ContentAnalysis` auto-detects the
  opaque-content centre. Same content-centring for fader/slider caps. Mirrored in
  `FilmstripEngine.cs`.
- **Preview transport.** Step back / Play–Stop / step forward / **Reset** (returns to the
  centred start); Play keeps working while the crosshair is enabled.
- **Header wordmark + About.** A wordmark logo on the header's right; a **?** About flyout
  showing the brandmark, version, https://stripkit.pro, VybeCode Software, and
  https://vybeco.de.
- **Windows installer + release pipeline.** An **Inno Setup** installer (per-user; choose
  the install dir; optional desktop + Start-Menu shortcuts; a registry-wiping uninstaller;
  both the StripKit brandmark and the VybeCode logo in the wizard). `scripts/Invoke-Release.ps1`
  (Stage 1: bump version across csproj / .iss / CHANGELOG, test-gate, self-contained publish,
  ISCC package, stage under `releases/latest/`, commit + tag + push) and
  `.github/workflows/auto-release.yml` (Stage 2: VirusTotal scan, then the single GitHub
  Release creator). The self-contained `win-x64` publish runs without the .NET SDK. See
  `docs/PACKAGING.md`.
- **Landing-page website.** A separate medium-light themed site (features grid, GitHub-driven
  download + changelog, privacy / terms / contact, VybeCode footer) under `StripKit-Website`.
- **App icon + favicon.** A multi-resolution `.ico` (16–256 px) embedded via
  `<ApplicationIcon>` and used for the window/taskbar; a compact `brand/favicon.ico`
  (16/32/48/64) for the future website. Generated from `stripkiticon02.png` (contain-fit,
  so the non-square source isn't distorted).

### Fixed
- Off-centre knob PNGs no longer "wobble"/orbit during the sweep — the rotation pivot is
  the art's detected content centre, not the image-rectangle centre.

### Removed
- **Velopack in-app auto-update** (`UpdateService`, `VelopackApp.Run()`, the package
  reference) — replaced by the Inno installer + website-download model. An Inno-installed
  app can't use Velopack's updater; updates now come via the website and GitHub Releases.

### Tests
- **49 passing.** `ContentAnalysis` detection, alignment render (off-centre art stays pinned
  to the frame centre across frames; a sanity test confirms it orbits without the fix),
  auto-center-on-load, and crosshair-toggle persistence. Golden baselines unchanged (the
  default 0.5,0.5 centre reproduces prior output).

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
  re-stack), importer VM tests, manifest mapping + JSON-Schema conformance tests.
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
