#requires -Version 5.1
<#
.SYNOPSIS
    StripKit local release driver (Stage 1 of SOFTWARE_RELEASE.md).

.DESCRIPTION
    Run on the developer machine when the owner says "release it". It:
      1. Gates on the test suite (green or it aborts).
      2. Bumps the version in lockstep across StripKit.csproj, the Inno .iss,
         and docs/CHANGELOG.md ([Unreleased] -> [X.Y.Z] - today).
      3. Publishes a self-contained win-x64 build.
      4. Packages it with Inno Setup into releases/latest/StripKit-Setup-X.Y.Z-x64.exe.
      5. Commits, tags (vX.Y.Z), and pushes.

    It does NOT create the GitHub Release. Pushing the installer under
    releases/latest/ triggers .github/workflows/auto-release.yml, which is the
    single, race-free release creator (it also runs the VirusTotal scan).

.PARAMETER Bump
    none  -> release the current version as-is (use for the first release).
    patch -> +0.0.1  (default; the meaning of a plain "release it")
    minor -> +0.1.0  ("major release")
    major -> +1.0.0

.EXAMPLE
    pwsh scripts/Invoke-Release.ps1 -Bump none      # first release, ships 0.6.0
    pwsh scripts/Invoke-Release.ps1                 # patch bump
#>
[CmdletBinding()]
param(
    [ValidateSet('none', 'patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$emdash = [char]0x2014

$csproj         = Join-Path $root 'src\StripKit\StripKit.csproj'
$iss            = Join-Path $root 'installer\StripKit.iss'
$changelog      = Join-Path $root 'docs\CHANGELOG.md'
$publishDir     = Join-Path $root 'publish'
$issOutDir      = Join-Path $root 'installer\Output'
$releasesLatest = Join-Path $root 'releases\latest'

function Save-Text($path, $text) {
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# --- Locate the Inno Setup compiler (handles the per-user install) ----------
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found. Install Inno Setup 6 or fix the path in this script." }

# --- Read current + compute new version -------------------------------------
[xml]$xml = Get-Content $csproj
$verNode = $xml.SelectNodes('//Version') | Select-Object -First 1
if (-not $verNode) { throw "<Version> not found in $csproj" }
$cur = $verNode.InnerText.Trim()
$parts = $cur.Split('.')
$maj = [int]$parts[0]; $min = [int]$parts[1]; $pat = [int]$parts[2]
switch ($Bump) {
    'patch' { $pat++ }
    'minor' { $min++; $pat = 0 }
    'major' { $maj++; $min = 0; $pat = 0 }
}
$new = "$maj.$min.$pat"
$date = (Get-Date).ToString('yyyy-MM-dd')
Step "Releasing v$new  (was v$cur, bump=$Bump)  $date"

# --- Test gate --------------------------------------------------------------
if (-not $SkipTests) {
    Step "Running test suite (release gate)"
    dotnet test (Join-Path $root 'StripKit.sln') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed - aborting before any version bump." }
}

# --- Bump versions ----------------------------------------------------------
Step "Bumping version to $new across csproj / .iss / CHANGELOG"

$csprojText = Get-Content $csproj -Raw -Encoding UTF8
$csprojText = [regex]::Replace($csprojText, '(<Version>)[^<]+(</Version>)', "`${1}$new`${2}")
Save-Text $csproj $csprojText

$issText = Get-Content $iss -Raw -Encoding UTF8
$issText = [regex]::Replace($issText, '(#define MyAppVersion ")[^"]+(")', "`${1}$new`${2}")
Save-Text $iss $issText

$clText = Get-Content $changelog -Raw -Encoding UTF8
$header = "## [$new] $emdash $date"
if ($clText -match '(?m)^##\s*\[Unreleased\].*$') {
    $clText = [regex]::Replace($clText, '(?m)^##\s*\[Unreleased\].*$', $header, 1)
} else {
    Write-Warning "No [Unreleased] section in CHANGELOG; inserting a stub for $new."
    $clText = [regex]::Replace($clText, '(?m)^(#\s+.*Changelog.*$)',
        "`$1`r`n`r`n$header`r`n`r`n- **Release $new.**", 1)
}
Save-Text $changelog $clText

# --- Publish (self-contained win-x64) ---------------------------------------
Step "Publishing self-contained win-x64 build"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed (version files already bumped to $new - re-run with -Bump none after fixing, or 'git checkout' the version files)." }

# --- Package with Inno Setup ------------------------------------------------
Step "Packaging installer with Inno Setup"
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }
$installer = Join-Path $issOutDir "StripKit-Setup-$new-x64.exe"
if (-not (Test-Path $installer)) { throw "Expected installer not produced: $installer" }

# --- Stage under releases/latest (the CI trigger path) ----------------------
Step "Staging installer under releases/latest/"
New-Item -ItemType Directory -Force -Path $releasesLatest | Out-Null
Get-ChildItem (Join-Path $releasesLatest '*.exe') -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item $installer $releasesLatest -Force
$sizeMB = [math]::Round((Get-Item $installer).Length / 1MB, 1)
Write-Host "  -> releases/latest/StripKit-Setup-$new-x64.exe ($sizeMB MB)"

# --- Commit, tag, push ------------------------------------------------------
Step "Committing, tagging v$new, and pushing"
git -C $root add src/StripKit/StripKit.csproj installer/StripKit.iss docs/CHANGELOG.md releases/latest
Write-Host "Staged for the release commit:" -ForegroundColor Yellow
git -C $root status --short
git -C $root commit -m "Release v$new"
git -C $root tag "v$new"
git -C $root push
git -C $root push origin "v$new"

Step "Done"
Write-Host "Pushed v$new. GitHub Actions (auto-release.yml) will now VirusTotal-scan the"
Write-Host "installer and create the GitHub Release. Watch it with:  gh run watch"
