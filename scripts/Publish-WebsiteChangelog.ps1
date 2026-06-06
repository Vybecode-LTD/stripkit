#requires -Version 5.1
<#
.SYNOPSIS
  Publish a plain-language changelog entry to a forward-facing "download website".
  Project-agnostic: reuse on ANY desktop app whose website follows the pattern below.

.DESCRIPTION
  Stage 3 of the release pipeline. It closes the one manual gap: getting a new version's
  user-facing changelog onto the marketing/download site.

  It assumes two conventions (both used by the VybeCode app + website template):
    1. The APP repo has a Keep-a-Changelog style docs/CHANGELOG.md:
         ## [1.2.0] - 2026-06-06
         ### Added / ### Changed / ### Fixed / ### Security ...
         - **Bold lead-in** - description ...
    2. The WEBSITE repo has an updates.json: a newest-first JSON array of
         { "version", "date", "summary", "changes": [ { "type", "text" } ] }
       rendered client-side, on a host that auto-deploys on push (Railway, Pages, etc.).

  The script DRAFTS an entry by extracting the requested version's section from the app
  CHANGELOG (Added->new, Fixed/Security->fix, everything else->improved), strips build/test
  bookkeeping, prepends it to updates.json (newest first), and validates the JSON. With
  -Push it commits + pushes the website repo, which triggers the host's auto-deploy.

  HYBRID by design: run once to auto-draft, refine the wording in updates.json, then -Push.
  (Or pass -Push on the first run for a fully hands-off, derivative entry.)

.PARAMETER WebsiteRepo   Path to the website repo (contains updates.json). Required.
.PARAMETER AppChangelog  Path to the app's docs/CHANGELOG.md (the drafting source).
.PARAMETER Version       Version to publish. Default: the newest version in AppChangelog.
.PARAMETER UpdatesJson   Path to updates.json. Default: <WebsiteRepo>\updates.json.
.PARAMETER Date          ISO date. Default: the date in the CHANGELOG header, else today.
.PARAMETER Summary       One-line plain-language summary. Default: auto-derived from the first lead-in.
.PARAMETER Commit        git add + commit updates.json in the website repo.
.PARAMETER Push          Implies -Commit; also push (triggers the host auto-deploy).
.PARAMETER DryRun        Print the drafted entry only; write nothing.

.EXAMPLE
  # Hybrid: draft, review, publish.
  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\MyApp-Website -AppChangelog docs\CHANGELOG.md -Version 1.2.0
  #   ...edit ..\MyApp-Website\updates.json to taste...
  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\MyApp-Website -Push

.EXAMPLE
  # One-shot (no review):
  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\MyApp-Website -AppChangelog docs\CHANGELOG.md -Push
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WebsiteRepo,
    [string]$AppChangelog,
    [string]$Version,
    [string]$UpdatesJson,
    [string]$Date,
    [string]$Summary,
    [switch]$Commit,
    [switch]$Push,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
function Fail($m) { throw $m }

if (-not (Test-Path $WebsiteRepo)) { Fail "Website repo not found: $WebsiteRepo" }
if (-not $UpdatesJson) { $UpdatesJson = Join-Path $WebsiteRepo 'updates.json' }
if (-not (Test-Path $UpdatesJson)) { Fail "updates.json not found: $UpdatesJson" }

# ---- helpers ---------------------------------------------------------------
# Quote+escape a string as JSON, then restore literal typographic chars (em-dash etc.)
# so the file stays human-readable. Both forms are valid JSON.
function ConvertTo-JsonText([string]$s) {
    if ($null -eq $s) { return '""' }
    $j = ($s | ConvertTo-Json)
    return [regex]::Replace($j, '\\u([0-9A-Fa-f]{4})', { param($m) [string][char][int]('0x' + $m.Groups[1].Value) })
}

# Collapse whitespace and drop build/test bookkeeping (anywhere) that is never user-facing.
function Format-ChangeText([string]$t) {
    $t = ($t -replace '\s+', ' ').Trim()
    $t = $t -replace '\*\*\+?\d+\s*tests?[^*]*\*\*', ''          # **+14 tests, suite 98->112.**
    $t = $t -replace '\(\s*\+?\d+\s*tests?[^)]*\)', ''           # (+4 tests ...)
    $t = $t -replace '\bbuild\s*0/0\b', ''
    $t = ($t -replace '\s+', ' ').Trim()
    $t = $t -replace '\s+([.,;:])', '$1'                         # tidy space-before-punctuation
    return $t.Trim()
}

function Get-ChangeType([string]$section) {
    $s = $section.ToLower()
    if ($s -match 'add|new') { return 'new' }
    if ($s -match 'fix|security|bug') { return 'fix' }
    return 'improved'
}

# ---- resolve version + extract the CHANGELOG block -------------------------
$changes = @()
$blockDate = $null
if ($AppChangelog -and (Test-Path $AppChangelog)) {
    $cl = Get-Content $AppChangelog -Raw -Encoding UTF8
    if (-not $Version) {
        $mv = [regex]::Match($cl, '(?m)^##\s*\[(?<v>\d+\.\d+\.\d+)\]')
        if ($mv.Success) { $Version = $mv.Groups['v'].Value }
    }
    if ($Version) {
        $pat = '(?ms)^##\s*\[' + [regex]::Escape($Version) + '\][^\r\n]*\r?\n(?<body>.*?)(?=^##\s*\[|\z)'
        $mb = [regex]::Match($cl, $pat)
        if ($mb.Success) {
            $hdr = [regex]::Match($cl, '(?m)^##\s*\[' + [regex]::Escape($Version) + '\][^\r\n]*')
            $md = [regex]::Match($hdr.Value, '(\d{4}-\d{2}-\d{2})')
            if ($md.Success) { $blockDate = $md.Groups[1].Value }
            $type = 'improved'; $cur = $null
            foreach ($line in ($mb.Groups['body'].Value -split "\r?\n")) {
                if ($line -match '^\s*###\s+(.+?)\s*$') {
                    if ($cur) { $changes += @{ type = $type; text = (Format-ChangeText $cur) }; $cur = $null }
                    $type = Get-ChangeType $matches[1]; continue
                }
                if ($line -match '^\s*[-*]\s+(.*)$') {
                    if ($cur) { $changes += @{ type = $type; text = (Format-ChangeText $cur) } }
                    $cur = $matches[1]; continue
                }
                if ($cur -and $line.Trim() -ne '' -and $line -notmatch '^\s*##') { $cur += ' ' + $line.Trim() }
            }
            if ($cur) { $changes += @{ type = $type; text = (Format-ChangeText $cur) } }
        }
    }
}
if (-not $Version) { Fail "No -Version given and none found in '$AppChangelog'." }
# Graceful fallback: never block a release on a parsing miss.
if ($changes.Count -eq 0) {
    $changes = @(@{ type = 'improved'; text = "TODO: describe what's new in $Version (auto-draft found nothing to parse)." })
    Write-Warning "No CHANGELOG bullets parsed for $Version - inserted a TODO stub to refine."
}

if (-not $Date) { if ($blockDate) { $Date = $blockDate } else { $Date = (Get-Date).ToString('yyyy-MM-dd') } }
if (-not $Summary) {
    $lead = [regex]::Match($changes[0].text, '\*\*(.+?)\*\*')
    if ($lead.Success) { $Summary = ($lead.Groups[1].Value.TrimEnd('.', ' ')) + '.' } else { $Summary = "What's new in $Version." }
}

# ---- build the entry text (matches the 2/4/6-space hand-formatted style) ---
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('  {')
[void]$sb.AppendLine('    "version": ' + (ConvertTo-JsonText $Version) + ',')
[void]$sb.AppendLine('    "date": ' + (ConvertTo-JsonText $Date) + ',')
[void]$sb.AppendLine('    "summary": ' + (ConvertTo-JsonText $Summary) + ',')
[void]$sb.AppendLine('    "changes": [')
for ($i = 0; $i -lt $changes.Count; $i++) {
    $comma = ''; if ($i -lt $changes.Count - 1) { $comma = ',' }
    [void]$sb.AppendLine('      { "type": ' + (ConvertTo-JsonText $changes[$i].type) + ', "text": ' + (ConvertTo-JsonText $changes[$i].text) + ' }' + $comma)
}
[void]$sb.AppendLine('    ]')
[void]$sb.Append('  }')
$entry = $sb.ToString()

if ($DryRun) {
    Write-Host "`n--- drafted updates.json entry for v$Version (date $Date) ---`n" -ForegroundColor Cyan
    Write-Host $entry
    Write-Host "`n(DryRun - nothing written. Re-run without -DryRun to insert.)" -ForegroundColor Yellow
    return
}

# ---- insert into updates.json (prepend; idempotent) -----------------------
$raw = Get-Content $UpdatesJson -Raw -Encoding UTF8
$existing = $raw | ConvertFrom-Json
$already = @($existing | Where-Object { $_.version -eq $Version }).Count -gt 0
if ($already) {
    Write-Host "v$Version is already in updates.json - leaving it untouched (refine by hand if needed)." -ForegroundColor Yellow
}
else {
    $idx = $raw.IndexOf('[')
    if ($idx -lt 0) { Fail "updates.json is not a JSON array." }
    $after = $raw.Substring($idx + 1).TrimStart("`r", "`n")
    $newText = $raw.Substring(0, $idx + 1) + "`r`n" + $entry + ",`r`n" + $after
    $null = $newText | ConvertFrom-Json    # validate or throw
    [System.IO.File]::WriteAllText($UpdatesJson, $newText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Inserted v$Version entry into $UpdatesJson" -ForegroundColor Green
}

# ---- optional commit / push (triggers the website host's auto-deploy) -----
if ($Commit -or $Push) {
    $leaf = Split-Path $UpdatesJson -Leaf
    $dirty = git -C $WebsiteRepo status --porcelain -- $leaf
    if (-not $dirty) {
        Write-Host "Nothing to commit in $leaf." -ForegroundColor Yellow
    }
    else {
        git -C $WebsiteRepo add $leaf
        git -C $WebsiteRepo commit -m "content: add v$Version changelog entry"
        if ($LASTEXITCODE -ne 0) { Fail "git commit failed in $WebsiteRepo." }
        if ($Push) {
            git -C $WebsiteRepo push
            if ($LASTEXITCODE -ne 0) { Fail "git push failed in $WebsiteRepo." }
            Write-Host "Pushed - the website host will auto-deploy shortly." -ForegroundColor Green
        }
    }
}
else {
    Write-Host "`nNext (hybrid): review $UpdatesJson, then publish with:" -ForegroundColor Yellow
    Write-Host "  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo `"$WebsiteRepo`" -Version $Version -Push" -ForegroundColor Yellow
}
