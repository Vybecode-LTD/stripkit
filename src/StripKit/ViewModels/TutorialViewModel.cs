using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripKit.Models;
using StripKit.Services;

namespace StripKit.ViewModels;

/// <summary>The screens (tabs), in tab order, that each have their own tutorial.</summary>
public enum TutorialScreen { Create = 0, Import = 1, Batch = 2, Skin = 3, Generate = 4, Assemble = 5 }

/// <summary>
/// The re-openable "Getting Started" guided overlay. Each screen (Create / Import / Batch / Skin)
/// has its own short walkthrough; <see cref="Open"/> shows the one for the requested tab. Opens
/// automatically on first run (tracked via <see cref="ISettingsService"/>) and is re-openable any
/// time from the header Help button. Holds no Avalonia UI types; the host view model wires
/// <see cref="LoadSampleRequested"/> to the sample-knob load.
/// </summary>
public partial class TutorialViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IReadOnlyDictionary<TutorialScreen, IReadOnlyList<TutorialStep>> _byScreen;

    public TutorialViewModel(ISettingsService settings)
    {
        _settings = settings;
        _byScreen = new Dictionary<TutorialScreen, IReadOnlyList<TutorialStep>>
        {
            [TutorialScreen.Create] = BuildCreateSteps(),
            [TutorialScreen.Import] = BuildImportSteps(),
            [TutorialScreen.Batch] = BuildBatchSteps(),
            [TutorialScreen.Skin] = BuildSkinSteps(),
            [TutorialScreen.Generate] = BuildGenerateSteps(),
            [TutorialScreen.Assemble] = BuildAssembleSteps(),
        };
        _steps = _byScreen[TutorialScreen.Create];
    }

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenName))]
    private TutorialScreen _screen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep), nameof(StepProgress), nameof(IsFirstStep),
                              nameof(IsLastStep), nameof(NextLabel), nameof(CurrentOffersSample),
                              nameof(CurrentHasTip))]
    private IReadOnlyList<TutorialStep> _steps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep), nameof(StepProgress), nameof(IsFirstStep),
                              nameof(IsLastStep), nameof(NextLabel), nameof(CurrentOffersSample),
                              nameof(CurrentHasTip))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    private int _currentIndex;

    public TutorialStep CurrentStep => Steps[Math.Clamp(CurrentIndex, 0, Steps.Count - 1)];
    public string StepProgress => $"Step {CurrentIndex + 1} of {Steps.Count}";
    public bool IsFirstStep => CurrentIndex == 0;
    public bool IsLastStep => CurrentIndex == Steps.Count - 1;
    public string NextLabel => IsLastStep ? "Done" : "Next";
    public bool CurrentOffersSample => CurrentStep.OffersSample;
    public bool CurrentHasTip => !string.IsNullOrWhiteSpace(CurrentStep.Tip);
    public string ScreenName => Screen switch
    {
        TutorialScreen.Create => "Create",
        TutorialScreen.Import => "Import",
        TutorialScreen.Batch => "Batch",
        TutorialScreen.Skin => "Skin",
        TutorialScreen.Generate => "Generate",
        TutorialScreen.Assemble => "Assemble",
        _ => "",
    };

    /// <summary>Raised when the user clicks "Load sample knob"; the host VM loads the bundled asset.</summary>
    public event Action? LoadSampleRequested;

    /// <summary>Opens the tutorial for the given screen index (the current tab). Always restarts at
    /// step 1 of that screen's walkthrough.</summary>
    [RelayCommand]
    private void Open(int screenIndex)
    {
        Screen = (TutorialScreen)Math.Clamp(screenIndex, 0, _byScreen.Count - 1);
        Steps = _byScreen[Screen];
        CurrentIndex = 0;
        IsOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanPrevious))]
    private void Previous()
    {
        if (CurrentIndex > 0) CurrentIndex--;
    }

    private bool CanPrevious() => CurrentIndex > 0;

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep) Finish();
        else CurrentIndex++;
    }

    [RelayCommand]
    private void Skip() => Finish();

    [RelayCommand]
    private void LoadSample() => LoadSampleRequested?.Invoke();

    /// <summary>Opens the Create walkthrough automatically the first time the app is run (until the
    /// user has finished or skipped any tutorial once). Called once at startup by the host VM.</summary>
    public void MaybeShowOnFirstRun()
    {
        if (!_settings.Settings.HasSeenTutorial)
            Open((int)TutorialScreen.Create);
    }

    private void Finish()
    {
        IsOpen = false;
        if (!_settings.Settings.HasSeenTutorial)
        {
            _settings.Settings.HasSeenTutorial = true;
            _settings.Save();
        }
    }

    // ---- per-screen walkthroughs ----

    private static IReadOnlyList<TutorialStep> BuildCreateSteps() =>
    [
        new TutorialStep
        {
            Title = "Welcome to StripKit 👋",
            Body = "The Create tab turns one transparent PNG of a control into an animated filmstrip "
                 + "(sprite sheet) for audio-plugin GUIs. Start by loading your art with “Load source "
                 + "image…” on the left — or try a sample to see the whole flow right now.",
            Tip = "Knob art works best with a little transparent margin so it doesn't clip when it spins.",
            OffersSample = true,
        },
        new TutorialStep
        {
            Title = "1 · Pick a component type",
            Body = "Choose what you're making from the Component Type dropdown — Rotary knob, Vertical "
                 + "fader, Horizontal slider, Meter, Button, or Toggle. StripKit applies sensible frame "
                 + "sizes and defaults for each type; buttons and toggles swap the sidebar to discrete "
                 + "on/off state art instead of a rotation or travel.",
        },
        new TutorialStep
        {
            Title = "2 · Align & scrub the preview",
            Body = "Drag the preview slider to scrub the frames. If a knob wobbles as it turns, click "
                 + "Auto-center (or use the alignment crosshair) so it spins around its true centre.",
            Tip = "The preview is continuous; what you export is the frame count in the sidebar.",
        },
        new TutorialStep
        {
            Title = "3 · Frames, value arc & layered art",
            Body = "Pick a frame count (32 / 64 / 128 are standard). Knobs can add a Serum-style value "
                 + "arc, and layered art (a static body plus a separate rotating pointer) keeps the "
                 + "body crisp while only the pointer moves — load the two layers by hand, auto-extract "
                 + "them from a flat image, or “Import layered file (SVG / PSD)…” directly.",
            Tip = "STATIC BACKGROUND (optional) lets you composite a fixed layer — a knob well, a meter housing — behind everything else, drawn once and never transformed.",
        },
        new TutorialStep
        {
            Title = "4 · Sprite-grid layout & parameter law",
            Body = "Under QUALITY & OUTPUT, Sprite layout can pack frames into an R×C grid atlas "
                 + "instead of one long strip, for loaders that expect a 2D sheet. Under PARAMETER LAW, "
                 + "a Skew or Logarithmic curve remaps the sweep to track a plugin's real parameter law "
                 + "(a log-frequency knob, a dB fader) instead of a straight divisor.",
            Tip = "Both default to Strip / Linear, so leaving them alone reproduces the classic StripKit output exactly.",
        },
        new TutorialStep
        {
            Title = "5 · Save your setup as a preset",
            Body = "Happy with a render setup? Give it a name under PRESETS (top of this panel) and "
                 + "click Save — it captures the type, frames, sweep, layout, meter/arc styling, and "
                 + "export options (not the loaded art). Apply it again any time to reload the whole "
                 + "setup in one click.",
        },
        new TutorialStep
        {
            Title = "6 · Export & wire it up",
            Body = "Click Export for one stacked PNG (plus an @2x/@3x/@4x copy and a skin.json manifest "
                 + "if ticked). Tick CODE EXPORT and StripKit also writes ready-to-paste loader code for "
                 + "JUCE, CSS/HTML, iPlug2, HISE, or React — no hand-wiring.",
            Tip = "After exporting, \"Show in folder\" jumps straight to the file. Re-open this guide for any tab any time from the Help button, top-right.",
        },
    ];

    private static IReadOnlyList<TutorialStep> BuildImportSteps() =>
    [
        new TutorialStep
        {
            Title = "Import · re-use an existing strip",
            Body = "The Import tab works from a finished filmstrip PNG rather than a single frame. Drop "
                 + "one onto this tab (or use the load button) and StripKit detects its layout — frame "
                 + "count, orientation, and kind — from the image dimensions.",
        },
        new TutorialStep
        {
            Title = "1 · Check the detection & scrub",
            Body = "Review the detected frame count and orientation. Square strips with an ambiguous "
                 + "count are flagged — fix the count if needed — then scrub the detected frames to "
                 + "confirm they line up.",
        },
        new TutorialStep
        {
            Title = "2 · Extract, re-stack or resample",
            Body = "Pull a single frame out, flip a strip between vertical and horizontal stacking, or "
                 + "resample it to a new frame count (nearest-frame, so a moving pointer never ghosts). "
                 + "Then export the result.",
            Tip = "\"Show in folder\" appears under the transport controls after your first export on this tab.",
        },
    ];

    private static IReadOnlyList<TutorialStep> BuildBatchSteps() =>
    [
        new TutorialStep
        {
            Title = "Batch · a whole folder at once",
            Body = "The Batch tab renders many sources into many filmstrips in one pass. Pick an input "
                 + "folder of source images and an output folder for the strips.",
        },
        new TutorialStep
        {
            Title = "1 · Set the template",
            Body = "The render template — component type, frame count, frame size, meter options, and the "
                 + "layered/backdrop toggle — is applied to every file in the folder. Optionally also "
                 + "write an @2x–@4x copy, a skin.json, and ready-to-paste loader code per strip.",
        },
        new TutorialStep
        {
            Title = "2 · Run & monitor",
            Body = "Click Run and watch the per-item progress; Cancel stops cleanly between files, and a "
                 + "single bad file is skipped without sinking the whole batch. A summary reports what "
                 + "succeeded and what failed.",
        },
    ];

    private static IReadOnlyList<TutorialStep> BuildSkinSteps() =>
    [
        new TutorialStep
        {
            Title = "Skin · one manifest, many controls",
            Body = "The Skin tab builds a single skin.json that binds several exported filmstrips to "
                 + "several plugin parameters — a whole control surface in one file.",
        },
        new TutorialStep
        {
            Title = "1 · Add your controls",
            Body = "Add a control from an existing strip (its frame count / size / orientation / kind are "
                 + "auto-detected) or add a blank one, then edit its id, type, parameter id, asset, frame "
                 + "size, on-screen bounds, and value range in the detail editor.",
        },
        new TutorialStep
        {
            Title = "2 · Skin metadata & export",
            Body = "Set the skin name, author, design resolution, and window background, then “Export "
                 + "skin.json…” writes the combined manifest to a folder — ready for your loader.",
            Tip = "Pairs perfectly with the Create tab's code export for a complete, wired skin.",
        },
    ];

    private static IReadOnlyList<TutorialStep> BuildGenerateSteps() =>
    [
        new TutorialStep
        {
            Title = "Generate · AI control art from a prompt",
            Body = "No source image? The Generate tab uses your own OpenAI, Gemini, or Claude API key "
                 + "(or any OpenAI-compatible endpoint) to draw layered SVG art — knobs get a static "
                 + "body plus a separate rotating pointer; buttons, toggles, and meters get an off/on "
                 + "pair — ready to animate. You bring the key; the art is generated on demand.",
            Tip = "Your API key is encrypted with your Windows account and never leaves this PC except to call the provider you choose.",
        },
        new TutorialStep
        {
            Title = "1 · Pick a provider & paste your key",
            Body = "Choose a provider, paste the matching API key, and click Save key (it's stored "
                 + "encrypted for next time). Picking Custom points at any OpenAI-compatible endpoint — "
                 + "OpenRouter, Ollama, LM Studio — with your own model id. Optionally pin a specific "
                 + "model — otherwise a sensible default is used.",
        },
        new TutorialStep
        {
            Title = "2 · Describe it & Generate",
            Body = "Pick what to make (knob / fader / slider / meter / button / toggle), a style, "
                 + "accent colour, and any extra direction (\"amber LED, knurled edge\"). An Avoid "
                 + "field keeps unwanted details out. Click Generate; each click is a fresh take, so "
                 + "Regenerate until you like it — the preview is the real imported result, so what "
                 + "you see will import cleanly.",
            Tip = "SEEDS save a whole style bundle (colours, effects, notes) by name, so your next control starts from the same look.",
        },
        new TutorialStep
        {
            Title = "3 · One prompt, a whole family",
            Body = "Tick several control types under MATCHING SET and click Generate set to create a "
                 + "consistent family in one pass — a head start on a full skin. Or use Generate "
                 + "variations for several takes of just the current control, to pick your favourite.",
        },
        new TutorialStep
        {
            Title = "4 · Refine & reference images",
            Body = "Not quite right? Type a change (\"thicker pointer, warmer accent\") and click "
                 + "Refine — it revises the current SVG rather than starting over. Or click “Describe "
                 + "a reference image…” to have a vision model describe a screenshot you like and fold "
                 + "that into your style direction.",
        },
        new TutorialStep
        {
            Title = "5 · Use in Create",
            Body = "Click “Use in Create” to send the control to the Create tab as a layered import — "
                 + "honouring whatever type you generated (knob, fader/slider, button/toggle, or "
                 + "meter) — then set the frame count and Export the filmstrip. Or Save the SVG to "
                 + "disk to reuse anywhere.",
            Tip = "Generated art is clean modern vector — great for synth-style controls.",
        },
    ];

    private static IReadOnlyList<TutorialStep> BuildAssembleSteps() =>
    [
        new TutorialStep
        {
            Title = "Assemble · pre-rendered frames → filmstrip",
            Body = "The Assemble tab stacks a folder of individually-rendered frames — for example a "
                 + "path-traced PNG sequence from Blender, KeyShot, or Octane — into one filmstrip. Choose "
                 + "a folder (or drop the frames onto the preview) and StripKit natural-sorts them into "
                 + "render order.",
            Tip = "Numbered frames like knob_0001.png … knob_0128.png are detected and ordered automatically — EXR / HDR / 16-bit frames are read too.",
        },
        new TutorialStep
        {
            Title = "1 · Check the order & layout",
            Body = "Scrub the preview to confirm the frames run from minimum to maximum. Pick the control "
                 + "type and stack direction; if a stray frame is the wrong size, choose how to reconcile "
                 + "it, and re-centre on content if your 3D object drifts between frames.",
        },
        new TutorialStep
        {
            Title = "2 · Catch render mistakes before you assemble",
            Body = "Click “Check frames” under RENDER QC to scan for object drift, a missing "
                 + "transparent background, blank frames, or premultiplied edges — the exact mistakes "
                 + "an offline render can introduce. Seeing dark halos around anti-aliased edges? Tick "
                 + "“Un-premultiply alpha” to remove them.",
        },
        new TutorialStep
        {
            Title = "3 · Render fewer, ship more",
            Body = "Under RESAMPLE, re-time to a standard count (32 / 64 / 128). Nearest picks the "
                 + "closest rendered frame and never ghosts; Crossfade blends the two bracketing frames "
                 + "to synthesise in-betweens — render ~32 expensive frames and ship 64 or 128 for "
                 + "slow, smooth motion.",
        },
        new TutorialStep
        {
            Title = "4 · Add a glow pass",
            Body = "If your renderer can output a separate emission/glow AOV, add it under EMISSION / "
                 + "GLOW PASS — one frame per beauty frame. It's additively composited over the beauty "
                 + "render at whatever intensity you set, so a lit LED or screen reads like real "
                 + "emitted light instead of being baked flat.",
        },
        new TutorialStep
        {
            Title = "5 · Export & wire it up",
            Body = "Assemble & export writes the stacked PNG — plus an @2x–@4x copy, a skin.json "
                 + "manifest, and ready-to-paste loader code for JUCE, CSS/HTML, iPlug2, HISE, or "
                 + "React if ticked. \"Show in folder\" jumps straight to the exported file afterwards.",
            Tip = "Render your sequence with the same sweep and frame count your runtime expects, so the last frame lands on the maximum.",
        },
        new TutorialStep
        {
            Title = "6 · Haven't rendered yet? Plan the render itself",
            Body = "RENDER RECIPE (at the bottom of this panel) exports a Blender script, or an "
                 + "engine-agnostic CSV/JSON table, of the exact frame/value/angle table StripKit "
                 + "expects — so your offline render lands each frame on the right value before you "
                 + "ever reach this tab.",
        },
    ];
}
