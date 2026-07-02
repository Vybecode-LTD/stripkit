using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using StripKit.Helpers;
using StripKit.Models;
using StripKit.Services;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace StripKit.ViewModels;

/// <summary>
/// Backs the "Generate" tab: use the user's own OpenAI / Gemini / Claude key to generate a layered
/// knob SVG (a static <c>body</c> group + a rotating <c>pointer</c> group), preview it, and hand it
/// to the Create tab to build the filmstrip. The generated SVG is validated by running it through the
/// real <see cref="ILayeredImportService"/> — so a preview here guarantees Create can import it. Keys
/// are stored encrypted via <see cref="ISecretStore"/>. Holds no Avalonia UI types (the
/// <see cref="AvBitmap"/> preview mirrors the Create tab's existing pattern).
/// </summary>
public partial class GenerateViewModel : ViewModelBase
{
    private readonly IAssetGenerationService _generation;
    private readonly ISecretStore _secrets;
    private readonly ISettingsService _settings;
    private readonly ILayeredImportService _layeredImport;
    private readonly IFileDialogService _dialogs;

    private CancellationTokenSource? _cts;
    private string? _lastSvgPath;          // the temp SVG of the current result (handed to Create)
    private ComponentType _lastControlType; // the control type that produced the current result
    private int _genCount;
    // Per-instance tag so concurrent generations (a second app window, or parallel tests) never collide
    // on the same temp file name.
    private readonly string _sessionTag = Guid.NewGuid().ToString("N")[..8];

    // ---- matching-set state ----
    private CancellationTokenSource? _setCts;
    private int _setRun;                                  // bumps each set generation (temp-file naming)
    private readonly List<string> _setSvgPaths = new();   // temp SVGs of the current set, cleared on regenerate

    public GenerateViewModel(IAssetGenerationService generation, ISecretStore secrets,
                             ISettingsService settings, ILayeredImportService layeredImport,
                             IFileDialogService dialogs)
    {
        _generation = generation;
        _secrets = secrets;
        _settings = settings;
        _layeredImport = layeredImport;
        _dialogs = dialogs;

        Providers = _generation.AvailableProviders.ToArray();

        // Restore the last-used provider, then load its stored key + preferred model directly (setting
        // the backing field avoids firing OnSelectedProviderChanged during construction).
        var initial = _settings.Settings.GenerateProvider;
        _selectedProvider = Providers.Contains(initial) ? initial
                          : Providers.Length > 0 ? Providers[0] : AiProvider.Claude;
        _apiKey = _secrets.Get(_selectedProvider) ?? "";
        _hasKey = _secrets.Has(_selectedProvider);
        _model = ModelPrefFor(_selectedProvider);
        _suggestedModels = _generation.ModelsFor(_selectedProvider);
        _keyStatus = _hasKey ? "Key saved (loaded)." : "No key stored for this provider.";
        _customBaseUrl = _settings.Settings.GenerateCustomBaseUrl ?? "";

        // The matching-set picker: a sensible default spread (knob + fader + button + meter ticked).
        foreach (var (type, label, on) in new[]
        {
            (ComponentType.RotaryKnob, "Knob", true),
            (ComponentType.VerticalFader, "Fader", true),
            (ComponentType.HorizontalSlider, "Slider", false),
            (ComponentType.Meter, "Meter", true),
            (ComponentType.Button, "Button", true),
            (ComponentType.Toggle, "Toggle", false),
        })
        {
            var option = new SetTypeOption(type, label, on);
            option.PropertyChanged += (_, _) => GenerateSetCommand.NotifyCanExecuteChanged();
            SetTypeOptions.Add(option);
        }

        // Prompt seeds: the built-in library first, then the user's saved seeds.
        foreach (var s in GenerationSeedLibrary.BuiltIn) Seeds.Add(s);
        foreach (var s in _settings.Settings.GenerateSeeds ?? new())
        {
            s.IsBuiltIn = false;
            Seeds.Add(s);
        }
    }

    /// <summary>Raised by "Use in Create" with the temp SVG path + the control type that generated it;
    /// the host VM imports it on the Create tab as that type (knob / fader / slider / button).</summary>
    public event Action<string, ComponentType>? UseInCreateRequested;

    // ---- choices ----
    public AiProvider[] Providers { get; }
    public GenerationStyle[] Styles { get; } =
        [GenerationStyle.Modern, GenerationStyle.Minimal, GenerationStyle.Skeuomorphic, GenerationStyle.Vintage, GenerationStyle.Flat];
    public int[] CanvasSizes { get; } = [256, 512, 768, 1024];

    /// <summary>The control types the AI can generate art for. Meters are generated as a layered
    /// off/on pair (the unlit + fully-lit meter) — the renderer reveals the lit art up to the value.</summary>
    public ComponentType[] GenerateComponentTypes { get; } =
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider, ComponentType.Meter, ComponentType.Button, ComponentType.Toggle];

    private IReadOnlyList<string> _suggestedModels = Array.Empty<string>();
    public IReadOnlyList<string> SuggestedModels
    {
        get => _suggestedModels;
        private set => SetProperty(ref _suggestedModels, value);
    }

    // ---- provider / key / model ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomProvider))]
    private AiProvider _selectedProvider;

    /// <summary>True when the OpenAI-compatible Custom provider is selected — gates the base-URL field.</summary>
    public bool IsCustomProvider => SelectedProvider == AiProvider.Custom;

    /// <summary>Base URL for the Custom provider, persisted to settings (the key stays in the secret store).</summary>
    [ObservableProperty] private string _customBaseUrl = "";

    partial void OnCustomBaseUrlChanged(string value)
    {
        _settings.Settings.GenerateCustomBaseUrl = value;
        _settings.Save();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveKeyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateSetCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateVariationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DescribeReferenceCommand))]
    private string _apiKey;

    [ObservableProperty] private string _model;
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private string _keyStatus;

    // ---- prompt shaping ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMeterType))]
    private ComponentType _generateControlType = ComponentType.RotaryKnob;
    [ObservableProperty] private GenerationStyle _style = GenerationStyle.Modern;
    [ObservableProperty] private string _styleNotes = "";
    [ObservableProperty] private string _avoid = "";
    [ObservableProperty] private string _accentColorHex = "#E8440A";
    [ObservableProperty] private string _bodyColorHex = "";
    [ObservableProperty] private int _canvasSize = 512;

    // ---- style effects ----
    [ObservableProperty] private bool _hasDropShadow;
    [ObservableProperty] private bool _hasOuterGlow;
    [ObservableProperty] private bool _hasBevel;
    [ObservableProperty] private bool _hasMetallicSheen;

    // ---- meter options ----
    /// <summary>Generate a wide landscape meter (fills left→right) instead of the default tall one
    /// (fills bottom→top). Only meaningful when the control type is Meter.</summary>
    [ObservableProperty] private bool _meterHorizontal;

    // ---- result / status ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefineCommand))]
    [NotifyCanExecuteChangedFor(nameof(DescribeReferenceCommand))]
    private bool _isGenerating;

    /// <summary>Plain-language change for "Refine" (e.g. "thicker pointer, warmer accent").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefineCommand))]
    private string _refineInstruction = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private AvBitmap? _previewImage;

    [ObservableProperty] private string? _generatedSvg;
    [ObservableProperty] private string? _lastRawResponse;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseInCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSvgCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefineCommand))]
    private bool _hasResult;

    [ObservableProperty] private string _statusMessage =
        "Pick a provider, paste your API key, describe the knob, then Generate.";

    public bool HasPreview => PreviewImage is not null;

    /// <summary>True when the meter control type is selected — gates the meter-only options (orientation).</summary>
    public bool IsMeterType => GenerateControlType == ComponentType.Meter;

    /// <summary>The exact system + user prompt the current settings would send — for the "show the
    /// prompt" expander. Recomputed whenever a prompt-shaping input changes (see OnPropertyChanged).</summary>
    public string PromptPreview
    {
        get
        {
            var req = BuildBaseRequest() with
            {
                ComponentType = GenerateControlType,
                Layered = AssetGenerationService.IsLayeredType(GenerateControlType),
            };
            var (system, user) = _generation.BuildPrompts(req);
            return $"SYSTEM PROMPT\n{system}\n\nUSER PROMPT\n{user}";
        }
    }

    private static readonly HashSet<string> PromptShapingProps =
    [
        nameof(GenerateControlType), nameof(Style), nameof(StyleNotes), nameof(Avoid),
        nameof(AccentColorHex), nameof(BodyColorHex), nameof(CanvasSize), nameof(MeterHorizontal),
        nameof(HasDropShadow), nameof(HasOuterGlow), nameof(HasBevel), nameof(HasMetallicSheen),
    ];

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null && PromptShapingProps.Contains(e.PropertyName))
            OnPropertyChanged(nameof(PromptPreview));
    }

    // ---- prompt seeds (saved style bundles) ----

    /// <summary>The seed library shown in the picker: the built-ins plus the user's saved seeds.</summary>
    public ObservableCollection<GenerationSeed> Seeds { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySeedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSeedCommand))]
    private GenerationSeed? _selectedSeed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSeedCommand))]
    private string _seedName = "";

    // ---- matching set ----

    /// <summary>The control types offered in the matching-set picker (checkable; populated in the ctor).</summary>
    public ObservableCollection<SetTypeOption> SetTypeOptions { get; } = new();

    /// <summary>The generated set's per-control results (preview + handoff path + raw SVG).</summary>
    public ObservableCollection<GeneratedSetResult> SetResults { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateSetCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateVariationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DescribeReferenceCommand))]
    private bool _isGeneratingSet;

    /// <summary>How many takes "Generate variations" produces of the selected control.</summary>
    public int[] VariationCounts { get; } = [2, 4, 6, 8];
    [ObservableProperty] private int _variationCount = 4;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSetCommand))]
    private bool _hasSetResults;

    [ObservableProperty] private string _setStatus =
        "Pick the controls you want, then Generate set — one consistent family from your current style settings.";

    // ---- reactions ----

    partial void OnSelectedProviderChanged(AiProvider value)
    {
        ApiKey = _secrets.Get(value) ?? "";
        HasKey = _secrets.Has(value);
        SuggestedModels = _generation.ModelsFor(value);
        Model = ModelPrefFor(value);
        KeyStatus = HasKey ? "Key saved (loaded)." : "No key stored for this provider.";
        _settings.Settings.GenerateProvider = value;
        _settings.Save();
    }

    private string ModelPrefFor(AiProvider p) =>
        _settings.Settings.GenerateModels.TryGetValue(p.ToString(), out var m) && !string.IsNullOrWhiteSpace(m)
            ? m
            : _generation.DefaultModelFor(p);

    // ---- commands ----

    private bool CanGenerate() => !IsGenerating && !string.IsNullOrWhiteSpace(ApiKey);

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsGenerating = true;
        try
        {
            var model = (Model ?? "").Trim();

            // Remember the provider + model the user committed to (null-safe for settings written by
            // an older build that predates these fields).
            _settings.Settings.GenerateProvider = SelectedProvider;
            (_settings.Settings.GenerateModels ??= new())[SelectedProvider.ToString()] = model;
            _settings.Save();

            var shownModel = string.IsNullOrWhiteSpace(model) ? _generation.DefaultModelFor(SelectedProvider) : model;
            StatusMessage = $"Generating with {SelectedProvider} ({shownModel})…";

            var request = BuildBaseRequest() with
            {
                ComponentType = GenerateControlType,
                Layered = AssetGenerationService.IsLayeredType(GenerateControlType),
            };

            // Up to two attempts: if the first result is structurally weak (a knob with no rotating
            // pointer, or a button/toggle/meter missing its second state) auto-regenerate once before
            // surfacing it — a common weak-model miss the user would otherwise hit Regenerate on.
            GenerationResult result = null!;
            GeneratedPreview? preview = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                result = await _generation.GenerateAsync(request, SelectedProvider, ApiKey, model, ct);
                LastRawResponse = result.RawResponse;
                if (!result.Success)
                {
                    StatusMessage = result.Error ?? "Generation failed.";
                    return;
                }

                // Validate + preview OFF the UI thread: write the temp SVG, import it through the SAME
                // pipeline the Create tab uses (so a preview here guarantees Create can import it), and
                // composite the at-rest bitmap — all CPU-bound, so the UI thread only assigns the result.
                preview = await Task.Run(() => BuildPreview(result.Svg!), ct);
                if (preview is null)
                {
                    StatusMessage = "The model returned an SVG, but its layers couldn't be read. Try Regenerate.";
                    return;
                }

                if (attempt == 2 || !IsWeakStructure(GenerateControlType, preview.RotateCount, preview.FrameCount))
                    break;
                StatusMessage = "The first take was missing its moving part — regenerating once…";
                TryDelete(preview.Path);   // discard the rejected attempt's temp SVG — _lastSvgPath won't track it
            }

            GeneratedSvg = result.Svg;
            _lastSvgPath = preview!.Path;
            _lastControlType = GenerateControlType;
            PreviewImage = preview.Image;   // best-effort — a result stands even if preview can't render
            HasResult = true;
            StatusMessage = DescribeResult(preview.LayerCount, preview.RotateCount, preview.FrameCount);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generation cancelled.";
        }
        catch (Exception ex)
        {
            // A generation must NEVER take the app down — surface the real error instead, and keep
            // the full detail in the raw-response panel for diagnosis.
            LastRawResponse = string.IsNullOrEmpty(LastRawResponse) ? ex.ToString() : $"{LastRawResponse}\n\n{ex}";
            StatusMessage = $"Generation failed: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private bool CanCancel() => IsGenerating;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanSaveKey() => !string.IsNullOrWhiteSpace(ApiKey);

    [RelayCommand(CanExecute = nameof(CanSaveKey))]
    private void SaveKey()
    {
        _secrets.Set(SelectedProvider, ApiKey);
        HasKey = _secrets.Has(SelectedProvider);
        KeyStatus = HasKey ? "Key saved (encrypted on this PC)." : "No key stored.";
        StatusMessage = $"API key saved for {SelectedProvider}, encrypted with your Windows account.";
    }

    [RelayCommand]
    private void ClearKey()
    {
        _secrets.Clear(SelectedProvider);
        ApiKey = "";
        HasKey = false;
        KeyStatus = "No key stored.";
        StatusMessage = $"Cleared the stored {SelectedProvider} key.";
    }

    // ---- prompt seeds ----

    private bool CanApplySeed() => SelectedSeed is not null;

    [RelayCommand(CanExecute = nameof(CanApplySeed))]
    private void ApplySeed()
    {
        var s = SelectedSeed;
        if (s is null) return;
        Style = s.Style;
        StyleNotes = s.StyleNotes;
        Avoid = s.Avoid;
        AccentColorHex = s.AccentColor;
        BodyColorHex = s.BodyColor;
        CanvasSize = s.CanvasSize;
        HasDropShadow = s.HasDropShadow;
        HasOuterGlow = s.HasOuterGlow;
        HasBevel = s.HasBevel;
        HasMetallicSheen = s.HasMetallicSheen;
        StatusMessage = $"Applied the \"{s.Name}\" seed — tweak anything, then Generate.";
    }

    private bool CanSaveSeed() => !string.IsNullOrWhiteSpace(SeedName);

    [RelayCommand(CanExecute = nameof(CanSaveSeed))]
    private void SaveSeed()
    {
        var name = SeedName.Trim();
        var seed = new GenerationSeed
        {
            Name = name, Style = Style, StyleNotes = StyleNotes, Avoid = Avoid,
            AccentColor = AccentColorHex, BodyColor = BodyColorHex, CanvasSize = CanvasSize,
            HasDropShadow = HasDropShadow, HasOuterGlow = HasOuterGlow, HasBevel = HasBevel,
            HasMetallicSheen = HasMetallicSheen, IsBuiltIn = false,
        };

        // Overwrite an existing user seed of the same name, else append (built-ins are never touched).
        var store = _settings.Settings.GenerateSeeds ??= new();
        store.RemoveAll(x => !x.IsBuiltIn && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        store.Add(seed);
        _settings.Save();

        var dup = Seeds.FirstOrDefault(x => !x.IsBuiltIn && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (dup is not null) Seeds.Remove(dup);
        Seeds.Add(seed);
        SelectedSeed = seed;
        SeedName = "";
        StatusMessage = $"Saved the \"{name}\" seed.";
    }

    private bool CanDeleteSeed() => SelectedSeed is { IsBuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanDeleteSeed))]
    private void DeleteSeed()
    {
        var s = SelectedSeed;
        if (s is null || s.IsBuiltIn) return;
        (_settings.Settings.GenerateSeeds ??= new())
            .RemoveAll(x => !x.IsBuiltIn && x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        Seeds.Remove(s);
        SelectedSeed = null;
        StatusMessage = $"Deleted the \"{s.Name}\" seed.";
    }

    /// <summary>Builds the success status, warning when the layer structure won't actually animate —
    /// a knob with no rotating pointer, or a button without both on/off state layers (a common weak-model
    /// failure the preview alone wouldn't flag).</summary>
    private string DescribeResult(int layerCount, int rotate, int frames) => GenerateControlType switch
    {
        ComponentType.RotaryKnob when rotate == 0 =>
            $"Generated a {layerCount}-layer knob, but no rotating pointer was detected — it won't animate. Try Regenerate.",
        ComponentType.RotaryKnob =>
            $"Generated a {layerCount}-layer knob ({rotate} set to rotate). Use in Create to build the filmstrip, or Save the SVG.",
        ComponentType.Button when frames < 2 =>
            $"Generated button art, but only {frames} on/off state layer(s) were found (expected off + on). Try Regenerate.",
        ComponentType.Button =>
            $"Generated a button with {frames} state layers (off / on). Use in Create to build the filmstrip, or Save the SVG.",
        ComponentType.Toggle when frames < 2 =>
            $"Generated toggle art, but only {frames} on/off state layer(s) were found (expected off + on). Try Regenerate.",
        ComponentType.Toggle =>
            $"Generated an on/off toggle with {frames} state layers. Use in Create to build the filmstrip, or Save the SVG.",
        ComponentType.Meter when layerCount < 2 =>
            $"Generated meter art, but only {layerCount} layer(s) were found (expected an off + on group). Try Regenerate.",
        ComponentType.Meter =>
            $"Generated a {layerCount}-layer meter (off / on). Use in Create to build the filmstrip, or Save the SVG.",
        ComponentType.VerticalFader =>
            "Generated a vertical fader cap. Use in Create to build the filmstrip, or Save the SVG.",
        ComponentType.HorizontalSlider =>
            "Generated a horizontal slider cap. Use in Create to build the filmstrip, or Save the SVG.",
        _ =>
            $"Generated a {layerCount}-layer control. Use in Create to build the filmstrip, or Save the SVG.",
    };

    /// <summary>Whether a freshly-imported result won't actually animate — a knob with no rotating
    /// pointer, or a button/toggle/meter missing its second state — and so is worth one auto-retry.</summary>
    private static bool IsWeakStructure(ComponentType type, int rotate, int frames) => type switch
    {
        ComponentType.RotaryKnob => rotate == 0,
        ComponentType.Button or ComponentType.Toggle or ComponentType.Meter => frames < 2,
        _ => false,
    };

    private bool CanUseInCreate() => HasResult && _lastSvgPath is not null;

    [RelayCommand(CanExecute = nameof(CanUseInCreate))]
    private void UseInCreate()
    {
        if (_lastSvgPath is null) return;
        UseInCreateRequested?.Invoke(_lastSvgPath, _lastControlType);
        StatusMessage = "Sent to the Create tab — set the frame count, then Export the filmstrip PNG.";
    }

    private bool CanSaveSvg() => HasResult;

    [RelayCommand(CanExecute = nameof(CanSaveSvg))]
    private async Task SaveSvgAsync()
    {
        if (GeneratedSvg is null) return;
        var path = await _dialogs.SaveSvgAsync($"{SelectedProvider.ToString().ToLowerInvariant()}-knob");
        if (path is null) return;
        try
        {
            await File.WriteAllTextAsync(path, GeneratedSvg);
            StatusMessage = $"Saved SVG → {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save: {ex.Message}";
        }
    }

    private bool CanRefine() => HasResult && !IsGenerating && !string.IsNullOrWhiteSpace(RefineInstruction) && GeneratedSvg is not null;

    [RelayCommand(CanExecute = nameof(CanRefine))]
    private async Task RefineAsync()
    {
        if (GeneratedSvg is null) return;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsGenerating = true;
        try
        {
            var model = (Model ?? "").Trim();
            var req = BuildBaseRequest() with
            {
                ComponentType = _lastControlType,
                Layered = AssetGenerationService.IsLayeredType(_lastControlType),
            };
            StatusMessage = "Refining the current control…";

            var result = await _generation.RefineAsync(req, GeneratedSvg, RefineInstruction, SelectedProvider, ApiKey, model, ct);
            LastRawResponse = result.RawResponse;
            if (!result.Success)
            {
                StatusMessage = result.Error ?? "Refine failed.";
                return;
            }

            var preview = await Task.Run(() => BuildPreview(result.Svg!), ct);
            if (preview is null)
            {
                StatusMessage = "The revised SVG couldn't be read. Try rephrasing the change.";
                return;
            }

            GeneratedSvg = result.Svg;
            _lastSvgPath = preview.Path;
            PreviewImage = preview.Image;
            HasResult = true;
            RefineInstruction = "";
            StatusMessage = "Refined — Use in Create, Save, or refine again.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Refine cancelled.";
        }
        catch (Exception ex)
        {
            LastRawResponse = string.IsNullOrEmpty(LastRawResponse) ? ex.ToString() : $"{LastRawResponse}\n\n{ex}";
            StatusMessage = $"Refine failed: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private bool CanDescribeReference() => !IsGenerating && !IsGeneratingSet && !string.IsNullOrWhiteSpace(ApiKey);

    [RelayCommand(CanExecute = nameof(CanDescribeReference))]
    private async Task DescribeReferenceAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;

        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(path); }
        catch (Exception ex) { StatusMessage = $"Couldn't read that image: {ex.Message}"; return; }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsGenerating = true;
        try
        {
            var model = (Model ?? "").Trim();
            StatusMessage = $"Looking at {Path.GetFileName(path)} to capture its style…";
            var desc = await _generation.DescribeReferenceAsync(bytes, MediaTypeFor(path), SelectedProvider, ApiKey, model, ct);
            if (!desc.Success)
            {
                StatusMessage = desc.Error ?? "Couldn't describe the image.";
                return;
            }

            // Fold the description into the extra-direction box so it shapes the next generation
            // (kept editable — append rather than clobber anything the user already typed).
            StyleNotes = string.IsNullOrWhiteSpace(StyleNotes) ? desc.Text! : $"{StyleNotes}\n{desc.Text}";
            StatusMessage = "Captured the reference style into Extra direction — tweak it, then Generate.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Reference cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reference failed: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private static string MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/png",
    };

    // ---- matching-set commands ----

    private bool CanGenerateSet() =>
        !IsGeneratingSet && !string.IsNullOrWhiteSpace(ApiKey) && SetTypeOptions.Any(o => o.Include);

    [RelayCommand(CanExecute = nameof(CanGenerateSet))]
    private async Task GenerateSetAsync()
    {
        _setCts?.Cancel();
        _setCts?.Dispose();
        _setCts = new CancellationTokenSource();
        var ct = _setCts.Token;

        var types = SetTypeOptions.Where(o => o.Include).Select(o => o.Type).ToList();
        if (types.Count == 0) return;

        IsGeneratingSet = true;
        ClearSetResults();
        _setRun++;
        try
        {
            var model = (Model ?? "").Trim();
            _settings.Settings.GenerateProvider = SelectedProvider;
            (_settings.Settings.GenerateModels ??= new())[SelectedProvider.ToString()] = model;
            _settings.Save();

            SetStatus = $"Generating {types.Count} matching controls with {SelectedProvider}…";

            var items = await _generation.GenerateSetAsync(BuildBaseRequest(), types, SelectedProvider, ApiKey, model, ct);

            // Build the previews off the UI thread (each import + composite is CPU-bound); the UI thread
            // only adds the finished results.
            var built = await Task.Run(() =>
            {
                var list = new List<GeneratedSetResult>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    list.Add(BuildSetResult(items[i], i));
                return list;
            }, ct);

            foreach (var r in built) SetResults.Add(r);
            HasSetResults = SetResults.Count > 0;

            int ok = built.Count(r => r.IsSuccess);
            SetStatus = ok == built.Count
                ? $"Generated {ok} matching controls. Send each to Create, or Save the set."
                : $"Generated {ok}/{built.Count} controls — Regenerate the ones that failed.";
        }
        catch (OperationCanceledException)
        {
            SetStatus = "Set generation cancelled.";
        }
        catch (Exception ex)
        {
            SetStatus = $"Set generation failed: {ex.Message}";
        }
        finally
        {
            IsGeneratingSet = false;
        }
    }

    private bool CanGenerateVariations() => !IsGeneratingSet && !string.IsNullOrWhiteSpace(ApiKey);

    [RelayCommand(CanExecute = nameof(CanGenerateVariations))]
    private async Task GenerateVariationsAsync()
    {
        _setCts?.Cancel();
        _setCts?.Dispose();
        _setCts = new CancellationTokenSource();
        var ct = _setCts.Token;

        IsGeneratingSet = true;
        ClearSetResults();
        _setRun++;
        try
        {
            var model = (Model ?? "").Trim();
            _settings.Settings.GenerateProvider = SelectedProvider;
            (_settings.Settings.GenerateModels ??= new())[SelectedProvider.ToString()] = model;
            _settings.Save();

            var type = GenerateControlType;
            var req = BuildBaseRequest() with { ComponentType = type, Layered = AssetGenerationService.IsLayeredType(type) };
            SetStatus = $"Generating {VariationCount} {Humanize(type).ToLowerInvariant()} variations…";

            var results = await _generation.GenerateVariationsAsync(req, VariationCount, SelectedProvider, ApiKey, model, ct);

            var built = await Task.Run(() =>
            {
                var list = new List<GeneratedSetResult>(results.Count);
                for (int i = 0; i < results.Count; i++)
                    list.Add(BuildSetResult(new GenerationSetItem(type, results[i]), i, $"{Humanize(type)} #{i + 1}"));
                return list;
            }, ct);

            foreach (var r in built) SetResults.Add(r);
            HasSetResults = SetResults.Count > 0;

            int ok = built.Count(r => r.IsSuccess);
            SetStatus = $"Generated {ok}/{built.Count} variations — pick one with Use in Create, or Regenerate any.";
        }
        catch (OperationCanceledException)
        {
            SetStatus = "Variations cancelled.";
        }
        catch (Exception ex)
        {
            SetStatus = $"Variations failed: {ex.Message}";
        }
        finally
        {
            IsGeneratingSet = false;
        }
    }

    private bool CanCancelSet() => IsGeneratingSet;

    [RelayCommand(CanExecute = nameof(CanCancelSet))]
    private void CancelSet() => _setCts?.Cancel();

    [RelayCommand]
    private void UseSetItemInCreate(GeneratedSetResult? item)
    {
        if (item?.SvgPath is null) return;
        UseInCreateRequested?.Invoke(item.SvgPath, item.ComponentType);
        SetStatus = $"Sent the {item.Label.ToLowerInvariant()} to the Create tab.";
    }

    [RelayCommand]
    private async Task SaveSetItemAsync(GeneratedSetResult? item)
    {
        if (item?.Svg is null) return;
        var path = await _dialogs.SaveSvgAsync($"{StyleSlug()}-{item.ComponentType.ToString().ToLowerInvariant()}");
        if (path is null) return;
        try { await File.WriteAllTextAsync(path, item.Svg); SetStatus = $"Saved {Path.GetFileName(path)}"; }
        catch (Exception ex) { SetStatus = $"Couldn't save: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task RegenerateSetItemAsync(GeneratedSetResult? item)
    {
        if (item is null || IsGeneratingSet) return;
        int index = SetResults.IndexOf(item);
        if (index < 0) return;

        // A fresh source every time — reusing _setCts with ??= keeps a source a prior CancelSet already
        // cancelled, which would make this regenerate abort immediately (a silent no-op).
        _setCts?.Cancel();
        _setCts?.Dispose();
        _setCts = new CancellationTokenSource();
        var ct = _setCts.Token;
        IsGeneratingSet = true;
        try
        {
            var model = (Model ?? "").Trim();
            var req = BuildBaseRequest() with
            {
                ComponentType = item.ComponentType,
                Layered = AssetGenerationService.IsLayeredType(item.ComponentType),
            };
            SetStatus = $"Regenerating the {item.Label.ToLowerInvariant()}…";
            var result = await _generation.GenerateAsync(req, SelectedProvider, ApiKey, model, ct);
            var rebuilt = await Task.Run(() => BuildSetResult(new GenerationSetItem(item.ComponentType, result), index), ct);
            if (index < SetResults.Count)
            {
                var outgoing = SetResults[index];
                SetResults[index] = rebuilt;
                outgoing.PreviewImage?.Dispose();   // free the replaced preview's native Skia bitmap
            }
            SetStatus = rebuilt.IsSuccess ? $"Regenerated the {item.Label.ToLowerInvariant()}." : rebuilt.StatusMessage;
        }
        catch (OperationCanceledException) { SetStatus = "Regeneration cancelled."; }
        catch (Exception ex) { SetStatus = $"Couldn't regenerate: {ex.Message}"; }
        finally { IsGeneratingSet = false; }
    }

    private bool CanSaveSet() => HasSetResults && SetResults.Any(r => r.IsSuccess);

    [RelayCommand(CanExecute = nameof(CanSaveSet))]
    private async Task SaveSetAsync()
    {
        var folder = await _dialogs.OpenFolderAsync("Choose a folder for the matching-set SVGs");
        if (folder is null) return;
        int saved = 0;
        foreach (var r in SetResults.Where(r => r.IsSuccess && r.Svg is not null))
        {
            var name = $"{StyleSlug()}-{r.ComponentType.ToString().ToLowerInvariant()}.svg";
            try { await File.WriteAllTextAsync(Path.Combine(folder, name), r.Svg!); saved++; }
            catch { /* skip the odd unwritable file */ }
        }
        SetStatus = $"Saved {saved} SVG(s) to {Path.GetFileName(folder)}.";
    }

    // ---- helpers ----

    /// <summary>The style inputs common to both the single generate and the matching set — everything
    /// except the per-control <see cref="GenerationRequest.ComponentType"/>/<c>Layered</c>, which the
    /// caller sets. Keeping one builder is what makes a set visually consistent with a single generate.</summary>
    private GenerationRequest BuildBaseRequest() => new()
    {
        Style = Style,
        StyleNotes = StyleNotes,
        Avoid = Avoid,
        AccentColor = AccentColorHex,
        BodyColor = BodyColorHex,
        CanvasSize = CanvasSize,
        HasDropShadow = HasDropShadow,
        HasOuterGlow = HasOuterGlow,
        HasBevel = HasBevel,
        HasMetallicSheen = HasMetallicSheen,
        MeterHorizontal = MeterHorizontal,
    };

    /// <summary>Turns one generated set item into a bindable result: writes the SVG to its own temp file,
    /// imports it (validating the layer structure), and composites an at-rest preview. Pure CPU work —
    /// call it inside a <see cref="Task.Run(Action)"/>.</summary>
    private GeneratedSetResult BuildSetResult(GenerationSetItem item, int index, string? labelOverride = null)
    {
        var label = labelOverride ?? Humanize(item.ComponentType);
        if (!item.Result.Success)
            return new GeneratedSetResult
            {
                ComponentType = item.ComponentType, Label = label, IsSuccess = false,
                StatusMessage = item.Result.Error ?? "Generation failed.",
            };

        var path = WriteSetSvg(item.Result.Svg!, index);
        var import = _layeredImport.Import(path);
        if (import is null || import.Layers.Count == 0)
            return new GeneratedSetResult
            {
                ComponentType = item.ComponentType, Label = label, Svg = item.Result.Svg, IsSuccess = false,
                StatusMessage = "The model returned an SVG, but its layers couldn't be read.",
            };

        var image = TryCompositePreview(import);   // composites, then disposes the layer art
        _setSvgPaths.Add(path);
        return new GeneratedSetResult
        {
            ComponentType = item.ComponentType, Label = label, SvgPath = path, PreviewImage = image,
            Svg = item.Result.Svg, IsSuccess = true, StatusMessage = "Ready.",
        };
    }

    /// <summary>Writes one set member's SVG to its own per-run temp file (siblings coexist, unlike the
    /// single-result temp which is replaced each generation).</summary>
    private string WriteSetSvg(string svg, int index)
    {
        var dir = Path.Combine(Path.GetTempPath(), "StripKit");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"set-{_sessionTag}-{_setRun}-{index}.svg");
        File.WriteAllText(path, svg);
        return path;
    }

    private void ClearSetResults()
    {
        // Unbind first (Clear notifies the UI to drop its references), then dispose the native Skia-backed
        // preview bitmaps — up to N per run — instead of leaving them for GC finalization.
        var outgoing = SetResults.ToList();
        SetResults.Clear();
        HasSetResults = false;
        foreach (var r in outgoing) r.PreviewImage?.Dispose();
        foreach (var p in _setSvgPaths) TryDelete(p);
        _setSvgPaths.Clear();
    }

    private string StyleSlug() => Style.ToString().ToLowerInvariant();

    private static string Humanize(ComponentType type) => type switch
    {
        ComponentType.RotaryKnob => "Knob",
        ComponentType.VerticalFader => "Fader",
        ComponentType.HorizontalSlider => "Slider",
        ComponentType.Meter => "Meter",
        ComponentType.Button => "Button",
        ComponentType.Toggle => "Toggle",
        _ => type.ToString(),
    };

    private sealed record GeneratedPreview(string Path, int LayerCount, int RotateCount, int FrameCount, AvBitmap? Image);

    /// <summary>Off-thread worker: writes the SVG to a temp file, imports it through the layered
    /// pipeline (which validates the structure), and composites the at-rest preview bitmap. Returns
    /// null when the SVG yields no readable layers. Runs entirely off the UI thread — the caller just
    /// assigns the result — so a large canvas doesn't hitch the dispatcher.</summary>
    private GeneratedPreview? BuildPreview(string svg)
    {
        var path = WriteTempSvg(svg);
        var import = _layeredImport.Import(path);
        if (import is null || import.Layers.Count == 0) return null;
        int rotate = import.Layers.Count(l => l.SuggestedBehavior == LayerBehavior.Rotate);
        int frames = import.Layers.Count(l => l.SuggestedBehavior == LayerBehavior.Frame);
        int count = import.Layers.Count;
        var image = TryCompositePreview(import);   // composites, then disposes the layer art
        return new GeneratedPreview(path, count, rotate, frames, image);
    }

    /// <summary>Writes the SVG to a per-session temp file the Create-tab handoff can re-import by path,
    /// first dropping the previous temp so they don't accumulate (by the time a new generation runs,
    /// the Create tab has already imported the prior one into memory).</summary>
    private string WriteTempSvg(string svg)
    {
        var dir = Path.Combine(Path.GetTempPath(), "StripKit");
        Directory.CreateDirectory(dir);
        TryDelete(_lastSvgPath);
        var path = Path.Combine(dir, $"generated-{_sessionTag}-{++_genCount}.svg");
        File.WriteAllText(path, svg);
        return path;
    }

    private static void TryDelete(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); }
        catch { /* best-effort — a leftover temp SVG is harmless */ }
    }

    /// <summary>Flattens the imported layers into one at-rest preview bitmap, then disposes them
    /// (the Create tab re-imports from the file fresh, so the preview copies aren't needed after).
    /// Best-effort: returns null if the Avalonia bitmap can't be built (e.g. no render platform under
    /// unit tests) — the generated result still stands and can be handed off / saved.</summary>
    private static AvBitmap? TryCompositePreview(LayeredImportResult import)
    {
        try
        {
            using var merged = new SKBitmap(import.CanvasWidth, import.CanvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var c = new SKCanvas(merged))
            {
                c.Clear(SKColors.Transparent);
                foreach (var layer in import.Layers)
                    c.DrawBitmap(layer.Art, 0, 0);
            }
            return SkiaImageInterop.ToAvaloniaBitmap(merged);
        }
        catch
        {
            return null;
        }
        finally
        {
            foreach (var layer in import.Layers)
                layer.Art.Dispose();
        }
    }
}
