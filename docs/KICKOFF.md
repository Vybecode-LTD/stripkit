# KICKOFF — StripKit

> Version 1.2.1 · last-updated 2026-06-14
>
> **Authoritative current state: `docs/HANDOFF.md` + `CLAUDE.md`.** This paste-in prompt can
> lag a release — trust those two for "where things stand" and "the next task."
>
> Paste the prompt below into a fresh Claude Code session at the repo root. It
> orients the agent at the **current** state. The full plan lives in
> `docs/ROADMAP.md`; the deep design reference is `docs/ARCHITECTURE.md`; the
> current state + next steps live in `docs/HANDOFF.md`.

---

## Prompt to paste

You are picking up **StripKit**, a shipped C#/Avalonia 11 desktop tool that turns
transparent PNGs into animated control filmstrips — rotary knobs (incl. **layered
base + pointer**), vertical faders, horizontal sliders, meters, and **buttons** (discrete
on/off state frames). It is a **five-tab** app — **Create** (make a strip), **Import**
(re-slice / re-stack / **resample**), **Batch** (a whole folder), **Skin** (assemble a
multi-control `skin.json`), and **Generate** (AI-generate layered control art from your own
OpenAI / Gemini / Claude key, then hand it to Create) — and can also emit ready-to-paste
loader code (JUCE / CSS-HTML / iPlug2 / HISE).

**Before doing anything, read `CLAUDE.md`, `docs/SOURCE_MAP.md`, and
`docs/ARCHITECTURE.md` in full**, then `docs/HANDOFF.md` for the current state and
next steps. Treat the skills in `.claude/skills/` as active (`avalonia-skia-interop`,
`avalonia-drag-drop-files`, `live-preview-render-loop`, `image-regression-testing`,
`filmstrip-importer-engine`, `plugin-asset-manifest`, `layer-aware-filmstrip-compositing`),
plus the globally-installed skills named in `CLAUDE.md`.

### Current state — shipped product, not a scaffold

The planned roadmap (Phases 0–8) is **complete**, and **v1.2.0 has shipped** (signed, the latest
public GitHub Release); **v1.2.1 is staged on the working tree** (its fixes sit under
`## [Unreleased]` in `docs/CHANGELOG.md`). Done: ✅ verify · ✅ drag-and-drop · ✅ importer (Import
tab, incl. **frame-count resampling**) · ✅ manifest export · ✅ golden-image tests · ✅ batch (Batch
tab, incl. **meter settings + layered/backdrop toggle**) · ✅ meter · ✅ packaging (Inno Setup +
release pipeline) · ✅ landing-page website · ✅ **value-arc / fill-ring** · ✅ **code / component
export** (JUCE/CSS/iPlug2/HISE) · ✅ **Skin tab** (multi-control `skin.json`) · ✅ ★ **layer-aware
knob — all 3 steps** (base + pointer + **auto-extract** + **layered PSD/SVG import**) · ✅
**Getting Started tutorial** (per-tab, auto-open first run) · ✅ **Generate tab** (AI SVG art,
DPAPI-encrypted keys) · ✅ **buttons + all-control-type generation** (`ComponentType.Button` +
`LayerBehavior.Frame`). `dotnet build` is 0/0 and `dotnet test` is **171/171 green**. Establish
that baseline first:

```bash
dotnet build StripKit.sln -c Debug   # expect 0/0
dotnet test                          # expect 171/171
```

If red, fix that before anything else.

### How releases work (read `docs/PACKAGING.md`)

Packaging is **Inno Setup**; there is no in-app updater. To ship a release, run
`scripts/Invoke-Release.ps1` (patch bump by default; `-Bump minor` for a feature release).
This machine has **no `pwsh`**, so invoke it via Windows PowerShell 5.1 —
`powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump patch` (the script
is encoding-safe under 5.1, and **keeps its UTF-8 BOM**). It test-gates, bumps the version across
`csproj` / `.iss` / `CHANGELOG` (promoting `## [Unreleased]` → `## [X.Y.Z]`), publishes
self-contained, **signs the exe + installer** (Azure Trusted Signing via signtool + the dlib — NOT
AzureSignTool), packages the installer, stages it under `releases/latest/`, and pushes. That push
triggers `.github/workflows/auto-release.yml`, which VirusTotal-scans the installer and is the
**sole** creator of the GitHub Release. The website reads the release live; refine its
`updates.json` via `scripts/Publish-WebsiteChangelog.ps1 -WebsiteRepo … -Version … -Push`.

**Release integrity:** the "Release" commit stages only version files + the installer **by design**
— so **commit the feature work first** (the v1.2.0 source was once orphaned because it wasn't;
recovered in `b55380f`).

### What NOT to do

- Do **not** rewrite or "simplify" `Services/SkiaFilmstripRenderer.cs`; the
  rotation/translation math and supersampling are deliberate. The `(N-1)` angle
  divisor is correct (last frame lands exactly on max) — do not change it to `N`.
- Do **not** move view-model logic into code-behind, or reference Avalonia UI types
  from view models (the preview `Bitmap` alias is the one allowed exception).
- Do **not** replace the house conventions: the **Obsidian glass** dark theme, the
  `#e8440a` accent, **Verdana-led sans-serif** (no monospace — JetBrains Mono was
  removed), CommunityToolkit source generators, compiled bindings. Re-use the
  `App.axaml` design tokens (incl. the `SectionHeader` control); don't hard-code hex.
- Do **not** "fix" the importer's detected count by trusting it — it is a guess; the
  UI makes it editable and the user verifies (see `docs/ARCHITECTURE.md` §10).
- Do **not** parse an untrusted SVG (an AI reply or an imported file) with a bare
  `XDocument.Parse` — always go through `Services/SafeXml.cs` (DTD prohibited; no entity
  expansion). Both current callers do.
- Do **not** let both the local script and CI create a release — CI is the single
  creator. And release scripts must read/write files as **UTF-8** and keep the `.ps1` BOM.
- Remember `FilmstripEngine.cs` (repo root) is a hand-maintained mirror of the renderer
  math — keep it in sync if you touch the math (the button state-frame path **is** mirrored
  there; app-only services like the Generate providers are **not**).

### Open work — next feature + maintenance

**Primary next task: ship v1.2.1** — its fixes are staged under `## [Unreleased]` (Generate→Create
handoff honours the generated control type; untrusted-SVG XML parse hardened via `SafeXml`;
`BindingPlugins.DataValidators.RemoveAt(0)` + a Generate structure warning). Run the pipeline above.

Then (see `docs/ROADMAP.md` + `docs/HANDOFF.md`):
- **Website "Getting started" how-to guide** at `stripkit.pro/getting-started/` (P2) — the in-app
  tutorial's web mirror (separate `StripKit-Website` repo).
- **Generate: fader / slider / meter polish** — all five control types are Generate targets now
  (meters generate as an off/on pair → background + revealed source), but the linear/meter generation
  paths want a live eyeball + prompt tuning (knob is the proven path).
- **More code-export targets** — React / Web Component, Unity / Godot (extend `CodeTarget`
  + a generator + tests; `CodeSnippetService` is built to grow).
- **Translate / opacity-ramp layer behaviours** — a *renderer* increment (touches
  `SkiaFilmstripRenderer` + the `FilmstripEngine.cs` mirror), unlocking faders/fades for layered import.
- **`actions/checkout@v4 → v5`** in both workflows (CI warns on Node-20 deprecation).
- **Meter peak-hold / stereo** (P3).

### Already-resolved decisions

- Importer = **second tab**, Batch = **third tab**, Skin = **fourth tab**, Generate = **fifth tab**;
  test framework = **xUnit** (all done).
- Meter: both procedural + layered (auto-selected), all four fill directions, discrete
  default + continuous toggle.
- Layered import: **both SVG + PSD** (Svg.Skia / MIT + Magick.NET-Q8 / Apache-2.0), Static/Rotate/Frame
  behaviours, auto-guess by name + manual override.
- Generate: **all three providers** behind one interface, **layered** output, **DPAPI-encrypted** keys,
  **all four control types** (knob/fader/slider/button).
- Packaging: **Inno Setup** + a CI release pipeline + website download (Velopack was removed);
  **code-signed** via Azure Trusted Signing.
