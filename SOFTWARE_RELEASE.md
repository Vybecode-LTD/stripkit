# SOFTWARE RELEASE — Desktop Download-App Directive

> Reusable release directive. `@include` this from a project's CLAUDE.md.
> **`ONLY_IF_DESKTOP_DOWNLOAD_APP → follow this directive.`**

**APPLIES TO:** Desktop apps distributed as a downloadable installer (.exe / .msi /
.dmg / .pkg / AppImage / .deb). **DOES NOT APPLY TO:** web apps, services, or libraries.

This document is binding when included. Its purpose: ship every desktop release through
**one race-free, automated pipeline** so that the version bump, installer, GitHub Release,
malware scan, and live website all stay in sync with **zero manual steps** after the trigger.

---

## Architecture overview

```
"release it"
  └─ STAGE 1 — Local build script (runs on your machine)
       → bump version in ALL version-bearing files
       → build release binary → package installer
       → update CHANGELOG.md → commit + tag + push
       → does NOT create a GitHub Release
            ↓ (the pushed installer triggers CI)
     STAGE 2 — GitHub Actions workflow (runs in the cloud)
       → detect new installer → extract version
       → malware scan (VirusTotal) → create GitHub Release
       → attach installer + scan link in release notes
            ↓
     STAGE 3 — Website (passive, reads from GitHub)
       → download.js fetches /releases/latest → updates button + version
       → changelog.js fetches CHANGELOG.md → renders update log
```

**One release creator.** Only the CI workflow creates the GitHub Release. The local script
never does. This eliminates the race condition where both try to create it.

---

## SETUP GUIDE — How to implement this from scratch

### Prerequisites

- A GitHub repo for your app
- A website repo (separate) with a landing page and download button
- A VirusTotal account (free) — get an API key at https://www.virustotal.com
- The project uses semantic versioning (e.g., 1.0.0, 1.0.1, 1.1.0)

---

### STAGE 1 SETUP — Local release script

Create a release script in your app repo at `scripts/Invoke-Release.ps1` (Windows) or
`scripts/release.sh` (macOS/Linux). The script must do these steps IN ORDER:

#### Step 1.1: Determine the new version

Read the current version from your project file, increment it, and compute the new version.

- **Minor release** (default for "release it"): `1.0.0 → 1.0.1`
- **Major release** (when user says "major release"): `1.0.0 → 1.1.0`

#### Step 1.2: Bump version in ALL version-bearing files

**Every file that contains the version string must be updated in one step.** They must
never drift. Common files that need bumping:

| Platform | Files to bump |
|---|---|
| C# / .NET | `*.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`) |
| Inno Setup | `*.iss` (`#define MyAppVersion`) |
| Node.js | `package.json` (`"version"`) |
| Electron | `package.json` + `electron-builder.yml` |
| Python | `pyproject.toml` or `setup.py` or `__version__` |
| C++ / CMake | `CMakeLists.txt` (`project(... VERSION ...)`) |
| macOS | `Info.plist` (`CFBundleShortVersionString`, `CFBundleVersion`) |

**The script must update ALL of these atomically.** If the app says 1.2.0 and the
installer says 1.1.0, the release is broken.

#### Step 1.3: Build a release binary

Run the platform's build command in Release/production mode:

```powershell
# .NET example
dotnet publish -c Release -r win-x64 --self-contained

# Node/Electron example
npm run build

# C++ example
cmake --build build --config Release
```

#### Step 1.4: Package the installer

Run the installer toolchain:

```powershell
# Inno Setup (Windows)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\MyAppSetup.iss

# Electron
npx electron-builder --win

# macOS
create-dmg ...
```

#### Step 1.5: Copy installer to a fixed watched location

The CI workflow watches a specific path for new installers. Copy the output there:

```powershell
# Create the folder if it doesn't exist
New-Item -ItemType Directory -Force -Path "releases/latest"

# Remove old installer(s)
Remove-Item "releases/latest/*.exe" -Force -ErrorAction SilentlyContinue

# Copy new installer (filename includes version)
Copy-Item "installer/Output/MyAppSetup-$Version-x64.exe" "releases/latest/"
```

**Important:** The installer filename MUST contain the version number in the pattern
`X.Y.Z` (e.g., `MyAppSetup-1.2.0-x64.exe`). The CI workflow extracts the version
from the filename using regex.

#### Step 1.6: Update CHANGELOG.md

The CHANGELOG must follow the [Keep a Changelog](https://keepachangelog.com/) format.
The script should:

1. If an `[Unreleased]` section exists, rename it to `[X.Y.Z] — YYYY-MM-DD`
2. Add the version link at the bottom of the file
3. Ensure each bullet under `### Added`, `### Fixed`, `### Changed` starts with
   `**Bold title**` followed by a description (required for website rendering)

Example format the website parser expects:

```markdown
## [1.2.0] — 2026-06-15

### Added
- **New feature name.** Description of what it does and why it matters.
- **Another feature.** More details here.

### Fixed
- **Bug description.** What was wrong and how it was fixed.

### Changed
- **What changed.** How behavior differs from before.
```

**Critical:** Each bullet MUST start with `- **Bold text.**` or `- **Bold text**`
followed by a description. If the bold part is missing, the website's changelog
renderer will show an empty title column.

#### Step 1.7: Commit, tag, and push

```powershell
git add -A
git commit -m "Release vX.Y.Z"
git tag "vX.Y.Z"
git push
git push --tags
```

#### Step 1.8: DO NOT create a GitHub Release

**This is critical.** The local script must NEVER run `gh release create` or any
equivalent. That is the CI workflow's job exclusively. If both the script and CI
create releases, they race — the loser errors out, and the VirusTotal scan silently
dies because the CI skips when the release already exists.

**If your script currently has `gh release create`, remove it now.**

---

### STAGE 2 SETUP — GitHub Actions workflow

Create the file `.github/workflows/auto-release.yml` in your app repo.

#### Step 2.1: Set repository secrets

Go to your GitHub repo → Settings → Secrets and variables → Actions → New repository secret:

| Secret name | Value | Where to get it |
|---|---|---|
| `VT_API_KEY` | Your VirusTotal API key | https://www.virustotal.com → Profile → API Key |

Note: `GITHUB_TOKEN` is provided automatically by GitHub Actions — you don't need to create it.

#### Step 2.2: Create the workflow file

Create `.github/workflows/auto-release.yml` with this content. **Customize the
values marked with `CUSTOMIZE`:**

```yaml
name: Auto-create release from installer

on:
  push:
    branches: [main]                    # CUSTOMIZE: your default branch
    paths:
      - 'releases/latest/*.exe'         # CUSTOMIZE: match your installer extension

jobs:
  create-release:
    runs-on: ubuntu-latest
    permissions:
      contents: write

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Find installer and extract version
        id: info
        run: |
          EXE=$(ls releases/latest/*.exe 2>/dev/null | head -1)
          if [ -z "$EXE" ]; then
            echo "No installer found in releases/latest/"
            exit 1
          fi
          FILENAME=$(basename "$EXE")
          VERSION=$(echo "$FILENAME" | grep -oP '\d+\.\d+\.\d+')
          if [ -z "$VERSION" ]; then
            echo "Could not extract version from $FILENAME"
            exit 1
          fi
          echo "exe=$EXE" >> "$GITHUB_OUTPUT"
          echo "filename=$FILENAME" >> "$GITHUB_OUTPUT"
          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
          echo "tag=v$VERSION" >> "$GITHUB_OUTPUT"

      - name: Check if release already exists
        id: check
        run: |
          TAG="${{ steps.info.outputs.tag }}"
          if gh release view "$TAG" > /dev/null 2>&1; then
            echo "exists=true" >> "$GITHUB_OUTPUT"
          else
            echo "exists=false" >> "$GITHUB_OUTPUT"
          fi
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract changelog section
        if: steps.check.outputs.exists == 'false'
        id: notes
        run: |
          VERSION="${{ steps.info.outputs.version }}"
          # Extract the section for this version from CHANGELOG.md
          SECTION=$(awk -v ver="$VERSION" '
            /^## \[/ { if (found) exit; if (index($0, ver)) found=1 }
            found { print }
          ' CHANGELOG.md)
          if [ -z "$SECTION" ]; then
            SECTION="Release v$VERSION"
          fi
          # Write to a temp file to avoid quoting issues
          echo "$SECTION" > /tmp/release_notes.md

      - name: VirusTotal scan
        if: steps.check.outputs.exists == 'false'
        id: vtscan
        run: |
          EXE="${{ steps.info.outputs.exe }}"
          VT_KEY="${{ secrets.VT_API_KEY }}"
          if [ -z "$VT_KEY" ]; then
            echo "vt_url=" >> "$GITHUB_OUTPUT"
            exit 0
          fi
          # Get upload URL for large files (>32MB)
          UPLOAD_URL=$(curl -s --request GET \
            --url https://www.virustotal.com/api/v3/files/upload_url \
            --header "x-apikey: $VT_KEY" | jq -r '.data // empty')
          if [ -z "$UPLOAD_URL" ]; then
            UPLOAD_URL="https://www.virustotal.com/api/v3/files"
          fi
          RESPONSE=$(curl -s --request POST \
            --url "$UPLOAD_URL" \
            --header "x-apikey: $VT_KEY" \
            --form "file=@$EXE")
          ANALYSIS_ID=$(echo "$RESPONSE" | jq -r '.data.id // empty')
          SHA256=$(sha256sum "$EXE" | cut -d' ' -f1)
          if [ -n "$ANALYSIS_ID" ]; then
            echo "vt_url=https://www.virustotal.com/gui/file/$SHA256" >> "$GITHUB_OUTPUT"
          else
            echo "vt_url=" >> "$GITHUB_OUTPUT"
          fi
          echo "sha256=$SHA256" >> "$GITHUB_OUTPUT"

      - name: Create GitHub Release
        if: steps.check.outputs.exists == 'false'
        run: |
          TAG="${{ steps.info.outputs.tag }}"
          EXE="${{ steps.info.outputs.exe }}"
          VT_URL="${{ steps.vtscan.outputs.vt_url }}"
          SHA256="${{ steps.vtscan.outputs.sha256 }}"

          # Append footer to release notes
          echo "" >> /tmp/release_notes.md
          echo "---" >> /tmp/release_notes.md
          echo "**Download:** ${{ steps.info.outputs.filename }}" >> /tmp/release_notes.md
          echo "**Platform:** Windows 10/11 (x64)" >> /tmp/release_notes.md
          if [ -n "$VT_URL" ]; then
            echo "**VirusTotal:** [$SHA256]($VT_URL)" >> /tmp/release_notes.md
          fi

          gh release create "$TAG" "$EXE" \
            --title "$(echo $TAG | sed 's/^v//' | xargs -I{} echo 'CUSTOMIZE_APP_NAME v{}')" \
            --notes-file /tmp/release_notes.md \
            --latest
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**CUSTOMIZE these values in the workflow:**
- `branches: [main]` — your default branch name
- `'releases/latest/*.exe'` — your installer file extension (.exe, .dmg, .pkg, etc.)
- `CUSTOMIZE_APP_NAME` in the release title — your app's display name

---

### STAGE 3 SETUP — Website (passive consumer)

The website reads from your app's GitHub repo on every page load. No CI pushes to the
website — it pulls live data.

#### Step 3.1: Download link script (`js/download.js`)

This script fetches the latest GitHub Release and updates the download button:

```javascript
(function () {
  var REPO = 'YOUR_ORG/YOUR_APP_REPO';  // CUSTOMIZE
  var API  = 'https://api.github.com/repos/' + REPO + '/releases/latest';
  var CACHE_KEY  = 'app_latest_release';
  var CACHE_TTL  = 300000; // 5 minutes

  function applyRelease(data) {
    var tag     = data.tag_name || '';
    var version = tag.replace(/^v/i, '');
    var asset   = null;

    // Find the installer asset (CUSTOMIZE the extension)
    for (var i = 0; i < (data.assets || []).length; i++) {
      var a = data.assets[i];
      if (a.name && a.name.toLowerCase().indexOf('.exe') !== -1) {
        asset = a;
        break;
      }
    }
    if (!asset) return;

    // Update download button href
    var btn = document.getElementById('download-btn');
    if (btn) btn.href = asset.browser_download_url;

    // Update version display
    var ver = document.getElementById('download-version');
    if (ver && version) ver.innerHTML = 'v' + version + ' &middot; Windows 10/11';

    // Update file size
    if (asset.size) {
      var mb = (asset.size / (1024 * 1024)).toFixed(0);
      var stats = document.querySelectorAll('.hero-stat-value');
      for (var j = 0; j < stats.length; j++) {
        if (stats[j].textContent.indexOf('MB') !== -1) {
          stats[j].textContent = mb + ' MB';
        }
      }
    }

    // Update VirusTotal badge link from release body
    var vtLink = document.getElementById('vt-link');
    if (vtLink && data.body) {
      var vtMatch = data.body.match(
        /https:\/\/www\.virustotal\.com\/gui\/file\/[a-f0-9]+/);
      if (vtMatch) vtLink.href = vtMatch[0];
    }
  }

  function fetchLatest() {
    try {
      var cached = sessionStorage.getItem(CACHE_KEY);
      if (cached) {
        var parsed = JSON.parse(cached);
        if (Date.now() - parsed.ts < CACHE_TTL) {
          applyRelease(parsed.data);
          return;
        }
      }
    } catch (e) {}

    fetch(API)
      .then(function (res) { return res.ok ? res.json() : Promise.reject(); })
      .then(function (data) {
        applyRelease(data);
        try {
          sessionStorage.setItem(CACHE_KEY,
            JSON.stringify({ ts: Date.now(), data: data }));
        } catch (e) {}
      })
      .catch(function () {});
  }

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', fetchLatest);
  else fetchLatest();
})();
```

#### Step 3.2: Changelog script (`js/changelog.js`)

This script fetches `CHANGELOG.md` from the app repo and renders it:

```javascript
(function () {
  var REPO = 'YOUR_ORG/YOUR_APP_REPO';  // CUSTOMIZE
  var RAW_URL = 'https://raw.githubusercontent.com/' + REPO + '/main/CHANGELOG.md';
  var CACHE_KEY = 'app_changelog';
  var CACHE_TTL = 300000; // 5 minutes
  var MAX_HOME = 3; // max entries on landing page (full page shows all)

  // Parser: extracts version blocks from Keep a Changelog format
  function parseChangelog(md) { /* see reference implementation */ }

  // Renderer: builds timeline HTML from parsed versions
  function renderTimeline(versions, container, limit) { /* see reference */ }

  // Entry point: fetch, parse, render
  function fetchChangelog() {
    var isFullPage = !!document.getElementById('changelog-full');
    // ... cache check, fetch, parse, render (see reference implementation)
  }

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', fetchChangelog);
  else fetchChangelog();
})();
```

#### Step 3.3: Required HTML elements

The website needs these elements for the scripts to target:

```html
<!-- Download button (download.js targets this) -->
<a href="#" class="btn" id="download-btn">Download for Windows</a>

<!-- Version display (download.js targets this) -->
<div id="download-version">v1.0.0 &middot; Windows 10/11</div>

<!-- VirusTotal badge (download.js targets this) -->
<a href="#" id="vt-link">VirusTotal Verified</a>

<!-- Changelog timeline (changelog.js targets this on landing page) -->
<div id="changelog-timeline">Loading...</div>

<!-- Changelog full (changelog.js targets this on dedicated changelog page) -->
<div id="changelog-full">Loading...</div>
```

---

## CORE RULES (non-negotiable)

1. **Exactly ONE release creator.** Only the CI workflow creates the GitHub Release.
   The local script NEVER does. If both try, they race — the loser errors out and the
   VirusTotal scan silently dies. **Remove `gh release create` from local scripts.**

2. **Version synchronized in one step.** Every version-bearing file is updated
   atomically. App says 1.2.0, installer says 1.2.0, CHANGELOG says 1.2.0.

3. **CHANGELOG-first.** The new version's entry is committed BEFORE the CI runs.
   The workflow slices that section into the release notes.

4. **CHANGELOG format matters.** Each bullet must start with `- **Bold title.**`
   followed by a description. Without the bold lead, the website renders an empty
   title column.

5. **SemVer increments.** "release it" = patch (+0.0.1). "major release" = minor
   (+0.1.0). Keep it fixed and documented per project.

6. **Pre-release gate.** Tests green. No running instances locking build outputs.
   Clean release build. Don't release red.

7. **Installer filename contains version.** The CI extracts the version from the
   filename using `grep -oP '\d+\.\d+\.\d+'`. If the filename doesn't match this
   pattern, the workflow fails.

---

## The `"release it"` key phrase

When the user says **"release it"**:

1. Confirm: ask what changed (or read `[Unreleased]` in CHANGELOG if it exists).
2. Determine version bump: patch by default, minor if user says "major".
3. Promote `[Unreleased]` → `## [X.Y.Z] — YYYY-MM-DD` in CHANGELOG.
4. Run the project's release script (Stage 1).
5. Wait for CI to create the release (Stage 2) — verify it actually ran.
6. Confirm the website updated (Stage 3) — check the download button and changelog.

## Verify (evidence, not assumption)

After triggering, confirm all three stages:

| Check | How |
|---|---|
| CI ran and succeeded | `gh run list --repo ORG/REPO --limit 1` |
| Release was created by CI | `gh release view vX.Y.Z --repo ORG/REPO` |
| Installer is attached | Check assets in the release |
| VirusTotal link in notes | Check the release body for `virustotal.com/gui/file/` |
| Release marked latest | `gh release list --repo ORG/REPO --limit 1` shows "Latest" |
| Website shows new version | Visit the site (or clear sessionStorage and reload) |
| Changelog updated on site | Check the update log section |

---

## Reference implementation — KaptureVault (Windows / Avalonia)

**App repo:** `Vybecode-LTD/KaptureVault`
**Website repo:** `Vybecode-LTD/Kapture.Tools-Website`
**Website URL:** `https://kapture.tools`

| Component | Location | Notes |
|---|---|---|
| Release script | `scripts/Invoke-Release.ps1` | Bumps `.csproj` + `.iss`, builds, Inno Setup, copies to `releases/latest/`, updates CHANGELOG, commit + tag + push. **No `gh release create`.** |
| CI workflow | `.github/workflows/auto-release.yml` | Triggers on `releases/latest/*.exe`. VirusTotal scan (handles >32MB via upload_url endpoint). Creates Release with CHANGELOG section + footer. |
| Download script | `js/download.js` | Reads `/releases/latest`, updates button href + version + size + VT badge. 5-min cache. |
| Changelog script | `js/changelog.js` | Reads `CHANGELOG.md` raw, parses Keep a Changelog format, renders timeline. 3 entries on landing page, full on `/changelog.html`. 5-min cache. |
| VT API key secret | Repo → Settings → Secrets → `VT_API_KEY` | VirusTotal API key |

**Gotcha log** (mistakes made and fixed during KaptureVault setup):
- Local script had `gh release create` → caused race with CI → VT scan was dead code → fixed by removing it from the script
- VirusTotal upload failed for >32MB files → fixed by fetching `/files/upload_url` endpoint first
- CHANGELOG bullets without `**bold lead**` → website rendered empty title column → fixed by requiring bold format
- Website showed stale data → sessionStorage cached for 1 hour → reduced to 5 minutes
- CI workflow and local script both creating releases → one always errors → fixed by making CI the single owner

---

## Adapting for a new project (checklist)

When setting up this pipeline for a new project, follow this checklist:

- [ ] **Stage 1:** Create `scripts/Invoke-Release.ps1` (or `.sh`) that bumps version,
      builds, packages, copies installer to `releases/latest/`, updates CHANGELOG,
      commits + tags + pushes. **No `gh release create`.**
- [ ] **Stage 1:** Ensure installer filename contains version number (e.g., `MyApp-1.0.0-x64.exe`)
- [ ] **Stage 1:** Ensure CHANGELOG.md exists and follows Keep a Changelog format
- [ ] **Stage 1:** Ensure every bullet starts with `- **Bold text.**` description
- [ ] **Stage 2:** Create `.github/workflows/auto-release.yml` (copy template above, customize)
- [ ] **Stage 2:** Set `VT_API_KEY` repo secret (Settings → Secrets → Actions)
- [ ] **Stage 2:** Test by pushing a dummy installer and verifying the workflow runs
- [ ] **Stage 3:** Add `js/download.js` to website with correct `REPO` value
- [ ] **Stage 3:** Add `js/changelog.js` to website with correct `REPO` value
- [ ] **Stage 3:** Add required HTML element IDs (`download-btn`, `download-version`, `vt-link`,
      `changelog-timeline`)
- [ ] **Stage 3:** Verify website updates after a release
- [ ] **Verify:** Run `"release it"` end-to-end and confirm all three stages complete
