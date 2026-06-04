# HANDOFF — StripKit

> Version 0.6.0 · last-updated 2026-06-04 · last-audit 2026-06-04
>
> Pick-up notes for the next session. Read `CLAUDE.md` and `docs/SOURCE_MAP.md`
> first, then this.

## Current state

- **v0.6.0 is SHIPPED and live:** https://github.com/Vybecode-LTD/stripkit/releases/tag/v0.6.0
  (asset `StripKit-Setup-0.6.0-x64.exe`, ~33.5 MB, self-contained — runs with no .NET SDK).
  VirusTotal ~4/71 heuristic false-positives **because it is unsigned** (not a real bug).
- **Tests green:** `dotnet test` → **49 / 49**.
- **Packaging is now Inno Setup** (Velopack was removed). The two-stage release
  pipeline is proven end-to-end this session — see "How to ship" below.
- **Website repo exists + pushed:** `Vybecode-LTD/StripKit-Website` (landing page,
  GitHub-driven download, Formspree contact form, VirusTotal shield). **Not yet
  deployed** to stripkit.pro.
- **Open bugs:** 0 (`docs/BUGS.md`).

## How to ship the next release ("release it")

```powershell
pwsh scripts/Invoke-Release.ps1            # patch bump → 0.6.1
pwsh scripts/Invoke-Release.ps1 -Bump minor   # feature release
gh run watch                               # watch CI scan + publish
```

Two stages, **CI is the sole release creator** (never create it locally):

1. **`scripts/Invoke-Release.ps1`** (`-Bump none|patch|minor|major`): test-gate → bump
   version across `csproj`/`.iss`/`CHANGELOG` → self-contained win-x64 publish → ISCC
   package → stage installer under `releases/latest/` → commit + tag `vX.Y.Z` + push.
   It does NOT create the GitHub Release.
2. **`.github/workflows/auto-release.yml`**: triggered by the pushed
   `releases/latest/*.exe` (or `workflow_dispatch`); VirusTotal scan (`VT_API_KEY`
   secret, already set) → creates the GitHub Release with notes from the
   `docs/CHANGELOG.md` `[version]` section.

Inno Setup 6.7.3 is installed at `%LOCALAPPDATA%\Programs\Inno Setup 6` (the script
auto-detects it). Flow details: `docs/PACKAGING.md`.

## Next steps (suggested priority)

1. **Deploy the website to stripkit.pro** — enable GitHub Pages on `StripKit-Website`
   or point the domain at your host. *(user action)*
2. **Code-signing certificate** — clears the VirusTotal false-positives + the Windows
   SmartScreen prompt. The `.iss` already has a SignTool hook (`docs/PACKAGING.md`).
   Shipping unsigned until then.
3. **Per-release website upkeep** — add a plain-language entry to the website's
   `updates.json` alongside each technical `docs/CHANGELOG.md` entry. This is the one
   manual coupling between the two repos (their changelogs are intentionally decoupled).
4. **Remaining product features** — importer frame-count resampling, multi-control
   manifests, meter peak-hold / stereo.

## Warnings / gotchas for the next session

- **Release scripts must read/write files as UTF-8.** PowerShell 5.1 `Get-Content`
  without `-Encoding UTF8` corrupts the changelog's em-dashes (bit us once; fixed in
  `f1b68d3`). Use `pwsh` (PS 7) and explicit UTF-8.
- **Never interpolate the changelog body into a shell command** — its backticks
  trigger command substitution. Use `--notes-file` (fixed in `a408bc9`).
- **Stray untracked file:** `C:\DEV\StripKit\teststrip01.png` (test artifact in the
  working tree) — delete it or add to `.gitignore`.
- `actions/checkout@v4` runs on Node 20 (GitHub deprecation mid-2026) — minor; upgrade
  when convenient.
- Commits are authored as **VybeCode** (`info@apmonster.ai`), co-authored Claude.
- **`FilmstripEngine.cs` (repo root) is a hand-maintained mirror** of
  `Services/SkiaFilmstripRenderer.cs` + `Models`; NOT compiled by the app. If you
  change renderer math, update it too. In sync as of this audit.
- **Do not** rewrite `SkiaFilmstripRenderer`, change the `(N-1)` angle divisor, move VM
  logic into code-behind, or reference Avalonia UI types from view models.

## Files to read first

1. `CLAUDE.md` — project context + conventions.
2. `docs/SOURCE_MAP.md` — where everything lives.
3. `scripts/Invoke-Release.ps1` + `.github/workflows/auto-release.yml` — the release
   pipeline (most-changed area this session).
4. `docs/PACKAGING.md` — publish → ISCC → GitHub Release → sign flow.
