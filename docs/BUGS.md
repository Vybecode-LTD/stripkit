# BUGS — StripKit

> Version 1.5.1 · last-updated 2026-07-04 · last-audit 2026-07-04

**Open bugs: 0.** **Resolved: 21.**

Each bug fixed gets a root cause and a regression guard. BUG-001/002 were
**pre-existing scaffold defects** surfaced by the first real compilation during
Phase 0 — neither was caused by the FilmstripForge → StripKit rename. BUG-003/004
were **release-tooling defects** caught during the first v0.6.0 release and fixed
forward (they corrupted published docs / blocked the release pipeline, not the app
binary). BUG-005/006/007 were **code-quality defects** found by audit and fixed in
commit `0aaa257` (resource leaks and a silent product gap). BUG-008/009 were
**Generate-tab defects** found in the 2026-06-14 audit of the shipped v1.2.0 and fixed
forward in `80dc1b5` (a broken handoff path + unhardened untrusted-XML parsing). BUG-010 was a
**2026-06-18 audit** finding: the BUG-009 hardening was applied to the SVG file-import path but ran
*after* Svg.Skia had already parsed the raw text, leaving the entity-expansion DoS reachable there.
BUG-017/018 were **2026-07-02 adversarial-review** findings, caught before commit/release, in the new
v1.5.0 sprite-grid + render-preset work (the 3 items deferred from the earlier v1.5.0 enhancement wave);
both were fixed, covered by regression tests, and shipped in commit `57f071c`. BUG-019/020 were
**owner-reported UI inconsistencies** found live-testing the same v1.5.0 build: a missing "Show in
folder" affordance on the Import tab, and a Getting Started tip that overflowed its dialog card.
BUG-021 was a **2026-07-04 docs-audit** finding: the Assemble tab's HDR ingest (.exr / .hdr / 16-bit
.tif) worked from "Choose folder…" but was silently dropped on drag-drop and unavailable in "Add
files…" — three accepted-extension lists had drifted; fixed by sharing one.

---

## Resolved

### BUG-001 — `.csproj` XML comment contains `--` (build blocker) ✅
- **Severity:** Critical (project would not load / build).
- **Symptom:** `error MSB4025: The project file could not be loaded. An XML comment
  cannot contain '--' …` at `StripKit.csproj(38)`.
- **Root cause:** the scaffold was never compiled on a machine with the SDK. A
  comment contained the literal `dotnet list package --include-transitive`; the `--`
  is illegal inside an XML `<!-- … -->` comment.
- **Fix (2026-06-03):** reworded the comment to avoid `--` (the exact command still
  lives in `README.md`, where markdown allows it).
- **Regression guard:** any `dotnet build` parses the `.csproj`; CI/`dotnet test`
  catches a recurrence immediately.

### BUG-002 — missing `Microsoft.Extensions.DependencyInjection` reference (build blocker) ✅
- **Severity:** Critical (would not compile).
- **Symptom:** `error CS0234: The type or namespace name 'Extensions' does not exist
  in the namespace 'Microsoft'` at `App.axaml.cs(7)`.
- **Root cause:** the composition root in `App.axaml.cs` used `ServiceCollection` /
  `AddSingleton`, and `CLAUDE.md` lists DI in the stack, but the `.csproj` never
  referenced the package. Never caught because the scaffold was never compiled.
- **Fix (2026-06-03):** added
  `<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />`.
- **Regression guard:** build; the DI wiring is exercised at app boot and indirectly
  by the headless `DropZoneViewTests`.

### BUG-003 — CHANGELOG mojibake on release (double-encoded UTF-8) ✅
- **Severity:** Medium (corrupted published docs, not the app binary).
- **Component:** `scripts/Invoke-Release.ps1`.
- **Reported / Fixed:** 2026-06-04 (first v0.6.0 release run).
- **Symptom:** the first run of `scripts/Invoke-Release.ps1` rewrote
  `docs/CHANGELOG.md` with double-encoded characters — em-dashes `—` became `â€"`,
  middots `·` became `Â·`. The corruption was committed (`d640a6f`) and would have
  surfaced in the GitHub Release notes and on the website.
- **Root cause:** PowerShell 5.1's `Get-Content -Raw` (with no `-Encoding`) decodes a
  UTF-8-without-BOM file as ANSI/Windows-1252; the script then re-wrote it as UTF-8,
  double-encoding every non-ASCII byte.
- **Fix (commit `f1b68d3`):** added `-Encoding UTF8` to every `Get-Content -Raw` read
  in `Invoke-Release.ps1`; restored the mangled `docs/CHANGELOG.md` (and the `.iss`)
  from the pre-release commit and re-applied the version promotion correctly. Verified
  the file is clean UTF-8.
- **Regression guard:** none (build-script issue, not unit-testable) — future releases
  read/write the changelog as UTF-8 and the workflow re-ran clean afterward.

### BUG-004 — CI "Create GitHub Release" step crashed on changelog backticks ✅
- **Severity:** High (blocked the release; the first auto-release run failed and no
  release was created).
- **Component:** auto-release GitHub Actions workflow ("Create GitHub Release" step).
- **Reported / Fixed:** 2026-06-04 (first v0.6.0 release run).
- **Symptom:** the auto-release workflow failed in the "Create GitHub Release" step
  with `command substitution: syntax error` and many `command not found` lines (e.g.,
  `ExperimentalAcrylicBorder`, `Border.card`).
- **Root cause:** the changelog body (full of Markdown backticks) was interpolated via
  `${{ steps.notes.outputs.body }}` directly into `gh release create --notes "..."`,
  so bash performed command substitution on the backticks and tried to execute the
  changelog text as shell commands.
- **Fix (commit `a408bc9`):** the workflow now writes the notes to a file and passes
  them with `--notes-file`, and all values flow through `env:` vars instead of inline
  `${{ }}` interpolation (injection-safe). Also added a `workflow_dispatch` trigger for
  manual re-runs; re-running produced the release successfully (run completed in
  ~1m21s).
- **Regression guard:** none (CI-workflow issue, not unit-testable) — the re-run
  produced the release cleanly, confirming the fix.

### BUG-005 — CancellationTokenSource leak in BatchViewModel ✅
- **Severity:** Low (GC eventually collects; WaitHandle leak + bad contributor pattern).
- **Component:** `src/StripKit/ViewModels/BatchViewModel.cs`.
- **Reported / Fixed:** 2026-06-04 (code audit).
- **Symptom:** each `RunAsync()` call created a new `CancellationTokenSource` without
  disposing the previous one, leaking the underlying `WaitHandle` until GC collected
  the instance.
- **Root cause:** missing `_cts?.Dispose()` before reassignment in `RunAsync()`.
- **Fix (commit `0aaa257`):** added `_cts?.Dispose();` immediately before
  `_cts = new CancellationTokenSource();` in `RunAsync()`.
- **Regression guard:** 49/49 green; no dedicated automated regression test — the fix
  is a one-line pattern check; future reviewers can verify by inspection.

### BUG-006 — DispatcherTimer not stopped on window close ✅
- **Severity:** Low (timer fires after disposal in tests and edge-case window
  teardown scenarios).
- **Component:** `src/StripKit/Views/MainWindow.axaml.cs`.
- **Reported / Fixed:** 2026-06-04 (code audit).
- **Symptom:** `_playTimer` (`DispatcherTimer`) had no cleanup path when the window
  was closed, leaving the timer running against a disposed view.
- **Root cause:** missing `Closed` event subscription in the constructor.
- **Fix (commit `0aaa257`):** added
  `Closed += (_, _) => _playTimer.Stop();` in the `MainWindow` constructor.
- **Regression guard:** 49/49 green.

### BUG-007 — ComponentType.Meter missing from BatchViewModel ✅
- **Severity:** Medium (silent product gap — users cannot batch-render meters via the
  Batch tab; no error is shown, the option simply does not exist).
- **Component:** `src/StripKit/ViewModels/BatchViewModel.cs`.
- **Reported / Fixed:** 2026-06-04 (code audit).
- **Symptom:** `BatchViewModel.ComponentTypes` listed only `RotaryKnob`,
  `VerticalFader`, and `HorizontalSlider`; `Meter` was absent from the batch template
  selector.
- **Root cause:** `ComponentType.Meter` was added to the Create tab (Phase 6 / v0.5.0)
  after `BatchViewModel` was already built; the Batch view model was never updated to
  include it.
- **Fix (commit `0aaa257`):** added `ComponentType.Meter` to the `ComponentTypes`
  array in `BatchViewModel`. Added a comment in `BuildSettings()` noting that
  meter-specific fields (`SegmentCount`, `FillDirection`, etc.) are not yet exposed in
  the batch template UI and will render using `FilmstripSettings` defaults until a
  dedicated batch-meter UI is added. *(Fully resolved in v0.8.0 — the Batch tab now
  exposes the meter settings + a layered/backdrop toggle.)*
- **Regression guard:** 49/49 green.

### BUG-008 — Generate → Create handoff hard-coded RotaryKnob (broken non-knob output) ✅
- **Severity:** High (broken output — a generated fader/slider/button produced an unusable strip).
- **Component:** `src/StripKit/ViewModels/MainWindowViewModel.cs` (the Generate → Create handoff)
  + `src/StripKit/ViewModels/GenerateViewModel.cs`.
- **Reported / Fixed:** 2026-06-14 (audit of the shipped v1.2.0).
- **Symptom:** after v1.2.0 let the Generate tab produce **all four control types**, the
  **"Use in Create"** handoff still forced `RotaryKnob`. A generated **fader/slider** was treated
  as a layered knob and **rotated** instead of sliding; a generated **button** stacked both `off`
  and `on` states on top of each other (the `Frame` indexing was lost).
- **Root cause:** `ImportLayeredFromPathAsync` (and the import that backs it) always set the knob
  type / appended Rotate layers, written before the button + linear control types existed in
  Generate.
- **Fix (commit `80dc1b5`):** the handoff now branches on the **generated control type** — a
  **knob** maps to the body + pointer layer stack; a **button** maps its `off`/`on` groups to
  `LayerBehavior.Frame` state layers; a **fader/slider** is flattened to the single source the
  linear renderer expects.
- **Regression guard:** `GenerateIntegrationTests` exercises the per-type handoff (knob →
  body/pointer layers; button → off/on Frame layers; the handoff carries the generated type).

### BUG-009 — Untrusted SVG parsed without DTD/entity hardening (DoS / XXE exposure) ✅
- **Severity:** High (security — entity-expansion denial-of-service and external-entity / SSRF on
  attacker-influenced input).
- **Component:** `src/StripKit/Services/SvgSanitizer.cs` (AI replies) +
  `src/StripKit/Services/LayeredImportService.cs` (imported `.svg` files).
- **Reported / Fixed:** 2026-06-14 (audit).
- **Symptom:** both the AI-reply sanitizer and the layered-file import picker parsed SVG with a bare
  `XDocument.Parse`, which honours DTDs — leaving "billion laughs" entity-expansion DoS and
  external-entity / SSRF (`<!ENTITY x SYSTEM "file://…">`) open on attacker-supplied content.
- **Root cause:** `XDocument.Parse(string)` uses default reader settings (`DtdProcessing.Parse`,
  a default resolver), unsafe for untrusted XML.
- **Fix (commit `80dc1b5`):** new `Services/SafeXml.cs` — `XDocument.Load` with
  `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`. Applied in
  **both** callers. A DTD now throws `XmlException`, which both callers already treat as "malformed
  SVG"; legitimate generated control art carries no DTD, so the happy path is unaffected.
- **Regression guard:** `SvgSanitizerTests` rejects a DTD-bearing document as malformed (so the
  prohibition is asserted, not just present). *(The matching `LayeredImportServiceTests` guard was
  not actually added until BUG-010 below — this line over-claimed it at the time.)*

### BUG-010 — billion-laughs DoS reachable via the SVG file-import path (BUG-009 hardening bypassed) ✅
- **Severity:** Medium (security — local entity-expansion denial-of-service: a crafted `.svg` opened
  via the layered-file picker could hang / exhaust memory. External-entity / SSRF was **not** reachable
  — `svg-net` defaults `ResolveExternalXmlEntites = ExternalType.None` — so this was DoS only).
- **Component:** `src/StripKit/Services/LayeredImportService.cs` (`ImportSvg`).
- **Reported / Fixed:** 2026-06-18 (audit).
- **Symptom:** BUG-009 added `SafeXml.Parse` (DTDs prohibited) to the SVG file-import path, but in
  `ImportSvg` it ran **after** `SvgSkia.FromSvg(text)` had already parsed the **raw** untrusted text.
  Svg.Skia builds its model with the `svg-net/SVG` library, which defaults to `DtdProcessing.Parse`
  with no `MaxCharactersFromEntities` cap — so a "billion-laughs" entity bomb was fully expanded
  inside `FromSvg` before `SafeXml` ever rejected it. The AI-reply path was unaffected (its SVG is
  re-serialized DTD-free by `SvgSanitizer` before it reaches the importer); only the **file picker**
  (arbitrary user SVG) was exposed.
- **Root cause:** parse ordering — the hardened gate ran second. The documented guarantee ("applied
  to both `SvgSanitizer` and the layered-file import picker") was only half-true for the importer.
- **Fix (2026-06-18):** moved `SafeXml.Parse(text)` to the **top** of `ImportSvg`, before
  `SvgSkia.FromSvg`. A DTD now throws at the gate (caught by `Import()` → "no usable layers"), so the
  raw text only reaches Svg.Skia once it is known DTD-free. Reorder only — no re-serialization, no new
  dependency, byte-identical output for legitimate (DTD-free) SVG.
- **Regression guard:** `LayeredImportServiceTests.Svg_import_rejects_a_doctype_entity_bomb_without_expanding_it`
  (a 1e9-expansion bomb must return null in < 5 s) +
  `Svg_import_does_not_resolve_an_external_entity`. Suite 172 → **174 green**.

### BUG-011 — release script aborted at `git add` on a benign git stderr warning (PS 5.1) ✅
- **Severity:** Medium (release-tooling defect — aborted the v1.3.0 release mid-way; the binary was
  built + signed but the commit/tag/push didn't run, leaving a partial release to finish by hand).
- **Component:** `scripts/Invoke-Release.ps1` (the commit/tag/push block).
- **Reported / Fixed:** 2026-06-18 (hit during the v1.3.0 release).
- **Symptom:** the script threw at `git -C $root add …` with a `NativeCommandError`, the message being
  git's harmless `warning: in the working copy of 'docs/CHANGELOG.md', LF will be replaced by CRLF`.
  The `git add` actually succeeded, but the script stopped before commit/tag/push.
- **Root cause:** the script runs under `$ErrorActionPreference = 'Stop'`; in **Windows PowerShell 5.1**
  ANY native-command stderr output is wrapped in an ErrorRecord, so git's progress/warning lines on
  stderr become terminating errors. The working-tree files had LF line endings (written this session),
  which made git emit the CRLF warning that prior CRLF-clean releases didn't.
- **Fix (2026-06-18):** wrapped the git commit/tag/push block in `$ErrorActionPreference = 'Continue'`
  (restored in a `finally`) and gated each git call on `$LASTEXITCODE` instead — so a stderr warning no
  longer aborts the release, while a real non-zero exit still throws. The v1.3.0 release itself was
  completed by hand (commit `f38a5f5`, tag `v1.3.0`) after the abort.
- **Regression guard:** none (release-script issue, not unit-testable) — the next release exercises it;
  the exit-code gating is the guard.

### BUG-012 — UnpremultiplyAlpha corrupted colours (Premul-tagged bitmap holding straight bytes) ✅
- **Severity:** High (the P3 "Un-premultiply alpha" halo fix produced visibly wrong colours on export).
- **Component:** `src/StripKit/Services/FrameSequenceAssembler.cs` (`UnpremultiplyAlpha`).
- **Reported / Fixed:** 2026-07-02 (audit of the unreleased v1.4.0 work).
- **Symptom:** ticking "Un-premultiply alpha" on the Assemble tab shifted colours. A straight
  (200,100,50) at α128 came off disk as ~(255,199,100,128).
- **Root cause:** the method wrote straight (un-premultiplied) RGB into a bitmap produced by `src.Copy()`,
  which **preserves the source's `Premul` alpha type**. Downstream (the canvas blit into the strip, the
  PNG encode) then re-interpreted the straight bytes as premultiplied. The existing test only inspected
  raw `dst.Bytes`, so it passed while the feature was broken.
- **Fix:** allocate the destination as `SKAlphaType.Unpremul` and write the straight bytes into it.
- **Regression guard:** `RenderQcTests.UnpremultiplyAlpha_returns_an_unpremultiplied_bitmap_that_survives_a_premultiplied_roundtrip`
  (asserts `AlphaType == Unpremul`, `GetPixel`, and a real PNG encode/decode) +
  `…recovers_colour_across_a_multi_pixel_frame…` (the stride loop). Both fail against the old code.

### BUG-013 — Layered SVG file-import fetched external `<image>` references (SSRF) ✅
- **Severity:** High (security — SSRF / local-file-existence oracle triggered by merely opening a file).
- **Component:** `src/StripKit/Services/LayeredImportService.cs` (`ImportSvg`) + `SvgSanitizer.cs`.
- **Reported / Fixed:** 2026-07-02 (audit; **verified live** — a beacon connected).
- **Symptom:** opening a crafted `knob.svg` containing `<image xlink:href="http://attacker/…">` (or
  `file:///…`) issued an outbound request during rasterization, before any content was shown.
- **Root cause:** the file-import path ran only `SafeXml.Parse` (a DTD/entity gate) and then handed the
  **raw** text to `Svg.Skia.FromSvg`. `SvgSanitizer` — which strips `<image>`/`<script>`/`<foreignObject>`/
  `on*`/non-`#` `href` — was only applied to the AI-reply path, so the file picker was unprotected, and
  svg-net resolves an external `<image href>` over HTTP(S)/disk during `DrawPicture`. (The BUG-009/010
  hardening addressed DTDs/entities only, not element-level external references.)
- **Fix:** `SvgSanitizer.Sanitize` is now public; `ImportSvg` runs it on the parsed document and feeds the
  re-serialized, sanitized SVG to `Svg.Skia` (both the full render and the per-layer renders).
- **Regression guard:** `LayeredImportServiceTests.Svg_import_does_not_fetch_an_external_image_reference`
  — imports an SVG referencing a loopback listener and asserts **no connection** (fails against the old code).

### BUG-014 — Static layer shifted/blanked button & toggle state frames (absolute-index matching) ✅
- **Severity:** Medium (broken state art for a realistic layered button/toggle with a shared border).
- **Component:** `src/StripKit/Services/SkiaFilmstripRenderer.cs` (`RenderButtonLayers`) + the
  `FilmstripEngine.cs` mirror + `MainWindowViewModel.cs` (state-frame count).
- **Reported / Fixed:** 2026-07-02 (audit).
- **Symptom:** a layered button/toggle with a `Static` border/shadow group ahead of its off/on states
  rendered frame 0 as border-only (state-less) and pushed off→frame 1; the state-frame count was also
  inflated by the Static layers.
- **Root cause:** `RenderButtonLayers` matched `Frame` layers by their **absolute** index in the layer
  stack (`i == frameIndex`), which only holds when no Static layer precedes them.
- **Fix:** match `Frame` layers by their **ordinal position among Frame layers** (renderer + mirror), and
  derive the state-frame count from the number of `Frame` layers, not the total.
- **Regression guard:** `ToggleRenderTests.A_static_border_before_the_state_layers_does_not_shift_the_off_and_on_states`.

### BUG-015 — RegenerateSetItem reused a cancelled CancellationTokenSource ✅
- **Severity:** Medium (a Generate-tab set item silently failed to regenerate after any prior cancel).
- **Component:** `src/StripKit/ViewModels/GenerateViewModel.cs` (`RegenerateSetItemAsync`).
- **Reported / Fixed:** 2026-07-02 (audit).
- **Symptom:** after cancelling a set generation, clicking Regenerate on one grid item did nothing and
  showed "Regeneration cancelled." even though the user hadn't cancelled that action.
- **Root cause:** `_setCts ??= new()` kept the shared, already-cancelled source; the item's token was
  cancelled before the work started (`Task.Run(…, ct)` threw immediately).
- **Fix:** cancel/dispose/recreate `_setCts` at the start of the command (matching `GenerateSetAsync`).
- **Regression guard:** `GenerateViewModelTests.Regenerating_a_set_item_after_a_cancel_still_works_and_does_not_abort`.

### BUG-016 — Audit low-severity cleanup (drift metric, resource leaks, blank tip box) ✅
- **Severity:** Low (batch of small correctness / resource / UI defects found in the same 2026-07-02 audit).
- **Fixed:** 2026-07-02.
- **Items:**
  - **QC drift over-reported for mixed-size sequences** (`FrameSequenceAssembler.AnalyzeQc`) — it scaled a
    per-frame-normalized centre spread by the largest cell, so an object at the same absolute pixel across
    a 64² and a 128² frame read as ~24px of drift. Now measured in absolute pixels. Guard:
    `RenderQcTests.AnalyzeQc_does_not_report_phantom_drift_for_mixed_size_frames…` (+ a positive/negative
    premultiplied-edge pair that the QC branch previously had no positive test for).
  - **Three resource leaks** (`GenerateViewModel` / `IAssetGenerationProvider`): matching-set & variation
    preview `Bitmap`s were dropped to GC instead of disposed; the auto-retry left the first attempt's temp
    SVG on disk; the provider never disposed its `HttpResponseMessage`. All now disposed/deleted.
  - **Tutorial tip box rendered blank** (`Views/TutorialOverlay.axaml`) — it bound the undefined
    `GlassFill`/`GlassBorder` keys (a Depth-rebrand rename miss); corrected to `GlassFillBrush`/`GlassBorderBrush`.
- **Regression guard:** the QC drift + premultiplied-edge tests above; the leaks/tip-box are pattern fixes
  verified by inspection (the disposes/renames), with the suite staying green.

### BUG-017 — GridColumns serialized into skin.json with no lower-bound guard (schema violation) ✅
- **Severity:** Medium (an exported manifest could violate its own JSON Schema — a downstream loader
  reading `skin.json` strictly could reject it).
- **Component:** `src/StripKit/Services/ManifestService.cs` (`BuildSingleControl`).
- **Reported / Fixed:** 2026-07-02 (4-dimension adversarial review of the v1.5.0 sprite-grid work,
  before commit/release).
- **Symptom:** a non-positive `GridColumns` — reachable via an unclamped VM property, a loaded
  `RenderPreset`, or a hand-built `FilmstripSettings` — was written straight through into the exported
  `skin.json`'s `gridColumns` field, which the `plugin-asset-manifest` JSON Schema declares
  `"minimum": 1`.
- **Root cause:** `BuildSingleControl` serialized `settings.GridColumns` verbatim; the renderer has its
  own defensive clamp, but the manifest serializer never mirrored it.
- **Fix:** clamp with `Math.Max(1, settings.GridColumns)` at the point of serialization, mirroring the
  renderer's existing clamp.
- **Regression guard:** `ManifestServiceTests.BuildSingleControl_clamps_a_non_positive_grid_columns_to_one`
  (Theory: `GridColumns` = 0 and -3). Shipped in commit `57f071c`.

### BUG-018 — DeletePreset could remove both entries of a duplicate-named preset (name vs. reference mismatch) ✅
- **Severity:** Low (persisted-store desync; only reachable via a hand-edited `settings.json`, since
  normal app usage prevents duplicate names via the save-overwrite guard).
- **Component:** `src/StripKit/ViewModels/MainWindowViewModel.cs` (`DeletePreset`).
- **Reported / Fixed:** 2026-07-02 (4-dimension adversarial review of the v1.5.0 render-presets work,
  before commit/release).
- **Symptom:** deleting a preset removed it from the UI's `Presets` collection **by object reference**,
  but removed matching entries from `_settings.Settings.RenderPresets` **by a case-insensitive name
  match** (`RemoveAll`). If two persisted presets ever shared a name, deleting one from the UI silently
  deleted **both** from the persisted store, desyncing the UI list from disk until the next app restart.
- **Root cause:** the two removals used different identity semantics — reference for the observable
  collection, name for the persisted list.
- **Fix:** switched the settings-side removal to reference-based
  (`_settings.Settings.RenderPresets.Remove(p)`), matching `ObservableCollection.Remove`'s semantics.
- **Regression guard:**
  `RenderPresetTests.Deleting_a_duplicate_named_preset_removes_only_the_selected_one_by_reference`.
  Shipped in commit `57f071c`.

### BUG-019 — Import tab missing the "Show in folder" affordance the Create/Assemble tabs have ✅
- **Severity:** Low (visual/UX inconsistency, not a functional defect — Extract/Re-stack/Resample all
  worked; only the post-export "jump to the file" convenience was absent).
- **Component:** `src/StripKit/ViewModels/ImporterViewModel.cs`, `src/StripKit/Views/ImporterView.axaml`.
- **Reported / Fixed:** 2026-07-03 (owner, live-testing the v1.5.0 build: "switching between Create /
  Assemble and Import looks odd — Import's transport tile is missing the button the other two have").
- **Symptom:** the v1.5 "Show in folder" enhancement (`RevealExportCommand` + `LastExportPath`,
  `Helpers/ShellHelper.RevealInFolder`) was wired into the Create and Assemble tabs but never added to
  the Import tab, even though Import also exports files (extract / re-stack / resample). The Import
  tab's transport-tile Grid also only had 2 rows (`RowDefinitions="*,Auto"`), one short of the
  `*,Auto,Auto` the other two tabs use for the button's own row.
- **Root cause:** the v1.5 enhancement (prior session) was scoped to "Create + Assemble" and the Import
  tab was never revisited for parity.
- **Fix:** added `LastExportPath`/`RevealExportCommand`/`CanReveal` to `ImporterViewModel` (set after
  each of `ExtractCurrentFrameAsync`/`ExportRestackedAsync`/`ExportResampledAsync`), added the missing
  grid row + button to `ImporterView.axaml`, matching Create/Assemble exactly.
- **Regression guard:** `ImporterViewModelTests.RevealExportCommand_is_disabled_until_something_has_been_exported`
  and `ImporterViewModelTests.Exporting_sets_LastExportPath_and_enables_the_reveal_command`.

### BUG-020 — Getting Started tip text overflowed the dialog card instead of wrapping ✅
- **Severity:** Low (visual only — the tip text ran off the right edge of the tutorial overlay instead
  of wrapping onto a second line).
- **Component:** `src/StripKit/Views/TutorialOverlay.axaml`.
- **Reported / Fixed:** 2026-07-03 (owner, live-testing the Getting Started overlay).
- **Symptom:** the 💡-icon-plus-tip row used a horizontal `StackPanel`, and the tip `TextBlock` had
  `TextWrapping="Wrap"` — but still rendered as one unbroken line past the card's right edge.
- **Root cause:** a `StackPanel` measures its children at *unconstrained* width along its orientation
  axis, so a horizontal `StackPanel`'s children never receive a width to wrap against — `Wrap` has
  nothing to do. (The rest of the codebase's horizontal `StackPanel`s are all fixed-width
  buttons/labels, so this is the only place the anti-pattern combined with wrapping text.)
- **Fix:** replaced the `StackPanel` with a `Grid ColumnDefinitions="Auto,*"` — the icon in the `Auto`
  column, the tip in the `*` column, which *is* width-constrained and wraps correctly.
- **Regression guard:** none automated (a pure-XAML layout constraint with no headless assertion for
  wrapped line count); verified live via computer-use screenshot.

### BUG-021 — Assemble tab silently dropped HDR frames (.exr / .hdr / 16-bit .tif) on drag-drop and "Add files…" ✅
- **Severity:** High (a documented, primary feature — path-traced HDR ingest — was unreachable via two
  of its three load paths; the frames were discarded with no warning).
- **Component:** `src/StripKit/Views/AssembleView.axaml.cs` (drop handler) +
  `src/StripKit/Services/FileDialogService.cs` (`OpenImagesAsync` picker filter).
- **Reported / Fixed:** 2026-07-04 (adversarial docs-audit finding — both the website and in-app tutorial
  claimed EXR/HDR/TIFF were accepted on drop).
- **Symptom:** dragging a `.exr` / `.hdr` / 16-bit `.tif` sequence onto the Assemble preview added
  nothing; the "Add files…" picker wouldn't even show those files. Only "Choose folder…" ingested them.
- **Root cause:** three separate accepted-extension lists had drifted. `FrameSequenceViewModel` and the
  "Choose folder…" enumeration used the full 9-extension list (incl. the HDR formats), but the view's
  `OnDrop` handler had a **private 5-extension duplicate** (`.png/.webp/.bmp/.jpg/.jpeg`) that filtered
  the HDR frames out *before* they reached the view model, and `FileDialogService.OpenImagesAsync`'s
  `FileTypeFilter` likewise omitted them.
- **Fix:** promoted `FrameSequenceViewModel.AcceptedExtensions` to `public static` as the single source
  of truth; the view's drop handler now references it directly (no duplicate to drift), and the
  `OpenImagesAsync` "Images" filter gained `*.exr / *.hdr / *.tif / *.tiff`.
- **Regression guard:** `FrameSequenceViewModelTests.AcceptedExtensions_include_the_HDR_formats` (locks
  the shared list) + `…Dropped_HDR_frames_are_accepted_not_silently_ignored` (four HDR frames dropped →
  four rows). Suite 333 → 335.

---

## Notes

- No runtime bugs are currently known. The renderer output is locked by golden-image
  tests; a future intentional render change must update baselines (see
  `docs/TESTING.md`).
- **Informational (not a bug):** the app **and** installer are **code-signed** via Azure
  Trusted Signing (the `VybeCode` certificate profile), wired into the release pipeline since
  v0.8.0's signed re-release and used for every release since. The earlier "~4/71 VirusTotal FPs
  on the *unsigned* installer / SmartScreen prompt" caveat is now **historical**. (Signing uses
  `signtool.exe` + the `Microsoft.Trusted.Signing.Client` dlib — not AzureSignTool, which 403s
  against Trusted Signing endpoints; see `docs/PACKAGING.md`.)
- **Informational (not a bug) — release integrity:** the v1.2.0 **feature source** was accidentally
  omitted from the "Release v1.2.0" commit (which staged only version files + the installer), so the
  `v1.2.0` tag could not rebuild its own installer. The source was committed retroactively
  (`b55380f`, 2026-06-14), matching the shipped binary, before the v1.2.1 fixes. Process guard: commit
  feature work **before** running the release script (which stages only version files by design). As
  of **v1.2.2** this guard is **enforced, not just documented** — `Invoke-Release.ps1` aborts the
  release if the tracked working tree has uncommitted source (untracked strays allowed; `-AllowDirty`
  overrides). *(The same v1.2.2 tooling pass also fixed the Stage-3 website-changelog splat — a
  trailing `-Push` mis-bound under array splatting; switched to hashtable splatting — a build-script
  fix, not a tracked app bug.)*
- Known *limitations* (not bugs) live in `docs/ROADMAP.md` / `docs/ARCHITECTURE.md`:
  importer detection is a dimension-based guess (editable + verified). The **Skin tab** does
  multi-control `skin.json` (the Create-tab export still emits a single control). Auto-pointer
  extraction (★ #3 step 2) leaves a small central residual dot when the needle passes through the
  pivot (a verify-and-tweak starting point, knob-only) — not a defect. Generate is type-aware
  across knob/fader/slider/button, but the fader/slider/meter output paths still want a live eyeball
  (knob is the proven path).
