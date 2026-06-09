#requires -Version 5.1
<#
.SYNOPSIS
    StripKit local release driver (Stages 1 + 3 of the release pipeline).

.DESCRIPTION
    Run on the developer machine when the owner says "release it". It:
      1. Gates on the test suite (green or it aborts).
      2. Bumps the version in lockstep across StripKit.csproj, the Inno .iss,
         and docs/CHANGELOG.md ([Unreleased] -> [X.Y.Z] - today).
      3. Publishes a self-contained win-x64 build.
      4. Signs StripKit.exe, packages with Inno Setup, signs the installer.
      5. Stages under releases/latest/ and commits + tags (vX.Y.Z) + pushes.
      6. (Stage 3) Auto-discovers the sibling ..\StripKit-Website repo and
         commits + pushes a plain-language website changelog entry (triggers
         Railway auto-deploy).  Pass -DraftWebsiteOnly to write the entry
         locally without pushing, for manual review first.

    It does NOT create the GitHub Release. Pushing the installer under
    releases/latest/ triggers .github/workflows/auto-release.yml, which is the
    single, race-free release creator (it also runs the VirusTotal scan).

.PARAMETER Bump
    none  -> release the current version as-is (use for the first release).
    patch -> +0.0.1  (default; the meaning of a plain "release it")
    minor -> +0.1.0
    major -> +1.0.0

.EXAMPLE
    powershell -File scripts/Invoke-Release.ps1 -Bump minor                # release + push website automatically
    powershell -File scripts/Invoke-Release.ps1 -Bump minor -DraftWebsiteOnly  # release + draft website, push manually
    powershell -File scripts/Invoke-Release.ps1 -Bump patch -SkipWebsite   # skip Stage 3 entirely
#>
[CmdletBinding()]
param(
    [ValidateSet('none', 'patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [switch]$SkipTests,
    # Stage 3 — website changelog. Auto-discovered from the sibling ..\StripKit-Website repo
    # if it exists. Override the path with -WebsiteRepo. Suppress entirely with -SkipWebsite.
    [string]$WebsiteRepo = '',
    # Skip the auto-push and only write a draft entry into updates.json for manual review.
    # By default, the sibling website repo is found and the entry is committed + pushed automatically.
    [switch]$DraftWebsiteOnly,
    [switch]$SkipWebsite
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$emdash = [char]0x2014

# ── Azure Trusted Signing ─────────────────────────────────────────────────────
# Code signing via Azure Trusted Signing. Uses Windows SDK signtool.exe + the
# Microsoft.Trusted.Signing.Client NuGet dlib (NOT AzureSignTool, which only
# supports Key Vault and returns 403 against Trusted Signing endpoints).
# Both binaries are signed: StripKit.exe (before ISCC) and the installer (after
# ISCC). Inno's SignTool= directive cannot invoke this command, so signing is
# done externally in this script.
#
# Prerequisites (one-time per machine):
#   1. Windows SDK installed            (provides signtool.exe)
#   2. Microsoft.Trusted.Signing.Client (NuGet; provides Azure.CodeSigning.Dlib.dll)
#   3. trusted-signing-metadata.json    (repo root; checked in — no secrets)
#   4. az login                         (before each release session)
#   5. "Trusted Signing Certificate Profile Signer" RBAC role on the profile
$atsTimestamp = 'http://timestamp.acs.microsoft.com'
$atsMetadata  = Join-Path $root 'trusted-signing-metadata.json'

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

function Find-SignTool {
    # Search Windows SDK installations for signtool.exe (newest SDK version first)
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $sdkRoot) {
        $found = Get-ChildItem $sdkRoot -Directory |
            Where-Object { $_.Name -match '^\d+\.' } |
            Sort-Object { [version]($_.Name) } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($found) { return $found }
    }
    # Fallback: check PATH
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "signtool.exe not found. Install the Windows 10/11 SDK (winget install Microsoft.WindowsSDK.10.0.26100)."
}

function Find-TrustedSigningDlib {
    # Search the NuGet global-packages folder for the Trusted Signing Client dlib
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.trusted.signing.client'
    if (-not (Test-Path $nugetRoot)) {
        throw "Microsoft.Trusted.Signing.Client NuGet package not found.`nInstall it once — see SOFTWARE_RELEASE_AUTOMATION.md 'Machine setup'."
    }
    $found = Get-ChildItem $nugetRoot -Directory |
        Sort-Object { try { [version]$_.Name } catch { [version]'0.0.0' } } -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin\x64\Azure.CodeSigning.Dlib.dll' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $found) {
        throw "Azure.CodeSigning.Dlib.dll not found under $nugetRoot.`nRe-install the Microsoft.Trusted.Signing.Client NuGet package."
    }
    return $found
}

function Invoke-TrustedSign([string[]]$Files) {
    $signtool = Find-SignTool
    $dlib     = Find-TrustedSigningDlib
    if (-not (Test-Path $atsMetadata)) {
        throw "Metadata file not found: $atsMetadata`nCreate trusted-signing-metadata.json in the repo root with Endpoint, CodeSigningAccountName, and CertificateProfileName."
    }
    foreach ($f in $Files) {
        Write-Host "  Signing: $(Split-Path $f -Leaf)" -ForegroundColor Gray
        & $signtool sign /v /fd SHA256 /tr $atsTimestamp /td SHA256 `
            /dlib $dlib /dmdf $atsMetadata $f
        if ($LASTEXITCODE -ne 0) {
            throw "Signing failed for $f.`n  * Are you logged in?  Run: az login`n  * Does your account have the 'Trusted Signing Certificate Profile Signer' role?"
        }
    }
}

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

# --- Sign the app binary BEFORE packaging -----------------------------------
# Sign StripKit.exe so the binary inside the installer is already signed.
# The installer itself is signed AFTER ISCC below (Inno can't invoke Trusted Signing).
Step "Signing StripKit.exe (Azure Trusted Signing)"
Invoke-TrustedSign @((Join-Path $publishDir 'StripKit.exe'))

# --- Package with Inno Setup ------------------------------------------------
Step "Packaging installer with Inno Setup"
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }
$installer = Join-Path $issOutDir "StripKit-Setup-$new-x64.exe"
if (-not (Test-Path $installer)) { throw "Expected installer not produced: $installer" }

# --- Sign the installer AFTER packaging ------------------------------------
Step "Signing installer (Azure Trusted Signing)"
Invoke-TrustedSign @($installer)

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
Write-Host ""

# --- Stage 3: website changelog (auto-discovered or explicit) ----------------
if (-not $SkipWebsite -and -not $WebsiteRepo) {
    $sibling = Join-Path (Split-Path $root -Parent) 'StripKit-Website'
    if (Test-Path $sibling) { $WebsiteRepo = $sibling }
}

if ($WebsiteRepo -and -not $SkipWebsite) {
    Step "Stage 3 - website changelog entry for v$new"
    $pubArgs = @('-WebsiteRepo', $WebsiteRepo, '-AppChangelog', $changelog, '-Version', $new)
    if (-not $DraftWebsiteOnly) { $pubArgs += '-Push' }
    & (Join-Path $PSScriptRoot 'Publish-WebsiteChangelog.ps1') @pubArgs
    if ($DraftWebsiteOnly) {
        Write-Host ""
        Write-Host "Entry DRAFTED (not pushed). Review $WebsiteRepo\updates.json, then publish:" -ForegroundColor Yellow
        Write-Host "  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo `"$WebsiteRepo`" -Version $new -Push" -ForegroundColor Yellow
    }
} else {
    Write-Host "──────────────────────────────────────────────────────────────────" -ForegroundColor Yellow
    Write-Host " REMINDER: add a plain-language entry to the WEBSITE changelog!"   -ForegroundColor Yellow
    Write-Host ""                                                                   -ForegroundColor Yellow
    Write-Host "  File : StripKit-Website/updates.json  (sibling repo)"            -ForegroundColor Yellow
    Write-Host "  Add  : a new entry for v$new at the TOP of the array."           -ForegroundColor Yellow
    Write-Host "  Shape: { version, date, summary, changes:[{type,text}] }"        -ForegroundColor Yellow
    Write-Host "  Types: new | improved | fix"                                      -ForegroundColor Yellow
    Write-Host ""                                                                   -ForegroundColor Yellow
    Write-Host "  The download button updates automatically from GitHub Releases;"  -ForegroundColor Yellow
    Write-Host "  the changelog section ONLY updates when you edit updates.json."  -ForegroundColor Yellow
    Write-Host "──────────────────────────────────────────────────────────────────" -ForegroundColor Yellow
}
