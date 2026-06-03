# BUGS — StripKit

> Version 0.5.0 · last-updated 2026-06-03 · last-audit 2026-06-03

**Open bugs: 0.** **Resolved: 2.**

Each bug fixed gets a root cause and a regression guard. Both bugs below were
**pre-existing scaffold defects** surfaced by the first real compilation during
Phase 0 — neither was caused by the FilmstripForge → StripKit rename.

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

---

## Notes

- No runtime bugs are currently known. The renderer output is locked by golden-image
  tests; a future intentional render change must update baselines (see
  `docs/TESTING.md`).
- Known *limitations* (not bugs) live in `docs/ROADMAP.md` / `docs/ARCHITECTURE.md`:
  importer detection is a dimension-based guess (editable + verified), the importer
  cannot yet resample frame *count*, and the manifest UI emits a single control.
