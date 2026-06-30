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
                 + "fader, Horizontal slider, or Meter. StripKit applies sensible frame sizes and "
                 + "defaults for each type.",
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
                 + "arc, and — for layered art — “Import layered file (SVG / PSD)…” keeps the body crisp "
                 + "while only the pointer rotates.",
        },
        new TutorialStep
        {
            Title = "4 · Export & wire it up",
            Body = "Click Export for one stacked PNG (plus @2x and a skin.json manifest if ticked). Tick "
                 + "CODE EXPORT and StripKit also writes ready-to-paste loader code for JUCE, CSS/HTML, "
                 + "iPlug2, or HISE — no hand-wiring.",
            Tip = "Re-open this guide for any tab any time from the Help button, top-right.",
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
                 + "write an @2x copy and a skin.json per strip.",
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
            Title = "Generate · AI knob art from a prompt",
            Body = "No source image? The Generate tab uses your own OpenAI, Gemini, or Claude API key "
                 + "to draw a layered knob SVG — a static body plus a separate rotating pointer — ready "
                 + "to animate. You bring the key; the art is generated on demand.",
            Tip = "Your API key is encrypted with your Windows account and never leaves this PC except to call the provider you choose.",
        },
        new TutorialStep
        {
            Title = "1 · Pick a provider & paste your key",
            Body = "Choose a provider, paste the matching API key, and click Save key (it's stored "
                 + "encrypted for next time). Optionally pin a specific model — otherwise a sensible "
                 + "default is used.",
        },
        new TutorialStep
        {
            Title = "2 · Describe it & Generate",
            Body = "Pick a style, accent colour, and add any extra direction (\"amber LED, knurled "
                 + "edge\"). Click Generate; each click is a fresh take, so Regenerate until you like "
                 + "it. The preview is the real imported result, so what you see will import cleanly.",
        },
        new TutorialStep
        {
            Title = "3 · Use in Create",
            Body = "Click “Use in Create” to send the knob to the Create tab as a layered import — only "
                 + "the pointer rotates — then set the frame count and Export the filmstrip. Or Save the "
                 + "SVG to disk to reuse anywhere.",
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
            Tip = "Numbered frames like knob_0001.png … knob_0128.png are detected and ordered automatically.",
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
            Title = "2 · Resample & export",
            Body = "Optionally re-time to a standard count (32 / 64 / 128) — render fewer expensive frames "
                 + "and ship more. Then Assemble & export writes the stacked PNG, plus @2x, a skin.json, and "
                 + "ready-to-paste loader code if ticked.",
            Tip = "Render your sequence with the same sweep and frame count your runtime expects, so the last frame lands on the maximum.",
        },
    ];
}
