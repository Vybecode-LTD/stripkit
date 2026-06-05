# KICKOFF — StripKit

> Version 0.8.0 · last-updated 2026-06-05
>
> Paste the prompt below into a fresh Claude Code session at the repo root. It
> orients the agent at the **current** state. The full plan lives in
> `docs/ROADMAP.md`; the deep design reference is `docs/ARCHITECTURE.md`; the
> current state + next steps live in `docs/HANDOFF.md`.

---

## Prompt to paste

You are picking up **StripKit**, a shipped C#/Avalonia 11 desktop tool that turns
transparent PNGs into animated control filmstrips — rotary knobs (incl. **layered
base + pointer**), vertical faders, horizontal sliders, and meters. It is a **four-tab**
app — **Create** (make a strip), **Import** (re-slice / re-stack / **resample**), **Batch**
(a whole folder), and **Skin** (assemble a multi-control `skin.json`) — and can also emit
ready-to-paste loader code (JUCE / CSS-HTML / iPlug2 / HISE).

**Before doing anything, read `CLAUDE.md`, `docs/SOURCE_MAP.md`, and
`docs/ARCHITECTURE.md` in full**, then `docs/HANDOFF.md` for the current state and
next steps. Treat the skills in `.claude/skills/` as active (`avalonia-skia-interop`,
`avalonia-drag-drop-files`, `live-preview-render-loop`, `image-regression-testing`,
`filmstrip-importer-engine`, `plugin-asset-manifest`, `layer-aware-filmstrip-compositing`),
plus the globally-installed skills named in `CLAUDE.md`.

### Current state — shipped product, not a scaffold

The planned roadmap (Phases 0–8) is **complete**, and **v0.8.0 has shipped** (the latest
public GitHub Release). Done: ✅ verify · ✅ drag-and-drop · ✅ importer (Import tab, incl.
**frame-count resampling**) · ✅ manifest export · ✅ golden-image tests · ✅ batch (Batch
tab, incl. **meter settings + layered/backdrop toggle**) · ✅ meter · ✅ packaging (Inno Setup
+ release pipeline) · ✅ landing-page website · ✅ **value-arc / fill-ring** · ✅ **code /
component export** (JUCE/CSS/iPlug2/HISE) · ✅ **Skin tab** (multi-control `skin.json`) ·
✅ ★ **layer-aware knob — steps 1 & 2** (base + pointer + **auto-extract from flat art**; step 2
is on `main`, unreleased). `dotnet build` is 0/0 and `dotnet test` is **98/98 green**. Establish
that baseline first:

```bash
dotnet build StripKit.sln -c Debug   # expect 0/0
dotnet test                          # expect 98/98
```

If red, fix that before anything else.

### How releases work (read `docs/PACKAGING.md`)

Packaging is **Inno Setup**; there is no in-app updater. To ship a release, run
`scripts/Invoke-Release.ps1` (patch bump by default; `-Bump minor` for a feature release).
This machine has **no `pwsh`**, so invoke it via Windows PowerShell 5.1 —
`powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump minor` (the script
is encoding-safe under 5.1). It test-gates, bumps the version across `csproj` / `.iss` /
`CHANGELOG`, publishes self-contained, packages the installer, stages it under
`releases/latest/`, and pushes. That push triggers
`.github/workflows/auto-release.yml`, which VirusTotal-scans the installer and is the
**sole** creator of the GitHub Release. The website reads the release live.

### What NOT to do

- Do **not** rewrite or "simplify" `Services/SkiaFilmstripRenderer.cs`; the
  rotation/translation math and supersampling are deliberate. The `(N-1)` angle
  divisor is correct (last frame lands exactly on max) — do not change it to `N`.
- Do **not** move view-model logic into code-behind, or reference Avalonia UI types
  from view models (the preview `Bitmap` alias is the one allowed exception).
- Do **not** replace the house conventions: the **Obsidian glass** dark theme, the
  `#e8440a` accent, **Verdana-led sans-serif** (no monospace — JetBrains Mono was
  removed), CommunityToolkit source generators, compiled bindings. Re-use the
  `App.axaml` design tokens; don't hard-code hex.
- Do **not** "fix" the importer's detected count by trusting it — it is a guess; the
  UI makes it editable and the user verifies (see `docs/ARCHITECTURE.md` §7).
- Do **not** let both the local script and CI create a release — CI is the single
  creator. And release scripts must read/write files as **UTF-8** (PowerShell 5.1
  `Get-Content` without `-Encoding UTF8` corrupts the changelog's em-dashes).
- Remember `FilmstripEngine.cs` (repo root) is a hand-maintained mirror of the
  renderer — keep it in sync if you touch the math.

### Open work — next feature + maintenance

**Primary next task: vNext ★ #3 — layer-aware animation, STEP 3 (layered PSD/SVG import).**
Steps 1 (base + pointer) and 2 (auto-pointer extraction, `PointerExtractor`) are **done**; the
remaining piece is parsing a real layered source (PSD / SVG) with per-layer behaviour tags
(rotate / stay / translate / opacity-ramp) into the existing `FilmstripSettings.Layers` model.
This is the **big dependency lift** — no PSD/SVG layer reader is in the .NET stack today, so
**scope the library + a license check first** (many PSD libs are commercial/GPL; the project
cares about licensing). Keep `FilmstripEngine.cs` in sync only if renderer math changes (the
parser is app-only). See `docs/ROADMAP.md` + `docs/HANDOFF.md`.

Then the two owner-requested onboarding items (see ROADMAP):
- **Interactive in-app help / tutorial system** (P1) — a guided first-run for the desktop app.
- **Website "Getting started" how-to guide** at `stripkit.pro/getting-started/` (P2).

Also open:
- **More code-export targets** — React / Web Component, Unity / Godot (extend `CodeTarget`
  + a generator + tests; `CodeSnippetService` is built to grow).
- **Deploy the website to stripkit.pro** + add its v0.7.0 **and v0.8.0** `updates.json` entries
  (user action; one cross-repo edit per release).
- **Code-signing certificate** — clears the VirusTotal FPs (~4/71, unsigned) + SmartScreen;
  the `.iss` has a `SignTool` hook (see `docs/PACKAGING.md`).
- **Meter peak-hold / stereo**; bump `actions/checkout@v4 → v5` (CI warns on Node-20 deprecation).

### Already-resolved decisions

- Importer = **second tab**, Batch = **third tab**, test framework = **xUnit** (all done).
- Meter: both procedural + layered (auto-selected), all four fill directions, discrete
  default + continuous toggle.
- Packaging: **Inno Setup** + a CI release pipeline + website download (Velopack was
  removed); shipping **unsigned** until a code-signing cert exists.
