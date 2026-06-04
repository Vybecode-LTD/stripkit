# BUGS — StripKit

> Version 0.6.0 · last-updated 2026-06-04 · last-audit 2026-06-04

**Open bugs: 0.** **Resolved: 4.**

Each bug fixed gets a root cause and a regression guard. BUG-001/002 were
**pre-existing scaffold defects** surfaced by the first real compilation during
Phase 0 — neither was caused by the FilmstripForge → StripKit rename. BUG-003/004
were **release-tooling defects** caught during the first v0.6.0 release and fixed
forward (they corrupted published docs / blocked the release pipeline, not the app
binary).

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

---

## Notes

- No runtime bugs are currently known. The renderer output is locked by golden-image
  tests; a future intentional render change must update baselines (see
  `docs/TESTING.md`).
- **Informational (not a bug):** VirusTotal reports ~4/71 detections on the unsigned
  installer — heuristic false-positives. A code-signing certificate is the planned
  remedy (the build currently ships unsigned → SmartScreen).
- Known *limitations* (not bugs) live in `docs/ROADMAP.md` / `docs/ARCHITECTURE.md`:
  importer detection is a dimension-based guess (editable + verified), the importer
  cannot yet resample frame *count*, and the manifest UI emits a single control.
