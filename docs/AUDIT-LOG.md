# AUDIT-LOG — StripKit

> Version 0.6.0 · last-updated 2026-06-04 · last-audit 2026-06-04
>
> A running record of documentation reconciliations and codebase audits. Newest first.

---

## 2026-06-04 — Full doc reconciliation against ground truth (v0.6.0)

**Scope:** cross-check every managed doc (`CLAUDE.md`, `docs/ROADMAP.md`, `BUGS.md`,
`TESTING.md`, `CHANGELOG.md`, `HANDOFF.md`, `PACKAGING.md`, `AUDIT-LOG.md`) against each
other and the codebase for drift after the v0.6.0 ship (Inno Setup pipeline + website).
Triggered by the doc-reconciler.

### Checked

- **Header style** across all managed docs (the StripKit convention is the single `>`
  line, not YAML frontmatter).
- **Ground truth:** v0.6.0 is the first GitHub Release (Inno installer; Velopack fully
  removed from `src/`), 49/49 tests pass, 0 open bugs (BUG-003/004 fixed), website repo
  pushed but not deployed, installer unsigned (VirusTotal ~4/71 heuristic FPs).
- **Code verification:** searched `src/` for `Velopack|VelopackApp|vpk pack|UpdateService`
  — **0 matches** (Velopack genuinely gone). `StripKit.csproj` `<Version>` = **0.6.0**,
  `SkiaSharp` 3.119.0. `dotnet test` — **49 passed / 0 failed / 0 skipped** (45
  `[Fact]`/`[Theory]` methods; 2 Theories expand 3 rows each — 43 + 3 + 3 = 49).
  Pipeline present: `installer/StripKit.iss`, `scripts/Invoke-Release.ps1`,
  `.github/workflows/auto-release.yml`.
- **Cross-doc story:** ROADMAP (P7 + P8 ✅), BUGS (0 open / 4 resolved), TESTING (49),
  CHANGELOG (`[0.6.0]` present + dated), CLAUDE last-task, HANDOFF state — all agree.
- **Website changelog split** (`updates.json` simplified, decoupled from the technical
  `docs/CHANGELOG.md`) — described consistently in CLAUDE / ROADMAP / HANDOFF / PACKAGING.

### Drift found — fixed

| # | Severity | Document | Issue | Resolution |
|---|----------|----------|-------|------------|
| 1 | MEDIUM | `docs/ROADMAP.md` | Header used a **YAML frontmatter block** — diverged from the single-`>`-line convention all siblings use; `last-audit` lagged at 2026-06-03. | Replaced with `> Version 0.6.0 · last-updated 2026-06-04 · last-audit 2026-06-04`. |
| 2 | LOW | `docs/ROADMAP.md` | Phase 4/5/6 status said `11/11` / `31/31` / `41/41` green with no time anchor (reads as the current suite size; actually 49). | Appended "**at that point**" to each; noted the suite is now 49. |
| 3 | LOW | `docs/ROADMAP.md` | Phase 7 done-condition said "**signed**, single-file" (the build ships **unsigned**). | Reworded to single-file with signing as a follow-up; status already noted unsigned. |
| 4 | LOW | `docs/ARCHITECTURE.md` | "Extension points" listed **Phase 7: signed single-file build** (no ✅), but it is **done and unsigned**. | Marked ✅ done; described the Inno-installer + GitHub-Release reality (unsigned); pointed to `docs/PACKAGING.md`. |
| 5 | LOW | `README.md` | "`dotnet test` # **41 tests**" — stale count. | Updated to **49 tests**. |

### Flagged for the doc-versioner (not changed here, per scope)

- **`docs/CHANGELOG.md`** header dates are `2026-06-03` (both) while CLAUDE / TESTING /
  HANDOFF are `2026-06-04`. Version 0.6.0 is correct; the `[0.6.0]` section is also dated
  2026-06-03. Versioner to decide whether to advance to 06-04.
- **`docs/BUGS.md`** header `last-audit` is `2026-06-03` (this audit is 2026-06-04).
- **`docs/PACKAGING.md`** header has **no `last-audit`** field and `last-updated` lags at
  `2026-06-03`; siblings carry `last-audit 2026-06-04`.
- **NON-MANAGED, stale (separate pass):** `docs/KICKOFF.md` (Version **0.5.0**; says
  "`41/41`", Phase 7 is "**next task**", produce a "**signed**" build, conventions still
  list "**JetBrains Mono**") and `docs/ARCHITECTURE.md` header (Version **0.5.0**). Not in
  the managed-reconcile set; KICKOFF needs a full rewrite to the v0.6.0 state.

### Verdict

**In line after fixes.** Findings: 5 (0 critical, 0 high, 1 medium, 4 low); **5 auto-fixed,**
0 needing manual code review. Velopack / auto-update / `vpk` appear only as clearly-historical
"Removed" / "superseded" notes — nothing presents them as current. 49/49 green, 0 open
bugs, v0.6.0 shipped. Recommended doc-version increment: **PATCH** (stamp finalize only).

---

## 2026-06-03 — Phase 6 (meter mode) + docs sync (v0.5.0)

**Scope:** design-first meter mode (signed off before coding), then implementation and
a full doc sync.

- **Added (code):** `ComponentType.Meter`, `MeterFillDirection` enum, meter fields on
  `FilmstripSettings` (all Skia-free), `RenderMeterFrame` (procedural segment bars +
  layered on/off-art reveal; four fill directions; discrete/continuous), nullable
  `source` on `RenderFrame`/`RenderStrip`, manifest `"meter"` mapping, Create-tab
  "Meter" type + METER settings. Mirrored into the standalone `FilmstripEngine.cs`.
- **Docs synced:** ROADMAP (P6 ✅), CHANGELOG (0.5.0), HANDOFF (next = P7), SOURCE_MAP,
  ARCHITECTURE (§3 table, §5.4 meter, §15), TESTING (+10 tests), CLAUDE.md, README,
  KICKOFF, BUGS. All stamps → **0.5.0**.
- **Mini-audit of new code:** no `async void`/`.Result`/`.Wait()`/`System.Drawing`;
  the only VM↦Skia use is `SKColor.TryParse` for the colour fields (the VM already
  uses SkiaSharp); meter fields kept out of Avalonia; engine mirror updated and
  consistent by inspection.
- **Verification:** `dotnet build` 0/0; `dotnet test` **41/41**; meter golden baselines
  reviewed (procedural sweep, layered reveal, horizontal); app boots with the meter UI.
- **Verdict:** in line. 0 open bugs. Next: Phase 7 (packaging) — the final phase.

---

## 2026-06-03 — Phase 5 (batch) + docs sync (v0.4.0)

**Scope:** built Phase 5 (batch processing, third tab) and kept every doc in sync.

- **Added (code):** `BatchModels`, `IBatchProcessor`/`BatchProcessor` (whole loop
  off-thread via `Task.Run`, per-item progress, between-item cancel that returns a
  result, failure isolation), `BatchViewModel`, `BatchView`, and
  `IFileDialogService.OpenFolderAsync`. DI updated; third tab wired.
- **Docs synced:** README, ROADMAP (P5 ✅), CHANGELOG (0.4.0), HANDOFF (next = P6),
  SOURCE_MAP, ARCHITECTURE (§3 tables, §4 DI, §8.1 Batch, §10, §15), TESTING (+6 tests),
  CLAUDE.md (architecture + last-task). All version stamps → **0.4.0**.
- **Mini-audit of new code:** no Avalonia UI types in `BatchViewModel`; no `async void`
  (Run is `async Task`, Cancel is a sync void command); no `.Result`/`.Wait()`/
  `System.Drawing`. DI complete. Batch runs entirely off the UI thread; progress
  marshals via a UI-thread `Progress<T>`.
- **Verification:** `dotnet build` 0/0; `dotnet test` **31/31**; app boots with three
  tabs (Create | Import | Batch).
- **Verdict:** in line. 0 open bugs. Next: Phase 6 (meter, design-first).

---

## 2026-06-03 — Full documentation reconciliation + codebase audit (v0.3.0)

**Scope:** after Phases 0–4 + manifest (P3), reconcile all docs to the real code,
document everything in depth, and audit the codebase for consistency and convention
adherence. Doc version bumped **0.2.0 → 0.3.0** across managed docs.

### Documentation reconciliation (drift found → fixed)

| Doc | Drift found | Action |
|-----|-------------|--------|
| `README.md` | Described the pre-tabs app; no drag-drop, importer, manifest, or tests; layout omitted new files. | **Rewritten**: two-tab usage (Create + Import), manifest, tests, full layout, doc links. |
| `KICKOFF.md` (root) | Identical duplicate of `docs/KICKOFF.md` (drift-prone) and stale. | **Replaced with a pointer** to `docs/KICKOFF.md` (single source of truth). |
| `docs/KICKOFF.md` | Described "verify the scaffold + Phase 1" as the next task; open questions already resolved. | **Rewritten** for the current state; next task = Phase 5; guardrails kept. |
| `docs/SOURCE_MAP.md` | Missing `FilmstripEngine.cs`, the new docs, and updated `tests/` contents. | Added the standalone engine, the full `docs/` set, and the expanded test list (done incrementally across P2/P3 + here). |
| `CLAUDE.md` | "Architecture" + "Start here" predated importer/manifest/tabs; no doc-set links; no version stamp. | Updated architecture (importer, manifest, two tabs, second VM/view, standalone engine), added doc links + version stamp. |
| `docs/ROADMAP.md` | Phase statuses lagged. | P1/P2/P3 marked ✅ with status notes; P4 noted brought-forward (done incrementally). |
| New docs | Missing. | **Created** `ARCHITECTURE.md`, `TESTING.md`, `CHANGELOG.md`, `BUGS.md`, `HANDOFF.md`, `AUDIT-LOG.md`. |

All managed docs now carry a `Version 0.3.0 · last-updated · last-audit` stamp and
agree on the same facts (25 tests, phases done, next = P5).

### Codebase audit (checks → results)

| Check | Result |
|-------|--------|
| Build (`dotnet build StripKit.sln -c Debug`) | ✅ 0 warnings / 0 errors → `StripKit.dll`. |
| Tests (`dotnet test`) | ✅ 25 passed / 0 failed / 0 skipped. |
| Naming — no `FilmstripForge` stragglers | ✅ only intentional history in `CLAUDE.md` + the deliberately-generic `skills/dotnet-cli-from-engine`. |
| MVVM boundary — no Avalonia UI types in view models | ✅ only the `AvBitmap = Avalonia.Media.Imaging.Bitmap` preview alias (a media type, by design). |
| Conventions — no `async void`, `.Result`, `.Wait()`, `System.Drawing`, `TODO/FIXME` | ✅ none in source (the only `System.Drawing` hits are auto-generated framework ref lists in `obj/`). |
| DI completeness | ✅ all services + both view models registered in `App.axaml.cs`; engine services singleton, view models transient. |
| Compiled bindings | ✅ every view has `x:DataType`; 0 AVLN warnings (compiled-bindings-by-default). |
| Source generators `partial` | ✅ both view models are `partial`. |
| `FilmstripEngine.cs` ↔ `SkiaFilmstripRenderer.cs` sync | ✅ in sync by inspection (identical math; only namespace differs). |

### Findings (non-blocking)

1. **(Minor)** View models dispose `SKBitmap`s on *replace* but do not implement
   `IDisposable` to release the final source/background/strip at window close — the
   OS reclaims at process exit. Consistent with the original scaffold; low impact.
   Candidate cleanup if a window-lifecycle pass happens.
2. **(Maintenance)** `FilmstripEngine.cs` duplicates the renderer; documented as a
   hand-maintained mirror. In sync now; must be updated alongside any renderer change.
3. **(Documented limitations, not defects)** importer detection is a dimension-based
   guess (editable + verified in the UI); no frame-*count* resampling; the manifest
   UI emits a single control. All recorded in `ROADMAP`/`ARCHITECTURE`/`HANDOFF`.

### Verdict

**Codebase is in line with the docs and conventions.** No open bugs (`docs/BUGS.md`);
no critical or high-severity findings. Safe to proceed to Phase 5.

---

## 2026-06-03 — Rename + Phase 0 verification (v0.2.0)

- Renamed FilmstripForge → StripKit across all docs/code; deleted the duplicate
  `skills/FilmstripForge/` mirror. Fixed two pre-existing build blockers (BUG-001,
  BUG-002). Verified build + boot + render round-trip. (See `docs/CHANGELOG.md`.)
