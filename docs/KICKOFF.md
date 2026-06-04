# KICKOFF — StripKit

> Version 0.7.0 · last-updated 2026-06-04
>
> Paste the prompt below into a fresh Claude Code session at the repo root. It
> orients the agent at the **current** state. The full plan lives in
> `docs/ROADMAP.md`; the deep design reference is `docs/ARCHITECTURE.md`; the
> current state + next steps live in `docs/HANDOFF.md`.

---

## Prompt to paste

You are picking up **StripKit**, a C#/Avalonia 11 desktop tool that turns a single
transparent PNG into an animated control filmstrip — rotary knobs, vertical faders,
horizontal sliders, and meters — **imports** existing strips, renders a whole folder
in **batch**, and emits a `skin.json` manifest.

**Before doing anything, read `CLAUDE.md`, `docs/SOURCE_MAP.md`, and
`docs/ARCHITECTURE.md` in full**, then `docs/HANDOFF.md` for the current state and
next steps. Treat the skills in `.claude/skills/` as active (`avalonia-skia-interop`,
`avalonia-drag-drop-files`, `live-preview-render-loop`, `image-regression-testing`,
`filmstrip-importer-engine`, `plugin-asset-manifest`), plus the globally-installed
skills named in `CLAUDE.md`.

### Current state — shipped product, not a scaffold

The planned roadmap (Phases 0–8) is **complete**, and **v0.7.0 has shipped** (the
latest public GitHub Release). Done: ✅ verify · ✅ drag-and-drop · ✅ importer (Import
tab) · ✅ manifest export · ✅ golden-image tests · ✅ batch (Batch tab) · ✅ meter ·
✅ packaging (Inno Setup + release pipeline) · ✅ landing-page website · ✅ **value-arc /
fill-ring** (Create-tab knob arc) · ✅ **code/component export** (JUCE/CSS/iPlug2/HISE
loader snippets). `dotnet build` is 0/0 and `dotnet test` is **72/72 green**. Establish
that baseline first:

```bash
dotnet build StripKit.sln -c Debug   # expect 0/0
dotnet test                          # expect 72/72
```

If red, fix that before anything else.

### How releases work (read `docs/PACKAGING.md`)

Packaging is **Inno Setup**; there is no in-app updater. To ship a release, run
`pwsh scripts/Invoke-Release.ps1` (patch bump) — it test-gates, bumps the version
across `csproj` / `.iss` / `CHANGELOG`, publishes self-contained, packages the
installer, stages it under `releases/latest/`, and pushes. That push triggers
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

**Primary next task: vNext ★ #3 — layer-aware animation.** Accept layered input so only
the pointer rotates (the body stays crisp). Owner-agreed build order: **base+pointer PNGs
→ auto-pointer extraction from flat art → PSD/SVG import**. It is a deep renderer/model
change — extend `RenderFrame` to composite a layer list, keep `FilmstripEngine.cs` in sync,
and gate behind defaults so existing single-source output is unchanged (as value-arc did).
See `docs/ROADMAP.md` + `docs/HANDOFF.md`.

Also open:
- **More code-export targets** — React / Web Component, Unity / Godot (extend `CodeTarget`
  + a generator + tests; `CodeSnippetService` is built to grow).
- **Deploy the website to stripkit.pro** + add its v0.7.0 `updates.json` entry (user action;
  one cross-repo edit per release).
- **Code-signing certificate** — clears the VirusTotal FPs (~4/71, unsigned) + SmartScreen;
  the `.iss` has a `SignTool` hook (see `docs/PACKAGING.md`).
- **Batch-tab meter settings UI**; importer frame-count resampling; multi-control manifests;
  meter peak-hold / stereo; bump `actions/checkout@v4 → v5`.

### Already-resolved decisions

- Importer = **second tab**, Batch = **third tab**, test framework = **xUnit** (all done).
- Meter: both procedural + layered (auto-selected), all four fill directions, discrete
  default + continuous toggle.
- Packaging: **Inno Setup** + a CI release pipeline + website download (Velopack was
  removed); shipping **unsigned** until a code-signing cert exists.
