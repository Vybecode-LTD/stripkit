# HANDOFF — StripKit

> Version 0.5.0 · last-updated 2026-06-03 · last-audit 2026-06-03
>
> Pick-up notes for the next session. Read `CLAUDE.md` and `docs/SOURCE_MAP.md`
> first, then this.

## Current state

- **Builds clean** (`dotnet build`, 0 warnings / 0 errors) on .NET 9; the GUI boots
  with three tabs (**Create** | **Import** | **Batch**); Create has four component
  types (knob, vfader, hslider, **meter**).
- **Tests green:** `dotnet test` → **41 / 41** (~0.5 s).
- **Roadmap:** ✅ P0 verify · ✅ P1 drag-drop · ✅ P2 importer · ✅ P3 manifest ·
  ✅ P4 golden tests · ✅ P5 batch · ✅ P6 meter · **next: P7 packaging** (final phase).
- **Open bugs:** 0 (`docs/BUGS.md`). Two pre-existing build blockers were fixed.

## What works end-to-end

- **Create:** load (button or drop) → live preview (scrub/play) → export PNG, optional
  `@2x`, optional `skin.json` manifest.
- **Import:** load a strip → dimension-based detection (editable count) → scrub →
  extract a frame / re-stack orientation.
- **Batch:** choose an input + output folder and a render template → export a strip
  per source, off the UI thread, with progress + a working cancel (optional @2x /
  manifest per strip); failures are isolated and the run continues.
- **Meter (Create):** procedural LED segments (On/Off colour, gap, count) or layered
  on/off art; four fill directions; discrete or continuous; previews + exports like
  any other type (procedural needs no source).
- Verified by tests + headless drives + reviewed golden baselines: a 64×6500 strip
  detects as 100 frames and slices/re-stacks correctly; a knob renders a clean 270°
  sweep; meters fill correctly in every direction; manifest output is schema-valid.

## Next step — Phase 7 (packaging & distribution, the final phase)

Produce a signed, single-file Windows build (and optionally an installer) so the tool
ships to non-developers. Skill: `dotnet-installer-publishing` (global). Likely shape:
`dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishSelfContained=true`,
an app icon, code-signing, and optionally an Inno/WiX/MSIX installer + auto-update.
**Confirm the distribution target** (single exe vs. installer; signing certificate
availability) before building the pipeline. **Done when:** a single-file exe runs on a
clean Windows machine without the SDK. After Phase 7 the roadmap is complete.

## How to resume

```bash
dotnet build StripKit.sln -c Debug      # expect 0/0
dotnet test                             # expect 41/41
dotnet run --project src/StripKit       # launch the app
```

To manually exercise drag-drop / import, drop a PNG onto a tab's preview. A sample
strip can be produced by re-running the throwaway harness at
`%TEMP%\stripkit-smoke` (a `ProjectReference`-based console that drives the real
renderer/importer/manifest and writes artifacts to `…\out`). The harness is outside
the repo and is not part of the build.

## Warnings / gotchas for the next session

- **`FilmstripEngine.cs` (repo root) is a hand-maintained mirror** of
  `Services/SkiaFilmstripRenderer.cs` + the `Models`. It is NOT compiled by the app.
  If you change the renderer math, update this file too (or it drifts). In sync as of
  this audit.
- **Importer detection is a guess** biased to the largest plausible frame count, so
  "round" strips (e.g. 80×640) read as 128 frames — the UI makes the count editable
  and the user verifies the sweep. This is by design; don't "fix" it by trusting the
  count.
- **Do not** rewrite `SkiaFilmstripRenderer`, change the `(N-1)` angle divisor, move
  VM logic into code-behind, or reference Avalonia UI types from view models (the
  preview `Bitmap` alias is the one allowed exception).
- The manifest UI emits **one control**; the model supports multi-control skins and
  value ranges if/when a multi-asset workflow is added.
- No GUI automation is available in this environment (computer-use disconnected) — UI
  changes are verified via build + boot + headless tests + the rendered-artifact
  harness, not by clicking the window.
