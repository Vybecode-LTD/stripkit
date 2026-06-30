# TESTING — StripKit

> Version 1.3.0 · last-updated 2026-06-30 · last-audit 2026-06-18
>
> How StripKit is tested, what is covered, and the known gaps. Test project:
> `tests/StripKit.Tests` (references the app project).

---

## Run

```bash
dotnet test                                      # whole suite (265 tests)
dotnet test --filter FullyQualifiedName~Importer # one class/area
UPDATE_BASELINES=1 dotnet test                   # regenerate golden-image baselines
dotnet test --collect:"XPlat Code Coverage"      # coverage via coverlet
```

Current status: **265 passed / 0 failed / 0 skipped** (~1.0 s). Build 0/0.

## CI (automated testing)

`.github/workflows/ci.yml` runs the full suite automatically on every push and every
pull-request targeting `main`. The job (`build-and-test`) runs on `windows-latest`
and uses the .NET 9 SDK (`9.0.x`); it pins `actions/checkout@v5` + `actions/setup-dotnet@v5`
(Node 24, ahead of the June 16 2026 Node-20 forcing). Steps: `dotnet restore` →
`dotnet build -c Debug` → `dotnet test --no-build -c Debug`. A red build or any failing test
blocks the branch. The separate `auto-release.yml` workflow handles the release pipeline
(VirusTotal scan + `gh release create`; also on `actions/checkout@v5`); it is not part of the
test gate.

## Frameworks

| Package | Version | Role |
|---------|---------|------|
| xUnit | 2.9.2 | test framework (`[Fact]`, `[Theory]`) |
| xunit.runner.visualstudio | 2.8.2 | VS / `dotnet test` runner |
| Microsoft.NET.Test.Sdk | 17.11.1 | test host |
| NSubstitute | 5.1.0 | mocks/fakes for service interfaces |
| FluentAssertions | 6.12.0 | readable assertions (6.x — MIT/free) |
| Avalonia.Headless.XUnit | 11.3.0 | headless UI tests (`[AvaloniaFact]`) |
| coverlet.collector | 6.0.4 | code coverage |
| SkiaSharp | 3.119.2 (transitive) | pixel comparison in golden tests |

Per the C#/.NET convention in `CLAUDE.md`: xUnit + NSubstitute + FluentAssertions,
`Avalonia.Headless` for view tests, golden-image regression for the renderer.

## Test inventory (265)

### Assemble tab (frame-sequence → filmstrip) — 28
The path-tracing-pipeline phase 1, covered without baselines where possible (pixel-identity over
golden images) plus one golden lock.
- `NaturalFileNameComparerTests.cs` — 7: numbered names sort numerically (`frame_2` before
  `frame_10`), leading zeros compare equal, an unpadded sequence sorts into render order, and
  non-numeric names fall back to case-insensitive text.
- `FrameSequenceAssemblerTests.cs` — 9: vertical/horizontal stacking dimensions; placed cells equal
  the source frames pixel-for-pixel; pad-to-largest pads + warns; crop-to-smallest; strict throws on a
  mismatch; <2 frames throws; resample retimes to the target and keeps the endpoints (via the importer);
  content re-centre moves an off-centre block to the cell centre.
- `FrameSequenceProbeTests.cs` — 2 (integration): a real `ImageLoadService` + the assembler probe
  natural-sort on-disk PNGs and report uniform vs mixed sizes (header-only, no full decode).
- `FrameSequenceViewModelTests.cs` — 8: export gated until ≥2 frames; a single frame isn't enough;
  dropped frames are numbered in order; remove/move renumber; clear disables export; non-image drops
  are ignored; the target presets set the resample count. Services mocked (NSubstitute).
- `FrameSequenceAssemblerGoldenTests.cs` — 1: a 4-frame strip of real art locks placement
  (`baselines/assemble_knob_mix_4.png`).
- `AssembleViewTests.cs` — 1 (`[AvaloniaFact]`): the `AssembleView` markup loads and realizes with a
  populated frame list (compiled bindings, design tokens, and the classic-binding reorder template).



### Generate tab (AI SVG generation) — 72 + integration
The networked, non-deterministic feature is covered without ever hitting a network: every AI
feature is unit-tested with a mocked `IAssetGenerationService` + a *real* importer + a fake
provider/`HttpMessageHandler` (no real network, no real keys). Vision payloads are verified by
capturing the outgoing request body's shape.
- `SvgSanitizerTests.cs` — 8: carve the SVG out of a fenced/chatty reply; strip
  script/`<image>`/`<foreignObject>`/event-handlers/off-document `href`; keep local `#id`
  refs; reject non-SVG and malformed XML (incl. a DTD — `SafeXml` prohibits it).
- `SecretStoreTests.cs` — 4: per-provider set/get round-trip, persistence across instances,
  blank-clears / clear-removes, and that the on-disk file never contains the plaintext key.
- `AssetGenerationProviderTests.cs` — 12: each provider against a fake `HttpMessageHandler` —
  the right URL, auth header, and body go out and the right field parses back; a 401 becomes a
  friendly `GenerationException` carrying the API's message; identity + default model.
- `AssetGenerationServiceTests.cs` — 20: a chatty reply reduces to a clean SVG that round-trips
  the real importer as tagged body/pointer layers; the prompt encodes the conventions + model
  fallback; **meter** and **toggle** prompts (off/on groups, switch vs push, full-height/-width),
  a **horizontal meter** (landscape canvas, left-to-right), the **avoid** field folded into the
  user prompt, `BuildPrompts`, the body-colour + effect flags in the prompt; the multi-control
  `GenerateSetAsync` (one result per type, in order, **with a per-item failure isolated**),
  `GenerateVariationsAsync` (N takes), `RefineAsync` (current SVG + instruction handed back; an
  empty instruction is rejected), `DescribeReferenceAsync` (vision description; fails cleanly
  without a key); failure paths (no SVG, provider error, missing key); provider display order.
- `CustomOpenAiProviderTests.cs` — 3: the OpenAI-compatible **custom endpoint** — a bare base URL
  is normalised to `…/chat/completions` (a full path is left as-is), Bearer auth, and a missing
  base URL fails with a friendly `GenerationException`. Only the network is faked.
- `VisionProviderTests.cs` — 3: per-provider **vision** request shape — Claude sends a base64
  `image` block, OpenAI an `image_url` data URI, Gemini `inline_data` — and each reads the text
  description back. The outgoing request body is captured and asserted.
- `GenerateViewModelTests.cs` — 21: key gating, per-provider key save/reload, the success path
  (import-validated + Create handoff fires with a real temp SVG), the two failure paths, and that
  **a custom/delisted model id (not in the suggestions) is sent verbatim** rather than dropped to a
  suggestion (the editable `AutoCompleteBox` model field); the colour/effect/control-type fields
  reaching the `GenerationRequest`; **meter** and **toggle** requested as layered off/on pairs; the
  **auto-retry** of a structurally-weak knob (a no-pointer take retried once; a well-formed knob is
  not); **refine** (gated on an instruction, then updates the result); **prompt seeds** (built-in
  library, apply, save/persist/reload/delete, built-ins are read-only); the **matching set**
  (gated on a key + ≥1 type, one result per included type, per-item Use-in-Create handoff) and
  **variations** (the grid fills with N takes of the selected type).
- `GenerateViewTests.cs` — 1: headless realization of `GenerateView` (compiled bindings,
  design tokens, the reveal binding, the colour-swatch buttons, the `AutoCompleteBox` model field,
  and the `StringConverters` usage all load at runtime).
- `GenerateIntegrationTests.cs` — 2: the end-to-end Generate→import path per control type: a
  generated knob round-trips as body/pointer layers, a generated **button** maps its `off`/`on`
  groups to `LayerBehavior.Frame` state layers, and the Generate→Create handoff carries the
  generated control type (no longer hard-forced to `RotaryKnob`).

### `RendererGoldenTests.cs` — 6 (golden-image, pure SkiaSharp)
Locks the renderer's pixel output against committed baselines.
- `Knob_min_frame_renders_pointer_at_start_angle` — frame 0 (−135°).
- `Knob_mid_frame_renders_pointer_near_top` — frame 32 (~0°).
- `Knob_max_frame_renders_pointer_at_end_angle` — frame 63 (+135°).
- `Knob_strip_stacks_eight_frames_vertically` — asserts 80×640 + baseline.
- `Vertical_fader_mid_frame_centres_the_cap`.
- `Horizontal_slider_mid_frame_centres_the_cap`.

### `MeterRenderTests.cs` — 9 (meter renderer)
- 5 golden baselines: `meter_proc_up_{empty,mid,full}`, `meter_proc_lr_mid`,
  `meter_layered_up_mid`.
- 4 pixel-logic: procedural fills from the bottom (Up) / top (Down) / left
  (LeftToRight), and the layered reveal shows on-art only up to the fill.

### `ValueArcRenderTests.cs` — 8 (value-arc / fill-ring renderer)
- 4 golden baselines: `arc_knob_{min,mid,max}` (the lit arc growing across the sweep)
  and `arc_knob_gradient_glow_mid` (gradient + glow at supersample 4).
- 4 pixel-logic: the arc is empty when off; the lit sweep grows from the start angle
  to the right side only at maximum and never enters the bottom wedge; the dim track
  covers the unlit remainder; the arc is a no-op for non-knob components.

### `LayeredKnobRenderTests.cs` — 9 (layer-aware knob renderer)
Layered knob = a static base body + a separate rotating pointer (the ★ #3 step-1 feature).
- 3 golden baselines: `layered_knob_{min,mid,max}` — the body stays fixed while only the
  pointer rotates (−135° / ~0° / +135°).
- 6 pixel-logic: the pointer rotates to the top at mid-travel (and is elsewhere at frame 0);
  a static base layer is identical in every frame; the body under a rotating pointer does not
  move; an **empty layer stack falls back to the single-source path** (the gate is
  `Layers.Count > 0`); the pointer pivot changes the render; and layers are ignored for
  non-knob components (also exercises `FilmstripSettings.Clone`'s deep-copy of `Layers`).

### Button state-frame renderer — discrete on/off frames
`ComponentType.Button` composites layer art per frame via `RenderButtonLayers`: a `Static`
layer draws on every frame; a `LayerBehavior.Frame` layer draws only when its list index equals
the frame index (index 0 = off, index 1 = on). Covered by pixel-logic over the button path (the
off-only frame shows the off layer, the on frame shows the on layer, a shared Static layer shows
on both) and end-to-end via `GenerateIntegrationTests` (a generated button's `off`/`on` groups
become Frame layers). The path is also mirrored in `FilmstripEngine.cs`.

### `ToggleRenderTests.cs` — 1 (toggle state-frame renderer)
A **Toggle** is its own `ComponentType` but renders exactly like a 2-state Button — it reuses the
Button state-frame path, so the renderer goldens are unchanged.
- `Frame_0_shows_the_off_state_and_frame_1_shows_the_on_state` — pixel-logic: the off
  (dark) `Frame` layer shows only on frame 0 and the on (lit) layer only on frame 1.

### `ImageLoadServiceTests.cs` — 3 (concrete PNG decode path)
The real `ImageLoadService` decode used across the app: it peeks header dimensions via `SKCodec`
and guards against a decompression-bomb (huge dimensions) before decoding.
- `Decodes_a_valid_png_at_its_real_dimensions` (a control-art PNG decodes to its real size).
- `Returns_null_for_a_missing_file`.
- `Returns_null_for_non_image_content` (a file with no decodable header).

### `PointerExtractorTests.cs` — 3 (auto-pointer extraction, pure SkiaSharp)
Splitting a flat knob into a symmetric base + the indicator via the radial-symmetry residual.
- `Extract_splits_a_flat_knob_into_a_symmetric_body_and_the_indicator` (the white indicator
  goes to the pointer; a body-only region yields none; the base erases the indicator and is
  rotationally symmetric; high confidence).
- `Extract_returns_null_for_a_missing_image`.
- `A_plain_symmetric_disc_yields_an_essentially_empty_pointer` (nothing to extract).

### `LayeredImportServiceTests.cs` — 10 (layered PSD/SVG import, ★ #3 step 3)
Parsing a real layered source into the renderer's layer stack. Fixtures are synthesized in memory
(an SVG string; a PSD written by Magick.NET) so no binary assets live in the repo.
- SVG: groups → named, behaviour-guessed layers; layers isolated + registered on the canvas; a
  group-less SVG is one static layer; a non-indicator group name stays Static; an `off`/`on` group
  becomes a `Frame` layer.
- PSD: the merged composite is dropped and the named layers kept with their guessed behaviours;
  layers isolated + registered on the canvas (proves the [composite, layer, layer…] read model).
- `Import` returns null for a missing/garbage file; `CanImport` recognizes `.svg`/`.psd`/`.psb` only.
- SVG parsing goes through `SafeXml` — a DTD-bearing document is rejected as malformed (no entity
  expansion).

### `LayeredImportViewModelTests.cs` — 11 (the Create-tab import command + the type-aware handoff)
- Importing an SVG populates tagged rows (body=Static, pointer=Rotate), forces the knob type,
  squares the frame, and gates the UI (`ShowLoadHint` off, `ExportCommand` enabled).
- The **type-aware Generate→Create handoff** (`ImportLayeredFromPathAsync(path, type)`): a button
  arrives as a **Button** with off/on `Frame` state frames; a **toggle** arrives as its own type
  (`IsStateFrames`, off/on Frame layers, frame count 2); an off/on file via the **picker
  auto-detects a toggle**; a **meter** routes `off`→background, `on`→source (a meter is a
  source+background pair, not a layer stack) and reads orientation from the art's **aspect**
  (tall → fill Up, wide → LeftToRight); a fader/slider **cap flattens to a single source** (Theory).
- Loading a base layer clears an active import (the two layered modes are mutually exclusive).
- Clearing the import drops the layers + preview.
- Exporting feeds the rows to the renderer as `Layers` + index-matched `layerArt`, and a per-layer
  behaviour **override** flows through to the rendered stack.

### `LayeredImportRenderTests.cs` — 2 (import → render, end-to-end)
- Golden `imported_svg_knob_mid` — a parsed SVG composited through the layer path (eyeballed).
- Pixel-logic: the indicator-named group rotates while the body group stays put across frames.

### `SettingsServiceTests.cs` — 3 (first-run persistence)
The minimal `AppSettings` JSON store: round-trips, and degrades to defaults for a missing or
corrupt file (settings are best-effort and never crash the app).

### `TutorialViewModelTests.cs` — 7 (Getting Started overlay, onboarding P1)
- First-run auto-opens when unseen and stays closed once seen; Skip/Finish persists "seen" so it
  never auto-reopens; re-opening from Help always restarts at step 1.
- Next advances and finishes on the last step (label becomes "Done"); Back is disabled on step 1.
- Step 1 offers the sample and "Load sample knob" raises `LoadSampleRequested`.

### `LoadPathTests.cs` — 13 (`MainWindowViewModel`, NSubstitute)
The shared Create-tab load path (used by both the button and drag-drop), the knob-alignment
auto-centring it performs on load, the layered base/pointer slots, the auto-extraction, and the
tutorial's sample-knob load (`IAssetService` → `LoadSourceFromPath`).
- `LoadSourceFromPath_sets_source_state_and_squares_the_frame_for_a_knob`.
- `LoadSourceFromPath_reports_an_error_when_the_image_cannot_be_decoded`.
- `OpenSource_button_uses_the_same_load_path_as_a_drop` (asserts no duplication).
- `Export_is_disabled_until_a_source_is_loaded` (command gating).
- `Export_is_enabled_for_a_procedural_meter_even_without_a_source`.
- `Loading_an_offcenter_knob_auto_centers_on_its_content` (auto-centre on load).
- `Source_center_persists_when_the_guide_is_toggled_off` (the "reverts when the
  crosshair is removed" report — the centre survives toggling the guide).
- `LoadBaseLayerFromPath_sets_state_squares_the_frame_and_seeds_the_pointer_pivot`.
- `LoadPointerFromPath_sets_pointer_state`.
- `Clearing_the_base_layer_disables_export_again` (export gating for the layered slot).
- `AutoExtractPointer_splits_a_flat_knob_into_the_base_and_pointer_slots` (the auto-extract
  command fills both slots and enables export).

### `ContentAnalysisTests.cs` — 4 (opaque-content centre detection)
Unit tests for `ContentAnalysis.DetectContentCenter`, which backs the alignment tools
(Auto-center, the draggable crosshair guide, knob auto-centring on load).
- `Centered_content_detects_near_half`.
- `Offset_content_detects_offset_center`.
- `Fully_transparent_falls_back_to_half`.
- `Null_bitmap_falls_back_to_half`.

### `AlignmentRenderTests.cs` — 3 (renderer, content-centre pivot)
Proves the alignment fix: pivoting on the detected content centre keeps an off-centre
knob spinning in place instead of orbiting.
- `Pivoting_on_content_centre_keeps_an_offcenter_knob_spinning_in_place` — content
  centre stays on the frame centre across the sweep.
- `Centering_on_content_places_an_offcenter_knob_at_the_frame_centre` — the centred knob
  is genuinely positioned at the frame centre (not just spun in place off to one side).
- `Without_centering_the_offcenter_knob_orbits` — sanity guard: the (0.5, 0.5) default
  orbits, so the tests above genuinely exercise the fix.

### `DropZoneViewTests.cs` — 1 (`[AvaloniaFact]`, headless)
- `Preview_border_opts_into_file_drops` — builds `MainWindow`, asserts
  `DragDrop.GetAllowDrop(PreviewBorder)` (the #1 drag-drop bug).

### `FilmstripImporterTests.cs` — 8 (importer engine)
- `Detect_infers_count_orientation_and_kind` (Theory, 3 cases: knob/vfader/hslider
  with dimensions chosen so the heuristic is unambiguous).
- `Detect_flags_low_confidence_when_a_square_strip_also_divides_by_an_adjacent_count`
  (the 64-vs-63 case).
- `ExtractFrame_returns_one_cell_and_frames_differ_across_the_sweep`.
- `Restack_flips_a_vertical_strip_to_horizontal_preserving_frames` (pixel-equal).
- `Resample_retimes_the_frame_count_with_nearest_frame_mapping` (8→4; endpoints land on the
  source min/max; each output frame equals a source frame).
- `Resample_to_the_same_count_reproduces_every_frame` (N→N identity).

### `ImporterViewModelTests.cs` — 2 (`ImporterViewModel`, NSubstitute)
- `LoadStripFromPath_runs_detection_and_publishes_the_layout` (incl. the resample target
  defaulting to the detected count + resample command enabled).
- `Extract_restack_and_resample_are_disabled_until_a_strip_is_loaded`.

### `ManifestServiceTests.cs` — 8 (manifest)
- `BuildSingleControl_maps_the_component_type` (Theory, 3 cases).
- `BuildSingleControl_carries_frames_size_stack_and_assets`.
- `Serialized_manifest_conforms_to_the_skill_schema` (JSON-Schema conformance).
- `Optional_fields_are_omitted_when_absent`.
- `BuildManifest_assembles_multiple_controls_and_global_metadata` (multi-control + window
  background + value range; schema-conformant).
- `BuildManifest_defaults_a_blank_name_and_omits_blank_author_and_background`.

### `SkinViewModelTests.cs` — 4 (`SkinViewModel`, NSubstitute)
The Skin tab's multi-control manifest builder.
- `Export_is_disabled_until_a_control_is_added` (command gating).
- `Add_from_strip_detects_the_layout_and_creates_a_control` (importer `Detect` auto-fills the row).
- `Remove_selected_drops_the_control_and_re_gates_export`.
- `Export_builds_a_manifest_with_every_control_and_the_globals` (all controls + skin metadata
  reach `BuildManifest`; the file is written as `<name>.skin.json`).

### `CodeSnippetServiceTests.cs` — 18 (code/component export)
Per-target loader-code generation (`CodeSnippetService`), all pure string assertions.
- JUCE: knob → a rotary `LookAndFeel`; fader → a linear `LookAndFeel`; meter → a
  `Component` with `setLevel`; **toggle** → a latching `juce::Button`
  (`setClickingTogglesState(true)`, `getToggleState() ? 1 : 0`) and a button/toggle differ
  only in the class name; the source rect follows the stack axis.
- CSS/HTML: a `<style>`+`<script>` sprite with a value setter; the axis and the HiDPI
  `@media` block follow the inputs.
- iPlug2: knob → `IBKnobControl`; fader → `IBSliderControl` with the right `EDirection`;
  **toggle** → `IBSwitchControl`.
- HISE: a `ScriptPanel` paint routine (`loadImage` + `setPaintRoutine`).
- Identifiers are sanitised; `FileName` maps each target (Theory, 4 rows); `SaveAsync`
  writes the snippet to disk matching `Generate`.

### `RenderRecipeServiceTests.cs` — 14 (render-recipe export, path-tracing P2)
The recipe's per-frame table must match the renderer's law exactly, so an offline render stacks cleanly.
- `BuildFrameTable`: N rows with the endpoints on the extremes; the deliberate `(N−1)` divisor (frame 1
  of 64 = 1/63, not 1/64); an odd count's exact geometric midpoint; non-rotary keeps angle 0 while the
  value still ramps; a single frame doesn't divide by zero.
- CSV: a header + one row per frame; numbers stay invariant-culture even under a comma-decimal locale.
- Blender: transparent film, the frame range + law, RGBA; rotation baked only for a rotary knob
  (`IS_ROTARY` True/False).
- JSON parses with its metadata + one entry per frame; `FileName` extensions + id sanitisation (Theory);
  `SaveAsync` writes the recipe to disk matching `Generate`.

### `RenderQcTests.cs` — 7 (render QC + un-premultiply, path-tracing P3)
- `UnpremultiplyAlpha`: recovers the straight colour from premultiplied bytes (50%-alpha pixel),
  and leaves fully opaque / fully transparent pixels alone.
- `AnalyzeQc`: detects object drift between frames (content-centre spread); flags frames with no
  transparency and fully-blank frames; reports clean for a well-behaved sequence.
- `Assemble` surfaces the QC warnings in its result.

### `BatchProcessorTests.cs` — 5 (integration, real services + temp files)
- `Renders_a_strip_for_each_input` (3 inputs → 3 correctly-sized strips; match-to-source).
- `Records_a_failure_for_an_undecodable_file_and_keeps_going` (failure isolation).
- `Honors_cancellation_between_items` (cancels after item 1 via a custom `IProgress`).
- `Also_writes_at2x_and_manifest_when_requested`.
- `Renders_meters_and_the_backdrop_toggle_changes_the_output` (both meter modes render at the
  right size; the layered vs backdrop toggle produces different pixels).

### `BatchViewModelTests.cs` — 3 (`BatchViewModel`, NSubstitute + temp folder)
- `Run_and_cancel_are_disabled_initially`.
- `Choosing_input_and_output_folders_enables_run`.
- `Meter_template_settings_and_backdrop_toggle_flow_into_the_batch_options` (segments, fill,
  continuous, and the backdrop toggle reach the `BatchOptions` passed to the processor).

## Golden-image regression (`ImageAssert` + `image-regression-testing` skill)

- **Baselines:** `tests/StripKit.Tests/baselines/*.png`, **committed** — they are the
  assertion; a changed baseline shows up as a visual diff in review. Eighteen baselines:
  `knob_default_{min,mid,max}`, `knob_strip8`, `vfader_default_mid`, `hslider_default_mid`,
  `meter_proc_up_{empty,mid,full}`, `meter_proc_lr_mid`, `meter_layered_up_mid`,
  `arc_knob_{min,mid,max}`, `arc_knob_gradient_glow_mid`, `layered_knob_{min,mid,max}`.
- **Tolerance:** a pixel "differs" if any channel differs by > 2/255; the test fails
  if > 0.1 % of pixels differ. Absorbs anti-aliasing jitter, catches real changes.
- **On mismatch:** writes `expected`/`actual`/`diff` PNGs to
  `tests/StripKit.Tests/output/` (gitignored) and fails with the numbers.
- **Approve workflow:** a missing baseline fails with "new baseline written — review
  and commit." Regenerate intentionally with `UPDATE_BASELINES=1 dotnet test`, then
  **eyeball each PNG** before committing.
- **Determinism:** SkiaSharp version pinned; CPU raster surface; fixed sizes/inputs;
  fixed `Rgba8888` premultiplied format; deterministic synthetic art (`TestImages.cs`).

## Headless UI testing

`TestAppBuilder.cs` registers `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`
and builds the app with `UseHeadless(new AvaloniaHeadlessPlatformOptions())` (default
headless drawing — enough to build the visual tree and read attached properties
without rendering). `[AvaloniaFact]` tests run on the headless UI thread.

## Known gaps (honest)

- **The release pipeline is validated by execution, not by automated tests.** The
  release script (`scripts/Invoke-Release.ps1`) and the GitHub Actions workflow
  (CI YAML) are PowerShell / pipeline glue, not application code; they are verified
  by running them (the release-tooling fixes — including the v1.2.2 release-integrity
  guard and the Stage-3 hashtable-splat fix — were confirmed by running the pipeline
  end-to-end, which shipped v1.2.1 and v1.2.2) rather than by unit tests. The packaging
  switch to Inno Setup likewise removed `UpdateService` (Velopack), which carried no
  tests, so the suite count was unchanged by that work.
- **The literal OS drag gesture is not auto-tested.** Avalonia.Headless cannot
  construct a synthetic `DragEventArgs` (the type's constructors are internal), so the
  drop is covered indirectly: the VM load path (`LoadPathTests`/`ImporterViewModelTests`)
  + the `AllowDrop` wiring (`DropZoneViewTests`). End-to-end drop is a manual check.
- **The Create-tab `ExportAsync` is not integration-tested through the dialog.** The
  underlying load → render → export → manifest chain *is* covered end-to-end by
  `BatchProcessorTests` (real files), and the pieces are tested separately
  (`BuildSettings`, golden render, `ManifestService`).
- **Preview rendering through `ToAvaloniaBitmap` is not asserted** in VM tests (it
  needs a UI platform; tests force the render/extract to throw so the swallowed
  preview path is skipped and load *state* is asserted). The importer's extraction is
  pixel-tested directly. The Generate tab's preview build runs off-thread in
  `BuildPreview` — still a UI-platform concern, so the Avalonia preview bitmap can't
  render under a plain unit test (best-effort null) and the VM tests assert the generated
  SVG / handoff state rather than the rendered bitmap.
- **Live AI generation is never hit.** Every AI feature is exercised through a mocked
  `IAssetGenerationService` + a *real* importer + a fake provider/`HttpMessageHandler`
  (vision payloads verified by capturing the outgoing request body shape) — but a real key
  + a real model call is never made, so it stays a manual smoke test. The new meter/toggle
  and AI-generated art **quality** can't be judged by the suite (the renderer goldens are
  unchanged because Toggle reuses Button's path; the AI reply is faked), so the
  meter/toggle/AI art output still wants a manual eyeball — knob is the longest-proven path.
- **No coverage threshold is enforced** yet (coverlet `6.0.4` is wired; a gate can be
  added with a CI step).
- **`FilmstripEngine.cs`** (the standalone portable renderer) is not under test — it
  is a hand-maintained mirror of `SkiaFilmstripRenderer` (now including the `RenderLayers`
  layered-knob path, the `RenderButtonLayers` button state-frame path, the `RenderLayer`/
  `LayerBehavior` types and `Layers` field); the in-app renderer is the tested one.
- **The Batch tab's meter on-screen output is not golden-tested.** The meter template now
  flows through to `BatchProcessor` (covered by `BatchViewModelTests` +
  `BatchProcessorTests`), and the meter renderer itself is locked by `MeterRenderTests`; the
  untested seam is purely the on-screen Batch meter *UI* (which fields are visible), a view
  concern not asserted headlessly.
