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
         ### Website (optional plain-language bullets — used first if present)
         ### Added / ### Changed / ### Fixed / ### Security ...
         - **Bold lead-in** - description ...
    2. The WEBSITE repo has an updates.json: a newest-first JSON array of
         { "version", "date", "summary", "changes": [ { "type", "text" } ] }
       rendered client-side, on a host that auto-deploys on push (Railway, Pages, etc.).

  Entry source priority (first match wins):
    1. "### Website" subsection in the CHANGELOG (hand-written plain language).
    2. AI rewrite: reads the API key already stored in the StripKit Generate-tab
       secret store (DPAPI), or falls back to ANTHROPIC_API_KEY / OPENAI_API_KEY /
       GEMINI_API_KEY env vars. Rewrites the technical bullets into plain English.
    3. Raw technical sections (Added/Fixed/...) with bookkeeping stripped — the
       original behaviour. Activated when no key is available or -SkipAiRewrite.

  With -Push the entry is committed + pushed to the website repo, triggering
  the host's auto-deploy (Railway, Pages, etc.).

.PARAMETER WebsiteRepo   Path to the website repo (contains updates.json). Required.
.PARAMETER AppChangelog  Path to the app's docs/CHANGELOG.md (the drafting source).
.PARAMETER Version       Version to publish. Default: the newest version in AppChangelog.
.PARAMETER UpdatesJson   Path to updates.json. Default: <WebsiteRepo>\updates.json.
.PARAMETER Date          ISO date. Default: the date in the CHANGELOG header, else today.
.PARAMETER Summary       One-line plain-language summary. Default: auto-derived from first bullet.
.PARAMETER SkipAiRewrite Skip the AI rewrite step and use the raw technical text.
.PARAMETER Commit        git add + commit updates.json in the website repo.
.PARAMETER Push          Implies -Commit; also push (triggers the host auto-deploy).
.PARAMETER DryRun        Print the drafted entry only; write nothing.

.EXAMPLE
  # Fully automated (used by Invoke-Release.ps1 Stage 3):
  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\StripKit-Website -AppChangelog docs\CHANGELOG.md -Push

.EXAMPLE
  # Draft and review before pushing:
  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\StripKit-Website -AppChangelog docs\CHANGELOG.md -DryRun
  scripts\Publish-WebsiteChangelog.ps1 -WebsiteRepo ..\StripKit-Website -Version 1.3.0 -Push
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WebsiteRepo,
    [string]$AppChangelog,
    [string]$Version,
    [string]$UpdatesJson,
    [string]$Date,
    [string]$Summary,
    [switch]$SkipAiRewrite,
    [switch]$Commit,
    [switch]$Push,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
function Fail($m) { throw $m }

if (-not (Test-Path $WebsiteRepo)) { Fail "Website repo not found: $WebsiteRepo" }
if (-not $UpdatesJson) { $UpdatesJson = Join-Path $WebsiteRepo 'updates.json' }
if (-not (Test-Path $UpdatesJson)) { Fail "updates.json not found: $UpdatesJson" }

# ---- text helpers -----------------------------------------------------------

# Quote+escape a string as JSON, then restore literal typographic chars (em-dash etc.)
# so the file stays human-readable. Both forms are valid JSON.
function ConvertTo-JsonText([string]$s) {
    if ($null -eq $s) { return '""' }
    $j = ($s | ConvertTo-Json)
    return [regex]::Replace($j, '\\u([0-9A-Fa-f]{4})', { param($m) [string][char][int]('0x' + $m.Groups[1].Value) })
}

# Collapse whitespace and drop build/test bookkeeping that is never user-facing.
function Format-ChangeText([string]$t) {
    $t = ($t -replace '\s+', ' ').Trim()
    $t = $t -replace '\*\*\+?\d+\s*tests?[^*]*\*\*', ''          # **+14 tests, suite 98->112.**
    $t = $t -replace '\(\s*\+?\d+\s*tests?[^)]*\)', ''           # (+4 tests ...)
    $t = $t -replace '\bbuild\s*0/0\b', ''
    $t = ($t -replace '\s+', ' ').Trim()
    $t = $t -replace '\s+([.,;:])', '$1'
    return $t.Trim()
}

function Get-ChangeType([string]$section) {
    $s = $section.ToLower()
    if ($s -match 'add|new') { return 'new' }
    if ($s -match 'fix|security|bug') { return 'fix' }
    return 'improved'
}

# ---- AI rewrite helpers -----------------------------------------------------

# Read the API key from the StripKit DPAPI secret store (same file the Generate tab uses),
# then fall back to environment variables. Returns @{Key; Provider} or $null.
function Get-StripKitApiKey {
    $secretsFile = Join-Path $env:APPDATA 'StripKit\secrets.dat'
    if (Test-Path $secretsFile) {
        try {
            Add-Type -AssemblyName System.Security -ErrorAction Stop
            $entropy = [System.Text.Encoding]::UTF8.GetBytes('StripKit.SecretStore.v1')
            $stored  = Get-Content $secretsFile -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($name in @('Claude', 'OpenAI', 'Gemini')) {
                $blob = $stored.$name
                if ($blob) {
                    $plain = [System.Security.Cryptography.ProtectedData]::Unprotect(
                        [Convert]::FromBase64String($blob), $entropy,
                        [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
                    $apiKey = [System.Text.Encoding]::UTF8.GetString($plain)
                    if ($apiKey) { return @{ Key = $apiKey; Provider = $name } }
                }
            }
        } catch { }
    }
    foreach ($pair in @(@{K='ANTHROPIC_API_KEY';P='Claude'}, @{K='OPENAI_API_KEY';P='OpenAI'}, @{K='GEMINI_API_KEY';P='Gemini'})) {
        $envVal = [System.Environment]::GetEnvironmentVariable($pair.K)
        if ($envVal) { return @{ Key = $envVal; Provider = $pair.P } }
    }
    return $null
}

# Extract the non-Website technical bullets from a changelog body as plain text.
function Get-TechnicalText([string]$body) {
    $lines = @(); $active = $false; $cur = $null
    foreach ($line in ($body -split "\r?\n")) {
        if ($line -match '^\s*###\s+(.+?)\s*$') {
            if ($cur) { $lines += (Format-ChangeText $cur); $cur = $null }
            $active = ($matches[1] -notmatch '^Website$')
            continue
        }
        if ($active) {
            if ($line -match '^\s*[-*]\s+(.*)$') {
                if ($cur) { $lines += (Format-ChangeText $cur) }
                $cur = $matches[1]; continue
            }
            if ($cur -and $line.Trim() -ne '' -and $line -notmatch '^\s*##') { $cur += ' ' + $line.Trim() }
        }
    }
    if ($cur) { $lines += (Format-ChangeText $cur) }
    return $lines -join "`n"
}

# Call the AI provider and return the raw response text.
function Invoke-AiRewrite([string]$technicalText, [string]$ver, [string]$apiKey, [string]$provider) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $prompt = @"
Write 3-5 plain-language release note bullets for the StripKit download website.
StripKit is a Windows desktop tool that turns image files into animated sprite sheets for audio plugin user interfaces (knobs, faders, sliders, buttons).

Rules:
- No class names, method names, code identifiers, backtick code, or angle brackets
- Each bullet: "- **Short title.** One sentence a non-developer understands."
- For bug fixes: "- **Fixed: title.** What was wrong and what works now."
- Maximum 5 bullets. Combine or drop minor internal changes.
- Return ONLY the bullets, nothing else.

Developer changelog for v${ver}:
${technicalText}
"@

    $ct = @{ 'content-type' = 'application/json' }
    if ($provider -eq 'Claude') {
        $ct['x-api-key']         = $apiKey
        $ct['anthropic-version'] = '2023-06-01'
        $reqBody = @{ model = 'claude-haiku-4-5-20251001'; max_tokens = 600
                      messages = @(@{ role = 'user'; content = $prompt }) } | ConvertTo-Json -Depth 5
        $resp = Invoke-RestMethod -Uri 'https://api.anthropic.com/v1/messages' -Method POST -Headers $ct -Body $reqBody
        return $resp.content[0].text
    }
    if ($provider -eq 'OpenAI') {
        $ct['Authorization'] = "Bearer $apiKey"
        $reqBody = @{ model = 'gpt-4.1-mini'; max_tokens = 600
                      messages = @(@{ role = 'user'; content = $prompt }) } | ConvertTo-Json -Depth 5
        $resp = Invoke-RestMethod -Uri 'https://api.openai.com/v1/chat/completions' -Method POST -Headers $ct -Body $reqBody
        return $resp.choices[0].message.content
    }
    if ($provider -eq 'Gemini') {
        $reqBody = @{ contents = @(@{ parts = @(@{ text = $prompt }) }) } | ConvertTo-Json -Depth 5
        $uri  = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=$apiKey"
        $resp = Invoke-RestMethod -Uri $uri -Method POST -Headers $ct -Body $reqBody
        return $resp.candidates[0].content.parts[0].text
    }
    throw "Unknown provider: $provider"
}

# Parse "- **Title.** Rest of sentence." lines into @{type, text} entries.
function Parse-AiBullets([string]$text) {
    $entries = @()
    foreach ($line in ($text -split "\r?\n")) {
        $line = $line.Trim()
        if ($line -match '^-\s+\*\*(.+?)[.*]+\*\*\s*(.*)$') {
            $title    = $matches[1].Trim(' ', '.', ':')
            $rest     = $matches[2].Trim()
            $entryType = if ($title -match '^Fixed') { 'fix' } else { 'new' }
            $full     = if ($rest) { "**${title}.** ${rest}" } else { "**${title}.**" }
            $entries += @{ type = $entryType; text = $full.Trim() }
        }
    }
    return $entries
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
        $mb  = [regex]::Match($cl, $pat)
        if ($mb.Success) {
            $hdr = [regex]::Match($cl, '(?m)^##\s*\[' + [regex]::Escape($Version) + '\][^\r\n]*')
            $md  = [regex]::Match($hdr.Value, '(\d{4}-\d{2}-\d{2})')
            if ($md.Success) { $blockDate = $md.Groups[1].Value }
            $bodyText = $mb.Groups['body'].Value

            # ---- priority 1: ### Website section (hand-written plain language) ----
            $websiteChanges = @(); $inWebsite = $false; $wCur = $null
            foreach ($line in ($bodyText -split "\r?\n")) {
                if ($line -match '^\s*###\s+Website\s*$') { $inWebsite = $true; $wCur = $null; continue }
                if ($inWebsite) {
                    if ($line -match '^\s*###') {
                        if ($wCur) {
                            $wType = if ($wCur -match '\bfix(ed|es)?\b') { 'fix' } else { 'new' }
                            $websiteChanges += @{ type = $wType; text = (Format-ChangeText $wCur) }
                            $wCur = $null
                        }
                        $inWebsite = $false; continue
                    }
                    if ($line -match '^\s*[-*]\s+(.*)$') {
                        $bulletText = $matches[1]  # capture before inner regex overwrites $matches
                        if ($wCur) {
                            $wType = if ($wCur -match '\bfix(ed|es)?\b') { 'fix' } else { 'new' }
                            $websiteChanges += @{ type = $wType; text = (Format-ChangeText $wCur) }
                        }
                        $wCur = $bulletText; continue
                    }
                    if ($wCur -and $line.Trim() -ne '' -and $line -notmatch '^\s*##') { $wCur += ' ' + $line.Trim() }
                }
            }
            if ($wCur) {
                $wType = if ($wCur -match '\bfix(ed|es)?\b') { 'fix' } else { 'new' }
                $websiteChanges += @{ type = $wType; text = (Format-ChangeText $wCur) }
            }

            if ($websiteChanges.Count -gt 0) {
                $changes = $websiteChanges

            # ---- priority 2: AI rewrite using the stored Generate-tab API key ----
            } elseif (-not $SkipAiRewrite) {
                $cred = Get-StripKitApiKey
                if ($cred) {
                    Write-Host "  AI rewriting with $($cred.Provider)..." -ForegroundColor Gray
                    try {
                        $techText  = Get-TechnicalText $bodyText
                        $aiRaw     = Invoke-AiRewrite $techText $Version $cred.Key $cred.Provider
                        $aiEntries = Parse-AiBullets $aiRaw
                        if ($aiEntries.Count -gt 0) {
                            $changes = $aiEntries
                            Write-Host "  AI rewrite: $($aiEntries.Count) bullets." -ForegroundColor Gray
                        } else {
                            Write-Warning "AI rewrite returned no parseable bullets - falling back to technical text."
                        }
                    } catch {
                        Write-Warning "AI rewrite failed ($($_.Exception.Message)) - falling back to technical text."
                    }
                }
            }

            # ---- priority 3: raw technical sections (original behaviour) --------
            if ($changes.Count -eq 0) {
                $type = 'improved'; $cur = $null
                foreach ($line in ($bodyText -split "\r?\n")) {
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
[void]$sb.AppendLine('    "date": '    + (ConvertTo-JsonText $Date)    + ',')
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
$jsonRaw  = Get-Content $UpdatesJson -Raw -Encoding UTF8
$existing = $jsonRaw | ConvertFrom-Json
$already  = @($existing | Where-Object { $_.version -eq $Version }).Count -gt 0
if ($already) {
    Write-Host "v$Version is already in updates.json - leaving it untouched (refine by hand if needed)." -ForegroundColor Yellow
}
else {
    $idx = $jsonRaw.IndexOf('[')
    if ($idx -lt 0) { Fail "updates.json is not a JSON array." }
    $after   = $jsonRaw.Substring($idx + 1).TrimStart("`r", "`n")
    $newText = $jsonRaw.Substring(0, $idx + 1) + "`r`n" + $entry + ",`r`n" + $after
    $null    = $newText | ConvertFrom-Json    # validate or throw
    [System.IO.File]::WriteAllText($UpdatesJson, $newText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Inserted v$Version entry into $UpdatesJson" -ForegroundColor Green
}

# ---- optional commit / push (triggers the website host's auto-deploy) -----
if ($Commit -or $Push) {
    $leaf  = Split-Path $UpdatesJson -Leaf
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
