---
document: HANDOFF
version: 1.3.0
last-updated: 2026-06-18
last-audit: 2026-06-18
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into animated filmstrip sprite
sheets for audio-plugin GUI controls — **knobs, faders, sliders, meters, buttons, and now on/off
toggles**. Stack: **.NET 9 / Avalonia 11.3 / SkiaSharp 3.119.2 / Inno Setup**. Public, MIT-licensed.
Layered-source import adds **Svg.Skia** (MIT) + **Magick.NET-Q8-x64** (Apache-2.0); the **Generate**
tab is an AI SVG-generation studio over the user's own OpenAI / Gemini / Claude key — or any
**OpenAI-compatible custom endpoint** (OpenRouter / Ollama / LM Studio) — with **DPAPI-encrypted** keys.

**Phase:** **v1.3.0 shipped** (tag `v1.3.0`, release commit `f38a5f5`, **signed** — verified Trusted
Signing chain, timestamped; CI `auto-release.yml` VirusTotal-scanned + created the GitHub Release; the
website `updates.json` v1.3.0 entry was pushed → Railway auto-deploy). The app is a **five-tab**
`TabControl` — **Create | Import | Batch | Skin | Generate** — plus a re-openable, per-tab **Getting
Started** overlay. **216 tests green, build 0/0.** All managed docs reconciled to **1.3.0 / 2026-06-18**.

---

## This Session (2026-06-18) — the v1.3.0 wave: AI-generation program + meters/toggles + hardening, shipped

A large feature wave that began as a codebase scan, surfaced a security bug, then grew into the biggest
Generate-tab release yet — reconciled, released (signed), and handed off. Suite **172 → 216 green**.

### Security / quality fixes
- **BUG-010** (`940b60f`) — the SVG **file-import** path ran `SafeXml.Parse` *after* `Svg.Skia.FromSvg`,
  so a "billion-laughs" entity bomb opened via the layered-file picker was expanded first (local DoS).
  Reordered the hardened parse to the top. (AI-reply path was never affected.)
- **Input-size caps** (`97fb22d`) — `ImageLoadService` peeks header dims via `SKCodec` (rejects >64 MP);
  `LayeredImportService` caps SVG text (20 MB) + PSD canvas (64 MP).
- **Manifest mapping** — `ManifestService` / `SkinViewModel` `MapType` now map Button→"button",
  Toggle→"toggle" (were silently "knob").
- **BUG-011** (`541b3c0`) — `Invoke-Release.ps1` aborted at `git add` on git's benign "LF→CRLF" stderr
  warning (PS 5.1 + `ErrorActionPreference=Stop`); the git block is now `Continue` + `$LASTEXITCODE`-gated.

### Meters & toggles
- **Meter generation + horizontal meters** (`d846686`, `72dcc46`) — the Generate tab makes meters as an
  unlit `off` + fully-lit `on` pair; the handoff wires `off`→meter background, `on`→the source the
  renderer reveals up to the value. Vertical or **horizontal** (fill direction inferred from the art's
  aspect). No renderer change — reuses the existing layered-meter reveal path.
- **`ComponentType.Toggle`** (`c0a60af`) — a first-class on/off toggle, distinct from Button but sharing
  its discrete state-frame render path (mirrored in `FilmstripEngine.cs`): generate / layered-import
  (auto-detected from off/on layer names) / create / code-export (JUCE latching toggle, iPlug2
  `IBSwitchControl`) all honour it.

### The AI-generation program (Generate tab)
- **Matching-set generator** (`5d07923`) — one prompt → a whole consistent family of controls,
  generated concurrently from one shared style (`GenerateSetAsync`); results grid with per-item
  Use-in-Create / Save / Regenerate + Save-set-to-folder.
- **Variations grid** (`bfcbba5`, `GenerateVariationsAsync`) · **Custom OpenAI-compatible endpoint**
  (`ef13091`, `AiProvider.Custom` / `CustomOpenAiProvider`) · **Refine** (`70cedce`, `RefineAsync`) ·
  **Reference-image match / vision** (`b4dd7e1`, per-provider `DescribeImageAsync` → `DescribeReferenceAsync`) ·
  **auto-retry + show-the-prompt** (`6e3f800`) · **"avoid" field** (`5d07923`) · **prompt seeds library**
  (`f3c0f4a`, `GenerationSeed`/`GenerationSeedLibrary`, 5 built-ins + user saves).

### New files
`Services/CustomOpenAiProvider.cs`, `ViewModels/GenerateSetModels.cs`; tests `ToggleRenderTests`,
`ImageLoadServiceTests`, `CustomOpenAiProviderTests`, `VisionProviderTests`.

### Reconcile + release
Stamped every managed doc to **1.3.0 / 2026-06-18** (`780d5e8`); consolidated the CHANGELOG
[Unreleased] → polished v1.3.0 entry (`d6eb870`). Released via `Invoke-Release.ps1 -Bump minor
-DraftWebsiteOnly` — it built + **signed** the exe + installer but aborted at `git add` (BUG-011), so the
release commit/tag/push were completed by hand (`f38a5f5` / `v1.3.0`); CI created the GitHub Release;
the website entry was written and pushed manually.

---

## Current State

### Working
- **v1.3.0 live + signed** on GitHub Releases (`github.com/Vybecode-LTD/stripkit/releases/tag/v1.3.0`,
  installer ~58 MB). Website changelog updated. `main` == origin; tests **216/216 green**, build 0/0;
  0 open bugs. CI runs on every push/PR.
- `csproj <Version>` / `.iss MyAppVersion` / CHANGELOG are at **1.3.0** (from the release script).

### Known issues / limitations (not bugs)
- **AI generation can't be live-verified here.** All the Generate features are unit-tested (mocked
  service + real importer + fake provider/network; vision verified by request-body shape), but the
  meter / toggle / set **art quality** and the new Generate UI still want a **live eyeball with a real
  API key** — knob is the proven path.
- **`FilmstripEngine.cs`** (repo root) mirrors the render math incl. the Button/Toggle state-frame path;
  app-only services (the Generate providers/service, importer, etc.) stay out — by design.
- **Vision / custom endpoint scope:** the custom endpoint uses **Bearer** auth (OpenRouter / Ollama /
  LM Studio); **Azure OpenAI** (api-key header + api-version) is not yet supported. Vision needs a
  vision-capable model on the selected provider.
- **Installer ~58 MB** (> GitHub's *recommended* 50 MB, under the 100 MB hard limit) — the ImageMagick
  native, accepted for PSD support.
- **Untracked strays (still not ours):** `docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json` —
  decide whether to commit, ignore, or remove.

---

## The release pipeline (one creator = CI)

Three stages. **Stage 1** `scripts/Invoke-Release.ps1` (Windows PowerShell 5.1; needs `az login`,
signtool + the Trusted Signing dlib, ISCC at `%LOCALAPPDATA%\Programs\Inno Setup 6`, gh auth): test-gate
→ integrity guard → bump → publish → **sign exe + installer** (Azure Trusted Signing) → ISCC → stage
under `releases/latest/` → commit + tag + push. **Stage 2** `.github/workflows/auto-release.yml`
(triggered by the tracked installer push): VirusTotal → the **sole** `gh release create`. **Stage 3**
the website: `scripts/Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\StripKit-Website -Version X.Y.Z -Push`
(reads the CHANGELOG `### Website` bullets) → Railway auto-deploy.

To ship the next version: `powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump
patch -WebsiteRepo ..\StripKit-Website` then `gh run watch`. (BUG-011 is fixed, so the git step no
longer aborts on a CRLF warning.)

---

## Next Steps (priority order)

1. **Live-eyeball the Generate output with a real key** — meters / toggles / matching-set / variations /
   vision art quality + prompt tuning (knob is proven; the linear/meter/set paths want a human look).
2. **Website P2 — `stripkit.pro/getting-started/` how-to guide** (owner-requested; separate
   `StripKit-Website` repo, Railway auto-deploys).
3. **Seeds → matching-set → auto-assemble a Skin** — chain the seeds library + matching set into the
   Skin tab so one prompt yields a ready `skin.json`.
4. **Azure OpenAI auth** (api-key header + api-version) for the custom endpoint.
5. **More code-export targets** (React / Web Component, Unity / Godot) · **translate / opacity-ramp
   layer behaviours** (renderer increment) · **meter peak-hold / stereo**.
6. **Decide on the untracked strays** (`docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json`).

---

## Warnings for the next agent

- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias is the exception).
  Gate new render paths behind defaults so prior goldens hold. **Toggle reuses Button's state-frame path
  — keep them together** (both mirrored in `FilmstripEngine.cs`).
- **Untrusted SVG must go through `SafeXml.Parse` FIRST** — before `Svg.Skia.FromSvg` — never a bare
  `XDocument.Parse` (BUG-010). Both callers do; keep it that way.
- **Release tooling:** Windows PowerShell 5.1 only (no `pwsh`); `az login` before a release; signing is
  **Trusted Signing** via signtool + the dlib (not AzureSignTool). Read/write release scripts as UTF-8,
  keep the `.ps1` BOM. CI is the **sole** release creator; commit feature work **before** the release
  script (it stages only version files + the installer). The git block tolerates stderr warnings now
  (BUG-011) but watch for other PS-5.1 native-stderr traps.
- **House design:** Obsidian dark glass, `#e8440a` accent, **sans-serif only** (Verdana-led, no
  monospace). Reuse the `App.axaml` tokens (incl. the `SectionHeader` control).

---

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (five tabs, all services incl. the new Generate suite).
3. `docs/ARCHITECTURE.md` — deep reference: Generate §11 (incl. the AI program + custom endpoint +
   vision), state-frames §5.7 (button/toggle), layered import §6.8, meter path §5.x.
4. `docs/PACKAGING.md` — the full release-pipeline reference (signing, Stage-3 website, the BOM guard).
5. `docs/ROADMAP.md` — releases + the vNext backlog.
