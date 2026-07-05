---
document: HANDOFF
version: 1.5.1
last-updated: 2026-07-04
last-audit: 2026-07-04
managed-by: session-orchestrator/handoff-builder
---

# Session Handoff — StripKit

## Quick Context

StripKit is a C#/Avalonia desktop tool that renders transparent PNGs into animated filmstrip sprite
sheets for audio-plugin GUI controls — **knobs, faders, sliders, meters, buttons, and toggles**. Stack:
**.NET 9 / Avalonia 11.3 / SkiaSharp 3.119.2 / Inno Setup**. Public, MIT-licensed. Layered-source import +
HDR frame ingest adds **Svg.Skia** (MIT) + **Magick.NET-Q16-HDRI-x64** (Apache-2.0, PSD + 16-bit/EXR); the
**Generate** tab is an AI SVG-generation studio over the user's own OpenAI / Gemini / Claude key (or any
OpenAI-compatible endpoint), with DPAPI-encrypted keys.

**Phase:** **v1.5.1 RELEASED and live.** `origin/main` = `eeafd22` (release commit), clean & pushed;
`csproj <Version>` / `.iss` are at **1.5.1**. The app is a **six-tab** `TabControl` —
**Create | Import | Batch | Skin | Generate | Assemble** — plus a per-tab Getting Started overlay.
**335 tests green, build clean, 0 open bugs.** The signed installer `StripKit-Setup-1.5.1-x64.exe` is
live on GitHub Releases (tag `v1.5.1`), VirusTotal 0/0/66 clean, website changelog pushed. README badges
are dynamic (they auto-reflect the latest release).

---

## This Session (2026-07-04) — cut v1.5.0 + v1.5.1, then overhauled the website docs

The prior handoff ended with the 12/12 enhancement wave feature-complete but uncommitted and unreleased.
This session shipped it end-to-end and then brought the marketing/docs site up to date.

### Shipped
- **v1.5.0 released** (tag `v1.5.0`, commit `dc37f85`) — the bundle the prior handoff described:
  path-tracing P1–P5 (Assemble tab + render recipe + render QC + crossfade + AOV pass + EXR/HDR ingest),
  the 2026-07-02 fine-tooth-comb audit (11 bugs, 2 HIGH), the Depth design-system rebrand, and the
  12-item enhancement wave (sprite-grid layout, parameter-law mapping, render presets, React export, …).
- **v1.5.1 released** (tag `v1.5.1`, release commit `eeafd22`) — a small follow-up:
  - **BUG-021** — the Assemble tab silently dropped HDR frames (`.exr` / `.hdr` / 16-bit `.tif`) on
    drag-drop AND in "Add files…" — only "Choose folder…" accepted them. Fixed via a single shared
    `FrameSequenceViewModel.AcceptedExtensions` used by all three intake paths.
  - **In-app Getting Started walkthrough expansions** across all six tabs (`TutorialViewModel.cs`).
  - Signed installer `StripKit-Setup-1.5.1-x64.exe` live on GitHub Releases, VirusTotal 0/0/66,
    website changelog pushed. Suite **335 green**, build clean.

### Website overhaul (sibling repo `C:\DEV\StripKit-Website`, all pushed — tip `275e806`)
- A full new **Tutorials reference**: hub `tutorials.html` + **7 per-tab pages** + `css/docs.css`.
- Refreshed `getting-started.html` / `index.html` to the current **6-component-type / 5-code-target /
  @2–4x** feature set — they were a version behind (said "4 control types", missing
  Button/Toggle/React/Generate/Assemble).
- Refreshed the app screenshot; small nav-consistency + hover-color polish.

---

## Current State

### Working
- **335/335 tests green, build clean, 0 open bugs.** CI runs on every push/PR (70% coverage gate).
- Six tabs functional. The renderer + `FilmstripEngine.cs` mirror are in sync.
- **App repo** `origin/main` = `eeafd22`, clean & pushed; `csproj`/`.iss` at 1.5.1.
- **Website repo** clean & pushed (tip `275e806`); README badges are dynamic.

### Known issues / limitations (not bugs)
- **AI generation** is exercised only through mocks/fakes; meter/toggle/set art quality wants a real key.
- **Magick.NET-Q16-HDRI-x64 14.14.0** — Q16 means `ToByteArray` is 16-bit; any Magick pixel read must go
  through `Helpers/MagickPixels` (never a raw 4-bytes/pixel copy).
- **Custom AI endpoint** is Bearer-only (no Azure OpenAI api-key header). Installer ~58 MB (accepted).
- **iPlug2 code-export for a grid-layout strip** is a documented limitation, not a bug: its built-in
  `IBitmap`/`LoadBitmap` can only read a 1D strip, so grid mode emits a warning comment.

---

## The release pipeline (one creator = CI) — unchanged

**Stage 1** `scripts/Invoke-Release.ps1` (Windows PowerShell 5.1; needs `az login`, signtool + the Trusted
Signing dlib, ISCC, gh auth): test-gate → integrity guard → bump → publish → **sign exe + installer** → ISCC
→ stage `releases/latest/` → commit + tag + push. **Stage 2** `.github/workflows/auto-release.yml`: VirusTotal
→ the sole `gh release create`. **Stage 3** the website changelog.

To cut the **next** release: `powershell -ExecutionPolicy Bypass -File scripts\Invoke-Release.ps1 -Bump
patch -WebsiteRepo ..\StripKit-Website` then `gh run watch`. (`-Bump patch` from 1.5.1 → **1.5.2**.) The
release-integrity guard aborts on uncommitted tracked source, so commit feature work first.

---

## Next Steps (carryover — none are blockers)

1. **Live-eyeball QA pass with REAL assets** — long-standing carryover, never done. The automated tests +
   golden images pass, but the fader/slider/meter **Generate** paths (with a real AI key) and the
   path-traced **Assemble** flow (with a real EXR/PNG sequence) have never had a hands-on real-data review.
2. **Low-priority in-app tutorial parity items** deliberately skipped in this session's docs audit
   (window/session persistence, the "Prompt to be sent" expander, seed silent-overwrite behavior) — these
   ARE documented on the website, just not in the in-app walkthroughs, by choice. Add them in-app only if
   full parity is wanted.
3. Deferred v2s (backlog): optical-flow interpolation, multi-layer EXR, stereo/dB meters, runtime AOV
   toggle, Unity/Godot code targets.

---

## Warnings for the next agent

- **Nothing critical is broken and nothing is uncommitted** — both repos are clean and pushed. This is a
  clean starting point.
- **Do NOT** rewrite `SkiaFilmstripRenderer`, change the `(N−1)` angle divisor, move VM logic into
  code-behind, or reference Avalonia UI types from VMs (the preview `Bitmap` alias aside). **Gate new render
  paths behind defaults so prior goldens stay byte-identical.** Any renderer math change must be mirrored in
  `FilmstripEngine.cs`.
- **The `TransportTile` Border must render at an identical height across Create/Import/Assemble** —
  `TransportTileAlignmentTests` asserts it. Do NOT add controls inside a `TransportTile`; put post-export
  affordances OUTSIDE it (a new parent-grid row).
- **Untrusted SVG** goes through `SafeXml.Parse` then `SvgSanitizer.Sanitize` before `Svg.Skia.FromSvg`
  (BUG-013 was a live SSRF). **Magick pixels** go through `Helpers/MagickPixels` (Q16 is 16-bit). **When
  hand-building bitmaps, set the correct `SKAlphaType`** (BUG-012 was a Premul-tagged straight-bytes bug).
- **New file-intake paths must share one accepted-extension list** — BUG-021 was exactly this: drag-drop
  and "Add files…" hard-coded a shorter list than "Choose folder…", silently dropping HDR frames. The
  Assemble tab now funnels all three through `FrameSequenceViewModel.AcceptedExtensions`; keep that pattern.
- **Manifest fields carrying a user-controlled count must be clamped before serialization** (BUG-017 was
  `GridColumns`). Any new manifest field with a schema `minimum` needs its own clamp at the
  `ManifestService` call site — the renderer's internal clamp does not protect the manifest.
- **Build annotation:** a pre-existing **xUnit1031** advisory in `LayeredImportServiceTests.cs`
  (blocking-task-op in a test) is harmless and expected — not a regression.
- **Release tooling:** Windows PowerShell 5.1 only; `az login` first; Trusted Signing via signtool + the
  dlib (not AzureSignTool). CI is the sole release creator; commit feature work before the release script.
- **House design:** Depth machined-grey dark theme, `#f25914` ember accent, Verdana sans + monospace numerics.

---

## Files to Read First

1. `CLAUDE.md` — project context, conventions, house rules, last task.
2. `docs/SOURCE_MAP.md` — where everything lives (six tabs, all services + the v1.5 helpers).
3. `docs/ARCHITECTURE.md` — deep reference.
4. `docs/ROADMAP.md` — releases + the vNext backlog.
5. `docs/BUGS.md` (all resolved) · `docs/PACKAGING.md` (the release pipeline).
