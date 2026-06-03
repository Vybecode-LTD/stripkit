# KICKOFF — StripKit

> Version 0.5.0 · last-updated 2026-06-03
>
> Paste the prompt below into a fresh Claude Code session at the repo root. It
> orients the agent at the **current** state and the next task. The full plan lives
> in `docs/ROADMAP.md`; the deep design reference is `docs/ARCHITECTURE.md`.

---

## Prompt to paste

You are picking up **StripKit**, a C#/Avalonia 11 desktop tool that turns a single
transparent PNG into an animated control filmstrip (knobs, vertical faders,
horizontal sliders), **imports** existing strips, and emits a `skin.json` manifest.

**Before doing anything, read `CLAUDE.md`, `docs/SOURCE_MAP.md`, and
`docs/ARCHITECTURE.md` in full**, then `docs/HANDOFF.md` for current state. Treat the
skills in `.claude/skills/` as active (`avalonia-skia-interop`,
`avalonia-drag-drop-files`, `live-preview-render-loop`, `image-regression-testing`,
`filmstrip-importer-engine`, `plugin-asset-manifest`), plus the globally-installed
skills named in `CLAUDE.md` (`csharp-mastery`, `avalonia-mvvm-patterns`, etc.).

### Current state (not a scaffold)

The app is **working and fully built**: ✅ Phase 0 verify · ✅ Phase 1 drag-and-drop ·
✅ Phase 2 importer (Import tab) · ✅ Phase 3 manifest export · ✅ Phase 4 golden-image
tests · ✅ Phase 5 batch (Batch tab) · ✅ Phase 6 meter (Create-tab "Meter" type).
`dotnet build` is 0/0 and `dotnet test` is **41/41 green**. Establish that baseline
first:

```bash
dotnet build StripKit.sln -c Debug   # expect 0/0
dotnet test                          # expect 41/41
```

If red, fix that before anything else.

### What NOT to do

- Do **not** rewrite or "simplify" `Services/SkiaFilmstripRenderer.cs`; the
  rotation/translation math and supersampling are deliberate. The `(N-1)` angle
  divisor is correct (last frame lands exactly on max) — do not change it to `N`.
- Do **not** move view-model logic into code-behind, or reference Avalonia UI types
  from view models (the preview `Bitmap` alias is the one allowed exception).
- Do **not** replace the conventions (dark theme, `#e8440a`, JetBrains Mono,
  CommunityToolkit source generators, compiled bindings).
- Do **not** "fix" the importer's detected count by trusting it — it is a guess; the
  UI makes it editable and the user verifies (see `docs/ARCHITECTURE.md` §7).
- Remember `FilmstripEngine.cs` (repo root) is a hand-maintained mirror of the
  renderer — keep it in sync if you touch the math.

### Next task — Phase 7 (packaging & distribution, the final phase)

Produce a signed, single-file Windows build (and optionally an installer) so the tool
ships to non-developers. Skill: `dotnet-installer-publishing` (global). Likely shape:
`dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishSelfContained=true`,
an app icon, code-signing, and optionally an Inno/WiX/MSIX installer + auto-update.
**Confirm the distribution target** (single exe vs. installer; signing certificate)
before building the pipeline. **Done when:** a single-file exe runs on a clean Windows
machine without the SDK. After Phase 7 the roadmap is complete.

### Already-resolved questions

- Importer UI: **second tab** (done). Batch UI: **third tab** (done). Test framework:
  **xUnit** (done).
- Meter (Phase 6, done): both procedural + layered (auto-selected), all four fill
  directions, discrete default + continuous toggle — design was signed off.
- Phase 7 packaging: confirm single-exe vs. installer + signing before building it.
