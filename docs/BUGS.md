# BUGS — StripKit

> Version 1.3.0 · last-updated 2026-06-18 · last-audit 2026-06-18

**Open bugs: 0.** **Resolved: 11.**

Each bug fixed gets a root cause and a regression guard. BUG-001/002 were
**pre-existing scaffold defects** surfaced by the first real compilation during
Phase 0 — neither was caused by the FilmstripForge → StripKit rename. BUG-003/004
were **release-tooling defects** caught during the first v0.6.0 release and fixed
forward (they corrupted published docs / blocked the release pipeline, not the app
binary). BUG-005/006/007 were **code-quality defects** found by audit and fixed in
commit `0aaa257` (resource leaks and a silent product gap). BUG-008/009 were
**Generate-tab defects** found in the 2026-06-14 audit of the shipped v1.2.0 and fixed
forward in `80dc1b5` (a broken handoff path + unhardened untrusted-XML parsing). BUG-010 was a
**2026-06-18 audit** finding: the BUG-009 hardening was applied to the SVG file-import path but ran
*after* Svg.Skia had already parsed the raw text, leaving the entity-expansion DoS reachable there.

---

## Resolved

### BUG-001 — `.csproj` XML comment contains `--` (build blocker) ✅
- **Severity:** Critical (project would not load / build).
- **Symptom:** `error MSB4025: The project file could not be loaded. An XML comment
  cannot contain '--' …` at `StripKit.csproj(38)`.
- **Root cause:** the scaffold was never compiled on a machine with the SDK. A
  comment contained the literal `dotnet list package --include-transitive`; the `--`
  is illegal inside an XML `<!-- … -->` comment.
- **Fix (2026-06-03):** reworded the comment to avoid `--` (the exact command still
  lives in `README.md`, where markdown allows it).
- **Regression guard:** any `dotnet build` parses the `.csproj`; CI/`dotnet test`
  catches a recurrence immediately.

### BUG-002 — missing `Microsoft.Extensions.DependencyInjection` reference (build blocker) ✅
- **Severity:** Critical (would not compile).
- **Symptom:** `error CS0234: The type or namespace name 'Extensions' does not exist
  in the namespace 'Microsoft'` at `App.axaml.cs(7)`.
- **Root cause:** the composition root in `App.axaml.cs` used `ServiceCollection` /
  `AddSingleton`, and `CLAUDE.md` lists DI in the stack, but the `.csproj` never
  referenced the package. Never caught because the scaffold was never compiled.
- **Fix (2026-06-03):** added
  `<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />`.
- **Regression guard:** build; the DI wiring is exercised at app boot and indirectly
  by the headless `DropZoneViewTests`.

### BUG-003 — CHANGELOG mojibake on release (double-encoded UTF-8) ✅
- **Severity:** Medium (corrupted published docs, not the app binary).
- **Component:** `scripts/Invoke-Release.ps1`.
- **Reported / Fixed:** 2026-06-04 (first v0.6.0 release run).
- **Symptom:** the first run of `scripts/Invoke-Release.ps1` rewrote
  `docs/CHANGELOG.md` with double-encoded characters — em-dashes `—` became `â€"`,
  middots `·` became `Â·`. The corruption was committed (`d640a6f`) and would have
  surfaced in the GitHub Release notes and on the website.
- **Root cause:** PowerShell 5.1's `Get-Content -Raw` (with no `-Encoding`) decodes a
  UTF-8-without-BOM file as ANSI/Windows-1252; the script then re-wrote it as UTF-8,
  double-encoding every non-ASCII byte.
- **Fix (commit `f1b68d3`):** added `-Encoding UTF8` to every `Get-Content -Raw` read
  in `Invoke-Release.ps1`; restored the mangled `docs/CHANGELOG.md` (and the `.iss`)
  from the pre-release commit and re-applied the version promotion correctly. Verified
  the file is clean UTF-8.
- **Regression guard:** none (build-script issue, not unit-testable) — future releases
  read/write the changelog as UTF-8 and the workflow re-ran clean afterward.

### BUG-004 — CI "Create GitHub Release" step crashed on changelog backticks ✅
- **Severity:** High (blocked the release; the first auto-release run failed and no
  release was created).
- **Component:** auto-release GitHub Actions workflow ("Create GitHub Release" step).
- **Reported / Fixed:** 2026-06-04 (first v0.6.0 release run).
- **Symptom:** the auto-release workflow failed in the "Create GitHub Release" step
  with `command substitution: syntax error` and many `command not found` lines (e.g.,
  `ExperimentalAcrylicBorder`, `Border.card`).
- **Root cause:** the changelog body (full of Markdown backticks) was interpolated via
  `${{ steps.notes.outputs.body }}` directly into `gh release create --notes "..."`,
  so bash performed command substitution on the backticks and tried to execute the
  changelog text as shell commands.
- **Fix (commit `a408bc9`):** the workflow now writes the notes to a file and passes
  them with `--notes-file`, and all values flow through `env:` vars instead of inline
  `${{ }}` interpolation (injection-safe). Also added a `workflow_dispatch` trigger for
  manual re-runs; re-running produced the release successfully (run completed in
  ~1m21s).
- **Regression guard:** none (CI-workflow issue, not unit-testable) — the re-run
  produced the release cleanly, confirming the fix.

### BUG-005 — CancellationTokenSource leak in BatchViewModel ✅
- **Severity:** Low (GC eventually collects; WaitHandle leak + bad contributor pattern).
- **Component:** `src/StripKit/ViewModels/BatchViewModel.cs`.
- **Reported / Fixed:** 2026-06-04 (code audit).
- **Symptom:** each `RunAsync()` call created a new `CancellationTokenSource` without
  disposing the previous one, leaking the underlying `WaitHandle` until GC collected
  the instance.
- **Root cause:** missing `_cts?.Dispose()` before reassignment in `RunAsync()`.
- **Fix (commit `0aaa257`):** added `_cts?.Dispose();` immediately before
  `_cts = new CancellationTokenSource();` in `RunAsync()`.
- **Regression guard:** 49/49 green; no dedicated automated regression test — the fix
  is a one-line pattern check; future reviewers can verify by inspection.

### BUG-006 — DispatcherTimer not stopped on window close ✅
- **Severity:** Low (timer fires after disposal in tests and edge-case window
  teardown scenarios).
- **Component:** `src/StripKit/Views/MainWindow.axaml.cs`.
- **Reported / Fixed:** 2026-06-04 (code audit).
- **Symptom:** `_playTimer` (`DispatcherTimer`) had no cleanup path when the window
  was closed, leaving the timer running against a disposed view.
- **Root cause:** missing `Closed` event subscription in the constructor.
- **Fix (commit `0aaa257`):** added
  `Closed += (_, _) => _playTimer.Stop();` in the `MainWindow` constructor.
- **Regression guard:** 49/49 green.

### BUG-007 — ComponentType.Meter missing from BatchViewModel ✅
- **Severity:** Medium (silent product gap — users cannot batch-render meters via the
  Batch tab; no error is shown, the option simply does not exist).
- **Component:** `src/StripKit/ViewModels/BatchViewModel.cs`.
- **Reported / Fixed:** 2026-06-04 (code audit).
- **Symptom:** `BatchViewModel.ComponentTypes` listed only `RotaryKnob`,
  `VerticalFader`, and `HorizontalSlider`; `Meter` was absent from the batch template
  selector.
- **Root cause:** `ComponentType.Meter` was added to the Create tab (Phase 6 / v0.5.0)
  after `BatchViewModel` was already built; the Batch view model was never updated to
  include it.
- **Fix (commit `0aaa257`):** added `ComponentType.Meter` to the `ComponentTypes`
  array in `BatchViewModel`. Added a comment in `BuildSettings()` noting that
  meter-specific fields (`SegmentCount`, `FillDirection`, etc.) are not yet exposed in
  the batch template UI and will render using `FilmstripSettings` defaults until a
  dedicated batch-meter UI is added. *(Fully resolved in v0.8.0 — the Batch tab now
  exposes the meter settings + a layered/backdrop toggle.)*
- **Regression guard:** 49/49 green.

### BUG-008 — Generate → Create handoff hard-coded RotaryKnob (broken non-knob output) ✅
- **Severity:** High (broken output — a generated fader/slider/button produced an unusable strip).
- **Component:** `src/StripKit/ViewModels/MainWindowViewModel.cs` (the Generate → Create handoff)
  + `src/StripKit/ViewModels/GenerateViewModel.cs`.
- **Reported / Fixed:** 2026-06-14 (audit of the shipped v1.2.0).
- **Symptom:** after v1.2.0 let the Generate tab produce **all four control types**, the
  **"Use in Create"** handoff still forced `RotaryKnob`. A generated **fader/slider** was treated
  as a layered knob and **rotated** instead of sliding; a generated **button** stacked both `off`
  and `on` states on top of each other (the `Frame` indexing was lost).
- **Root cause:** `ImportLayeredFromPathAsync` (and the import that backs it) always set the knob
  type / appended Rotate layers, written before the button + linear control types existed in
  Generate.
- **Fix (commit `80dc1b5`):** the handoff now branches on the **generated control type** — a
  **knob** maps to the body + pointer layer stack; a **button** maps its `off`/`on` groups to
  `LayerBehavior.Frame` state layers; a **fader/slider** is flattened to the single source the
  linear renderer expects.
- **Regression guard:** `GenerateIntegrationTests` exercises the per-type handoff (knob →
  body/pointer layers; button → off/on Frame layers; the handoff carries the generated type).

### BUG-009 — Untrusted SVG parsed without DTD/entity hardening (DoS / XXE exposure) ✅
- **Severity:** High (security — entity-expansion denial-of-service and external-entity / SSRF on
  attacker-influenced input).
- **Component:** `src/StripKit/Services/SvgSanitizer.cs` (AI replies) +
  `src/StripKit/Services/LayeredImportService.cs` (imported `.svg` files).
- **Reported / Fixed:** 2026-06-14 (audit).
- **Symptom:** both the AI-reply sanitizer and the layered-file import picker parsed SVG with a bare
  `XDocument.Parse`, which honours DTDs — leaving "billion laughs" entity-expansion DoS and
  external-entity / SSRF (`<!ENTITY x SYSTEM "file://…">`) open on attacker-supplied content.
- **Root cause:** `XDocument.Parse(string)` uses default reader settings (`DtdProcessing.Parse`,
  a default resolver), unsafe for untrusted XML.
- **Fix (commit `80dc1b5`):** new `Services/SafeXml.cs` — `XDocument.Load` with
  `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`. Applied in
  **both** callers. A DTD now throws `XmlException`, which both callers already treat as "malformed
  SVG"; legitimate generated control art carries no DTD, so the happy path is unaffected.
- **Regression guard:** `SvgSanitizerTests` rejects a DTD-bearing document as malformed (so the
  prohibition is asserted, not just present). *(The matching `LayeredImportServiceTests` guard was
  not actually added until BUG-010 below — this line over-claimed it at the time.)*

### BUG-010 — billion-laughs DoS reachable via the SVG file-import path (BUG-009 hardening bypassed) ✅
- **Severity:** Medium (security — local entity-expansion denial-of-service: a crafted `.svg` opened
  via the layered-file picker could hang / exhaust memory. External-entity / SSRF was **not** reachable
  — `svg-net` defaults `ResolveExternalXmlEntites = ExternalType.None` — so this was DoS only).
- **Component:** `src/StripKit/Services/LayeredImportService.cs` (`ImportSvg`).
- **Reported / Fixed:** 2026-06-18 (audit).
- **Symptom:** BUG-009 added `SafeXml.Parse` (DTDs prohibited) to the SVG file-import path, but in
  `ImportSvg` it ran **after** `SvgSkia.FromSvg(text)` had already parsed the **raw** untrusted text.
  Svg.Skia builds its model with the `svg-net/SVG` library, which defaults to `DtdProcessing.Parse`
  with no `MaxCharactersFromEntities` cap — so a "billion-laughs" entity bomb was fully expanded
  inside `FromSvg` before `SafeXml` ever rejected it. The AI-reply path was unaffected (its SVG is
  re-serialized DTD-free by `SvgSanitizer` before it reaches the importer); only the **file picker**
  (arbitrary user SVG) was exposed.
- **Root cause:** parse ordering — the hardened gate ran second. The documented guarantee ("applied
  to both `SvgSanitizer` and the layered-file import picker") was only half-true for the importer.
- **Fix (2026-06-18):** moved `SafeXml.Parse(text)` to the **top** of `ImportSvg`, before
  `SvgSkia.FromSvg`. A DTD now throws at the gate (caught by `Import()` → "no usable layers"), so the
  raw text only reaches Svg.Skia once it is known DTD-free. Reorder only — no re-serialization, no new
  dependency, byte-identical output for legitimate (DTD-free) SVG.
- **Regression guard:** `LayeredImportServiceTests.Svg_import_rejects_a_doctype_entity_bomb_without_expanding_it`
  (a 1e9-expansion bomb must return null in < 5 s) +
  `Svg_import_does_not_resolve_an_external_entity`. Suite 172 → **174 green**.

### BUG-011 — release script aborted at `git add` on a benign git stderr warning (PS 5.1) ✅
- **Severity:** Medium (release-tooling defect — aborted the v1.3.0 release mid-way; the binary was
  built + signed but the commit/tag/push didn't run, leaving a partial release to finish by hand).
- **Component:** `scripts/Invoke-Release.ps1` (the commit/tag/push block).
- **Reported / Fixed:** 2026-06-18 (hit during the v1.3.0 release).
- **Symptom:** the script threw at `git -C $root add …` with a `NativeCommandError`, the message being
  git's harmless `warning: in the working copy of 'docs/CHANGELOG.md', LF will be replaced by CRLF`.
  The `git add` actually succeeded, but the script stopped before commit/tag/push.
- **Root cause:** the script runs under `$ErrorActionPreference = 'Stop'`; in **Windows PowerShell 5.1**
  ANY native-command stderr output is wrapped in an ErrorRecord, so git's progress/warning lines on
  stderr become terminating errors. The working-tree files had LF line endings (written this session),
  which made git emit the CRLF warning that prior CRLF-clean releases didn't.
- **Fix (2026-06-18):** wrapped the git commit/tag/push block in `$ErrorActionPreference = 'Continue'`
  (restored in a `finally`) and gated each git call on `$LASTEXITCODE` instead — so a stderr warning no
  longer aborts the release, while a real non-zero exit still throws. The v1.3.0 release itself was
  completed by hand (commit `f38a5f5`, tag `v1.3.0`) after the abort.
- **Regression guard:** none (release-script issue, not unit-testable) — the next release exercises it;
  the exit-code gating is the guard.

---

## Notes

- No runtime bugs are currently known. The renderer output is locked by golden-image
  tests; a future intentional render change must update baselines (see
  `docs/TESTING.md`).
- **Informational (not a bug):** the app **and** installer are **code-signed** via Azure
  Trusted Signing (the `VybeCode` certificate profile), wired into the release pipeline since
  v0.8.0's signed re-release and used for every release since. The earlier "~4/71 VirusTotal FPs
  on the *unsigned* installer / SmartScreen prompt" caveat is now **historical**. (Signing uses
  `signtool.exe` + the `Microsoft.Trusted.Signing.Client` dlib — not AzureSignTool, which 403s
  against Trusted Signing endpoints; see `docs/PACKAGING.md`.)
- **Informational (not a bug) — release integrity:** the v1.2.0 **feature source** was accidentally
  omitted from the "Release v1.2.0" commit (which staged only version files + the installer), so the
  `v1.2.0` tag could not rebuild its own installer. The source was committed retroactively
  (`b55380f`, 2026-06-14), matching the shipped binary, before the v1.2.1 fixes. Process guard: commit
  feature work **before** running the release script (which stages only version files by design). As
  of **v1.2.2** this guard is **enforced, not just documented** — `Invoke-Release.ps1` aborts the
  release if the tracked working tree has uncommitted source (untracked strays allowed; `-AllowDirty`
  overrides). *(The same v1.2.2 tooling pass also fixed the Stage-3 website-changelog splat — a
  trailing `-Push` mis-bound under array splatting; switched to hashtable splatting — a build-script
  fix, not a tracked app bug.)*
- Known *limitations* (not bugs) live in `docs/ROADMAP.md` / `docs/ARCHITECTURE.md`:
  importer detection is a dimension-based guess (editable + verified). The **Skin tab** does
  multi-control `skin.json` (the Create-tab export still emits a single control). Auto-pointer
  extraction (★ #3 step 2) leaves a small central residual dot when the needle passes through the
  pivot (a verify-and-tweak starting point, knob-only) — not a defect. Generate is type-aware
  across knob/fader/slider/button, but the fader/slider/meter output paths still want a live eyeball
  (knob is the proven path).
