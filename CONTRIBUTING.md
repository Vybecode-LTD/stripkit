# Contributing to StripKit

Thanks for your interest in contributing! StripKit is a C#/.NET 9 / Avalonia 11 desktop
tool that turns transparent PNG art into animated filmstrip sprite sheets for audio-plugin
GUIs. Whether you are fixing a bug, adding a feature, or improving docs - all contributions
are welcome.

## Before you start

Read these to orient yourself quickly:
- `CLAUDE.md` -- project conventions and current state
- `docs/KICKOFF.md` -- fast bootstrap for a new session
- `docs/ARCHITECTURE.md` -- how the app is built (the real deep-dive)
- `docs/SOURCE_MAP.md` -- file-by-file map of the repo
- `docs/ROADMAP.md` -- what is planned; check here before proposing a feature

## Setup

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Vybecode-LTD/stripkit.git
cd stripkit
dotnet build StripKit.sln -c Debug    # expect 0 errors / 0 warnings
dotnet test                            # expect 49/49 green
```

## Workflow

1. **Fork** the repo.
2. **Branch** off `main` (e.g. `feat/value-arc-generator`, `fix/batch-cancellation`).
3. **Make your change** -- see the conventions below.
4. **Add tests** -- every bug fix needs a test that fails before your fix and passes after. New files get a test file.
5. **Run `dotnet test`** -- must be green before submitting.
6. **Open a PR** against `main` -- the PR template will walk you through the checklist.

CI runs automatically on every PR (build + tests on Windows).

## House conventions

**Design system**
- The UI uses the **Depth** machined-grey dark theme (vendored `Depth/Depth.axaml`, mapped onto StripKit's keys in `App.axaml`) -- re-use the tokens, never hard-code hex values.
- Accent: `#f25914` (ember). Font: **Verdana-led sans-serif** (`Verdana, Segoe UI, Arial`) for labels/body; **monospace for numerics only** (`NumericUpDown` + numeric readouts).

**MVVM boundary**
- View models must **not** reference Avalonia UI types (the preview `Bitmap` alias is the one allowed exception).
- Source-generator classes must be `partial`. Use compiled bindings (`x:DataType`).

**Code rules**
- No `System.Drawing` -- use SkiaSharp for all image work.
- No `.Result` or `.Wait()` on Tasks -- use `await`.
- `async void` only for event handlers.

## The renderer is sacred

`Services/SkiaFilmstripRenderer.cs` is the mathematical heart. Please **do not rewrite it**.

- The `(N-1)` angle divisor is **deliberate** -- it ensures the last frame lands exactly on the max angle.
- If you change the renderer math, you **must** mirror the change in `FilmstripEngine.cs` (the standalone copy at the repo root).

## Reporting bugs

Use the **Bug report** issue template. For rendering bugs, include source image dimensions and the settings you used.
