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
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider, ComponentType.Meter, ComponentType.Button];

    private IReadOnlyList<string> _suggestedModels = Array.Empty<string>();
    public IReadOnlyList<string> SuggestedModels
    {
        get => _suggestedModels;
        private set => SetProperty(ref _suggestedModels, value);
    }

    // ---- provider / key / model ----
    [ObservableProperty] private AiProvider _selectedProvider;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveKeyCommand))]
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
    private bool _isGenerating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private AvBitmap? _previewImage;

    [ObservableProperty] private string? _generatedSvg;
    [ObservableProperty] private string? _lastRawResponse;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseInCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSvgCommand))]
    private bool _hasResult;

    [ObservableProperty] private string _statusMessage =
        "Pick a provider, paste your API key, describe the knob, then Generate.";

    public bool HasPreview => PreviewImage is not null;

    /// <summary>True when the meter control type is selected — gates the meter-only options (orientation).</summary>
    public bool IsMeterType => GenerateControlType == ComponentType.Meter;

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

            var request = new GenerationRequest
            {
                ComponentType = GenerateControlType,
                Layered = GenerateControlType is ComponentType.RotaryKnob or ComponentType.Button or ComponentType.Meter,
                Style = Style,
                StyleNotes = StyleNotes,
                AccentColor = AccentColorHex,
                BodyColor = BodyColorHex,
                CanvasSize = CanvasSize,
                HasDropShadow = HasDropShadow,
                HasOuterGlow = HasOuterGlow,
                HasBevel = HasBevel,
                HasMetallicSheen = HasMetallicSheen,
                MeterHorizontal = MeterHorizontal,
            };

            var result = await _generation.GenerateAsync(request, SelectedProvider, ApiKey, model, ct);
            LastRawResponse = result.RawResponse;

            if (!result.Success)
            {
                StatusMessage = result.Error ?? "Generation failed.";
                return;
            }

            // Validate + preview OFF the UI thread: write the temp SVG, import it through the SAME
            // pipeline the Create tab uses (so a preview here guarantees Create can import it), and
            // composite the at-rest bitmap — all CPU-bound, so the UI thread only assigns the result.
            var preview = await Task.Run(() => BuildPreview(result.Svg!), ct);
            if (preview is null)
            {
                StatusMessage = "The model returned an SVG, but its layers couldn't be read. Try Regenerate.";
                return;
            }

            GeneratedSvg = result.Svg;
            _lastSvgPath = preview.Path;
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

    // ---- helpers ----

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
        var path = Path.Combine(dir, $"generated-{++_genCount}.svg");
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
