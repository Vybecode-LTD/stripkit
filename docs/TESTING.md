# TESTING — StripKit

> Version 0.6.0 · last-updated 2026-06-04 · last-audit 2026-06-04
>
> How StripKit is tested, what is covered, and the known gaps. Test project:
> `tests/StripKit.Tests` (references the app project).

---

## Run

```bash
dotnet test                                      # whole suite (49 tests)
dotnet test --filter FullyQualifiedName~Importer # one class/area
UPDATE_BASELINES=1 dotnet test                   # regenerate golden-image baselines
dotnet test --collect:"XPlat Code Coverage"      # coverage via coverlet
```

Current status: **49 passed / 0 failed / 0 skipped** (~0.8 s).

## Frameworks

| Package | Version | Role |
|---------|---------|------|
| xUnit | 2.9.2 | test framework (`[Fact]`, `[Theory]`) |
| xunit.runner.visualstudio | 2.8.2 | VS / `dotnet test` runner |
| Microsoft.NET.Test.Sdk | 17.11.1 | test host |
| NSubstitute | 5.1.0 | mocks/fakes for service interfaces |
| FluentAssertions | 6.12.0 | readable assertions (6.x — MIT/free) |
| Avalonia.Headless.XUnit | 11.3.0 | headless UI tests (`[AvaloniaFact]`) |
| coverlet.collector | 6.0.2 | code coverage |
| SkiaSharp | 3.119 (transitive) | pixel comparison in golden tests |

Per the C#/.NET convention in `CLAUDE.md`: xUnit + NSubstitute + FluentAssertions,
`Avalonia.Headless` for view tests, golden-image regression for the renderer.

## Test inventory (49)

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

### `LoadPathTests.cs` — 7 (`MainWindowViewModel`, NSubstitute)
The shared Create-tab load path (used by both the button and drag-drop), plus the
knob-alignment auto-centring it now performs on load.
- `LoadSourceFromPath_sets_source_state_and_squares_the_frame_for_a_knob`.
- `LoadSourceFromPath_reports_an_error_when_the_image_cannot_be_decoded`.
- `OpenSource_button_uses_the_same_load_path_as_a_drop` (asserts no duplication).
- `Export_is_disabled_until_a_source_is_loaded` (command gating).
- `Export_is_enabled_for_a_procedural_meter_even_without_a_source`.
- `Loading_an_offcenter_knob_auto_centers_on_its_content` (auto-centre on load).
- `Source_center_persists_when_the_guide_is_toggled_off` (the "reverts when the
  crosshair is removed" report — the centre survives toggling the guide).

### `ContentAnalysisTests.cs` — 4 (opaque-content centre detection)
Unit tests for `ContentAnalysis.DetectContentCenter`, which backs the alignment tools
(Auto-center, the draggable crosshair guide, knob auto-centring on load).
- `Centered_content_detects_near_half`.
- `Offset_content_detects_offset_center`.
- `Fully_transparent_falls_back_to_half`.
- `Null_bitmap_falls_back_to_half`.

### `AlignmentRenderTests.cs` — 2 (renderer, content-centre pivot)
Proves the alignment fix: pivoting on the detected content centre keeps an off-centre
knob spinning in place instead of orbiting.
- `Pivoting_on_content_centre_keeps_an_offcenter_knob_spinning_in_place` — content
  centre stays on the frame centre across the sweep.
- `Without_centering_the_offcenter_knob_orbits` — sanity guard: the (0.5, 0.5) default
  orbits, so the test above genuinely exercises the fix.

### `DropZoneViewTests.cs` — 1 (`[AvaloniaFact]`, headless)
- `Preview_border_opts_into_file_drops` — builds `MainWindow`, asserts
  `DragDrop.GetAllowDrop(PreviewBorder)` (the #1 drag-drop bug).

### `FilmstripImporterTests.cs` — 6 (importer engine)
- `Detect_infers_count_orientation_and_kind` (Theory, 3 cases: knob/vfader/hslider
  with dimensions chosen so the heuristic is unambiguous).
- `Detect_flags_low_confidence_when_a_square_strip_also_divides_by_an_adjacent_count`
  (the 64-vs-63 case).
- `ExtractFrame_returns_one_cell_and_frames_differ_across_the_sweep`.
- `Restack_flips_a_vertical_strip_to_horizontal_preserving_frames` (pixel-equal).

### `ImporterViewModelTests.cs` — 2 (`ImporterViewModel`, NSubstitute)
- `LoadStripFromPath_runs_detection_and_publishes_the_layout`.
- `Extract_and_restack_are_disabled_until_a_strip_is_loaded`.

### `ManifestServiceTests.cs` — 6 (manifest)
- `BuildSingleControl_maps_the_component_type` (Theory, 3 cases).
- `BuildSingleControl_carries_frames_size_stack_and_assets`.
- `Serialized_manifest_conforms_to_the_skill_schema` (JSON-Schema conformance).
- `Optional_fields_are_omitted_when_absent`.

### `BatchProcessorTests.cs` — 4 (integration, real services + temp files)
- `Renders_a_strip_for_each_input` (3 inputs → 3 correctly-sized strips; match-to-source).
- `Records_a_failure_for_an_undecodable_file_and_keeps_going` (failure isolation).
- `Honors_cancellation_between_items` (cancels after item 1 via a custom `IProgress`).
- `Also_writes_at2x_and_manifest_when_requested`.

### `BatchViewModelTests.cs` — 2 (`BatchViewModel`, NSubstitute + temp folder)
- `Run_and_cancel_are_disabled_initially`.
- `Choosing_input_and_output_folders_enables_run`.

## Golden-image regression (`ImageAssert` + `image-regression-testing` skill)

- **Baselines:** `tests/StripKit.Tests/baselines/*.png`, **committed** — they are the
  assertion; a changed baseline shows up as a visual diff in review. Eleven baselines:
  `knob_default_{min,mid,max}`, `knob_strip8`, `vfader_default_mid`, `hslider_default_mid`,
  `meter_proc_up_{empty,mid,full}`, `meter_proc_lr_mid`, `meter_layered_up_mid`.
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
  by running them (the latest two release-tooling fixes were confirmed by re-running
  the pipeline end-to-end) rather than by unit tests. The packaging switch to Inno
  Setup likewise removed `UpdateService` (Velopack), which carried no tests, so the
  suite count was unchanged by that work.
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
  pixel-tested directly.
- **No coverage threshold is enforced** yet (coverlet is wired; a gate can be added
  with a CI step).
- **`FilmstripEngine.cs`** (the standalone portable renderer) is not under test — it
  is a hand-maintained mirror of `SkiaFilmstripRenderer`; the in-app renderer is the
  tested one.
