# ROADMAP — StripKit

A phased plan for taking StripKit from its current v1 scaffold to a complete
tool. Each phase is sized to compile and run on its own, names the skill that
should guide it, and states a done-condition. Work the phases in order; do not
jump ahead without confirming the open questions in `docs/KICKOFF.md`.

## Phase 0 — Verify the scaffold (do first)

Build and run the existing project, load a PNG, confirm the live preview animates
and Export writes a strip. Fix only what blocks the build (most likely a SkiaSharp
transitive-version conflict — align per `README.md`).
**Done when:** `dotnet run --project src/StripKit` opens the window and a
round-trip (load → preview → export) works.

## Phase 1 — Drag-and-drop input

Let the user drop a PNG onto the preview area (or window) to load it as the source.
Reuse the existing view-model load logic; do not duplicate it.
**Skill:** `avalonia-drag-drop-files`.
**Done when:** dropping a PNG loads and previews it exactly as the Load button does.
**Status:** ✅ Done (2026-06-03) — the preview `Border` is a drop zone; the button
and the drop handler share `MainWindowViewModel.LoadSourceFromPath`. Covered by VM
load-path tests and a headless drop-zone test.

## Phase 2 — Filmstrip importer

Add the ability to open an *existing* strip (KnobMan export, purchased pack),
detect its frame count and layout, and either extract a single frame or re-stack
it at a new orientation. Confirm with the user whether this is a second tab or a
separate window before building the UI.
**Skill:** `filmstrip-importer-engine`.
**Done when:** an existing strip's frame count is detected, displayed, editable,
and a frame can be extracted; detection is verified visually, not trusted blindly.
**Status:** ✅ Done (2026-06-03) as a second tab (confirmed). `FilmstripImporter`
(SkiaSharp) detects count/orientation/kind from dimensions + flags ambiguous cases;
the **Import** tab shows the detection, an editable count, a frame scrubber, and
extract / re-stack-orientation export. Covered by importer-engine + VM tests, and
verified visually (a 64×6500 strip → 100 frames, frames sliced + re-stacked
correctly). Frame-count *resampling* (downsampling N) noted in the skill but not
yet built.

## Phase 3 — Manifest export

When exporting, optionally also emit a `skin.json` entry (or a full manifest) that
binds the strip to a parameter id, with frame count, frame size, stack direction,
and bounds — so a skinning engine / JUCE LookAndFeel can auto-load it.
**Skill:** `plugin-asset-manifest`.
**Done when:** export can produce a valid manifest fragment alongside the PNG,
validated against the schema in the skill.
**Status:** ✅ Done (2026-06-03). The Create-tab export has an "Also write a
skin.json manifest" toggle + a parameter-id field; on export it writes
`<name>.skin.json` next to the PNG (one control, relative `asset`/`asset2x`,
frames, frame size, stack, base-resolution `bounds`). `ManifestService` (System.Text.Json,
camelCase) builds + serializes it; tests assert the mapping and conformance to the
skill's JSON Schema (required keys, enums, types). Multi-control skins and the
optional `valueMin/Max` are supported by the model but not yet surfaced in the UI.

## Phase 4 — Golden-image tests

Stand up a small test project and lock the renderer's output with golden-image
tests so later refactors cannot silently change the pixels. Baseline the three
component types at default settings plus the min/last-frame edge cases.
**Skill:** `image-regression-testing`. Confirm the test framework (xUnit assumed).
**Done when:** `dotnet test` passes against committed baselines, and an
intentional render change is caught by a failing test with a diff image.
**Status:** ✅ Brought forward to 2026-06-03 (alongside Phase 1, to close the
drop-test gap). `tests/StripKit.Tests` has 6 committed baselines (3 knob frames, an
8-frame strip, fader + slider mids) plus VM and headless tests; `ImageAssert` emits
expected/actual/diff PNGs on mismatch. **11/11 green.**

## Phase 5 — Batch processing

Process a folder of source images into many filmstrips in one run, with progress
and cancellation. Keep the render off the UI thread.
**Skills:** `live-preview-render-loop` (threading patterns), `csharp-mastery`.
**Done when:** a folder of N PNGs exports N strips with a progress indicator and a
working cancel.
**Status:** ✅ Done (2026-06-03) as a third tab (**Batch**). `BatchProcessor`
(SkiaSharp, no Avalonia) runs the whole loop via `Task.Run` (off the UI thread),
reports progress per item, and cancels between items without throwing (returns a
result). The **Batch** tab picks an input + output folder and a render template,
shows a progress bar + per-file text + a results summary, and has a working Cancel.
Per-knob frame sizing via "Square knob frames to each source"; optional @2x + manifest
per strip. Failures are isolated (one bad file doesn't abort the run). Covered by
4 processor integration tests + 2 VM gating tests. **31/31 green.**

## Phase 6 — Meter mode (design first)

Extend the engine to level/VU meters (progressive segment lighting; a needle is
just a rotary). Design the segment model before coding — this is a new render
mode, not a tweak. Note that the existing `filmstrip-asset-engineering` skill
already covers meter frames and is the reference here.
**Done when:** a meter filmstrip renders with N lit states and previews correctly.
**Status:** ✅ Done (2026-06-03), design signed off first. New `ComponentType.Meter`
+ `MeterFillDirection` enum + meter fields on `FilmstripSettings`. The renderer's
`RenderMeterFrame` does **procedural** segment bars (On/Off colour + gap) when no art
is loaded, or a **layered** reveal (source = on-state art over the background off-state
art) when art is present — auto-selected. All four fill directions (Up/Down/Left→Right/
Right→Left); discrete by default with a continuous toggle. Create-tab "Meter" type +
METER settings; preview/export/manifest(`"meter"`)/batch reuse the existing paths;
a procedural meter exports without a source. Mirrored in `FilmstripEngine.cs`. Covered
by 9 renderer tests (5 golden baselines + 4 pixel-logic) + 1 VM test. **41/41 green.**
Deferred: peak-hold, dual/stereo meters, dB segment spacing, per-segment colour ramps.

## Phase 7 — Packaging and distribution

Produce a signed, single-file Windows build (and optionally an installer) so the
tool ships to non-developers.
**Skill:** `dotnet-installer-publishing` (global).
**Done when:** a single-file exe runs on a clean Windows machine without the SDK.
**Status:** 🔶 In progress (2026-06-03). A self-contained `win-x64` publish runs
without the SDK; **Velopack 1.1.1** is integrated (`VelopackApp.Run()` first in
`Main`, verified by the packer) and an **unsigned installer**
(`StripKit-win-Setup.exe`) + update feed (`*-full.nupkg` + `RELEASES`) build via
`vpk pack`. In-app update check (`UpdateService`) targets a **GitHub Releases** feed
(no-op until the repo URL is set + a release is published). Remaining: the app
**icon** (supplied PNG → multi-res `.ico` → `<ApplicationIcon>` + window + `vpk -i`),
fill the real repo URL, and a `docs/PACKAGING.md` workflow doc. Code signing is
deferred (the `signtool` step will be documented).

## Phase 8 — Landing page website (future)

A public landing/marketing page for StripKit: what it does, screenshots/mockups, a
download button for the latest release, and links to the docs. Static site or a
small framework, SEO/GEO-optimized per `SEO_OPTIMIZATION.md`. Deferred — scoped once
packaging ships.
**Done when:** a deployed page describes StripKit and links to the current download.

## Standing conventions for every phase

Keep the MVVM boundary (no UI types in view models, minimal code-behind), the
design tokens (Obsidian glassmorphism — dark theme, `#e8440a` accent, Verdana-led
sans-serif; see `App.axaml`), compiled bindings, and `python -m pip` style invocation
in any Python helpers. After each phase, build, run, confirm, and update the "Last
completed task" section of `CLAUDE.md`.
