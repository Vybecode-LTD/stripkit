---
document: HANDOFF
version: 0.8.0
last-updated: 2026-06-05
last-audit: 2026-06-05
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into animated
filmstrip sprite sheets for audio-plugin GUI controls (knobs, faders, sliders, meters).
Stack: .NET 9 / Avalonia 11.3 / SkiaSharp / Inno Setup. Public, MIT-licensed.

**Phase:** Post-ship (v0.8.0 live). The app is a four-tab `TabControl` —
**Create | Import | Batch | Skin**. Two of the three ★ vNext bets are done (value-arc,
code-export); the third (**layer-aware animation**) is in progress — **steps 1 & 2 of 3
done**, step 3 (PSD/SVG import) remains.

## Last Session (2026-06-05) — three gap features + v0.8.0 ship + ★ step 2

A long, productive session. Everything is committed on `main` (== origin).

### Shipped in v0.8.0 (live on GitHub Releases)
Four features, each its own commit, then cut as **v0.8.0** via `Invoke-Release.ps1 -Bump minor`
(test gate **94/94** at the cut) → CI VirusTotal-scanned + created the public release:
- **★ Layer-aware knob — step 1: base + pointer** (`31c203b`). A general layer model
  (`Models/RenderLayer.cs`: `LayerBehavior {Static, Rotate}` + a per-layer pivot) on
  `FilmstripSettings.Layers`; `RenderLayers` composites a static body + a rotating pointer in
  `RenderFrame`/`RenderStrip` (optional index-matched `layerArt` param). Explicit **Base/Pointer
  slots** in the Create tab's rotary panel; the pointer rotates about its own pivot. **Gated by
  defaults — empty `Layers` renders the single source byte-identical** (all prior goldens
  unchanged). Mirrored in `FilmstripEngine.cs`. +12 tests.
- **Batch-tab meter settings** (`e126daf`). The Batch template exposes the full meter panel
  (segments, fill, continuous, on/off colours) + a **"source is a backdrop"** toggle (each file is
  the lit on-state art → layered, or a housing with procedural LEDs → procedural). New
  `BatchOptions.MeterSourceIsBackdrop`. Resolves the v0.6.0 carryover (BUG-007 follow-up). +2 tests.
- **Skin tab — multi-control `skin.json` builder** (`4a9e2ac`). A new fourth tab that binds
  several strips to several parameters in one manifest. Add controls **from a strip**
  (`FilmstripImporter.Detect` auto-fills) or **blank**; per-control detail editor (id/type/param/
  asset/frames/size/stack/**bounds**/**value range**); skin name/author/design-resolution/window
  background; **Export skin.json…** to a folder. New `IManifestService.BuildManifest`,
  `SkinViewModel` + `SkinControlEntry` + `SkinView`. +6 tests.
- **Importer frame-count resampling** (`322a80d`). The Import tab re-times a strip to a new frame
  count (`FilmstripImporter.Resample`, **nearest-frame** `round(j·(N−1)/(M−1))` — endpoints land on
  min/max, no blending so a pointer never ghosts). New "Resample frame count" target + Export. +2 tests.

Also committed: the **`layer-aware-filmstrip-compositing` project skill** (`5fa2ba4`,
`.claude/skills/`) — the reusable static-base + transformed-overlay pattern; passed the
skill-authoring-linter (0/0), distributable `.skill` packaged under `dist/` (gitignored).

### Unreleased on `main` (after the v0.8.0 tag) — ★ step 2
- **★ Layer-aware knob — step 2: auto-pointer extraction** (`afca651`). An **"Auto-extract from
  flat knob…"** button splits a single flat knob image into the base + pointer slots automatically.
  `Services/PointerExtractor.cs` uses the **radial-symmetry residual**: a knob body is rotationally
  symmetric, so the indicator is whatever breaks that symmetry — the robust per-radius mean is the
  symmetric base, the residual is the pointer. Returns a **confidence** (low for asymmetric bodies,
  flagged). A starting guess the user verifies via the preview/scrub; assumes the art is drawn at
  the minimum (frame-0) position. Pure SkiaSharp (like `ContentAnalysis`); **app-only — NOT mirrored
  in `FilmstripEngine.cs`** (the engine holds only render math). +4 tests. **Eyeballed**: the
  extracted base is a clean symmetric body and the rendered sweep shows a crisp needle rotating
  about a static body (one minor cosmetic central dot at the pivot, inherent to a needle through
  the centre). Suite **98/98**.

### Decisions made (not derivable from the diff)
- **Layer model = a general `RenderLayer` list** (not a single bolt-on pointer field) — chosen to
  set up steps 2–3, which reuse the same model + slot UI.
- **Pointer has its own pivot** (independent of the body), seeded from the body centre.
- **Batch meters via a toggle** — both layered (lit art) and procedural (backdrop + LEDs) modes,
  because a batch file is meaningful as either; one bool threads through.
- **Skin authoring = a dedicated tab** (not a Create-tab accumulator) for clean separation.
- **Resampling = nearest-frame only** — blended resampling intentionally not built (it ghosts a
  moving indicator; nearest is correct for filmstrips).
- **Step-2 extraction = radial-symmetry residual** (over luminance thresholding) — principled,
  shape-agnostic, and produces both layers cleanly; **auto-fill-and-verify** workflow (not a
  one-shot convert); **assume the flat art is the frame-0 position** (simplest; user verifies).

## Current State

### Working
- **v0.8.0 live:** https://github.com/Vybecode-LTD/stripkit/releases/tag/v0.8.0
  (`StripKit-Setup-0.8.0-x64.exe`, ~33.5 MB self-contained).
- Tests **98/98 green**; build 0/0; app boots clean with all four tabs. CI runs on every push/PR.
- `main` == origin (step 2 pushed). 0 open bugs.

### Known issues / limitations (not bugs)
- `FilmstripEngine.cs` is a hand-maintained mirror of the renderer + render-math models (now
  includes the `RenderLayers` layered path + `RenderLayer`/`LayerBehavior` + `Layers`). It does
  **not** include `PointerExtractor`/`ContentAnalysis`/importer/etc. (app-only services) — by design.
- Auto-pointer extraction leaves a small central residual dot when the needle passes through the
  pivot; it is a verify-and-tweak starting point, knob-only, best on a round body with one indicator.
- VirusTotal heuristic FPs on the unsigned installer (not a real bug; code-signing cert is the fix).
- Two untracked files sit in the working tree and are **not ours** — `docs/PRESS-RELEASE.md` and
  `press/`. They appeared mid-session; left untouched and excluded from every commit. Decide what
  they are.

## How to Ship the Next Release

```powershell
# pwsh isn't installed on this machine; the script is encoding-safe under Windows PowerShell 5.1:
powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump minor   # -> 0.9.0
gh run watch    # CI (auto-release.yml) is the SOLE release creator
```
Commit feature work **first** (the script only stages the version files + installer). Full flow:
`docs/PACKAGING.md`. After each release, add a plain-language entry to the website's `updates.json`.

## Next Steps (priority order)

1. **★ #3 step 3 — layered PSD/SVG import** (the last ★ piece, **the big dependency lift**). No
   PSD/SVG layer reader is in the .NET stack today, so this needs a **library decision + a license
   check** (many PSD libs are commercial/GPL — the project cares about licensing). Parse a real
   layered source with per-layer behaviour tags (rotate / stay / translate / opacity-ramp) → the
   existing `Layers` model. **Scope the library/approach before building.**
2. **Interactive in-app help / tutorial system** (P1, owner-requested) — a guided first-run /
   tutorial for the desktop app (load → choose type → align → export → wire the loader). See ROADMAP.
3. **Website "Getting started" how-to guide** at `stripkit.pro/getting-started/` (P2,
   owner-requested) — illustrated step-by-step on the `StripKit-Website` repo; pairs with the deploy.
4. **Code-export targets** — React / Web Component + Unity / Godot (extend `CodeTarget`).
5. **Deploy the website to stripkit.pro** + add a v0.7.0 **and** v0.8.0 entry to `updates.json`.
6. **Code-signing certificate**; **bump `actions/checkout@v4 → v5`** (Node-20 deprecation — CI warns).

## Warnings for Next Agent

- **`FilmstripEngine.cs` (repo root) is a hand-maintained mirror** of the renderer + render-math
  models. Sync it if renderer math changes. App-only services (`PointerExtractor`, `ContentAnalysis`,
  importer, manifest, batch, code-snippet) are **not** in it. NOT compiled, NOT tested.
- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias is the one
  exception). Extend, don't rewrite. Gate new render paths behind defaults so prior goldens hold.
- **Release scripts must read/write UTF-8** (PS 5.1 `Get-Content` without `-Encoding UTF8` corrupts
  em-dashes — BUG-003); **never inline a changelog body into `gh release create --notes "..."`**
  (backticks → shell injection — BUG-004; use `--notes-file`). Both already fixed in the pipeline.
- **House design rule:** Obsidian dark glass, `#e8440a` accent, **sans-serif only** (Verdana-led,
  no monospace). Reuse the `App.axaml` tokens.
- The two untracked strays (`docs/PRESS-RELEASE.md`, `press/`) are not ours — don't commit them.

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (four tabs, all services).
3. `docs/ARCHITECTURE.md` — deep reference. Layer-aware: §5.6 (`RenderLayers`), §6.6 (slots),
   §6.7 (`PointerExtractor`). Skin tab: §9.2. Value-arc §5.5, code export §9.1.
4. `docs/ROADMAP.md` — releases + the vNext backlog (★ step 3 next; the two new onboarding items).
5. `docs/PACKAGING.md` — full release-pipeline reference with the BUG-003/004 guards.
