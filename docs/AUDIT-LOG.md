# AUDIT-LOG — StripKit

> Version 1.2.2 · last-updated 2026-06-14 · last-audit 2026-06-14
>
> A running record of documentation reconciliations and codebase audits. Newest first.

---

## 2026-06-14 — v1.2.2 polish wave (editable model + off-thread preview + release-integrity guard) + full reconcile

**Type:** Feature/tooling delivery + release + documentation reconciliation.

**Scope:** Shipped the v1.2.2 polish + tooling wave (both v1.2.1 and v1.2.2 went live the same day),
then reconciled every managed doc from 1.2.1 → **1.2.2 / 2026-06-14**.

### Ground truth verified (against the codebase)
- `src/StripKit/StripKit.csproj` `<Version>` = **1.2.2**; `installer/StripKit.iss` `MyAppVersion` = **1.2.2**
  (both bumped by the release script).
- Generate model picker is an **`AutoCompleteBox`** (`GenerateView.axaml` line 45 — free text + suggestions;
  the provider / control-type / style pickers stay `ComboBox`).
- `GenerateViewModel` builds the preview **off the UI thread**: `await Task.Run(() => BuildPreview(...))`
  (the `BuildPreview` method does temp-write + layered import + composite + PNG-encode); `TryDelete(_lastSvgPath)`
  drops the prior temp SVG each generation.
- `Invoke-Release.ps1` has the **release-integrity guard**: a `-AllowDirty` switch, a `git status --porcelain`
  check that excludes untracked (`^\?\?`) entries, an abort `throw`, and **hashtable** splatting (`@pubArgs`) for
  the Stage-3 `Publish-WebsiteChangelog.ps1` call (the comment notes a trailing `-Push` mis-binds under array splat).
- CI: `ci.yml` pins `actions/checkout@v5` + `actions/setup-dotnet@v5`; `auto-release.yml` pins `actions/checkout@v5`.
- `tests/StripKit.Tests/StripKit.Tests.csproj` references `coverlet.collector` **6.0.4**.
- New skill present: `.claude/skills/release-source-integrity-guard/SKILL.md` (already listed in CLAUDE/SOURCE_MAP —
  verified, not duplicated).
- New test present: `GenerateViewModelTests.A_custom_model_id_not_in_the_suggestions_is_honored` ("a typed/delisted
  model id is sent verbatim"). Test suite **172** (was 171; +1 this wave).
- Working tree's only untracked strays: `docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json` (not ours).

### What shipped in 1.2.2 (commits)
- **`cdc466e`** — Generate: editable model input (`AutoCompleteBox`), off-thread preview (`BuildPreview` in a
  `Task.Run`), temp-SVG cleanup; bump `coverlet.collector` 6.0.2→6.0.4.
- **`e124e47`** — release-integrity guard in `Invoke-Release.ps1` (abort if tracked source uncommitted; `-AllowDirty`
  override) + the Stage-3 array→hashtable splat fix.
- **`0fc64db`** / **`33fc522`** — `actions/checkout@v4→v5` and `actions/setup-dotnet@v4→v5` (Node 24, ahead of the
  June 16 2026 Node-20 forcing).
- **`114f8e5`** — new portable skill `release-source-integrity-guard` (linter 0/0) + listed in CLAUDE / SOURCE_MAP.
- **`5e9e587`** — CHANGELOG `[1.2.2]` (then promoted by the release script), **`ad4e1c2`** — `Release v1.2.2`,
  **`78e7081`** — HANDOFF v1.2.2-shipped.

### Releases
- **v1.2.1 AND v1.2.2 both shipped 2026-06-14** — tags `v1.2.1`, `v1.2.2` are live on GitHub Releases (signed via
  Azure Trusted Signing; CI VirusTotal-scanned each; the website changelog was auto-pushed by Stage 3 → Railway
  redeploy). The 1.2.2 release itself validated the new release-integrity guard and the Stage-3 splat fix end-to-end.

### Doc reconciliation
- All managed docs → **1.2.2 / 2026-06-14** (CHANGELOG + HANDOFF were already at 1.2.2 from the release; this pass
  stamped SOURCE_MAP, TESTING, ARCHITECTURE, ROADMAP, AUDIT-LOG, BUGS, KICKOFF, PACKAGING, CLAUDE).
- **Test count → 172** everywhere it was at 171 (SOURCE_MAP, TESTING ×several, KICKOFF, CLAUDE). The +1 is
  `GenerateViewModelTests` (5→6 documented; Generate-section header 27→28).
- **Editable model + off-thread preview** documented in SOURCE_MAP (GenerateViewModel/GenerateView lines),
  ARCHITECTURE §11 + §13 (threading) + §3.6, and CLAUDE's GenerateViewModel/GenerateView bullets.
- **Release-integrity guard + Stage-3 splat fix** documented in PACKAGING (§3 driver + §8.0/§8.4), SOURCE_MAP
  (scripts line), CLAUDE (Release section), ROADMAP (v1.2.2 entry + the `checkout@v4→v5` follow-up flipped ✅).
- **coverlet 6.0.4 + CI v5** noted in TESTING (frameworks table + CI section), ARCHITECTURE §18, SOURCE_MAP
  (workflows line), CLAUDE (stack/CI).
- **ROADMAP** added the **v1.2.1** and **v1.2.2** release entries and flipped the `actions/checkout@v4→v5`
  operational follow-up to ✅ (done in v1.2.2).
- **CLAUDE** gained a new "Last completed task" entry for the 1.2.2 wave; older entries condensed to keep the
  section readable.
- **BUGS** header bumped to 1.2.2; **no new tracked bug** — the orphaned-v1.2.0-source and the Stage-3 splat are
  both already captured as informational release-integrity notes / fixed-in-1.2.2 tooling, so neither warrants a
  new BUG-### entry. (Added a forward note that the orphaned-source process guard is now *enforced* by the
  Stage-1 integrity guard, not just documented.)

### Pre-existing contradiction noted (NOT introduced here; flagged for the next tooling pass)
- **`docs/PACKAGING.md` §13.3 "Code signing" still describes the AzureSignTool path** (`Tool: AzureSignTool v7.0.1`,
  "wired into `Invoke-Release.ps1`", and an `AzureSignTool sign` example) while **every other signing reference**
  — CLAUDE, the PACKAGING §0 file table / §1 stage diagram intent, BUGS-note, HANDOFF, KICKOFF — says signing uses
  **signtool + the `Microsoft.Trusted.Signing.Client` dlib, NOT AzureSignTool (which 403s against Trusted Signing
  endpoints)**. This is a stale §13.3 sub-section that predates the signtool switch; it contradicts the rest of the
  doc. **Left as-is this pass** (out of the 1.2.2 delta scope, and rewriting the signing setup steps wants the
  owner's confirmation of the exact current `Invoke-Release.ps1` signing block). **Recommend** rewriting §13.3 to
  the signtool + dlib reality in a follow-up.

### Verdict
**Green.** Build 0/0, **172/172** tests, app boots clean (five tabs + first-run tutorial), `main` == origin,
0 open bugs, **v1.2.1 + v1.2.2 both live + signed**, website changelog auto-pushed. Doc-version increment:
**PATCH** (1.2.1 → 1.2.2, stamp + the shipped polish/tooling). One pre-existing contradiction (PACKAGING §13.3
AzureSignTool vs signtool) flagged for a follow-up, not auto-fixed.

---

## 2026-06-14 — 5-dimension audit + orphaned-v1.2.0-source recovery + the 1.2.1 fix wave + full reconcile

**Type:** Codebase audit + release-integrity recovery + correctness/security fixes + documentation reconciliation.

**Scope:** Audited the codebase across five dimensions (release integrity, correctness, security, MVVM/convention
adherence, doc↔code drift), recovered an orphaned release's source, fixed three findings forward, and reconciled
every managed doc from 1.0.0 → **1.2.1 / 2026-06-14**.

### Ground truth verified
- `src/StripKit/StripKit.csproj` `<Version>` = **1.2.0** (the release script will bump to 1.2.1 — left untouched).
- `MainWindow.axaml` has **five** `TabItem`s: Create | Import | Batch | Skin | Generate.
- `ComponentType` enum = **5** values: `RotaryKnob`, `VerticalFader`, `HorizontalSlider`, `Meter`, **`Button`**.
- `LayerBehavior` enum = **3** values: `Static`, `Rotate`, **`Frame`** (off=index 0 / on=index 1 state frames).
- New source present: `Services/SafeXml.cs`, `Helpers/HexToColorBrushConverter.cs`, `Controls/SectionHeader.cs`,
  `tests/StripKit.Tests/GenerateIntegrationTests.cs`. Deps present: SkiaSharp **3.119.2**, Svg.Skia 5.0.0,
  Magick.NET-Q8-x64 14.13.1, `System.Security.Cryptography.ProtectedData` 9.0.0, `Avalonia.Controls.ColorPicker` 11.3.0.
- Test suite **171/171 green** (was 157; +14 this session). Build 0/0. Working tree's only untracked strays:
  `docs/PRESS-RELEASE.md`, `press/`, `.claude/launch.json` (not ours).

### Finding 1 — release integrity: v1.2.0 feature source was orphaned (recovered)
**Severity: HIGH.** The "Release v1.2.0" commit (`70cf259`) staged **only** the version files + the installer; the
actual v1.2.0 **feature source was never committed**. The released binary + the `v1.2.0` tag were live, but the tag
**could not rebuild its own installer** — its source was absent from history. **Recovery:** committed that source
as-is (matching the shipped binary) in **`b55380f`** *before* fixing forward to 1.2.1, restoring "every released
tag is rebuildable from its own tree." (Process guard now in HANDOFF/KICKOFF: commit features *before* running the
release script, which stages only version files by design.)

### Findings 2–4 — the 1.2.1 fix wave (committed `80dc1b5`, staged for release)
- **Generate → Create handoff hard-coded `RotaryKnob`** (HIGH — broken output): a generated fader/slider/button
  broke on handoff (faders/sliders rotated instead of sliding; buttons stacked both states). **Fixed:** the handoff
  now branches on the generated type — knob → body+pointer stack; button → `off`/`on` as `LayerBehavior.Frame`
  layers; fader/slider → flattened to the single source the linear renderer expects.
- **Untrusted-SVG XML parsing unhardened** (HIGH — security): bare `XDocument.Parse` on AI replies + imported SVG
  left entity-expansion DoS ("billion laughs") and external-entity / SSRF open. **Fixed:** new `Services/SafeXml.cs`
  (`DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`), applied in **both**
  `SvgSanitizer` and the layered-file import picker. A DTD now throws (both callers already treat that as "malformed
  SVG"); legitimate generated art has no DTD, so the happy path is unchanged.
- **CommunityToolkit + Avalonia double-validation** (MEDIUM): added the missing
  `BindingPlugins.DataValidators.RemoveAt(0)` in `App.axaml.cs`; the Generate tab now also **warns** when a knob has
  no rotating pointer / a button lacks an on or off state.

### Mini-audit of the code
- New services Avalonia-free where required (`SafeXml`: BCL `System.Xml` only). `HexToColorBrushConverter` is a view-layer
  `IValueConverter` (correct). `SectionHeader` is a `TemplatedControl` (view layer). VMs hold no Avalonia UI types
  (the preview `Bitmap` alias excepted). Source-gen VMs `partial`. The button state-frame path **is** mirrored in
  `FilmstripEngine.cs` (it's render math: `RenderButtonLayers` + `LayerBehavior.Frame`); the Generate providers /
  sanitizer / secret store are correctly **not** mirrored (app-only). DI complete. No `async void` outside handlers;
  no `.Result`/`.Wait()`/`System.Drawing`. Renderer gated by defaults → all prior goldens byte-identical.

### Doc reconciliation
- All managed docs → **1.2.1 / 2026-06-14** (CHANGELOG, SOURCE_MAP, TESTING, ARCHITECTURE, ROADMAP, AUDIT-LOG, BUGS,
  KICKOFF, PACKAGING, CLAUDE, HANDOFF frontmatter).
- **CHANGELOG:** fixed the stale "Version 1.0.0" header → 1.2.1; added a `## [Unreleased]` section (the three v1.2.1
  fixes, ### Fixed) above the existing `[1.2.0]` (left `[1.2.0]`/`[1.1.0]` intact; the release script renames
  `[Unreleased]` → `[1.2.1]`).
- **HANDOFF:** rewritten for the current session — five tabs, 171 tests, the v1.1.0/1.2.0/1.2.1 work, the
  orphaned-source recovery, the strays, and "ship 1.2.1" as the next step.
- **Test count → 171** everywhere it was stale (SOURCE_MAP 152, TESTING ~5 places at 152, README 98 ×2, HANDOFF 125).
- **Tab count → five + a Generate mention** in README (was "four-tab", no Generate), HANDOFF, KICKOFF.
- **New code documented** in SOURCE_MAP / ARCHITECTURE / CLAUDE: `ComponentType.Button`, `LayerBehavior.Frame`,
  `SafeXml.cs`, `HexToColorBrushConverter.cs`, `Controls/SectionHeader.cs`, `GenerateIntegrationTests.cs`.
- **ARCHITECTURE:** the §11 "v1 is knob-only / faders-sliders-meters are future" line corrected (Generate is now
  type-aware across all four control types); documented the Button state-frame render path (`RenderButtonLayers`) +
  the `SafeXml` hardening; SkiaSharp 3.119.0→3.119.2 + the newer packages noted.
- **ROADMAP:** added v1.1.0 / v1.2.0 / v1.2.1 release entries; flipped Generate "fader/slider/meter (knob-only today)"
  and "Boolean trigger components (buttons/toggles)" to ✅ where v1.2.0 delivered them.
- **BUGS:** header bump; retro-logged BUG-008 (handoff control-type) and BUG-009 (untrusted-SVG parse hardening) as
  resolved for traceability.

### Verdict
**Green.** Build 0/0, **171/171** tests, app boots clean (five tabs + first-run tutorial), `main` == origin, 0 open
bugs, **v1.2.0 live + signed, v1.2.1 staged**. The orphaned-v1.2.0-source integrity gap is closed (`b55380f`). Next:
ship 1.2.1; then website P2 getting-started guide. Recommended doc-version increment: **PATCH** (1.2.0 → 1.2.1,
stamp + the staged fixes).

---

## 2026-06-06 — v1.0.0 ship (★ #3 finish + onboarding) + website automation + handoff

**Type:** Feature delivery + major release + release-tooling fixes + reusable Stage-3 automation + handoff.

**Scope:** Finished the last ★ bet (layered PSD/SVG import), built the in-app onboarding tutorial +
About modal, cut **v1.0.0** (signed, live), closed the website-changelog gap with a project-agnostic
publisher, and reconciled every managed doc to 1.0.0.

### Delivered (code, each its own commit)
- **★ #3 step 3 — layered PSD/SVG import** (`03b441a`): `ILayeredImportService`/`LayeredImportService`
  (Svg.Skia groups + Magick.NET-Q8 PSD layers → named, behaviour-tagged, canvas-registered layers);
  `ImportedLayerRow` + VM mapping onto the existing `Layers` stack (no renderer change); deps
  Svg.Skia 5.0.0, Magick.NET-Q8-x64 14.13.1, SkiaSharp 3.119.0→3.119.2. App-only. +14 tests.
- **Onboarding tutorial + About modal** (`21e2994`): per-screen `TutorialViewModel`/`TutorialOverlay`
  (auto-open first run via new `ISettingsService`; Help button per tab; bundled sample knob via
  `IAssetService`; tooltips); solid centered `Border.dialog` token; About flyout → centered modal;
  `AppVersion` binds the live assembly version (was hardcoded "v0.6.0"). +11 tests.
- **Website-changelog automation** (`a2c6a16`): project-agnostic `Publish-WebsiteChangelog.ps1`
  (auto-draft from CHANGELOG → updates.json → optional commit/push); wired into `Invoke-Release.ps1`
  as optional Stage 3 (`-WebsiteRepo`). ASCII-only (BOM-safe).

### Release-tooling fixes (committed)
- **Signing** (`198230e`): the release initially signed nothing — the `AzureSignTool sign` call
  passed no credential, AND AzureSignTool speaks Key Vault and **403s** against Trusted Signing
  endpoints. Switched to **signtool + `Microsoft.Trusted.Signing.Client` dlib +
  `trusted-signing-metadata.json`** (owner's correction); now signs **both** the exe and the installer.
- **Script BOM:** a no-BOM save of `Invoke-Release.ps1` made PS 5.1 mojibake its em-dashes → parse
  failure mid-release; re-added the UTF-8 BOM and verified a clean parse (PACKAGING §9A corollary).

### Release
- `Invoke-Release.ps1 -Bump major`: gate **125/125** → 0.8.0 → **1.0.0** (csproj/.iss/CHANGELOG) →
  publish → **sign exe + installer** ("Succeeded") → Inno installer → commit `3849792` + tag `v1.0.0`
  + push. CI `auto-release.yml` VirusTotal-scanned and created the public GitHub Release (verified
  live; `StripKit-Setup-1.0.0-x64.exe`, 58.3 MB, signed).
- Website: v1.0.0 `updates.json` entry (StripKit-Website `c4fa2f6`) → Railway redeployed →
  stripkit.pro changelog verified live; download button auto-resolves to 1.0.0.

### Mini-audit of new code
- New services Avalonia-free where required (`LayeredImportService`, `SettingsService`: SkiaSharp/BCL
  only; `AssetService` is the app-layer Avalonia holder behind `IAssetService`). VMs hold no Avalonia
  UI types (the `Bitmap` alias excepted). Source-gen VMs `partial`. Renderer untouched → all prior
  goldens byte-identical (gated). `FilmstripEngine.cs` correctly **not** changed (parser + onboarding
  are app-only; no render-math change). DI complete. Build 0/0; no `async void` outside handlers; no
  `.Result`/`.Wait()`/`System.Drawing`.

### Doc reconciliation
- All managed docs → **1.0.0 / 2026-06-06**. ARCHITECTURE +§6.8 (layered import) +§6.9 (onboarding)
  +§3.2 service rows (Settings/Asset/LayeredImport); SOURCE_MAP lists new services/VM/view/models/
  asset + both release scripts (count → 125); TESTING +LayeredImport*/Tutorial*/Settings suites
  (count → 125); CHANGELOG `[1.0.0]`; ROADMAP marks v1.0.0 + all three ★ bets + onboarding P1 done;
  PACKAGING §8.4 (Stage-3 automation + reuse) + §9A (script-BOM); CLAUDE last-task + Current State.
  HANDOFF rewritten (very detailed).

### Verdict
**Green.** Build 0/0, **125/125** tests, app boots clean (four tabs + first-run tutorial), `main` ==
origin, 0 open bugs, **v1.0.0 live + signed**, stripkit.pro updated, all three ★ bets complete. Next:
website P2 getting-started guide; React/Web-Component + Unity/Godot code targets; `checkout@v4→v5`.

---

## 2026-06-05 — Independent doc reconciliation pass (post-v0.8.0 ship + handoff)

**Type:** Documentation reconciliation (doc-reconciler). No source code touched; app/tests not run.

**Scope:** Cross-checked all managed docs (`CLAUDE.md`, `docs/ROADMAP.md`, `BUGS.md`, `TESTING.md`,
`CHANGELOG.md`, `HANDOFF.md`, `AUDIT-LOG.md`, `ARCHITECTURE.md`, `SOURCE_MAP.md`, `PACKAGING.md`,
`KICKOFF.md`, `README.md`) against each other and the codebase after the v0.8.0 ship + ★ step-2
handoff.

### Ground truth verified
- `src/StripKit/StripKit.csproj` `<Version>` = **0.8.0**.
- `MainWindow.axaml` has **four** `TabItem`s: Create | Import | Batch | Skin.
- New source present: `Services/PointerExtractor.cs`, `ViewModels/SkinViewModel.cs` +
  `SkinControlEntry.cs`, `Views/SkinView.axaml(.cs)`, `Models/RenderLayer.cs`.
- New skill present: `.claude/skills/layer-aware-filmstrip-compositing/SKILL.md`.
- 19 test files; every file/suite named in `TESTING.md` exists (incl. `PointerExtractorTests`,
  `SkinViewModelTests`, `LayeredKnobRenderTests`). Suite size **98** taken as ground truth (not
  re-run this pass, per scope).

### Drift found
| # | Severity | Document | Issue | Resolution |
|---|----------|----------|-------|------------|
| 1 | MEDIUM | `README.md` | "`dotnet test` # **49 tests**" — stale current count. | **Fixed → 98.** |
| 2 | MEDIUM | `README.md` | Contributing: "must stay green (currently **49**)". | **Fixed → 98.** |
| 3 | MEDIUM | `README.md` | "StripKit is a **three-tab** app: Create, Import, and Batch." — omits the Skin tab. | **Fixed → four-tab; added a "Skin —" usage paragraph.** |
| 4 | LOW | `README.md` | Project-layout block + features list omitted Skin/PointerExtractor/CodeModels and importer resampling. | **Fixed** (layout VM/Views/Models/Services lines updated; importer + manifest feature bullets note resample + multi-control Skin; `ci.yml` added to the workflows line). |
| 5 | MEDIUM | `docs/KICKOFF.md` | **Body is materially stale at v0.7.0** despite a correct 0.8.0 header — see Flagged. | **Flagged** (header is trivially fine; body needs your call). |

### Flagged for manual review (not auto-fixed — substantive)
- **`docs/KICKOFF.md` body is a v0.7.0 snapshot.** The header is correct (`Version 0.8.0 ·
  last-updated 2026-06-05`), but the paste-in prompt prose still describes the *previous* state:
  - "**v0.7.0 has shipped** (the latest public GitHub Release)" and "`dotnet test` is **72/72
    green**" / "expect 72/72" — should be v0.8.0 and **98**.
  - Lists the app as importer/batch/manifest only and says "✅ code/component export" as the last
    done item — **omits the v0.8.0 work** (Skin tab, Batch-tab meter settings, importer resampling,
    layer-aware step 1) and the unreleased ★ step 2 (`PointerExtractor`).
  - "**Primary next task: vNext ★ #3 — layer-aware animation … build order: base+pointer PNGs →
    auto-pointer extraction → PSD/SVG import**" — the first two are now **done**; the real next task
    is **★ step 3 (layered PSD/SVG import)**, then the two owner-requested onboarding items
    (in-app tutorial; website getting-started guide).
  - "Open work" still lists "Batch-tab meter settings UI; importer frame-count resampling;
    multi-control manifests" as pending — all **shipped in v0.8.0**.
  - "add its **v0.7.0** `updates.json` entry" — should also include v0.8.0.
  This is a body rewrite to the v0.8.0/step-2 state (mirroring HANDOFF "Next Steps"). Left for you
  to decide rather than rewritten wholesale. Consistent with prior practice — KICKOFF has lagged
  before (see the 2026-06-04 reconcile, which flagged it stale at v0.5.0).

### Cross-doc agreement (checked, consistent)
- **Version headers:** all eleven managed docs + CLAUDE.md read `Version 0.8.0 · last-updated
  2026-06-05 · last-audit 2026-06-05` (KICKOFF uses the short no-`last-audit` form; HANDOFF uses
  YAML frontmatter 0.8.0 / 2026-06-05 / 2026-06-05). **No version-stamp drift.**
- **Four-tab story:** CLAUDE, ARCHITECTURE, SOURCE_MAP, HANDOFF, CHANGELOG, TESTING all say four
  tabs incl. Skin. (README fixed above; KICKOFF body flagged.)
- **v0.8.0 vs unreleased vs remaining:** CHANGELOG (`[0.8.0]` = the 3 gap features + layer step 1;
  `[Unreleased]` = step 2), ROADMAP (Releases section + ✅/🔄 markers), HANDOFF, CLAUDE last-task,
  and BUGS (BUG-007 follow-up resolved) **all agree** — shipped = Skin/Batch-meter/resample/step-1;
  unreleased = ★ step-2 (`PointerExtractor`); remaining ★ = step-3 (layered PSD/SVG import).
- **Test-count mentions:** TESTING (98), SOURCE_MAP (one stale-looking "**92**" — see note),
  CHANGELOG/ROADMAP/CLAUDE per-feature counts are correctly time-anchored ("suite 72", "84
  passing", "94→98", "Suite 94/98"). No bare "current = old-number" claims except README (fixed).
- **Cross-references:** spot-checked `docs/*.md` links (HANDOFF → ARCHITECTURE §5.6/§6.6/§6.7/§9.2,
  SOURCE_MAP, PACKAGING; README → CLAUDE/KICKOFF/ARCHITECTURE/SOURCE_MAP/BUGS) — all targets exist.

### Minor note (LOW, not fixed — borderline)
- `docs/SOURCE_MAP.md` line ~39 says `tests/StripKit.Tests/` "(**92**)" while TESTING.md and the
  header are at **98**. Reads as a slightly stale count rather than a time-anchored one. Left for
  the versioner to confirm (consistent everywhere else at 98); flagging rather than silently
  editing prose mid-sentence.

### Verdict
**In line after fixes.** Findings: 5 + 1 minor note (0 critical, 0 high, 3 medium, 2 low + 1 note);
**4 auto-fixed** (all in README), **1 manual review** (KICKOFF body rewrite), 1 minor note flagged.
Version stamps uniform at 0.8.0/2026-06-05; no code touched. Recommended doc-version increment:
**PATCH** (stamp/count finalize only) — or none, since headers are already current.

---

## 2026-06-05 — three gap features + v0.8.0 ship + ★ step 2 + handoff

**Type:** Feature delivery + release + session handoff

**Scope:** Built layer-aware step 1 (committed) + three carryover "gap" features, shipped them
as **v0.8.0**, added the `layer-aware-filmstrip-compositing` skill, then built ★ step 2
(auto-pointer extraction, unreleased), and reconciled every managed doc.

### Delivered (code, each its own commit)
- **★ Layer-aware step 1 — base + pointer** (`31c203b`): `RenderLayer`/`LayerBehavior` model +
  `FilmstripSettings.Layers`; `RenderLayers` in `RenderFrame`/`RenderStrip` (optional `layerArt`);
  Create-tab Base/Pointer slots + per-layer pointer pivot. Gated by defaults; mirrored in
  `FilmstripEngine.cs`. +12 tests.
- **Batch-tab meter settings** (`e126daf`): full meter panel + `MeterSourceIsBackdrop` toggle
  (layered on-art vs procedural-over-backdrop); `BatchProcessor` routes the file to source/background.
  +2 tests.
- **Skin tab — multi-control manifest** (`4a9e2ac`): `SkinViewModel`/`SkinControlEntry`/`SkinView`
  (4th tab) + `IManifestService.BuildManifest`; add-from-strip auto-detect, per-control detail
  editor (bounds + value range), skin metadata + window background, export to folder. +6 tests.
- **Importer resampling** (`322a80d`): `FilmstripImporter.Resample` (nearest-frame re-time). +2 tests.
- **★ Layer-aware step 2 — auto-pointer extraction** (`afca651`, unreleased): `PointerExtractor`
  (radial-symmetry residual) splits a flat knob into base + pointer with a confidence score;
  Create-tab "Auto-extract from flat knob…". App-only (not in `FilmstripEngine.cs`). +4 tests.
- **Skill** (`5fa2ba4`): `.claude/skills/layer-aware-filmstrip-compositing/SKILL.md` (linter 0/0).

### Mini-audit of new code
- No `async void` except event handlers; no `.Result`/`.Wait()`/`System.Drawing`. New services
  (`PointerExtractor`) are Avalonia-free; `SkinViewModel`/`BatchViewModel` use only `SkiaSharp`
  (`SKColor`/`SKBitmap`) — no Avalonia UI types. Renderer's layered path is gated (`Layers` empty →
  byte-identical), so all prior goldens hold. Source-generator VMs are `partial`. DI: `SkinViewModel`
  registered, exposed by `MainWindowViewModel`. `FilmstripEngine.cs` mirror updated for the layered
  path (step 1); correctly **not** updated for `PointerExtractor` (app-only). 0 build warnings.

### Release
- `Invoke-Release.ps1 -Bump minor` (run under Windows PowerShell 5.1 — `pwsh` absent; the script is
  encoding-safe): test gate **94/94** → 0.7.0 → 0.8.0 across csproj/.iss/CHANGELOG → publish → Inno
  installer → commit `65a9c4f` + tag `v0.8.0` + push. CI `auto-release.yml` VirusTotal-scanned and
  created the public release (verified live; 33.5 MB asset). The release commit staged only the
  version files + installer; the two stray untracked files were excluded.

### Doc reconciliation
- All managed docs bumped to **0.8.0** (CLAUDE, HANDOFF, ROADMAP, ARCHITECTURE, SOURCE_MAP,
  CHANGELOG, TESTING, BUGS, AUDIT-LOG, KICKOFF, PACKAGING). ARCHITECTURE gained §5.6/§6.6/§6.7
  (layers, slots, extraction) + §9.2 (Skin tab) + §8/§10.2 updates (batch meter, resample); SOURCE_MAP
  + TESTING list the new files/suites (count 72 → 98); CHANGELOG `[0.8.0]` + `[Unreleased]` (step 2);
  ROADMAP marks the shipped items ✅, adds a Releases section, and **two new owner-requested items**
  (in-app help/tutorial; website getting-started guide). BUGS: 0 open (the BUG-007 batch-meter
  follow-up is now resolved by the Batch meter feature). HANDOFF rewritten.

### Verdict
**Green.** Build 0/0, **98/98** tests, app boots clean (four tabs), `main` == origin, 0 open bugs,
v0.8.0 live. Next: ★ step 3 — layered PSD/SVG import (scope the parser library + license first).

---

## 2026-06-04 — vNext ★ #1 + #2 shipped (v0.7.0) + handoff

**Type:** Feature delivery + release + session handoff

**Scope:** Built the first two ★ vNext features, shipped v0.7.0 via the pipeline, and
reconciled all managed docs.

### Delivered (code)
- **Value-arc / fill-ring** (knobs): new `RenderValueArc` + `StrokePaint` in
  `SkiaFilmstripRenderer`, 11 Skia-free `FilmstripSettings` arc fields gated on
  `ShowValueArc` (default off → existing goldens byte-identical), Create-tab "VALUE ARC"
  panel, mirrored in `FilmstripEngine.cs`. +8 tests.
- **Code / component export:** new `Models/CodeModels.cs` (`CodeTarget` +
  `CodeSnippetRequest`), pure `ICodeSnippetService`/`CodeSnippetService` (JUCE / CSS-HTML /
  iPlug2 / HISE), DI registration, VM wiring (`ExportCode` + per-target toggles + live
  `GeneratedCode`), Create-tab "CODE EXPORT" panel + copy-to-clipboard. +15 tests.

### Mini-audit of new code
- No `async void` except the `OnCopyCode` event handler; no `.Result`/`.Wait()`/
  `System.Drawing`. The new services are Avalonia-free (`CodeSnippetService` is BCL-only).
  Renderer additions are gated/knob-scoped so existing output is unchanged. VM funnel keeps
  code-only inputs (`ParameterId`, `CodePreviewTarget`) off the image-render path.
  `FilmstripEngine.cs` mirror updated (arc path) and consistent by inspection.

### Release
- `Invoke-Release.ps1 -Bump minor`: test gate **72/72**, version 0.6.0 → 0.7.0 across
  csproj / .iss / CHANGELOG, self-contained publish, Inno installer, commit `fe24ca3` +
  tag `v0.7.0` + push. CI `auto-release.yml` VirusTotal-scanned and created the public
  release (verified live; 33.5 MB installer asset).

### Doc reconciliation
- All managed docs bumped to **0.7.0** (CLAUDE, HANDOFF, ROADMAP, ARCHITECTURE, SOURCE_MAP,
  CHANGELOG, TESTING, BUGS, AUDIT-LOG). ARCHITECTURE gained §5.5 (value arc) + §9.1 (code
  export); SOURCE_MAP + TESTING list the new files/suites (count 49 → 72); ROADMAP marks the
  two ★ items done. `.gitignore` now ignores `tests/**/TestResults/`.

### Verdict
**Green.** Build 0/0, 72/72 tests, app boots clean, working tree clean, `main` == origin,
0 open bugs, v0.7.0 live. Next: vNext ★ #3 — layer-aware animation (base+pointer MVP first).

---

## 2026-06-04 — Session handoff: full doc overhaul + OSS hardening + code audit (v0.6.0)

**Type:** Session handoff

**Scope:** Full doc overhaul + OSS hardening + code audit. Checked: `ARCHITECTURE.md`,
`PACKAGING.md`, `ROADMAP.md`, `BUGS.md`, `TESTING.md`, `HANDOFF.md`, `CLAUDE.md`,
and source code (`auto-release.yml`, `BatchViewModel`, `MainWindow`, `FilmstripEngine.cs`,
`Assets/README.txt`).

### Findings

| # | Severity | Area | Finding |
|---|----------|------|---------|
| H1 | HIGH | CI | `auto-release.yml` missing — CI pipeline absent from repo |
| H2 | HIGH | Docs | Stale font reference in documentation (JetBrains Mono) |
| H3 | HIGH | Repo | No community files (LICENSE, public README, metadata) |
| M1 | MEDIUM | Code | CTS (CancellationTokenSource) leak in `BatchViewModel` |
| M2 | MEDIUM | Code | `DispatcherTimer` not stopped on window close |
| M3 | MEDIUM | Docs | Meter mode missing from `ARCHITECTURE.md` component table |
| M4 | MEDIUM | Docs | `KICKOFF.md` test count stale (41, should be 49) |
| L1 | LOW | Repo | Community files now fixed (LICENSE.md, public README) |
| L4 | LOW | Docs | Cross-platform caveat not documented (Inno Setup = Windows only) |
| L5 | LOW | CI | `actions/checkout@v3` — should be `@v4` |

### Actions taken

- **All High findings resolved:** CI workflow restored; font references corrected;
  LICENSE + public README + repo metadata added (OSS hardening complete).
- **All Medium findings resolved:** CTS disposed in `BatchViewModel`; timer stopped
  on window close; meter mode added to `ARCHITECTURE.md`; `KICKOFF.md` count updated.
- **Low findings:** L1 resolved (community files added). L4 cross-platform caveat
  added to `PACKAGING.md`. L5 (`checkout@v4`) deferred — non-breaking, separate PR.

### Verdict

**Green.** All docs consistent. 49/49 tests pass. Repo OSS-ready.

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
