# PACKAGING.md — StripKit

> Version 0.6.0 · last-updated 2026-06-03

How StripKit is built into a Windows installer and how auto-update works. StripKit
ships via **Velopack**, which produces *both* the installer (`Setup.exe`) and the
delta-based auto-update feed from one `vpk pack` — so there is no separate Inno
Setup / Squirrel step to keep in sync.

## What ships

| Artifact (in `releases/`) | Purpose |
| --- | --- |
| `StripKit-win-Setup.exe` | The installer users download and run (per-user install, Desktop + Start-Menu shortcuts, uninstaller). |
| `StripKit-win-Portable.zip` | A no-install build (unzip and run). |
| `StripKit-<ver>-full.nupkg` + `RELEASES` / `*.win.json` | The **update feed** the running app reads to self-update. |

`releases/` and `publish/` are git-ignored — they are build outputs, recreated on demand.

## Prerequisites

- .NET 9 SDK.
- The Velopack CLI, pinned to the same version as the `Velopack` package reference
  (currently **1.1.1**):
  ```
  dotnet tool install --global vpk --version 1.1.1
  ```
  (If a new shell can't find `vpk`, it's at `%USERPROFILE%\.dotnet\tools\vpk.exe`.)

## Release steps

All commands run from the repo root. Bump `<ver>` to the release version (match the
app's version; we are at `0.6.0`).

### 1 — Self-contained publish (runs without the .NET SDK)

```
dotnet publish src/StripKit/StripKit.csproj -c Release -r win-x64 --self-contained true -o publish
```

This bundles the .NET runtime + the native libs (`libSkiaSharp.dll`, `av_libGLESv2.dll`)
into `publish/` (~100 MB). Boot `publish/StripKit.exe` once to sanity-check it runs.

### 2 — (Not the first release) pull existing releases so deltas can be built

```
vpk download github --repoUrl https://github.com/Vybecode-LTD/stripkit --token <GITHUB_TOKEN> --outputDir releases
```

Skip this for the very first release.

### 3 — Pack the installer + feed

```
vpk pack --packId StripKit --packVersion <ver> --packDir publish --mainExe StripKit.exe ^
         --packTitle "StripKit" --packAuthors "VybeCod.ing" ^
         --icon src/StripKit/Assets/stripkit.ico --outputDir releases
```

`vpk` verifies `VelopackApp.Run()` is wired in `Main` and compresses `publish/` into the
artifacts above. The build is **unsigned** today — see *Code signing* below.

### 4 — Publish to GitHub Releases (makes auto-update live)

```
vpk upload github --repoUrl https://github.com/Vybecode-LTD/stripkit --token <GITHUB_TOKEN> ^
         --publish --releaseName "StripKit <ver>" --tag v<ver> --outputDir releases
```

Use a token with `repo` scope (the GitHub CLI's token works: `gh auth token`). Once the
release is published, installed copies pick the update up on their next launch.

## How auto-update works in the app

`src/StripKit/Services/UpdateService.cs` runs in the background from `App.OnFrameworkInitializationCompleted`:

1. `new UpdateManager(new GithubSource("https://github.com/Vybecode-LTD/stripkit", null, false))`.
2. Returns immediately unless `manager.IsInstalled` — so **dev runs (`dotnet run`), the
   portable build, and headless tests never touch the network or update**.
3. `CheckForUpdatesAsync` → `DownloadUpdatesAsync` → `WaitExitThenApplyUpdates`: the update
   is staged and applied the next time the user closes the app, never mid-session.

All failures (offline, no release yet) are swallowed — updates never crash the app.

## Versioning

The `--packVersion` is the release version Velopack compares against. Keep it in step with
the project/docs version. SemVer; pre-1.0 we bump the minor for each shipped feature set.

## Code signing (deferred — currently shipping unsigned)

Unsigned builds raise a Windows SmartScreen prompt on first run. To sign, put a code-signing
cert on the build machine (or use a cloud signer) and pass signtool args to `vpk pack`:

```
vpk pack ... --signParams "/a /fd sha256 /tr http://timestamp.digicert.com /td sha256"
```

`vpk` signs every packaged binary + the `Setup.exe`. Azure Trusted Signing is also
supported via `--azureTrustedSignFile`. Document the cert source here once chosen.

## Regenerating the icon

The app icon is `src/StripKit/Assets/stripkit.ico` (a 32-bit multi-resolution ICO —
16/24/32/48/64/128/256 px) plus `Assets/stripkit.png` (256 px, the window icon),
generated from the master `stripkitIcon01.png`. SkiaSharp can resize but **cannot encode
ICO**, so the `.ico` container is assembled by hand: resize the master to each size into
`Bgra8888`/unpremul, then write `ICONDIR` + one `ICONDIRENTRY` + one `BITMAPINFOHEADER`
DIB (height doubled for the AND mask, which is left all-zero so the alpha channel drives
transparency) per size. To regenerate after changing the master, re-run that conversion
and rebuild — `dotnet build` embeds the `.ico` via `<ApplicationIcon>` and fails the build
(CS7064) if the container is malformed.
