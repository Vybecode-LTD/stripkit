---
document: HANDOFF
version: 1.0.0
last-updated: 2026-06-06
last-audit: 2026-06-06
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into animated
filmstrip sprite sheets for audio-plugin GUI controls (knobs, faders, sliders, meters).
Stack: **.NET 9 / Avalonia 11.3 / SkiaSharp 3.119.2 / Inno Setup**. Public, MIT-licensed.
Layered-source import adds **Svg.Skia** (MIT) + **Magick.NET-Q8-x64** (Apache-2.0).

**Phase:** **v1.0.0 shipped** (signed, live on GitHub Releases; stripkit.pro updated). The app
is a four-tab `TabControl` — **Create | Import | Batch | Skin** — plus a re-openable, per-tab
**Getting Started** overlay. The three ★ vNext bets are **all done** (value-arc, code-export,
layer-aware animation). The release pipeline is now a near-single-command flow that also
publishes the website changelog.

---

## This Session (2026-06-06) — ★ #3 finish, onboarding, the 1.0.0 cut, website automation

A long, multi-feature session that took the app from 0.8.0 → **1.0.0**. Everything below is
committed on `main` and **pushed** (origin == local).

### Commits (oldest → newest)
| Commit | What |
|--------|------|
| `03b441a` | **feat(import): layered PSD/SVG import** (★ #3 step 3) — the layer-aware bet's final piece. |
| `21e2994` | **feat(onboarding): Getting Started tutorial + About modal** (incl. the About-version fix, the UI polish). |
| `198230e` | **fix(release): sign via signtool + Trusted Signing dlib** (replaces the broken AzureSignTool call; BOM-safe). |
| `3849792` | **Release v1.0.0** (version files + the signed installer under `releases/latest/`; tag `v1.0.0`). |
| `a2c6a16` | **feat(release): reusable website-changelog automation** (Stage 3; `Publish-WebsiteChangelog.ps1`). |

(Website repo `Vybecode-LTD/StripKit-Website`: commit `c4fa2f6` added the v1.0.0 `updates.json` entry.)

### 1. ★ #3 step 3 — layered PSD/SVG import (`03b441a`)
Completes the layer-aware bet. A new **"Import layered file (SVG / PSD)…"** button (Create-tab
layered panel) reads a real layered source and maps each layer onto the renderer's **existing**
N-layer stack — **no renderer change**.
- **`Services/LayeredImportService.cs`** (app-only, `ILayeredImportService`): SVG groups via
  **Svg.Skia** (render the doc once for the canonical canvas, then rasterize each top-level `<g>`
  as a standalone SVG so groups register pixel-for-pixel); PSD/PSB layers via **Magick.NET-Q8**
  (drop the unlabeled merged composite, blit each named layer onto the canvas at its page offset).
  Each `ImportedLayer` carries a name-guessed behaviour (pointer/needle/indicator/… → Rotate, else
  Static).
- **VM:** `ImportedLayerRow` rows (name + editable Static/Rotate + canvas-sized art) drive
  `BuildSettings().Layers` + `BuildLayerArt()` when non-empty (`IsImportedKnob`); importing squares
  the frame, forces the knob type, seeds the rotation axis, and **replaces** the base/pointer slots
  (the two layered modes are mutually exclusive). Gated → all prior goldens byte-identical.
- **Not** mirrored into `FilmstripEngine.cs` (parser is app-only; renderer math unchanged).
- Deps: `Svg.Skia` 5.0.0, `Magick.NET-Q8-x64` 14.13.1; **SkiaSharp 3.119.0 → 3.119.2** (Svg.Skia's
  floor; no baseline shift). Installer grew ~22 MB (ImageMagick win-x64 native). +14 tests.

### 2. Onboarding P1 — interactive Getting Started tutorial (`21e2994`)
A re-openable guided overlay (`Views/TutorialOverlay.axaml` + `ViewModels/TutorialViewModel.cs`).
- **Per-screen:** each tab (Create / Import / Batch / Skin) has its own short walkthrough; the
  header **Help** button opens the one for the current tab (`Open(int screenIndex)`, bound via
  `CommandParameter="{Binding SelectedTabIndex}"`).
- **Auto-opens on first run** (the Create walkthrough) via a new minimal **`ISettingsService` /
  `SettingsService`** that persists `HasSeenTutorial` to `%APPDATA%/StripKit/settings.json` — the
  app's only saved state. Finishing/skipping persists "seen".
- **Bundled sample knob:** step 1 offers **"Load sample knob"** → `IAssetService` extracts
  `Assets/sample-knob.png` to temp → normal `LoadSourceFromPath`.
- **Contextual tooltips** on the key Create-tab controls (load / type / frames / export).
- **UI is a solid, centered, drop-shadowed dialog** (new `Border.dialog` token in `App.axaml`:
  opaque `DialogFillGradient` + deep shadow) over a dimming scrim. (Owner asked for it to be
  opaque + centered after the first translucent/bottom version.)

### 3. About box — centered modal + live version (`21e2994`)
- The header **"?"** opens a **centered, drop-shadowed About modal** (same `dialog` style) over a
  scrim, with a Close button (replaced the old corner flyout).
- `MainWindowViewModel.AppVersion` binds the **live assembly version** (`GetName().Version.ToString(3)`,
  driven by the csproj `<Version>`) — was a hardcoded "v0.6.0" literal. Now tracks every release.

### 4. v1.0.0 release — the major cut (`3849792`) + two tooling snags fixed
Ran `Invoke-Release.ps1 -Bump major` (0.8.0 → **1.0.0**). It hit **two release-tooling problems**,
both diagnosed and fixed (see Decisions), then ran clean:
- Test gate **125** → bump → publish (self-contained win-x64) → **sign `StripKit.exe`** (Trusted
  Signing, "Succeeded") → Inno installer → **sign the installer** too → stage `releases/latest/` →
  commit + tag `v1.0.0` + push. CI `auto-release.yml` then **VirusTotal-scanned + created the
  GitHub Release** (the sole release creator).
- **Live + signed:** https://github.com/Vybecode-LTD/stripkit/releases/tag/v1.0.0 —
  `StripKit-Setup-1.0.0-x64.exe`, **58.3 MB** (grew from ~33.5 MB: ImageMagick native + Svg.Skia/
  HarfBuzz). GitHub warns it's over the *recommended* 50 MB but it's under the 100 MB hard limit.

### 5. Website — the gap, the fix, and reusable automation (`a2c6a16`)
The owner reported stripkit.pro "didn't update." Root cause: **the app release doesn't touch the
website repo**, so the host (Railway, auto-deploys the `StripKit-Website` repo on push) had nothing
new; and the changelog reads `updates.json`, which had no 1.0.0 entry. (The **download button** was
never stale — `js/download.js` reads the latest GitHub release client-side; it was browser/
`sessionStorage` cache.)
- Added the v1.0.0 `updates.json` entry (website `c4fa2f6`) → Railway redeployed → **verified live**.
- Built **`scripts/Publish-WebsiteChangelog.ps1`** — a **project-agnostic** Stage-3 tool that
  auto-drafts a version's plain-language `updates.json` entry from `docs/CHANGELOG.md`
  (Added→new, Fixed/Security→fix, else→improved; strips test/build bookkeeping), prepends it
  newest-first, validates the JSON, and (with `-Push`) commits + pushes so the host auto-deploys.
  **Hybrid:** auto-draft → refine the wording → `-Push`. ASCII-only (no BOM trap). Wired into
  `Invoke-Release.ps1` as an optional Stage 3 (`-WebsiteRepo <path>`).

---

## Decisions made (not derivable from the diff)

- **Layered import: both SVG + PSD in one increment**, via **Svg.Skia (MIT)** + **Magick.NET-Q8
  (Apache-2.0)** — both permissive, no copyleft/paid. Map only to the **existing Static/Rotate**
  behaviours (translate/opacity-ramp deferred to a later *renderer* increment). **Auto-guess by
  layer name + manual per-layer override.** (Owner-confirmed forks.)
- **Magick package = `-Q8-x64`, not `-AnyCPU`** — the app ships win-x64 only, so the RID-specific
  package keeps the self-contained publish from bundling every platform's ImageMagick native.
- **Tutorial = a guided overlay** (not coach-marks / not a static window); **per-screen**; **auto-
  open first run + re-openable**; **bundle a sample knob**. After first pass: the card must be
  **opaque + centered** (a `dialog` token), and the **About** box a matching centered modal.
- **Code-signing is Azure Trusted Signing via `signtool.exe` + the `Microsoft.Trusted.Signing.Client`
  dlib + `trusted-signing-metadata.json` — NOT AzureSignTool.** AzureSignTool speaks the Key Vault
  protocol and **403s** against Trusted Signing endpoints (we hit this mid-release). The metadata
  JSON (Endpoint/CodeSigningAccountName/CertificateProfileName = `VybeCode`) is checked in (no
  secrets); auth is the `az login` session. **This cert profile signs any VybeCode app.**
- **`.ps1` files with non-ASCII must keep a UTF-8 BOM** (or be pure ASCII). A no-BOM save of
  `Invoke-Release.ps1` made PS 5.1 mojibake its em-dashes → parse failure mid-release. Fixed by
  re-adding the BOM; `Publish-WebsiteChangelog.ps1` was written ASCII-only on purpose.
- **stripkit.pro is on Railway (auto-deploy on push to the website repo)** — *not* GitHub Pages/
  shared hosting (the DNS IPs misled an initial diagnosis). So a website-repo push redeploys; an
  app-repo release does not.
- **Website changelog automation is hybrid** (auto-draft from CHANGELOG, then refine) — the copy is
  intentionally friendlier/plainer than the technical `docs/CHANGELOG.md`.

---

## Current State

### Working
- **v1.0.0 live + signed:** https://github.com/Vybecode-LTD/stripkit/releases/tag/v1.0.0
  (`StripKit-Setup-1.0.0-x64.exe`, 58.3 MB self-contained, code-signed exe + installer).
- **stripkit.pro updated** (v1.0.0 changelog live; download button auto-points to 1.0.0).
- Tests **125/125 green**; build 0/0; app boots clean (four tabs + the first-run tutorial). CI runs
  on every push/PR. `main` == origin; 0 open bugs.

### Known issues / limitations (not bugs)
- **`FilmstripEngine.cs`** (repo root) is a hand-maintained mirror of the renderer + render-math
  models. App-only services (`LayeredImportService`, `PointerExtractor`, `ContentAnalysis`,
  importer, manifest, batch, code-snippet, settings, asset) are **not** in it — by design.
- **Layered-import MVP boundaries** (ARCHITECTURE §6.8): top-level SVG groups = layers (no Figma
  single-root unwrap); PSD layer order follows the file (no reorder UI); behaviours limited to the
  rendered Static/Rotate.
- **Installer is 58.3 MB** (> GitHub's *recommended* 50 MB; fine under the 100 MB hard limit). The
  growth is the ImageMagick native — accepted cost of PSD support.
- **Two untracked strays remain and are NOT ours** — `docs/PRESS-RELEASE.md` and `press/`. They've
  sat in the working tree across sessions; excluded from every commit. Decide what they are.

---

## The release pipeline (now near-single-command, incl. the website)

Three stages, **one release creator** (CI). `Invoke-Release.ps1` (Stage 1, local) → CI
`auto-release.yml` (Stage 2, the sole `gh release create` + VirusTotal) → the website (Stage 3,
Railway auto-deploys the site repo).

**To ship the next release** (commit feature work first, then):
```powershell
# pwsh isn't installed; use Windows PowerShell 5.1. Requires: az login (signing), AzureSignTool's
# replacement is the Trusted Signing dlib + signtool (already set up), ISCC, gh auth.
powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump minor -WebsiteRepo ..\StripKit-Website
gh run watch            # watch the Auto Release run create the GitHub Release
#   ...refine ..\StripKit-Website\updates.json (the auto-draft is technical) ...
powershell -ExecutionPolicy Bypass -File scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\StripKit-Website -Version <X.Y.Z> -Push
```
That single flow does: test-gate → bump (csproj/.iss/CHANGELOG) → publish → **sign exe + installer**
→ Inno installer → commit/tag/push → (CI: VirusTotal + GitHub Release) → **auto-draft the website
changelog**; then you refine + `-Push` (hybrid) → Railway redeploys.

**Reuse on another desktop app + download site:** copy `scripts/Publish-WebsiteChangelog.ps1`
(+ the `-WebsiteRepo` block in `Invoke-Release.ps1`), keep a Keep-a-Changelog `docs/CHANGELOG.md`
and a website `updates.json` array on an auto-deploy host, and reuse the same Trusted Signing
profile. See `docs/PACKAGING.md` §8.4.

---

## Next Steps (priority order)

1. **Website P2 — `stripkit.pro/getting-started/` how-to guide** (owner-requested) — the in-app
   tutorial's web mirror (separate `StripKit-Website` repo; Railway auto-deploys on push). The
   in-app Getting Started flow is the source of truth to mirror.
2. **More code-export targets** — React / Web Component + Unity / Godot (extend `CodeTarget` +
   `CodeSnippetService`).
3. **`actions/checkout@v4 → v5`** in both workflows (`ci.yml`, `auto-release.yml`) — the v1.0.0
   Auto Release run warned about Node-20 deprecation (mid-2026).
4. **Translate / opacity-ramp layer behaviours** — a *renderer* increment (would touch
   `SkiaFilmstripRenderer` + the `FilmstripEngine.cs` mirror), unlocking faders/fades for layered
   import beyond Static/Rotate.
5. **Optional:** decide on the `docs/PRESS-RELEASE.md` / `press/` strays.

---

## Warnings for the next agent

- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias is the one
  exception). Extend, don't rewrite. Gate new render paths behind defaults so prior goldens hold.
- **Keep `FilmstripEngine.cs` in sync** only if renderer *math* changes; app-only services stay out.
- **Code signing:** it's **Trusted Signing via signtool + the dlib**, *not* AzureSignTool (which
  403s against Trusted Signing). `az login` before a release. The metadata JSON is checked in.
- **Release scripts: read/write UTF-8 and keep the `.ps1` BOM** (PS 5.1 mojibakes a no-BOM file with
  non-ASCII → parse fail — BUG-003 corollary, see PACKAGING §9A). Never inline a changelog body into
  `gh release create --notes "…"` (backticks → shell injection — BUG-004; use `--notes-file`).
- **The website is a separate concern:** an app release does NOT update stripkit.pro. The download
  button auto-updates client-side; the **changelog** needs a `updates.json` entry (now scriptable via
  `Publish-WebsiteChangelog.ps1`) pushed to the website repo, which Railway redeploys.
- **House design rule:** Obsidian dark glass, `#e8440a` accent, **sans-serif only** (Verdana-led, no
  monospace). Modals use the `Border.dialog` token (opaque + drop shadow); reuse the `App.axaml` tokens.
- The two untracked strays (`docs/PRESS-RELEASE.md`, `press/`) are not ours — don't commit them.

---

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (four tabs, all services, the two release scripts).
3. `docs/ARCHITECTURE.md` — deep reference. Layered import §6.8; onboarding/tutorial §6.9;
   `RenderLayers` §5.6; base/pointer §6.6; auto-extract §6.7; Skin tab §9.2.
4. `docs/PACKAGING.md` — the full release-pipeline reference: §8.4 (Stage-3 website automation +
   reuse), §9A (UTF-8 / script-BOM guard), §9B (`--notes-file`).
5. `docs/ROADMAP.md` — releases + the vNext backlog (★ bets all done; website P2 next).
