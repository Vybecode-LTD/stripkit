---
document: HANDOFF
version: 1.2.1
last-updated: 2026-06-14
last-audit: 2026-06-14
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into animated
filmstrip sprite sheets for audio-plugin GUI controls (knobs, faders, sliders, meters,
and now **buttons**). Stack: **.NET 9 / Avalonia 11.3 / SkiaSharp 3.119.2 / Inno Setup**.
Public, MIT-licensed. Layered-source import adds **Svg.Skia** (MIT) + **Magick.NET-Q8-x64**
(Apache-2.0); the **Generate** tab adds AI SVG generation over the user's own OpenAI / Gemini /
Claude key with **DPAPI-encrypted** keys (`System.Security.Cryptography.ProtectedData`).

**Phase:** **v1.2.2 shipped** (tag `v1.2.2`, commit `ad4e1c2`, signed; CI VirusTotal-scanned + created
the GitHub Release; website changelog auto-pushed by the release script's Stage 3 → Railway
auto-deploy). The app is a **five-tab** `TabControl` — **Create | Import | Batch | Skin | Generate** —
plus a re-openable, per-tab **Getting Started** overlay. All three ★ vNext bets remain done (value-arc,
code-export, layer-aware animation); recent work is the **Generate** tab (v1.1.0), **buttons +
all-control-type generation** (v1.2.0), the **1.2.1 fix wave** (handoff-honours-type, SVG-parse
hardening, double-validation fix), and the **1.2.2 polish wave** (editable model input, off-thread
preview, temp cleanup, the **release-integrity guard** in `Invoke-Release.ps1`, CI actions → v5, and
the Stage-3 website-changelog splat fix — both the guard and the Stage-3 fix were validated by the
1.2.2 release itself). **172 tests green.** Note: the other managed-doc headers/counts are one patch
behind (1.2.1 / 172) — fold a full reconcile into the next formal handoff.

---

## This Session (2026-06-14) — orphaned-source recovery + the 1.2.1 fix wave + reconcile

A correctness + integrity session. Two commits on `main`:

| Commit | What |
|--------|------|
| `b55380f` | **feat(v1.2.0): Button + all-control-type generation + colour pickers + Frame layer** — the **feature SOURCE for the already-released v1.2.0**, committed retroactively (see Release-integrity finding). |
| `80dc1b5` | **fix(generate): handoff honours control type; harden SVG parsing; misc audit fixes** — the v1.2.1 fix wave. |

### Release-integrity finding + recovery (`b55380f`)
The **"Release v1.2.0" commit (`70cf259`) staged only the version files + the installer** — the
actual v1.2.0 **feature source was never committed**. So the released v1.2.0 binary existed and the
`v1.2.0` tag was live, but the tag **could not rebuild its own installer** (the source behind it was
missing from history). This session committed that source as-is (matching the shipped binary) in
`b55380f` **before** fixing forward to 1.2.1, restoring the invariant that every released tag can be
rebuilt from its own tree. The recovered v1.2.0 source: `ComponentType.Button` + `LayerBehavior.Frame`;
Generate's four control types + button off/on prompt + colour-picker flyouts; the renderer +
`FilmstripEngine.cs` button state-frame path; the importer's `off`/`on`→`Frame` mapping; and the
supporting `CodeSnippetService` / `ImportedLayerRow` / view wiring.

### The 1.2.1 fix wave (`80dc1b5`)
Three audit findings against the shipped v1.2.0, fixed forward:

1. **Generate → Create handoff ignored the generated control type.** It hard-coded `RotaryKnob`, so a
   generated **fader/slider/button** broke on handoff — faders/sliders *rotated* instead of sliding,
   and buttons stacked both states. The handoff now branches: **knob** → body+pointer layer stack;
   **button** → `off`/`on` groups as `LayerBehavior.Frame` state layers; **fader/slider** → flattened
   to the single source the linear renderer expects.
2. **Untrusted-SVG XML parsing was unhardened.** New `Services/SafeXml.cs`
   (`DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`) now parses SVG in
   **both** `SvgSanitizer` (AI replies) and the layered-file import picker, closing an entity-expansion
   DoS ("billion laughs") and external-entity / SSRF probes. A DTD now throws — which both callers
   already treat as "malformed SVG" — and legitimate generated art has no DTD, so the happy path is
   unaffected.
3. **Double validation + a silent gap.** Added the missing `BindingPlugins.DataValidators.RemoveAt(0)`
   in `App.axaml.cs` (the CommunityToolkit + Avalonia double-validation fix); the Generate tab now also
   **warns** when a knob has no rotating pointer, or a button is missing an on/off state.

### New files this work added (now documented in SOURCE_MAP / ARCHITECTURE / CLAUDE)
- `src/StripKit/Services/SafeXml.cs` — hardened untrusted-XML parse.
- `src/StripKit/Helpers/HexToColorBrushConverter.cs` — the Generate colour-swatch converter.
- `src/StripKit/Controls/SectionHeader.cs` — the accent-divider section label used across the sidebars.
- `tests/StripKit.Tests/GenerateIntegrationTests.cs` — Generate-pipeline integration tests.

---

## Recent releases (context)

- **v1.1.0** (released, signed) — **Generate tab**: AI-generate a **layered knob SVG** via the user's
  own OpenAI / Gemini / Claude key (`IAssetGenerationService` + three `IAssetGenerationProvider`s over a
  shared `HttpClient`; `SvgSanitizer`; **DPAPI-encrypted** keys via `ISecretStore`/`DpapiSecretStore`),
  validated by importing it (preview = the real import). Plus the **verified-model dropdown** (fixes a
  retired-Gemini crash), **body + accent colour** inputs with live swatches, **style-effect** checkboxes,
  and the renderer **content-centering** fix. **+27 then +1, suite →157.**
- **v1.2.0** (released, signed) — **`ComponentType.Button`** + **`LayerBehavior.Frame`** (discrete off/on
  state frames); Generate supports **all four control types** (knob / fader / slider / button); **colour-picker
  flyouts** (`Avalonia.Controls.ColorPicker` 11.3.0). Mirrored in `FilmstripEngine.cs`. *(Feature source was
  orphaned at release and recovered this session — see above.)*
- **v1.2.1** (staged, about to release — `## [Unreleased]` in the CHANGELOG) — the three fixes above
  (handoff-honours-type, SVG-parse hardening, DataValidators + no-pointer/no-state warning). **suite 157→171.**

---

## Current State

### Working
- **v1.2.0 live + signed** on GitHub Releases; **v1.2.1 staged on the working tree** (CHANGELOG
  `## [Unreleased]` holds its fixes — the release script promotes it to `[1.2.1]`).
- Tests **171/171 green**; build 0/0; app boots clean (five tabs + the first-run tutorial). CI runs on
  every push/PR. `main` == origin (after the two commits above); 0 open bugs.
- `csproj` `<Version>` = **1.2.0** (the release script bumps it to 1.2.1 — **do not hand-edit**).

### Known issues / limitations (not bugs)
- **`FilmstripEngine.cs`** (repo root) is a hand-maintained mirror of the render math + render-math models.
  It now includes the **button state-frame path** (`RenderButtonLayers` / `LayerBehavior.Frame`) alongside
  the meter / value-arc / `RenderLayers` paths. App-only services (`LayeredImportService`, `PointerExtractor`,
  `ContentAnalysis`, importer, manifest, batch, code-snippet, settings, asset, **the Generate providers /
  service / sanitizer / secret store**) are **not** in it — by design.
- **Generate is layered-knob-first in structure but type-aware in output:** knob → body+pointer, button →
  off/on Frame layers, fader/slider → a single `body` cap shape (flattened on handoff), meter → an off/on
  pair adopted as background + revealed source (continuous vertical fill). All five control types are now
  Generate targets; fader/slider/meter output still wants a live eyeball.
- **Layered-import MVP boundaries** (ARCHITECTURE §6.8): top-level SVG groups = layers (no Figma single-root
  unwrap); PSD layer order follows the file (no reorder UI); behaviours limited to the rendered
  Static / Rotate / Frame.
- **Installer is ~58 MB** (> GitHub's *recommended* 50 MB; fine under the 100 MB hard limit). The growth is the
  ImageMagick native — accepted cost of PSD support.
- **The only untracked strays are NOT ours** — `docs/PRESS-RELEASE.md`, `press/`, and `.claude/launch.json`.
  They've sat in the working tree across sessions; excluded from every commit. Decide what they are.

---

## The release pipeline (near-single-command, incl. the website)

Three stages, **one release creator** (CI). `Invoke-Release.ps1` (Stage 1, local) → CI
`auto-release.yml` (Stage 2, the sole `gh release create` + VirusTotal) → the website (Stage 3,
Railway auto-deploys the site repo). Code-signing is **Azure Trusted Signing** (signtool + the
`Microsoft.Trusted.Signing.Client` dlib — **not** AzureSignTool, which 403s against Trusted Signing).

**To ship v1.2.1** (the working tree is staged; commit any remaining work first, then):
```powershell
# pwsh isn't installed; use Windows PowerShell 5.1. Requires: az login (signing), the Trusted Signing
# dlib + signtool (already set up), ISCC, gh auth.
powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump patch -WebsiteRepo ..\StripKit-Website
gh run watch            # watch the Auto Release run create the GitHub Release
#   ...refine ..\StripKit-Website\updates.json (the auto-draft is technical) ...
powershell -ExecutionPolicy Bypass -File scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\StripKit-Website -Version 1.2.1 -Push
```
That flow does: test-gate (171) → bump (csproj/.iss/CHANGELOG `[Unreleased]`→`[1.2.1]`) → publish →
**sign exe + installer** → Inno installer → commit/tag/push → (CI: VirusTotal + GitHub Release) →
**auto-draft the website changelog**; then refine + `-Push` (hybrid) → Railway redeploys.

---

## Next Steps (priority order)

1. **Ship v1.2.1** — run the pipeline above (the fixes are staged under `## [Unreleased]`).
2. **Website P2 — `stripkit.pro/getting-started/` how-to guide** (owner-requested) — the in-app
   tutorial's web mirror (separate `StripKit-Website` repo; Railway auto-deploys on push).
3. **Generate: fader / slider / meter polish** — the structure is type-aware, but the linear/meter
   generation paths want a live eyeball + prompt tuning (knob is the proven path).
4. **More code-export targets** — React / Web Component + Unity / Godot (extend `CodeTarget` +
   `CodeSnippetService`).
5. **`actions/checkout@v4 → v5`** in both workflows (`ci.yml`, `auto-release.yml`) — the Node-20
   deprecation warning.
6. **Translate / opacity-ramp layer behaviours** — a *renderer* increment (would touch
   `SkiaFilmstripRenderer` + the `FilmstripEngine.cs` mirror), unlocking faders/fades for layered import.
7. **Optional:** decide on the `docs/PRESS-RELEASE.md` / `press/` / `.claude/launch.json` strays.

---

## Warnings for the next agent

- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias is the one exception).
  Extend, don't rewrite. Gate new render paths behind defaults so prior goldens hold.
- **Keep `FilmstripEngine.cs` in sync** only if renderer *math* changes; app-only services stay out. The
  button state-frame path **is** mirrored there (it is render math); the Generate providers are **not**.
- **Untrusted SVG must go through `SafeXml.Parse`** — never bare `XDocument.Parse` on an AI reply or an
  imported file (entity-expansion DoS / external-entity). Both current callers do; keep it that way.
- **Code signing:** Trusted Signing via signtool + the dlib, *not* AzureSignTool. `az login` before a
  release. The metadata JSON is checked in.
- **Release scripts: read/write UTF-8 and keep the `.ps1` BOM** (PS 5.1 mojibakes a no-BOM file with
  non-ASCII → parse fail — see PACKAGING §9A). Never inline a changelog body into `gh release create
  --notes "…"` (backticks → shell injection — BUG-004; use `--notes-file`).
- **Release integrity:** the "Release" commit stages only version files + the installer **by design** — so
  the **feature work must be committed first** (the v1.2.0 source was orphaned because it wasn't; recovered
  in `b55380f`). Commit features, *then* run the release script.
- **The website is a separate concern:** an app release does NOT update stripkit.pro. The download button
  auto-updates client-side; the **changelog** needs a `updates.json` entry (scriptable via
  `Publish-WebsiteChangelog.ps1`) pushed to the website repo, which Railway redeploys.
- **House design rule:** Obsidian dark glass, `#e8440a` accent, **sans-serif only** (Verdana-led, no
  monospace). Reuse the `App.axaml` tokens (incl. the `SectionHeader` control + the `Border.dialog` modal token).
- The untracked strays (`docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json`) are not ours — don't commit them.

---

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (five tabs, all services, the two release scripts).
3. `docs/ARCHITECTURE.md` — deep reference. Generate §11; layered import §6.8; onboarding/tutorial §6.9;
   `RenderLayers` §5.6; base/pointer §6.6; auto-extract §6.7; Skin tab §9.2.
4. `docs/PACKAGING.md` — the full release-pipeline reference: §8.4 (Stage-3 website automation +
   reuse), §9A (UTF-8 / script-BOM guard), §9B (`--notes-file`).
5. `docs/ROADMAP.md` — releases + the vNext backlog (★ bets all done; ship 1.2.1, then website P2).
