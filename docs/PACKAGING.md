# PACKAGING.md — StripKit release-automation reference

> Version 0.7.0 · last-updated 2026-06-04 · last-audit 2026-06-04

**Audience: an agent (or human) who has never seen this repo and must build,
debug, or extend StripKit's release pipeline without breaking it.** This is the
authoritative reference. It documents every file, every script section, every CI
step, the `.gitignore` negation trick that makes the whole thing work, the two
historical bugs that must never come back, and runbooks for cutting a release
and recovering from failures. When in doubt, prefer this document over memory,
and **verify any change against the actual files** listed in the next section.

> StripKit is a C#/.NET 9 + Avalonia desktop app. It ships as a **self-contained
> `win-x64` Windows installer** (no .NET runtime needed on the target machine),
> built with **Inno Setup**, distributed as a **GitHub Release download**. There
> is **no in-app auto-updater** — a new release is just a new GitHub Release that
> the website surfaces. Windows-only today; macOS/Linux are unbuilt (see
> [§13 How to extend](#13-how-to-extend)).

---

## 0. The files that make up the system

Read these before touching anything. The pipeline is exactly these files plus
two git-tracked binaries; nothing else participates.

| File | Stage / role |
| --- | --- |
| `scripts/Invoke-Release.ps1` | **Stage 1** — local PowerShell driver. Test-gate, version bump, publish, package, stage, commit, tag, push. **Does NOT create the release.** |
| `.github/workflows/auto-release.yml` | **Stage 2** — CI. The **single, sole release creator**. Scans with VirusTotal and calls `gh release create`. |
| `installer/StripKit.iss` | Inno Setup script. Turns `publish/` into the per-user installer `.exe`. |
| `installer/wizard-large.bmp` | Inno large wizard image (left panel) — StripKit brandmark + VybeCode logo. **Tracked binary.** |
| `installer/wizard-small.bmp` | Inno small wizard image (top-right) — small logo. **Tracked binary.** |
| `.gitignore` | The negation rules that make `releases/latest/*.exe` **tracked** (the CI trigger) while everything else under `publish/`, `installer/Output/`, and `releases/` is ignored. |
| `src/StripKit/StripKit.csproj` | Holds `<Version>` — the **single source of truth** for the version. Self-contained publish target. |
| `docs/CHANGELOG.md` | The `## [Unreleased]` → `## [X.Y.Z] — date` section the script promotes and CI reads for release notes. |
| `releases/latest/StripKit-Setup-<ver>-x64.exe` | The **staged installer**. Pushing this file is the literal trigger for Stage 2. **Tracked binary.** |
| `docs/PACKAGING.md` | This document. |

Governing policy doc (project-root, applies to all VybeCode projects):
`SOFTWARE_RELEASE.md` — the three-stage / single-release-creator constitution
StripKit implements here.

Repo: `https://github.com/Vybecode-LTD/stripkit` (remote `origin`). Note the
GitHub **org casing is `Vybecode-LTD`**; URLs are case-insensitive on GitHub but
keep this casing in docs and metadata.

---

## 1. The 3-stage model and the "single release creator" rule

```
  ┌─────────────────────────────────────────────────────────────────────┐
  │ STAGE 1 — local (your machine)                                        │
  │ scripts/Invoke-Release.ps1                                            │
  │   test-gate → bump version (csproj + .iss + CHANGELOG) → dotnet       │
  │   publish (self-contained win-x64) → ISCC builds the installer →      │
  │   copy installer to releases/latest/ → git commit + tag vX.Y.Z + push │
  │   ── DOES NOT create a GitHub Release ──                              │
  └───────────────────────────────┬─────────────────────────────────────┘
                                   │ git push touches releases/latest/*.exe
                                   ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │ STAGE 2 — CI (GitHub Actions, ubuntu-latest)                          │
  │ .github/workflows/auto-release.yml                                   │
  │   read version from filename → skip if release exists (idempotent) →  │
  │   VirusTotal scan → build notes from CHANGELOG → gh release create    │
  │   ── THE ONLY THING THAT EVER CALLS gh release create ──             │
  └───────────────────────────────┬─────────────────────────────────────┘
                                   │ a GitHub Release now exists
                                   ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │ STAGE 3 — website (passive, separate repo: Vybecode-LTD/StripKit-     │
  │ Website)                                                             │
  │   js/download.js reads the latest release from the GitHub API;        │
  │   the site's updates.json carries a plain-language changelog.         │
  │   No deploy is coupled to a release — publishing the release is        │
  │   enough for the live site to surface the new download.               │
  └─────────────────────────────────────────────────────────────────────┘
```

**The single-release-creator rule.** Exactly one actor — Stage 2 CI — ever runs
`gh release create`. Stage 1 only *builds and pushes*; the website only *reads*.

**Why it's race-free.** A naive setup where the local script *also* creates the
release races CI: two creators, one tag, nondeterministic winner, possible
duplicate/half-made releases. Here:

- Stage 1's sole hand-off is **a git push that includes a changed
  `releases/latest/*.exe`**. That is data, not an action against the Releases API.
- Stage 2 is **idempotent**: its first real step is "skip if release `vX.Y.Z`
  already exists." Re-runs, double-pushes, and manual `workflow_dispatch` re-runs
  all converge to "exactly one release per version."
- Stage 3 never writes anything; it only reads the GitHub Releases API.

So no matter how many times the workflow fires for a given version, **at most one
release is ever created**, and there is never a second creator to race.

---

## 2. Every file and its exact role

### `scripts/Invoke-Release.ps1` — Stage 1 driver
PowerShell 5.1-compatible (`#requires -Version 5.1`). Run it on the dev machine.
It is the **only** place versions get bumped. It ends by pushing; it never
touches the Releases API. Full walk-through in [§3](#3-invoke-releaseps1--section-by-section).

### `.github/workflows/auto-release.yml` — Stage 2, sole release creator
Runs on `ubuntu-latest`. Triggered by a push that changes `releases/latest/*.exe`
(or manually via `workflow_dispatch`). It is the **only** caller of
`gh release create`. Full walk-through in [§5](#5-auto-releaseyml--step-by-step).

### `installer/StripKit.iss` — Inno Setup script
Compiled by `ISCC.exe`. Packages everything in `..\publish\*` into a per-user
installer named `StripKit-Setup-<MyAppVersion>-x64.exe` in `installer\Output\`.
Directive-by-directive in [§4](#4-stripkitiss--directive-by-directive).

### `installer/wizard-large.bmp`, `installer/wizard-small.bmp`
The two wizard images Inno paints into the installer UI (`WizardImageFile` /
`WizardSmallImageFile`). **Tracked binaries** (Inno needs them at compile time;
they are not regenerated by the pipeline). Large = StripKit brandmark + VybeCode
logo on the left wizard panel; small = the top-right corner logo. Regenerated by
hand from source PNGs when branding changes (luminance-checked so the dark
VybeCode logo sits on a light chip — see [§4.10](#410-wizard-art-the-two-bmps)).

### `.gitignore`
Encodes the negation that tracks **only** `releases/latest/*.exe` while ignoring
all other build output. This is load-bearing — break it and either the trigger
file stops being committed (no releases fire) or transient junk gets committed.
Mechanics in [§7](#7-gitignore-negation-mechanics).

### `src/StripKit/StripKit.csproj`
`<Version>0.6.0</Version>` is the **single source of truth**. Stage 1 reads it,
computes the next version, and writes it back here, into the `.iss`, and into the
CHANGELOG — all three must stay in lockstep. Also defines the self-contained
publish surface (`<OutputType>WinExe</OutputType>`, `net9.0`,
`<ApplicationIcon>Assets\stripkit.ico</ApplicationIcon>`).

### `docs/CHANGELOG.md`
Stage 1 promotes the top `## [Unreleased]` heading to `## [X.Y.Z] — <date>`.
Stage 2 greps that `## [X.Y.Z]` section out for the release-notes body. The exact
header format matters to both — see [§3.6](#36-changelog-promotion) and
[§5.5](#55-build-release-notes-from-changelog).

### `releases/latest/StripKit-Setup-<ver>-x64.exe`
The single **tracked** installer artifact. Its presence-in-a-push is the entire
Stage-1→Stage-2 contract. The version is parsed back out of this filename by CI,
so the `StripKit-Setup-<X.Y.Z>-x64.exe` naming is also load-bearing.

---

## 3. `Invoke-Release.ps1` — section by section

> File: `scripts/Invoke-Release.ps1`. Line numbers below refer to that file as of
> v0.6.0. `$ErrorActionPreference = 'Stop'` (line 36) means any unhandled error
> aborts; most steps also `throw` explicitly on `$LASTEXITCODE -ne 0`.

### 3.0 Invocation, parameters, and the `-Bump` semantics (lines 29–34)

```powershell
[CmdletBinding()]
param(
    [ValidateSet('none', 'patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [switch]$SkipTests
)
```

`-Bump` controls the version arithmetic against the current `<Version>` in the
csproj (lines 66–73):

| `-Bump` | Effect on `maj.min.pat` | Meaning |
| --- | --- | --- |
| `none` | no change — release current version as-is | **Used for the FIRST release** (shipped `0.6.0` with `-Bump none`). Also: re-release the same version after fixing a mid-run failure. |
| `patch` *(default)* | `pat++` | A plain "release it". `+0.0.1`. |
| `minor` | `min++; pat = 0` | "major release" in casual terms. `+0.1.0`. |
| `major` | `maj++; min = 0; pat = 0` | `+1.0.0`. |

`-SkipTests` bypasses the test gate (line 78 guard). **Do not use it for a real
release** — it exists for debugging the packaging steps only.

> **First-release fact:** v0.6.0 was cut with `pwsh scripts/Invoke-Release.ps1
> -Bump none`. There was no prior GitHub Release; the repo's existing `0.6.0`
> version was shipped unchanged, so the bump was `none`.

Path setup (lines 37–45): `$root` is the repo root (`Split-Path -Parent
$PSScriptRoot`). `$emdash = [char]0x2014` is the literal em-dash (U+2014) used in
the CHANGELOG header — **a real Unicode character, written via UTF-8** (this is
half of historical bug A; see [§9](#9-two-historical-bugs--do-not-reintroduce)).
Key paths: `$csproj`, `$iss`, `$changelog`, `$publishDir` (`publish/`),
`$issOutDir` (`installer/Output`), `$releasesLatest` (`releases/latest`).

### 3.1 The UTF-8 helper (lines 47–49)

```powershell
function Save-Text($path, $text) {
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}
```

Every file write goes through this. `UTF8Encoding($false)` = **UTF-8 with NO BOM**.
Combined with reading via `Get-Content -Raw -Encoding UTF8`, this is the fix for
historical bug A (em-dash double-encoding). **Never replace these with bare
`Set-Content`/`Out-File`** (PS 5.1 defaults to ANSI/UTF-16) or bare `Get-Content`
(PS 5.1 reads as ANSI). See [§9A](#9a-do-not-reintroduce-utf-8-readwrite-discipline).

### 3.2 ISCC auto-detection (lines 53–59)

```powershell
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found. ..." }
```

Probes three locations **in order**, takes the first that exists:

1. `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` — the **per-user install**
   (this is how Inno is installed on the current dev machine; checked first).
2. `%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe` — default 32-bit machine-wide install.
3. `%ProgramFiles%\Inno Setup 6\ISCC.exe` — 64-bit machine-wide install.

If none exist it throws. To support a different install location, add a path to
this array (don't hard-code elsewhere).

### 3.3 Read current + compute new version (lines 61–75)

Parses the csproj as XML, selects the first `//Version` node, splits on `.`,
applies the bump (see [§3.0](#30-invocation-parameters-and-the--bump-semantics)),
and computes `$new`. `$date` is `yyyy-MM-dd` (local time). Throws if there is no
`<Version>` node. Prints e.g. `=== Releasing v0.7.0 (was v0.6.0, bump=minor) 2026-06-04 ===`.

### 3.4 Test gate (lines 77–82)

```powershell
if (-not $SkipTests) {
    dotnet test (Join-Path $root 'StripKit.sln') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed - aborting before any version bump." }
}
```

Runs the full xUnit suite in **Debug** against `StripKit.sln`. **Crucially this
happens BEFORE any version files are touched** — a red suite aborts with zero
side effects (nothing to revert). Current baseline: 49/49 green.

### 3.5 Version bump — csproj + .iss (lines 84–93)

```powershell
$csprojText = Get-Content $csproj -Raw -Encoding UTF8
$csprojText = [regex]::Replace($csprojText, '(<Version>)[^<]+(</Version>)', "`${1}$new`${2}")
Save-Text $csproj $csprojText

$issText = Get-Content $iss -Raw -Encoding UTF8
$issText = [regex]::Replace($issText, '(#define MyAppVersion ")[^"]+(")', "`${1}$new`${2}")
Save-Text $iss $issText
```

- csproj: regex-replaces the inner text of `<Version>…</Version>`.
- .iss: regex-replaces the value of `#define MyAppVersion "…"`.

Both read+write strictly as **UTF-8**. The `` `${1} `` / `` `${2} `` are
PowerShell-escaped regex backreferences (the backtick escapes `$` so PowerShell
doesn't expand it as a variable).

### 3.6 CHANGELOG promotion (lines 95–104)

```powershell
$clText = Get-Content $changelog -Raw -Encoding UTF8
$header = "## [$new] $emdash $date"          # e.g. "## [0.7.0] — 2026-06-04"
if ($clText -match '(?m)^##\s*\[Unreleased\].*$') {
    $clText = [regex]::Replace($clText, '(?m)^##\s*\[Unreleased\].*$', $header, 1)
} else {
    Write-Warning "No [Unreleased] section in CHANGELOG; inserting a stub for $new."
    $clText = [regex]::Replace($clText, '(?m)^(#\s+.*Changelog.*$)',
        "`$1`r`n`r`n$header`r`n`r`n- **Release $new.**", 1)
}
Save-Text $changelog $clText
```

- The happy path **renames the first `## [Unreleased]` line in place** to
  `## [X.Y.Z] — <date>` (em-dash via `$emdash`). Only the first match is replaced
  (`, 1`). All bullets that were under `[Unreleased]` become that version's notes.
- Fallback: if there is no `[Unreleased]` heading, it inserts a minimal stub
  section right after the top-level `# … Changelog …` title.
- **Header format is a contract** with CI ([§5.5](#55-build-release-notes-from-changelog)):
  CI greps `^## \[X.Y.Z\]`. Keep the `## [VERSION]` shape; the em-dash and date
  are display-only and CI ignores them.

> **Workflow note:** before releasing, accumulate changes under `## [Unreleased]`
> in `docs/CHANGELOG.md`. The release script "freezes" them into the versioned
> section. (You'll usually add a fresh empty `## [Unreleased]` back at the top
> as the next session begins; the script tolerates its absence via the fallback.)

### 3.7 Publish — self-contained win-x64 (lines 106–110)

```powershell
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed (version files already bumped to $new ...)." }
```

Clean publish into `publish/`. **Self-contained** (`--self-contained true`,
RID `win-x64`) — bundles the .NET runtime so target machines need no SDK/runtime.
Output ~33.5 MB after packaging.

> The error message is a deliberate breadcrumb: if publish fails, **the version
> files are already bumped**. Recovery is in [§10.1](#101-build-fails-after-a-version-bump).

### 3.8 Package with Inno Setup (lines 112–117)

```powershell
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }
$installer = Join-Path $issOutDir "StripKit-Setup-$new-x64.exe"
if (-not (Test-Path $installer)) { throw "Expected installer not produced: $installer" }
```

Runs the detected `ISCC.exe` on the `.iss`. The `.iss` writes to
`installer\Output\StripKit-Setup-<MyAppVersion>-x64.exe`. Because Stage 1 just
set `MyAppVersion` to `$new`, the expected path is computable; the script asserts
it exists.

### 3.9 Stage under `releases/latest/` (lines 119–125)

```powershell
New-Item -ItemType Directory -Force -Path $releasesLatest | Out-Null
Get-ChildItem (Join-Path $releasesLatest '*.exe') -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item $installer $releasesLatest -Force
```

**Deletes any previous `*.exe` in `releases/latest/`** (so only one installer is
ever tracked there), then copies the new one in. This directory and the `.exe`
inside it are the **only** tracked release artifacts (see [§7](#7-gitignore-negation-mechanics)).
Prints the size in MB.

### 3.10 Commit, tag, push (lines 127–135)

```powershell
git -C $root add src/StripKit/StripKit.csproj installer/StripKit.iss docs/CHANGELOG.md releases/latest
git -C $root status --short
git -C $root commit -m "Release v$new"
git -C $root tag "v$new"
git -C $root push
git -C $root push origin "v$new"
```

Stages **exactly** the four bumped/added paths (never `git add -A`), prints the
short status for a secret-scan eyeball, commits `Release v$new`, tags `v$new`,
then pushes the branch and the tag separately.

> The branch push is what carries the changed `releases/latest/*.exe` to GitHub
> and **fires Stage 2** (the workflow's `paths:` filter). The tag push is for
> human/website convenience and lands the `vX.Y.Z` git tag the release attaches to.

### 3.11 It deliberately does NOT create the release (lines 16–17, 137–139)

The script's final message says CI will scan + create the release; **the script
never calls `gh release create`.** This is the single-release-creator rule in
code. If you ever add release creation here, you reintroduce the race — don't.

---

## 4. `installer/StripKit.iss` — directive by directive

> File: `installer/StripKit.iss`. Inno Setup 6 script. `ISCC.exe` compiles it.

### 4.0 `#define`s (lines 9–13)

```
#define MyAppName      "StripKit"
#define MyAppVersion   "0.6.0"          ; ← bumped by Invoke-Release.ps1 (§3.5)
#define MyAppPublisher "VybeCode Software"
#define MyAppURL       "https://stripkit.pro"
#define MyAppExeName   "StripKit.exe"
```

`MyAppVersion` is the value Stage 1 regex-replaces. It feeds `AppVersion`,
`AppVerName`, and `OutputBaseFilename`.

### 4.1 Fixed `AppId` (line 17)

```
AppId={{B2E9B0A1-5C3D-4E7A-9F12-6A4D8C0E1F23}
```

A **fixed GUID** identifying the product across versions. The doubled leading
`{{` is Inno's escape for a literal `{`. **Never change this** — it's what ties
an upgrade/uninstall to the same installed product. Change it and existing
installs become un-upgradable orphans.

### 4.2 App metadata (lines 18–24)

`AppName`, `AppVersion`, `AppVerName` (`StripKit 0.6.0`), `AppPublisher`, and the
URL trio (`AppPublisherURL`/`AppSupportURL`/`AppUpdatesURL`, all `stripkit.pro`) —
surfaced in Add/Remove Programs and the wizard.

### 4.3 Install directory — `{autopf}` chooseable (line 25)

```
DefaultDirName={autopf}\{#MyAppName}
```

`{autopf}` resolves to the appropriate "Program Files" per privilege level: for a
per-user install it's `%LOCALAPPDATA%\Programs`; if elevated, the real
`Program Files`. The directory **is chooseable** in the wizard (no `DisableDirPage`).

### 4.4 Start-Menu group (lines 26–27)

```
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
```

`{group}` → a `StripKit` Start-Menu folder. `AllowNoIcons=yes` lets the user tick
"Don't create a Start Menu folder."

### 4.5 Welcome page + output (lines 28–33)

`DisableWelcomePage=no` (show the welcome page — needed for the large wizard
image to appear), `OutputDir=Output` (relative to the `.iss` → `installer\Output`),
`Compression=lzma2` + `SolidCompression=yes` (max compression), `WizardStyle=modern`.

### 4.6 `OutputBaseFilename` with the version (line 30)

```
OutputBaseFilename=StripKit-Setup-{#MyAppVersion}-x64
```

Produces `StripKit-Setup-0.6.0-x64.exe`. **CI parses the version back out of this
filename** ([§5.2](#52-identify-installer--version)), so this pattern
(`StripKit-Setup-<X.Y.Z>-x64.exe`) is a contract — keep the embedded
`MAJOR.MINOR.PATCH`.

### 4.7 Icons (lines 34–35)

`SetupIconFile=..\src\StripKit\Assets\stripkit.ico` (the wizard/installer EXE
icon) and `UninstallDisplayIcon={app}\{#MyAppExeName}` (the installed EXE provides
the uninstall entry's icon).

### 4.8 Architecture (lines 38–39)

```
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
```

`x64compatible` allows x64 **and** ARM64 (which runs x64 under emulation), and
installs in 64-bit mode there. The published payload is `win-x64`.

### 4.9 Per-user privileges (lines 40–41)

```
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
```

`lowest` = **no forced UAC**; installs per-user by default. `...OverridesAllowed=dialog`
shows a dialog letting the user choose "install for all users" (which elevates).
This is why the app installs without admin rights.

### 4.10 Wizard art — the two BMPs (lines 36–37)

```
WizardImageFile=wizard-large.bmp        ; left panel — StripKit brandmark + VybeCode logo
WizardSmallImageFile=wizard-small.bmp   ; top-right corner — small logo
```

Paths are relative to the `.iss` (`installer/`). Both are **tracked binaries**.
Regenerate from source PNGs only when branding changes. The BMPs are
luminance-checked so the dark VybeCode logo sits on a light chip (a System.Drawing
conversion snippet was used; keep it in session notes if you regenerate).

### 4.11 `[Languages]` (lines 43–44)

English, `compiler:Default.isl`.

### 4.12 `[Tasks]` — `desktopicon` (lines 46–47)

```
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
```

Defines the **opt-in** "Create a desktop icon" checkbox (used by `[Icons]`).

### 4.13 `[Files]` (lines 49–50)

```
[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
```

Copies **everything** from `..\publish` (the self-contained output) into `{app}`,
recursively. `ignoreversion` = always overwrite regardless of file version (right
for a self-contained bundle). **This is the link to Stage 1's publish step** — the
`.iss` ships exactly what `dotnet publish -o publish` produced.

### 4.14 `[Icons]` (lines 52–55)

```
Name: "{group}\{#MyAppName}";            Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";      Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
```

Start-Menu launcher, Start-Menu uninstall entry, and a desktop shortcut **only if
the `desktopicon` task was ticked** (`{autodesktop}` = per-user or common Desktop
matching the privilege level).

### 4.15 `[Registry]` — the registry-wiping uninstaller (lines 57–61)

```
Root: HKA; Subkey: "Software\VybeCode\StripKit"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\VybeCode"; Flags: uninsdeletekeyifempty
```

The **only** registry footprint. `HKA` = `HKEY_CURRENT_USER` for a per-user
install (or `HKLM` if elevated) — it follows the privilege level automatically.

- `uninsdeletekey` on `...\VybeCode\StripKit` → the StripKit key (and everything
  under it) is **deleted entirely on uninstall**.
- `uninsdeletekeyifempty` on the parent `...\VybeCode` → the parent is removed
  too, **but only if empty** (so a sibling VybeCode product's key survives).

Combined with Inno automatically removing its **own** uninstall registration, an
uninstall leaves **no registry trace**.

### 4.16 `[Run]` — launch after install (lines 63–64)

```
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,...}"; Flags: nowait postinstall skipifsilent
```

Offers "Launch StripKit" at the end. `postinstall` (a checkbox on the finish
page), `nowait` (don't block the installer), `skipifsilent` (no launch on silent
installs).

---

## 5. `auto-release.yml` — step by step

> File: `.github/workflows/auto-release.yml`. Runs on `ubuntu-latest`. This is the
> **sole release creator.** Every job step after `meta` is gated on
> `steps.exists.outputs.exists == 'false'`, so a re-run against an
> already-released version is a clean no-op.

### 5.1 Triggers + permissions (lines 15–23)

```yaml
on:
  push:
    branches: [main]
    paths:
      - 'releases/latest/*.exe'
  workflow_dispatch: {}

permissions:
  contents: write
```

- **`push` on `main` filtered to `releases/latest/*.exe`** — fires *only* when the
  staged installer changes. This is the Stage-1→Stage-2 hand-off. (It will not
  fire on doc-only or code-only pushes.)
- **`workflow_dispatch`** — lets you re-run the workflow manually against whatever
  installer is currently committed under `releases/latest/` (recovery path; see
  [§10.5](#105-re-running-via-workflow_dispatch)).
- **`permissions: contents: write`** — required so the built-in `GITHUB_TOKEN`
  can create a release and upload an asset.

Single job `release` on `ubuntu-latest`.

### 5.2 `actions/checkout@v4` (line 29)

Checks out the repo (so the committed `releases/latest/*.exe` and
`docs/CHANGELOG.md` are present). **Node-20 deprecation note:** `@v4` runs on
Node 20; GitHub has begun warning about Node-20 actions as Node 24 becomes
standard. When that bites, bump to the then-current `actions/checkout` major (e.g.
`@v5`). It is the only third-party action used; everything else is `gh`/`curl`/`jq`.

### 5.3 `Identify installer + version` — step id `meta` (lines 31–42)

```bash
file=$(ls releases/latest/*.exe | head -n1)
base=$(basename "$file")
ver=$(echo "$base" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -n1)
{ echo "file=$file"; echo "base=$base"; echo "version=$ver"; } >> "$GITHUB_OUTPUT"
```

Picks the first `.exe` under `releases/latest/`, extracts `MAJOR.MINOR.PATCH` from
its **filename** via regex, and exports `file` / `base` / `version` as step
outputs consumed by every later step. (This is why [§4.6](#46-outputbasefilename-with-the-version)'s
filename pattern is a contract.)

### 5.4 `Skip if release already exists` — step id `exists` (lines 44–55)

```bash
if gh release view "v$VERSION" >/dev/null 2>&1; then
  echo "exists=true"  >> "$GITHUB_OUTPUT"
else
  echo "exists=false" >> "$GITHUB_OUTPUT"
fi
```

`GH_TOKEN: ${{ github.token }}`. Sets `exists=true/false`. **This is the
idempotency guard** that makes the whole pipeline race-free: every subsequent
step has `if: steps.exists.outputs.exists == 'false'`.

### 5.5 `VirusTotal scan` — step id `vt` (lines 57–94)

Gated on `exists == 'false'`. Env: `VT_API_KEY: ${{ secrets.VT_API_KEY }}` and
`FILE: ${{ steps.meta.outputs.file }}`. The **VirusTotal large-file flow**
(installers exceed the 32 MB simple-upload limit, so it uses the special endpoint):

1. `sha=$(sha256sum "$FILE" | cut -d' ' -f1)` and
   `gui="https://www.virustotal.com/gui/file/$sha"` — computed locally regardless
   of whether the API key is present.
2. Default `verdict="scan skipped (no API key configured)"`. If `VT_API_KEY` is
   non-empty:
   - **GET the large-file upload URL:**
     `GET https://www.virustotal.com/api/v3/files/upload_url`, header
     `x-apikey: $VT_API_KEY`; `up=$(... | jq -r .data)`.
   - **POST the file to that URL:** `POST "$up"` with `--form file=@"$FILE"`;
     `aid=$(... | jq -r .data.id)` — the analysis id.
   - Default `verdict="scan inconclusive (timed out)"`, then **poll up to 30×**,
     `sleep 10` each (≈5 min ceiling):
     `GET https://www.virustotal.com/api/v3/analyses/$aid`. When
     `.data.attributes.status == "completed"`, read
     `stats.malicious / suspicious / undetected` and set
     `verdict="$mal malicious, $susp suspicious, $undet clean"`, then `break`.
3. Export `sha256`, `gui`, `verdict` as step outputs.

**Failure tolerance:** no API key → "scan skipped" (release still proceeds);
timeout after 30 polls → "scan inconclusive (timed out)" (release still proceeds).
A non-zero from `curl`/`jq` inside the keyed branch would fail the step (`set -e`),
which fails the job — see [§10.3](#103-ci-fails). For v0.6.0 the verdict was
~4/71 heuristic false-positives (unsigned-binary heuristics).

### 5.6 `Build release notes from CHANGELOG` (lines 96–120)

Gated on `exists == 'false'`. Env passes `VERSION`, `BASE`, `VERDICT`, `GUI`,
`SHA` — **all via `env:`, never interpolated into the shell body** (this is
historical bug B; see [§9B](#9b-do-not-reintroduce-pass-notes-via---notes-file-and-env-never-inline)).

```bash
section=$(awk -v v="$VERSION" '
  $0 ~ ("^## \\[" v "\\]") { p=1; next }   # start printing AFTER the "## [X.Y.Z]" line
  p && /^## \[/ { p=0 }                     # stop at the next "## [" heading
  p { print }
' docs/CHANGELOG.md)
{
  if [ -n "$section" ]; then printf '%s\n' "$section"; else echo "Release v$VERSION."; fi
  echo ""; echo "---"
  printf '**Download:** `%s` - a per-user Windows installer (...).\n' "$BASE"
  echo ""
  printf '**VirusTotal:** %s - [full report](%s)\n' "$VERDICT" "$GUI"
  printf 'SHA-256: `%s`\n' "$SHA"
} > "$RUNNER_TEMP/release-notes.md"
cat "$RUNNER_TEMP/release-notes.md"
```

`awk` slices the body of the `## [X.Y.Z]` section (between that heading and the
next `## [`). If empty, falls back to `Release vX.Y.Z.`. Then appends a `---`
divider, the download line (with the installer filename), the VirusTotal verdict +
report link, and the SHA-256. The whole thing is written to a **file** in
`$RUNNER_TEMP`. The notes use `printf`/`echo` (literal text) — backticks inside
the CHANGELOG body are written to the file verbatim, never executed.

### 5.7 `Create GitHub Release` — the one and only `gh release create` (lines 122–132)

```bash
gh release create "v$VERSION" "$FILE" \
  --title "StripKit $VERSION" \
  --notes-file "$RUNNER_TEMP/release-notes.md"
```

Gated on `exists == 'false'`. Env: `GH_TOKEN: ${{ github.token }}`, `VERSION`,
`FILE`. Creates release **`vX.Y.Z`**, attaches the installer `$FILE` as the
download asset, titles it `StripKit <X.Y.Z>`, and supplies notes **via
`--notes-file`** (never `--notes "<body>"`). **This is the only place in the
entire system that creates a release.**

---

## 6. Secrets

| Secret | Used by | Notes |
| --- | --- | --- |
| `VT_API_KEY` | Stage 2, the `vt` step ([§5.5](#55-virustotal-scan--step-id-vt)) | VirusTotal API key. **Already set** on the repo. If absent/empty, the scan is *skipped* (verdict "scan skipped") and the release still ships — but you lose the report link and the SHA-confirmed scan. |
| `GITHUB_TOKEN` | Stage 2, `exists` + `create` steps | The built-in token (`${{ github.token }}`). Not a stored secret — provided automatically; needs `permissions: contents: write` (set, [§5.1](#51-triggers--permissions)). |

Set or rotate the VirusTotal key:

```bash
gh secret set VT_API_KEY --repo Vybecode-LTD/stripkit
# (paste the key when prompted, or:)
gh secret set VT_API_KEY --repo Vybecode-LTD/stripkit --body "<vt-api-key>"
# verify it exists (value is never shown):
gh secret list --repo Vybecode-LTD/stripkit
```

A free VirusTotal account provides a Public API key (rate-limited; sufficient for
release cadence). Get it from your VirusTotal account → API key.

---

## 7. `.gitignore` negation mechanics

> File: `.gitignore`, lines 23–29. Goal: **track exactly one binary** —
> `releases/latest/*.exe` (the CI trigger) — and ignore every other build output.

```gitignore
publish/                 # (line 23) the self-contained publish — fully ignored
installer/Output/        # (line 24) ISCC's raw output dir — fully ignored
releases/*               # (line 25) ignore everything directly under releases/
!releases/latest/        # (line 26) but DO re-include the latest/ directory itself
releases/latest/*        # (line 27) ignore everything inside latest/ ...
!releases/latest/*.exe   # (line 28) ... except *.exe files (TRACKED)
*.app                    # (line 29) macOS bundle dirs — ignored
```

**Why the ordering matters (git applies rules top-to-bottom; the last match
wins):**

1. `releases/*` ignores `releases/RELEASES`, `releases/*.nupkg`,
   `releases/StripKit-win-Setup.exe`, etc. (the leftover Velopack-era files at the
   top of `releases/` stay ignored — they are **not** part of this pipeline).
2. **But** `releases/*` would also ignore the `releases/latest/` directory — so
   `!releases/latest/` (line 26) **re-includes the directory** (a negation must
   re-include the parent dir before its children can be matched again).
3. `releases/latest/*` then ignores everything *inside* `latest/`.
4. `!releases/latest/*.exe` re-includes only the installer `.exe`. **Last match
   wins**, so the installer is tracked while any other file in `latest/` is not.

> **Critical rule:** a negation (`!`) cannot re-include a file if a parent
> directory is still excluded. That's why both line 26 (`!releases/latest/`) and
> line 28 (`!releases/latest/*.exe`) are needed — and why they must appear **after**
> the broad `releases/*` / `releases/latest/*` excludes. **Reordering or deleting
> any of lines 25–28 breaks the pipeline:** drop line 28 and the trigger file
> stops being committed (no releases ever fire); drop line 25/27 and transient
> junk (Velopack files, logs) gets committed.

**Verified tracked set** (`git ls-files releases/ installer/`): only
`installer/StripKit.iss`, `installer/wizard-large.bmp`, `installer/wizard-small.bmp`,
and `releases/latest/StripKit-Setup-0.6.0-x64.exe`. Everything in `publish/`,
`installer/Output/`, and the rest of `releases/` is correctly ignored.

---

## 8. Runbook — how to cut a release

### 8.0 Pre-flight
1. On `main`, working tree clean (`git status`). The release commit must be clean
   and reproducible.
2. **Accumulate this release's notes under `## [Unreleased]`** in
   `docs/CHANGELOG.md` (the script promotes that section; CI uses it for the
   GitHub Release body).
3. Confirm `gh auth status` shows you authenticated with push access to
   `Vybecode-LTD/stripkit`.

### 8.1 Run Stage 1 locally
```powershell
# first/only-version release (no bump) — how v0.6.0 shipped:
pwsh scripts/Invoke-Release.ps1 -Bump none

# normal cadence:
pwsh scripts/Invoke-Release.ps1                # patch  (+0.0.1)
pwsh scripts/Invoke-Release.ps1 -Bump minor    # +0.1.0  ("major release")
pwsh scripts/Invoke-Release.ps1 -Bump major    # +1.0.0
```
Watch the console for the `=== … ===` step banners. It test-gates, bumps the
three files, publishes, runs ISCC, stages `releases/latest/`, then commits + tags
`vX.Y.Z` + pushes. On success it prints "Pushed vX.Y.Z. GitHub Actions … will now
… create the GitHub Release."

### 8.2 Watch Stage 2 (CI)
```bash
gh run watch                                   # tail the most recent run live
# or list/inspect:
gh run list --workflow "Auto Release" --limit 5
gh run view --log                              # full logs of the latest run
```
Expect, in order: `Identify installer + version` → `Skip if release already
exists` (exists=false) → `VirusTotal scan` (polls, prints a verdict) → `Build
release notes from CHANGELOG` (echoes the notes) → `Create GitHub Release`
("Created release vX.Y.Z").

### 8.3 Verify the release
```bash
gh release view "vX.Y.Z" --repo Vybecode-LTD/stripkit
gh release view "vX.Y.Z" --repo Vybecode-LTD/stripkit --json assets --jq '.assets[].name'
# → StripKit-Setup-X.Y.Z-x64.exe
```

### 8.4 What the website does afterward (Stage 3)
Nothing to deploy. `Vybecode-LTD/StripKit-Website` (`js/download.js`) reads the
latest release from the GitHub API and surfaces the new `.exe` download link
automatically. **Separately**, per `SOFTWARE_RELEASE.md`, add a **plain-language**
entry to the website's `updates.json` (the user-facing changelog, intentionally
decoupled from the technical `docs/CHANGELOG.md`).

---

## 9. Two historical bugs — DO NOT REINTRODUCE

Both bit us once on the way to v0.6.0 and are now fixed. Treat these as hard
invariants when editing the script or the workflow.

### 9A. DO NOT REINTRODUCE — UTF-8 read/write discipline
**Symptom (fixed in `f1b68d3`):** PowerShell 5.1's `Get-Content` (and
`Set-Content`/`Out-File`) **default to ANSI**, not UTF-8. Reading the UTF-8
`docs/CHANGELOG.md` as ANSI mis-decodes multibyte characters; the em-dash (U+2014,
3 bytes in UTF-8) got **double-encoded into mojibake** (e.g. `â€"`) and written
back corrupted.

**The fix, which must stay:**
- **Read** every text file with `Get-Content … -Raw -Encoding UTF8`
  (`Invoke-Release.ps1` lines 87, 91, 95).
- **Write** every text file via the `Save-Text` helper, which uses
  `UTF8Encoding($false)` (no BOM) (lines 47–49).
- The em-dash is a real Unicode char from `[char]0x2014` (line 38) — written
  correctly *only because* the writer is UTF-8.

**Guard:** never swap these for bare `Get-Content` / `Set-Content` / `Out-File`
/ `>` redirection. PS 5.1 will silently revert to ANSI/UTF-16 and re-break the
em-dash. (PowerShell 7 defaults to UTF-8, but this script targets 5.1 and must
not assume the host.)

### 9B. DO NOT REINTRODUCE — pass notes via `--notes-file` and `env:`, never inline
**Symptom (fixed in `a408bc9`):** an earlier workflow built the release with
`gh release create … --notes "${{ <changelog body> }}"`. The CHANGELOG body is
**full of backticks** (inline code like `` `App.axaml` ``). When that body was
interpolated into a bash command line, **bash performed command substitution on
the backticks** — running fragments of the changelog as shell commands — and the
step died (or, worse, could execute arbitrary text).

**The fix, which must stay:**
- Build the notes into a **file**: `… > "$RUNNER_TEMP/release-notes.md"`
  ([§5.6](#56-build-release-notes-from-changelog-lines-96120)).
- Create the release with **`--notes-file "$RUNNER_TEMP/release-notes.md"`**, never
  `--notes "<body>"` ([§5.7](#57-create-github-release--the-one-and-only-gh-release-create-lines-122132)).
- Pass **all** dynamic values to steps via **`env:`** (`VERSION`, `BASE`,
  `VERDICT`, `GUI`, `SHA`, `FILE`) and reference them as `$VAR` inside `run:` —
  **never** interpolate `${{ … }}` directly into the shell body of a `run:` block.
  GitHub expands `${{ }}` *before* bash sees the script, so untrusted/expandable
  content (backticks, `$()`, quotes) becomes live shell. `env:` values arrive as
  inert environment strings.

**Guard:** when adding any new dynamic value to a `run:` step, wire it through
`env:` and reference `$VAR`. When emitting release notes, always go through a file.

---

## 10. Failure recovery per stage

### 10.1 Build fails AFTER a version bump (Stage 1, publish or ISCC step)
The version files (`csproj`, `.iss`, `CHANGELOG`) were **already written** before
publish/ISCC ran (§3.5–3.8), but **nothing was committed or pushed yet** (the git
steps are last, §3.10). Two clean options:

- **Revert the bump, fix, retry:**
  ```powershell
  git checkout -- src/StripKit/StripKit.csproj installer/StripKit.iss docs/CHANGELOG.md
  # fix the build issue, then re-run:
  pwsh scripts/Invoke-Release.ps1 -Bump <same as before>
  ```
- **Keep the already-bumped version, fix, re-run with `-Bump none`** (so it
  doesn't bump *again* on top of the already-bumped files):
  ```powershell
  # fix the build issue, then:
  pwsh scripts/Invoke-Release.ps1 -Bump none
  ```
  (This is exactly what the publish-failure `throw` message recommends.)

### 10.2 ISCC (Inno) fails
- **"ISCC.exe not found"** → install Inno Setup 6, or add your install path to the
  probe array (§3.2).
- **Compile error in the `.iss`** → ISCC prints the offending line. Common causes:
  a missing `..\publish` (publish step didn't run / was cleaned), or a missing
  `installer/wizard-*.bmp`. Fix and re-run (treat as §10.1 — version may be bumped).
- **"Expected installer not produced"** (§3.8 assert) → `MyAppVersion` in the
  `.iss` doesn't match `$new`, or `OutputDir`/`OutputBaseFilename` was changed.
  Reconcile the `.iss` with §4.5/§4.6.

### 10.3 CI fails (Stage 2)
- Open logs: `gh run view --log` (or `gh run watch` live).
- **`vt` step failed** with a key present → a transient VirusTotal/`curl`/`jq`
  error under `set -e`. Re-run the workflow (§10.5). If VirusTotal is down, you
  can temporarily clear `VT_API_KEY` to ship with "scan skipped" — restore it after.
- **`create` step failed** → usually a permissions issue
  (`permissions: contents: write` missing) or the tag/release already partially
  exists. Inspect and either delete the partial release (`gh release delete
  vX.Y.Z --cleanup-tag`) and re-run, or proceed to §10.4.
- The workflow did **not** fire at all → the push didn't change
  `releases/latest/*.exe` (path filter), or you pushed to a non-`main` branch.
  Confirm the staged installer is committed (`git ls-files releases/latest`) and
  on `main`; then re-run via `workflow_dispatch` (§10.5).

### 10.4 A release already exists
This is **not** an error — it's the idempotency design. The `exists` step sets
`exists=true` and every later step is skipped; the run goes green doing nothing.
If you need to **replace** an existing release (e.g. bad notes), do it explicitly:
```bash
gh release delete "vX.Y.Z" --repo Vybecode-LTD/stripkit --cleanup-tag --yes
# (the --cleanup-tag also removes the git tag; re-push the tag if you keep the commit)
git push origin "vX.Y.Z"          # if the commit/tag should remain
gh workflow run "Auto Release"     # re-run; exists is now false → it recreates
```

### 10.5 Re-running via `workflow_dispatch`
The workflow has `workflow_dispatch: {}`, so you can re-run it against whatever
installer is **currently committed** under `releases/latest/` — no new push
needed:
```bash
gh workflow run "Auto Release" --ref main
gh run watch
```
Because Stage 2 is idempotent, this is always safe: it recreates the release only
if one doesn't already exist for that version.

---

## 11. Manual build (debug the package without releasing)

Replays Stage 1's publish + ISCC steps **without** bumping versions, committing,
or pushing — useful for inspecting the installer locally:
```powershell
dotnet publish src/StripKit/StripKit.csproj -c Release -r win-x64 --self-contained true -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer/StripKit.iss
# → installer/Output/StripKit-Setup-0.6.0-x64.exe   (~33.5 MB self-contained)
```
The output lands in `installer/Output/` (git-ignored). It is **not** staged to
`releases/latest/`, so it will not trigger CI. Delete `publish/` and
`installer/Output/` afterward if you like (both are ignored anyway).

---

## 12. Prerequisites (build machine)

- **.NET 9 SDK** — for `dotnet test` and the self-contained `dotnet publish -r
  win-x64`.
- **Inno Setup 6** — provides `ISCC.exe`. On the current dev machine it's a
  **per-user install** at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe` (probed
  first; §3.2). Download from jrsoftware.org.
- **GitHub CLI (`gh`)**, authenticated with push access to
  `Vybecode-LTD/stripkit` (`gh auth status` / `gh auth login`). Used by the
  runbook commands and, in CI, by the release steps.
- **PowerShell** — the script is 5.1-compatible (works under Windows PowerShell
  5.1 and PowerShell 7).
- Repo secret **`VT_API_KEY`** for CI (already set; §6).

---

## 13. How to extend

### 13.1 Add a platform (macOS / Linux)
StripKit is Windows-only today. To add another OS without breaking the
single-creator model:

1. **Build/package per OS.** Add a CI matrix or extra local script that publishes
   the new RID (`osx-arm64`/`osx-x64`, `linux-x64`/`linux-arm64`) and packages it
   (macOS: codesign-hardened `.app` → notarize → DMG, or single-file to simplify
   signing; Linux: AppImage / `.deb` / `.rpm`). On Avalonia, single-file publish
   (`PublishSingleFile=true`) drastically simplifies macOS signing (one Mach-O).
2. **Stage each artifact under `releases/latest/`** with a platform-disambiguated
   name (e.g. `StripKit-Setup-X.Y.Z-x64.exe`, `StripKit-X.Y.Z-arm64.dmg`,
   `StripKit-X.Y.Z-x86_64.AppImage`). **Track them in `.gitignore`** with new
   negations mirroring line 28 (e.g. `!releases/latest/*.dmg`,
   `!releases/latest/*.AppImage`, `!releases/latest/*.deb`).
3. **Generalize the workflow `paths:` filter** if you keep a single workflow
   (`releases/latest/*` instead of `*.exe`), and have CI attach **all** staged
   artifacts to the one release: `gh release create "v$VERSION"
   releases/latest/* --title … --notes-file …`. Keep the version-extraction
   robust (parse it from one canonical filename, or from the csproj/tag).
4. **Preserve idempotency:** still one release per version, still the `exists`
   guard, still `--notes-file`. Do not add a second `gh release create` caller.

### 13.2 Change the bump logic
Edit the `switch ($Bump)` block (`Invoke-Release.ps1` lines 68–72) and/or the
`ValidateSet` (line 31). The new value just needs to produce a valid
`maj.min.pat` string into `$new`; everything downstream (regex replaces, filename,
tag) keys off that string. Keep `none` for the "ship current version / re-run
after a failure" path.

### 13.3 Add code signing (currently shipping unsigned)
Unsigned installers raise SmartScreen on first run and contribute to the VirusTotal
heuristic FPs (~4/71 for v0.6.0). Once an EV/standard code-signing cert exists:

- **Sign the payload before packaging** (in Stage 1, between publish and ISCC):
  ```powershell
  signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 publish\StripKit.exe
  # sign the native DLLs too if the AV heuristics still flag them
  ```
- **Have Inno sign the produced installer** — define a SignTool in the Inno IDE /
  config and reference it from `[Setup]`:
  ```
  ; in installer/StripKit.iss [Setup]:
  SignTool=mysigntool
  ```
  where `mysigntool` is a SignTool command line registered in Inno
  (`SignTool` entries map a name to a `signtool sign … $f` command).
- **In CI**, do signing only in protected workflows with secret-driven cert
  material (e.g. AzureSignTool + a secret-stored cert). Never commit a cert or
  password.
- Document the cert source/thumbprint here once chosen. Re-VirusTotal after the
  first signed build — the FP count should drop.

### 13.4 `actions/checkout@v4` Node-20 deprecation
The workflow pins `actions/checkout@v4` (Node-20 runtime). As GitHub deprecates
Node-20 actions in favor of Node-24, expect a warning annotation on runs. Bump to
the then-current major (`@v5`+) when prompted. It's the only third-party action in
the workflow, so this is a one-line change (line 29).

---

## 14. What ships (summary)

| Artifact | Where | Purpose |
| --- | --- | --- |
| `StripKit-Setup-<X.Y.Z>-x64.exe` | a GitHub Release (asset) **and** `releases/latest/` in-repo (tracked) | The per-user, **self-contained** Windows installer users download and run. No .NET SDK/runtime needed on the target. ~33.5 MB. |

`publish/`, `installer/Output/`, and everything under `releases/` **except**
`releases/latest/*.exe` are git-ignored build outputs (§7). The tracked
`releases/latest/*.exe` is the CI trigger, not a user-download path — users get the
installer from the GitHub Release.
