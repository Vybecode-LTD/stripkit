# PACKAGING.md — StripKit

> Version 0.6.0 · last-updated 2026-06-03

How StripKit is built into a Windows installer and shipped. StripKit follows the
three-stage model in `SOFTWARE_RELEASE.md`: a **local script builds + stages** the
installer, **CI is the single release creator**, and the **website reads the live
release** passively. Packaging is **Inno Setup** (chooseable install dir, optional
desktop + Start-Menu shortcuts, a registry-wiping uninstaller). There is no in-app
auto-updater — updates are delivered as new GitHub Releases that the website surfaces.

## What ships

| Artifact | Where | Purpose |
| --- | --- | --- |
| `StripKit-Setup-<X.Y.Z>-x64.exe` | a GitHub Release (and `releases/latest/` in-repo) | The per-user installer users download and run. Self-contained — no .NET SDK/runtime needed on the target machine. |

`publish/` and `installer/Output/` are git-ignored build outputs. `releases/latest/*.exe`
**is** tracked: pushing it is what triggers the release workflow.

## Prerequisites (build machine)

- **.NET 9 SDK.**
- **Inno Setup 6** — the compiler `ISCC.exe`. On this machine it's a per-user install at
  `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` (the release script auto-detects it).
- **GitHub CLI** (`gh`), authenticated with push access to `Vybecode-LTD/stripkit`.
- One repo secret: **`VT_API_KEY`** (VirusTotal) — already set on the repo; used only by CI.

## Releasing — "release it"

```powershell
# first release (ships the current version, 0.6.0, with no bump):
pwsh scripts/Invoke-Release.ps1 -Bump none
# subsequent releases:
pwsh scripts/Invoke-Release.ps1            # patch  (+0.0.1) — a plain "release it"
pwsh scripts/Invoke-Release.ps1 -Bump minor   # "major release" (+0.1.0)
```

### Stage 1 — `scripts/Invoke-Release.ps1` (local)

1. **Test gate** — `dotnet test`; aborts the release if anything is red.
2. **Version bump** in lockstep: `StripKit.csproj` `<Version>`, `installer/StripKit.iss`
   `#define MyAppVersion`, and `docs/CHANGELOG.md` (`## [Unreleased]` → `## [X.Y.Z] — date`).
3. **Publish** a self-contained `win-x64` build into `publish/`.
4. **Package** with Inno Setup → `installer/Output/StripKit-Setup-X.Y.Z-x64.exe`.
5. **Stage** that installer under `releases/latest/` (replacing the previous one).
6. **Commit, tag `vX.Y.Z`, and push** (branch + tag).

It deliberately does **not** call `gh release create`. The push is the hand-off.

### Stage 2 — `.github/workflows/auto-release.yml` (CI, the sole release creator)

Triggered by the push touching `releases/latest/*.exe`. It:

1. Reads the version from the installer filename.
2. Skips if a release for that version already exists (idempotent / race-free).
3. **VirusTotal scan** of the installer (large-file upload API, `VT_API_KEY`), capturing the
   detection ratio + report link.
4. Extracts that version's section from `docs/CHANGELOG.md` for the release notes.
5. **Creates the GitHub Release**, attaching the installer and appending the VirusTotal
   verdict + SHA-256.

### Stage 3 — the website (passive)

`StripKit-Website` reads the latest release from the GitHub API (`js/download.js`) and the
changelog from `docs/CHANGELOG.md` (`js/changelog.js`). No deploy step is coupled to a
release — publishing the release is enough for the site to update.

## The installer (`installer/StripKit.iss`)

- Per-user install (`PrivilegesRequired=lowest`, dialog override allowed) — no forced UAC.
- The **install directory is chooseable**; **desktop shortcut** is an opt-in task; a
  **Start-Menu** group is created (the user can decline it).
- The **uninstaller wipes all traces**: the only registry footprint
  (`HKA\Software\VybeCode\StripKit`) is flagged `uninsdeletekey`, and Inno removes its own
  uninstall key automatically.
- Wizard art: `installer/wizard-large.bmp` (the StripKit brandmark + the VybeCode logo) and
  `installer/wizard-small.bmp`; the setup/uninstaller icon is `Assets/stripkit.ico`. The
  BMPs are regenerated from the source PNGs with the System.Drawing snippet kept in the
  session notes (luminance-checked so the dark VybeCode logo sits on a light chip).

## Manual build (debugging the package without a release)

```powershell
dotnet publish src/StripKit/StripKit.csproj -c Release -r win-x64 --self-contained true -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer/StripKit.iss
# → installer/Output/StripKit-Setup-0.6.0-x64.exe  (~33 MB)
```

## Code signing (deferred — currently shipping unsigned)

Unsigned installers raise a Windows SmartScreen prompt on first run. To sign once a cert is
available: sign `publish/StripKit.exe` (and the native DLLs) with `signtool` before packaging,
and add a `SignTool` directive to `[Setup]` so Inno signs the produced installer:

```
signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 publish\StripKit.exe
; in StripKit.iss [Setup]:  SignTool=mysigntool
```

Document the cert source here once chosen.

## Regenerating the icon

The app icon is `src/StripKit/Assets/stripkit.ico` (a 32-bit multi-resolution ICO —
16/24/32/48/64/128/256 px) plus `Assets/stripkit.png` (256 px, the window icon),
generated from the master `stripkiticon02.png`. SkiaSharp can resize but **cannot encode
ICO**, so the `.ico` container is assembled by hand: resize the master to each size into
`Bgra8888`/unpremul, then write `ICONDIR` + one `ICONDIRENTRY` + one `BITMAPINFOHEADER`
DIB (height doubled for the AND mask, which is left all-zero so the alpha channel drives
transparency) per size. The master is non-square, so each size is **contain-fit** (centred,
aspect-preserved) rather than stretched. To regenerate after changing the master, re-run
that conversion and rebuild — `dotnet build` embeds the `.ico` via `<ApplicationIcon>` and
fails the build (CS7064) if the container is malformed.
