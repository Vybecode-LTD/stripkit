# Workflow and conventions: StripKit

> Moved verbatim from `CLAUDE.md` on 2026-10-15 (lean-context pass). Section numbers are unchanged, so a citation such as `CLAUDE.md §5a` is found here by its heading.

> Moved from `CLAUDE.md` on 2026-10-15.

## What this is

StripKit is a C#/Avalonia desktop tool that turns a single transparent PNG
into an animated **filmstrip** (sprite sheet) for audio-plugin GUI controls:
rotary knobs, vertical faders, horizontal sliders, **meters**, and **buttons**
(discrete on/off state frames). The output PNG is consumed
by a JUCE-style LookAndFeel filmstrip loader (one frame shown per parameter value).
It is the asset-production companion to the GUI skinning system / VybeForge.

> Moved from `CLAUDE.md` on 2026-10-15.

## Start here

1. Read `docs/SOURCE_MAP.md` (where everything lives) and this file.
2. `docs/ARCHITECTURE.md` is the deep design reference; `docs/HANDOFF.md` is the
   current state + next task; `docs/ROADMAP.md` is the full phased plan;
   `docs/KICKOFF.md` is the paste-in prompt for a new session.
3. Other managed docs: `docs/TESTING.md`, `docs/CHANGELOG.md`, `docs/BUGS.md`,
   `docs/AUDIT-LOG.md`. Packaging + release flow: `docs/PACKAGING.md`.
4. The skills in `.claude/skills/` are scoped to this repo — use them.

> Moved from `CLAUDE.md` on 2026-10-15.

## OSS / Contributing

- License: **MIT** (`LICENSE` at repo root).
- Public README with badges (`README.md`).
- `CONTRIBUTING.md` — contribution guide.
- `.github/ISSUE_TEMPLATE/` — `bug_report.md` + `feature_request.md`.
- `.github/pull_request_template.md` — PR checklist.

> Moved from `CLAUDE.md` on 2026-10-15.

## Run / build

- `dotnet run --project src/StripKit` (needs the .NET 9 SDK).
- If NuGet warns of a SkiaSharp version conflict with Avalonia, align the
  `SkiaSharp` version in the csproj to Avalonia's transitive one
  (`dotnet list package --include-transitive`).
- Use `python -m pip` style invocation in any Python helper scripts (bare
  `pip`/`python` are not on PATH in this environment).

> Moved from `CLAUDE.md` on 2026-10-15.

## Project skills (`.claude/skills/`)

- `avalonia-skia-interop` — SkiaSharp rendering inside Avalonia; pixel interop.
- `avalonia-drag-drop-files` — file drag-and-drop into an Avalonia view (Phase 1).
- `live-preview-render-loop` — the responsive preview pattern this VM embodies.
- `image-regression-testing` — golden-image tests to lock renderer output (Phase 4).
- `filmstrip-importer-engine` — detect/re-slice existing strips (Phase 2).
- `plugin-asset-manifest` — JSON manifest binding strips to parameters (Phase 3).
- `layer-aware-filmstrip-compositing` — the layered-knob (base + pointer) compositing model.
- `release-source-integrity-guard` — ensure a release tag can rebuild its own artifact (commit feature source before the release script runs; portable).

> Moved from `CLAUDE.md` on 2026-10-15.

## Globally-installed skills to lean on

`csharp-mastery`, `avalonia-mvvm-patterns`, `avalonia-mvvm-app-scaffold`,
`avalonia-layout-patterns`, `cc-regression-tester-pro`, `dotnet-installer-publishing`,
`debug-protocol`, `code-review-discipline`.

> Moved from `CLAUDE.md` on 2026-10-15.

## Filmstrip conventions (do not change without reason)

- Frames stack **vertically** by default; frame 0 = minimum value, frame N-1 = max.
- Rotary angle for frame i: `start + (end - start) * i / (N - 1)`. The `(N-1)`
  divisor is deliberate — it lands the last frame exactly on the max. Not `N`.
- Default sweep 270° (frame 0 at -135° / 7 o'clock, last at +135° / 5 o'clock).
- 32-bit RGBA, transparent background. Standard frame counts: 32 / 64 / 128.
- Ship a HiDPI variant (the Export scale is selectable — `@2x` / `@3x` / `@4x` via the `HiDpiScale`
  property; the export suffix, the render/upscale factor, and the manifest hi-res asset all follow it;
  default 2).
- Knob art should carry ~10% transparent margin so corners don't clip on rotation.
- **Untrusted SVG** (AI replies, imported files) is parsed only through `SafeXml.Parse`
  (DTD prohibited) — never a bare `XDocument.Parse`.

> Moved from `CLAUDE.md` on 2026-10-15.

## House conventions

- **Depth design system** (machined-grey, dark; rebranded from Obsidian glass in v1.4.0): ember accent
  `#f25914`, **sans for labels/body** (`Verdana, Segoe UI, Arial, sans-serif`) and **monospace for
  numerics only** (`JetBrains Mono, Consolas, …` — `NumericUpDown` + numeric readouts). The Depth tokens
  are vendored in `src/StripKit/Depth/Depth.axaml` (`DepthBg`/`DepthChrome*`/`DepthInset`/`DepthLine*`,
  `DepthInk*`, `DepthEmber*`, `DepthRadius*`, `DepthRaise*`) and **mapped** onto StripKit's existing keys
  in `App.axaml` (`AccentBrush`/`AccentHiBrush`, `Text1/2/3Brush`, the `GlassFill*`/`GlassBorder*` surface
  keys — now solid greys, the input-well + dropdown + checkbox/slider keys, `SectionTextBrush`,
  `DialogFillGradient`), promoted **global** so all six tabs + dialogs share one look. The window is a
  **solid `DepthBg`** base (no acrylic / no glow — the old `ObsidianAcrylic` material is gone). Panels are
  `Border.card`; **neutral buttons** are dark raised "keycaps" (`Button:not(.accent)` — bevel + drop
  shadow, lighter on hover), **primary** buttons use the `.accent` class (ember face). Re-use these
  mapped keys / Depth tokens — don't hard-code hex, and keep monospace to numerics (not labels/body).
- View models never reference Avalonia UI types (testability). Source-generator
  classes must be `partial`. Use `Path.Combine`, never raw separators.
- No `System.Drawing`; no `.Result`/`.Wait()`; `async void` only for event handlers.
- Do NOT rewrite `SkiaFilmstripRenderer` or re-scaffold the project; extend it.

> Moved from `CLAUDE.md` on 2026-10-15.

## Conventions for working with the owner (Big Haas)

- After delivering something useful, suggest follow-on **skills** (even loosely
  related) and ask whether to save reusable outputs to artifacts.
- When creating a skill, emit a `.skill` file with a description field < 1024
  chars so it is one-click installable; run the skill-authoring-linter first.

