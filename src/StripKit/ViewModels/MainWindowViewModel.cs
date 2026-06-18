using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripKit.Helpers;
using StripKit.Models;
using StripKit.Services;
using SkiaSharp;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace StripKit.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IImageLoadService _imageLoad;
    private readonly IFilmstripRenderer _renderer;
    private readonly IFileDialogService _dialogs;
    private readonly IExportService _export;
    private readonly IManifestService _manifest;
    private readonly ICodeSnippetService _codeSnippets;
    private readonly ILayeredImportService _layeredImport;
    private readonly IAssetService _assets;

    private SKBitmap? _source;
    private SKBitmap? _background;
    private string? _sourcePath;

    // Layer-aware knob (base + pointer): a static body layer and a rotating pointer layer.
    // Empty ⇒ the single source above animates as before.
    private SKBitmap? _baseLayer;
    private SKBitmap? _pointer;
    private string? _baseLayerPath;

    // Imported layered source (SVG/PSD → N tagged layers). When non-empty it drives the layered
    // render instead of the base/pointer slots; the two layered modes are mutually exclusive.
    private string? _importPath;

    // Suppresses the preview/recompute funnel while we set several properties at
    // once (e.g. applying type defaults), so we refresh only once afterwards.
    private bool _suspendRefresh;

    // The preview frame is rendered at roughly this many pixels on its long edge,
    // so it stays sharp regardless of the (often small) real frame size.
    private const double PreviewDisplaySize = 380.0;

    public MainWindowViewModel(IImageLoadService imageLoad, IFilmstripRenderer renderer,
                               IFileDialogService dialogs, IExportService export,
                               IManifestService manifest, ICodeSnippetService codeSnippets,
                               ILayeredImportService layeredImport, IAssetService assets,
                               ImporterViewModel importer, BatchViewModel batch, SkinViewModel skin,
                               TutorialViewModel tutorial, GenerateViewModel generate)
    {
        _imageLoad = imageLoad;
        _renderer = renderer;
        _dialogs = dialogs;
        _export = export;
        _manifest = manifest;
        _codeSnippets = codeSnippets;
        _layeredImport = layeredImport;
        _assets = assets;
        Importer = importer;
        Batch = batch;
        Skin = skin;
        Tutorial = tutorial;
        Generate = generate;
        Tutorial.LoadSampleRequested += OnTutorialLoadSampleRequested;
        Generate.UseInCreateRequested += OnGenerateUseInCreate;

        SourceInfo = "No image loaded.";
        BackgroundInfo = "None.";
        StatusMessage = "Load a source image to begin.";
        UpdateReadouts();
        UpdateCodePreview();

        // Open the Getting Started guide automatically the first time the app is run.
        Tutorial.MaybeShowOnFirstRun();
    }

    /// <summary>The Getting Started tutorial overlay's view model.</summary>
    public TutorialViewModel Tutorial { get; }

    /// <summary>The app version (from the assembly, which the csproj <c>&lt;Version&gt;</c> drives),
    /// shown in the About box — so it tracks every release bump instead of going stale.</summary>
    public string AppVersion =>
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Loads the bundled sample knob (the tutorial's "Load sample knob" shortcut), so a
    /// brand-new user can see the whole flow without their own art. Resets to a single-source knob.</summary>
    private void OnTutorialLoadSampleRequested()
    {
        var path = _assets.GetSampleKnobPath();
        if (path is null)
        {
            StatusMessage = "Sample knob is unavailable.";
            return;
        }

        DiscardImportedLayers();   // start clean: a single-source knob
        DiscardBasePointer();
        if (ComponentType != ComponentType.RotaryKnob)
            ComponentType = ComponentType.RotaryKnob;
        LoadSourceFromPath(path);
        StatusMessage = "Sample knob loaded — scrub the preview, then continue the guide.";
    }

    /// <summary>The Generate tab's "Use in Create" handoff: jump to the Create tab and import the
    /// generated SVG as the control type it was generated for (knob / fader / slider / button).</summary>
    private async void OnGenerateUseInCreate(string svgPath, ComponentType type)
    {
        try
        {
            SelectedTabIndex = 0;
            await ImportLayeredFromPathAsync(svgPath, type);
        }
        catch (Exception ex)
        {
            // async void must never let an exception escape to the synchronization context.
            StatusMessage = $"Could not use the generated art: {ex.Message}";
        }
    }

    [RelayCommand] private void ShowAbout() => IsAboutOpen = true;
    [RelayCommand] private void CloseAbout() => IsAboutOpen = false;

    /// <summary>The "Import" tab's view model (hosted in a second tab in the window).</summary>
    public ImporterViewModel Importer { get; }

    /// <summary>The "Batch" tab's view model (hosted in a third tab in the window).</summary>
    public BatchViewModel Batch { get; }

    /// <summary>The "Skin" tab's view model (the multi-control manifest builder).</summary>
    public SkinViewModel Skin { get; }

    /// <summary>The "Generate" tab's view model (AI-generated SVG control art).</summary>
    public GenerateViewModel Generate { get; }

    // ---- combo box choices ----
    public ComponentType[] ComponentTypes { get; } =
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider, ComponentType.Meter, ComponentType.Button, ComponentType.Toggle];

    public StackDirection[] StackDirections { get; } =
        [StackDirection.Vertical, StackDirection.Horizontal];

    public MeterFillDirection[] FillDirections { get; } =
        [MeterFillDirection.Up, MeterFillDirection.Down, MeterFillDirection.LeftToRight, MeterFillDirection.RightToLeft];

    public int[] SupersampleOptions { get; } = [1, 2, 4, 8];

    public CodeTarget[] CodeTargets { get; } =
        [CodeTarget.Juce, CodeTarget.Css, CodeTarget.IPlug2, CodeTarget.Hise];

    // ---- source / background ----
    [ObservableProperty] private string _sourceInfo = "";
    [ObservableProperty] private string _backgroundInfo = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(ShowLoadHint))]
    private bool _hasSource;

    [ObservableProperty] private bool _hasBackground;

    // ---- component / frames ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRotary), nameof(IsLinear), nameof(IsMeter), nameof(IsButton), nameof(IsToggle), nameof(IsStateFrames), nameof(ShowLoadHint))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private ComponentType _componentType = ComponentType.RotaryKnob;

    [ObservableProperty] private int _frameCount = 64;
    [ObservableProperty] private int _frameWidth = 80;
    [ObservableProperty] private int _frameHeight = 80;

    // ---- rotary ----
    [ObservableProperty] private double _sweepDegrees = 270;
    [ObservableProperty] private bool _rotationClockwise = true;
    [ObservableProperty] private double _startAngleDegrees = -135;
    [ObservableProperty] private double _endAngleDegrees = 135;
    [ObservableProperty] private double _pivotOffsetX;
    [ObservableProperty] private double _pivotOffsetY;

    // ---- content alignment (knob centre / cap cross-centre) ----
    [ObservableProperty] private double _sourceCenterX = 0.5;
    [ObservableProperty] private double _sourceCenterY = 0.5;
    [ObservableProperty] private bool _showCenterGuide;

    // ---- linear ----
    [ObservableProperty] private double _edgeMargin = 4;
    [ObservableProperty] private double _capCrossOffset;

    // ---- quality / output ----
    [ObservableProperty] private int _supersample = 4;
    [ObservableProperty] private StackDirection _stackDirection = StackDirection.Vertical;
    [ObservableProperty] private bool _exportAt2x = true;
    [ObservableProperty] private bool _exportManifest = true;
    [ObservableProperty] private string _parameterId = "";

    // ---- meter ----
    [ObservableProperty] private int _segmentCount = 12;
    [ObservableProperty] private MeterFillDirection _fillDirection = MeterFillDirection.Up;
    [ObservableProperty] private bool _continuousFill;
    [ObservableProperty] private string _onColorHex = "#FFE8440A";
    [ObservableProperty] private string _offColorHex = "#FF2A2A2A";

    // ---- value arc / fill ring (knob) ----
    [ObservableProperty] private bool _showValueArc;
    [ObservableProperty] private double _arcRadius = 0.88;
    [ObservableProperty] private double _arcThickness = 4;
    [ObservableProperty] private bool _arcRoundCaps = true;
    [ObservableProperty] private string _arcColorHex = "#FFE8440A";
    [ObservableProperty] private bool _arcGradient;
    [ObservableProperty] private string _arcColor2Hex = "#FFFFC107";
    [ObservableProperty] private bool _arcTrack = true;
    [ObservableProperty] private string _arcTrackColorHex = "#33FFFFFF";
    [ObservableProperty] private bool _arcGlow;
    [ObservableProperty] private double _arcGlowSize = 6;

    // ---- layered knob (base + pointer) ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(ShowLoadHint))]
    private bool _hasBaseLayer;
    [ObservableProperty] private bool _hasPointer;
    [ObservableProperty] private string _baseLayerInfo = "None.";
    [ObservableProperty] private string _pointerInfo = "None.";
    [ObservableProperty] private double _pointerPivotX = 0.5;
    [ObservableProperty] private double _pointerPivotY = 0.5;

    // ---- imported layered source (SVG / PSD → N tagged layers) ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(ShowLoadHint))]
    private bool _hasImportedLayers;
    [ObservableProperty] private string _importInfo = "None.";

    /// <summary>The parsed layers of an imported SVG/PSD (bottom-first): each row's name +
    /// user-overridable behaviour + canvas-sized art. Drives the layered render when non-empty.</summary>
    public ObservableCollection<ImportedLayerRow> ImportedLayers { get; } = [];

    // ---- code export (loader snippets) ----
    [ObservableProperty] private bool _exportCode;
    [ObservableProperty] private bool _emitCodeJuce = true;
    [ObservableProperty] private bool _emitCodeCss = true;
    [ObservableProperty] private bool _emitCodeIPlug2;
    [ObservableProperty] private bool _emitCodeHise;
    [ObservableProperty] private CodeTarget _codePreviewTarget = CodeTarget.Juce;
    [ObservableProperty] private string _generatedCode = "";

    // ---- preview ----
    [ObservableProperty] private double _previewValue = 0.5;
    [ObservableProperty] private AvBitmap? _previewImage;
    [ObservableProperty] private string _previewReadout = "";
    [ObservableProperty] private string _stripDimensions = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isPlaying;

    // ---- window-level UI state (tab selection + the About modal) ----
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isAboutOpen;

    public bool IsRotary => ComponentType == ComponentType.RotaryKnob;
    public bool IsLinear => ComponentType is ComponentType.VerticalFader or ComponentType.HorizontalSlider;
    public bool IsMeter => ComponentType == ComponentType.Meter;
    public bool IsButton => ComponentType == ComponentType.Button;
    public bool IsToggle => ComponentType == ComponentType.Toggle;

    /// <summary>Button and Toggle share the discrete state-frame render + Create logic (off/on layers),
    /// so the state-frame UI and layer-building branch on this rather than on Button alone.</summary>
    public bool IsStateFrames => IsButton || IsToggle;

    /// <summary>The "load a source" overlay shows only when there is nothing to preview;
    /// a procedural meter renders without a source, and a layered knob or button previews
    /// from its base layer or an imported layer stack.</summary>
    public bool ShowLoadHint => !HasSource && !IsMeter && !HasBaseLayer && !HasImportedLayers;

    /// <summary>True when a layered knob — either the base/pointer slots or an imported SVG/PSD
    /// stack — should drive the render instead of the single source. Knob-only.</summary>
    private bool IsLayeredKnob => IsRotary && (_baseLayer is not null || HasImportedLayers);

    /// <summary>True when an imported SVG/PSD stack (rather than the base/pointer slots) is the
    /// active layered source. Takes precedence over the base/pointer slots.</summary>
    private bool IsImportedKnob => IsRotary && HasImportedLayers;

    /// <summary>Source pixel size (0 when none) — the alignment overlay uses it to map the
    /// crosshair onto the drawn art within the preview frame.</summary>
    public int SourcePixelWidth => _source?.Width ?? 0;
    public int SourcePixelHeight => _source?.Height ?? 0;

    /// <summary>
    /// Single funnel: any meaningful input change recomputes derived values and
    /// refreshes the preview. Outputs are filtered out to avoid feedback loops.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Inputs that only change the emitted loader code — refresh the snippet, never the image.
        if (e.PropertyName is nameof(ParameterId) or nameof(CodePreviewTarget))
        {
            UpdateCodePreview();
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(PreviewImage):
            case nameof(PreviewReadout):
            case nameof(StripDimensions):
            case nameof(StatusMessage):
            case nameof(SourceInfo):
            case nameof(BackgroundInfo):
            case nameof(IsRotary):
            case nameof(IsLinear):
            case nameof(IsMeter):
            case nameof(IsButton):
            case nameof(IsToggle):
            case nameof(IsStateFrames):
            case nameof(ShowLoadHint):
            case nameof(IsPlaying):
            case nameof(SelectedTabIndex):
            case nameof(IsAboutOpen):
            case nameof(ExportManifest):
            case nameof(HasBaseLayer):
            case nameof(HasPointer):
            case nameof(BaseLayerInfo):
            case nameof(PointerInfo):
            case nameof(HasImportedLayers):
            case nameof(ImportInfo):
            case nameof(GeneratedCode):
            case nameof(ExportCode):
            case nameof(EmitCodeJuce):
            case nameof(EmitCodeCss):
            case nameof(EmitCodeIPlug2):
            case nameof(EmitCodeHise):
                return;
        }

        if (_suspendRefresh)
            return;

        if (e.PropertyName is nameof(SweepDegrees) or nameof(RotationClockwise))
            RecomputeAnglesFromSweep();

        if (e.PropertyName == nameof(ComponentType))
            ApplyTypeDefaults();

        UpdateReadouts();
        UpdateCodePreview();
        RefreshPreview();
    }

    private void RecomputeAnglesFromSweep()
    {
        double half = SweepDegrees / 2.0;
        _suspendRefresh = true;
        if (RotationClockwise)
        {
            StartAngleDegrees = -half;
            EndAngleDegrees = half;
        }
        else
        {
            StartAngleDegrees = half;
            EndAngleDegrees = -half;
        }
        _suspendRefresh = false;
    }

    private void ApplyTypeDefaults()
    {
        _suspendRefresh = true;
        switch (ComponentType)
        {
            case ComponentType.RotaryKnob:
                var knobArt = _source ?? _baseLayer;
                if (knobArt is not null)
                {
                    FrameWidth = Math.Max(knobArt.Width, knobArt.Height);
                    FrameHeight = FrameWidth;
                }
                else
                {
                    FrameWidth = 80;
                    FrameHeight = 80;
                }
                break;

            case ComponentType.VerticalFader:
                FrameWidth = 40;
                FrameHeight = 128;
                break;

            case ComponentType.HorizontalSlider:
                FrameWidth = 128;
                FrameHeight = 32;
                break;

            case ComponentType.Meter:
                // Default to a tall vertical meter; the user widens it for horizontal
                // fill directions.
                FrameWidth = 48;
                FrameHeight = 160;
                break;

            case ComponentType.Button:
            case ComponentType.Toggle:
                // Square by default; 2 frames covers off/on. A button can add more frames for
                // hover, pressed, or disabled states; a toggle stays at off/on.
                FrameWidth = 80;
                FrameHeight = 80;
                _suspendRefresh = false;
                FrameCount = 2;
                return;
        }
        _suspendRefresh = false;
    }

    private FilmstripSettings BuildSettings()
    {
        var settings = new FilmstripSettings
        {
            ComponentType = ComponentType,
            FrameCount = FrameCount,
            FrameWidth = FrameWidth,
            FrameHeight = FrameHeight,
            StartAngleDegrees = StartAngleDegrees,
            EndAngleDegrees = EndAngleDegrees,
            PivotOffsetX = PivotOffsetX,
            PivotOffsetY = PivotOffsetY,
            SourceCenterX = SourceCenterX,
            SourceCenterY = SourceCenterY,
            EdgeMargin = EdgeMargin,
            CapCrossOffset = CapCrossOffset,
            Supersample = Supersample,
            StackDirection = StackDirection,
            SegmentCount = SegmentCount,
            FillDirection = FillDirection,
            ContinuousFill = ContinuousFill,
            OnColorArgb = ParseArgb(OnColorHex, 0xFFE8440A),
            OffColorArgb = ParseArgb(OffColorHex, 0xFF2A2A2A),
            ShowValueArc = ShowValueArc,
            ArcRadius = ArcRadius,
            ArcThickness = ArcThickness,
            ArcRoundCaps = ArcRoundCaps,
            ArcColorArgb = ParseArgb(ArcColorHex, 0xFFE8440A),
            ArcGradient = ArcGradient,
            ArcColor2Argb = ParseArgb(ArcColor2Hex, 0xFFFFC107),
            ArcTrack = ArcTrack,
            ArcTrackColorArgb = ParseArgb(ArcTrackColorHex, 0x33FFFFFF),
            ArcGlow = ArcGlow,
            ArcGlowSize = ArcGlowSize,
        };

        // Layered knob. An imported SVG/PSD stack (N tagged layers) takes precedence; otherwise the
        // base/pointer slots. Left empty for every other case, so the single-source render is unchanged.
        if (IsImportedKnob)
        {
            // Every imported layer is the same canvas size, so a Rotate layer's normalized pivot is
            // the shared knob axis (the detected content centre) for all of them.
            foreach (var row in ImportedLayers)
                settings.Layers.Add(new RenderLayer
                {
                    Behavior = row.Behavior,
                    PivotX = SourceCenterX,
                    PivotY = SourceCenterY,
                });
        }
        else if (IsStateFrames && HasImportedLayers)
        {
            // Button/toggle state layers: each layer maps to one frame index (off=0, on=1, …).
            // Pivot is irrelevant here (no rotation) but we set it defensively.
            foreach (var row in ImportedLayers)
                settings.Layers.Add(new RenderLayer { Behavior = row.Behavior, PivotX = 0.5, PivotY = 0.5 });
        }
        else if (IsLayeredKnob)
        {
            settings.Layers.Add(new RenderLayer { Behavior = LayerBehavior.Static });
            if (_pointer is not null)
                settings.Layers.Add(new RenderLayer
                {
                    Behavior = LayerBehavior.Rotate,
                    PivotX = PointerPivotX,
                    PivotY = PointerPivotY,
                });
        }

        return settings;
    }

    /// <summary>The layer bitmaps matching <see cref="FilmstripSettings.Layers"/> — the imported
    /// stack in order, or the base body + pointer, or null when not layered.</summary>
    private IReadOnlyList<SKBitmap>? BuildLayerArt()
    {
        if (IsImportedKnob || (IsStateFrames && HasImportedLayers))
            return ImportedLayers.Select(r => r.Art).ToList();
        if (!IsLayeredKnob) return null;
        var art = new List<SKBitmap> { _baseLayer! };
        if (_pointer is not null) art.Add(_pointer);
        return art;
    }

    /// <summary>Parses a "#AARRGGBB"/"#RRGGBB" colour to packed ARGB, or the fallback.</summary>
    private static uint ParseArgb(string hex, uint fallback) =>
        SKColor.TryParse(hex, out var c)
            ? ((uint)c.Alpha << 24) | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue
            : fallback;

    private void UpdateReadouts()
    {
        int n = Math.Max(1, FrameCount);
        int idx = Math.Clamp((int)Math.Round(PreviewValue * (n - 1)), 0, n - 1);
        double t = n > 1 ? (double)idx / (n - 1) : 0.0;

        PreviewReadout = IsRotary
            ? $"Frame {idx + 1}/{n} · {StartAngleDegrees + (EndAngleDegrees - StartAngleDegrees) * t:0.0}°"
            : $"Frame {idx + 1}/{n} · {t * 100:0}%";

        bool vertical = StackDirection == StackDirection.Vertical;
        int w = vertical ? FrameWidth : FrameWidth * n;
        int h = vertical ? FrameHeight * n : FrameHeight;
        StripDimensions = ExportAt2x
            ? $"Strip: {w}×{h}px   ·   @2x: {w * 2}×{h * 2}px"
            : $"Strip: {w}×{h}px";
    }

    // ---- code export ----

    /// <summary>The snippet request for the filenames the current settings would export
    /// (the live preview uses these before an actual save path is chosen).</summary>
    private CodeSnippetRequest BuildCodeRequest()
    {
        var baseName = Path.GetFileNameWithoutExtension(_sourcePath ?? _baseLayerPath) ?? (IsMeter ? "meter" : "filmstrip");
        var asset = $"{baseName}_{FrameCount}frames.png";
        var asset2x = ExportAt2x ? AppendSuffix(asset, "@2x") : null;
        var parameterId = string.IsNullOrWhiteSpace(ParameterId) ? baseName : ParameterId.Trim();
        return new CodeSnippetRequest(ComponentType, FrameCount, FrameWidth, FrameHeight,
                                      StackDirection, asset, asset2x, baseName, parameterId);
    }

    private void UpdateCodePreview() =>
        GeneratedCode = _codeSnippets.Generate(CodePreviewTarget, BuildCodeRequest());

    /// <summary>The code targets ticked for emission on export.</summary>
    private IEnumerable<CodeTarget> SelectedCodeTargets()
    {
        if (EmitCodeJuce) yield return CodeTarget.Juce;
        if (EmitCodeCss) yield return CodeTarget.Css;
        if (EmitCodeIPlug2) yield return CodeTarget.IPlug2;
        if (EmitCodeHise) yield return CodeTarget.Hise;
    }

    // ---- commands ----

    [RelayCommand]
    private async Task OpenSourceAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;

        LoadSourceFromPath(path);
    }

    /// <summary>
    /// Loads a source image from a local path, then refreshes the preview. This is
    /// the single shared load path used by both the "Load source image…" button and
    /// the drag-and-drop handler, so the two behave identically. It takes a plain
    /// path string and never touches Avalonia UI types, keeping the view model
    /// testable.
    /// </summary>
    public void LoadSourceFromPath(string path)
    {
        var bmp = _imageLoad.Load(path);
        if (bmp is null)
        {
            StatusMessage = "Error: could not load that image.";
            return;
        }

        _source?.Dispose();
        _source = bmp;
        _sourcePath = path;
        HasSource = true;
        SourceInfo = $"{Path.GetFileName(path)} — {bmp.Width}×{bmp.Height}px";

        _suspendRefresh = true;
        if (IsRotary)
        {
            // Square the frame to the art, and auto-centre on the opaque content so an
            // off-centre knob spins in place instead of orbiting (tweakable afterwards).
            FrameWidth = Math.Max(bmp.Width, bmp.Height);
            FrameHeight = FrameWidth;
            (SourceCenterX, SourceCenterY) = ContentAnalysis.DetectContentCenter(bmp);
        }
        else
        {
            SourceCenterX = 0.5;
            SourceCenterY = 0.5;
        }
        _suspendRefresh = false;

        StatusMessage = "Source loaded.";
        UpdateReadouts();
        RefreshPreview();
    }

    [RelayCommand]
    private async Task OpenBackgroundAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;

        var bmp = _imageLoad.Load(path);
        if (bmp is null)
        {
            StatusMessage = "Error: could not load the background image.";
            return;
        }

        _background?.Dispose();
        _background = bmp;
        HasBackground = true;
        BackgroundInfo = $"{Path.GetFileName(path)} — {bmp.Width}×{bmp.Height}px";
        RefreshPreview();
    }

    [RelayCommand]
    private void ClearBackground()
    {
        _background?.Dispose();
        _background = null;
        HasBackground = false;
        BackgroundInfo = "None.";
        RefreshPreview();
    }

    // ---- layered knob (base + pointer) ----

    [RelayCommand]
    private async Task OpenBaseLayerAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;
        LoadBaseLayerFromPath(path);
    }

    /// <summary>
    /// Loads the static base body of a layered knob. The body defines the frame size and the
    /// spin centre (its detected content centre), and seeds the pointer's default pivot to
    /// that same centre (the knob axis). Shared by the button and any future drop handler;
    /// touches no Avalonia UI types.
    /// </summary>
    public void LoadBaseLayerFromPath(string path)
    {
        var bmp = _imageLoad.Load(path);
        if (bmp is null)
        {
            StatusMessage = "Error: could not load the base layer.";
            return;
        }

        DiscardImportedLayers();   // base/pointer and an imported stack are mutually exclusive
        _baseLayer?.Dispose();
        _baseLayer = bmp;
        _baseLayerPath = path;
        HasBaseLayer = true;
        BaseLayerInfo = $"{Path.GetFileName(path)} — {bmp.Width}×{bmp.Height}px";

        _suspendRefresh = true;
        // The body squares the frame and defines the knob centre; the pointer pivot defaults
        // to that centre (so a same-canvas pointer rotates about the body's axis).
        FrameWidth = Math.Max(bmp.Width, bmp.Height);
        FrameHeight = FrameWidth;
        var (cx, cy) = ContentAnalysis.DetectContentCenter(bmp);
        SourceCenterX = cx;
        SourceCenterY = cy;
        PointerPivotX = cx;
        PointerPivotY = cy;
        _suspendRefresh = false;

        StatusMessage = HasPointer
            ? "Base layer loaded — body static, pointer rotates."
            : "Base layer loaded — add a pointer layer to animate it.";
        UpdateReadouts();
        RefreshPreview();
    }

    [RelayCommand]
    private void ClearBaseLayer()
    {
        _baseLayer?.Dispose();
        _baseLayer = null;
        _baseLayerPath = null;
        HasBaseLayer = false;
        BaseLayerInfo = "None.";
        RefreshPreview();
    }

    [RelayCommand]
    private async Task OpenPointerAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;
        LoadPointerFromPath(path);
    }

    /// <summary>Loads the rotating pointer layer of a layered knob (drawn on top of the
    /// base body). Its pivot stays at the body's centre by default — adjust it with the
    /// pointer-pivot fields. Touches no Avalonia UI types.</summary>
    public void LoadPointerFromPath(string path)
    {
        var bmp = _imageLoad.Load(path);
        if (bmp is null)
        {
            StatusMessage = "Error: could not load the pointer layer.";
            return;
        }

        DiscardImportedLayers();   // base/pointer and an imported stack are mutually exclusive
        _pointer?.Dispose();
        _pointer = bmp;
        HasPointer = true;
        PointerInfo = $"{Path.GetFileName(path)} — {bmp.Width}×{bmp.Height}px";
        StatusMessage = HasBaseLayer
            ? "Pointer layer loaded — only the pointer rotates."
            : "Pointer loaded — also load a base (body) layer.";
        RefreshPreview();
    }

    [RelayCommand]
    private void ClearPointer()
    {
        _pointer?.Dispose();
        _pointer = null;
        HasPointer = false;
        PointerInfo = "None.";
        RefreshPreview();
    }

    /// <summary>Resets the pointer's rotation pivot to the body's detected centre (the knob axis).</summary>
    [RelayCommand]
    private void CenterPointerOnBody()
    {
        _suspendRefresh = true;
        PointerPivotX = SourceCenterX;
        PointerPivotY = SourceCenterY;
        _suspendRefresh = false;
        RefreshPreview();
    }

    /// <summary>
    /// Auto-splits a single FLAT knob image into a base body + a rotating pointer (radial-symmetry
    /// residual, <see cref="PointerExtractor"/>) and fills both layer slots — a starting guess the
    /// user verifies via the preview/scrub. Knob-only; assumes the art shows the indicator at the
    /// minimum (frame-0) position.
    /// </summary>
    [RelayCommand]
    private async Task AutoExtractPointerAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;

        var flat = _imageLoad.Load(path);
        if (flat is null)
        {
            StatusMessage = "Error: could not load that image.";
            return;
        }

        var (cx, cy) = ContentAnalysis.DetectContentCenter(flat);
        var result = PointerExtractor.Extract(flat, cx, cy);
        flat.Dispose();   // the extractor returns fresh base/pointer bitmaps; the flat source is done

        if (result is null)
        {
            StatusMessage = "Error: could not extract a pointer from that image.";
            return;
        }

        DiscardImportedLayers();   // base/pointer and an imported stack are mutually exclusive
        _baseLayer?.Dispose();
        _pointer?.Dispose();
        _baseLayer = result.BaseLayer;
        _pointer = result.PointerLayer;
        _baseLayerPath = path;
        HasBaseLayer = true;
        HasPointer = true;
        BaseLayerInfo = $"Auto-extracted body — {Path.GetFileName(path)}";
        PointerInfo = $"Auto-extracted pointer — {result.Confidence * 100:0}% confidence";

        _suspendRefresh = true;
        FrameWidth = Math.Max(_baseLayer.Width, _baseLayer.Height);
        FrameHeight = FrameWidth;
        SourceCenterX = cx;
        SourceCenterY = cy;
        PointerPivotX = cx;
        PointerPivotY = cy;
        _suspendRefresh = false;

        StatusMessage = result.LowConfidence
            ? $"Pointer extracted, but confidence is low ({result.Confidence * 100:0}%) — verify the sweep. Auto-extract works best on a round knob with one indicator; otherwise load the base + pointer by hand."
            : $"Pointer auto-extracted ({result.Confidence * 100:0}% confidence). Scrub to verify the sweep; adjust the pointer pivot if needed.";
        UpdateReadouts();
        RefreshPreview();
    }

    // ---- imported layered source (SVG / PSD) ----

    /// <summary>
    /// Imports a real layered source — an <c>.svg</c> (vector groups) or <c>.psd</c>/<c>.psb</c>
    /// (raster layers) — via <see cref="ILayeredImportService"/>, mapping each parsed layer onto the
    /// renderer's layer stack with a name-guessed Static/Rotate behaviour the user can override per
    /// layer. Knob-only; replaces the base/pointer slots (the two layered modes are mutually
    /// exclusive). A starting point the user verifies via the preview/scrub — like the importer and
    /// the auto-extract workflow, it assumes the art is drawn at the minimum (frame-0) position.
    /// </summary>
    [RelayCommand]
    private async Task ImportLayeredFileAsync()
    {
        var path = await _dialogs.OpenLayeredFileAsync();
        if (path is null) return;
        await ImportLayeredFromPathAsync(path);
    }

    /// <summary>
    /// Imports a layered SVG/PSD from a path into the Create tab — shared by the file picker and the
    /// Generate tab's "Use in Create" handoff. Off-thread parse. <paramref name="asType"/> picks the
    /// target: null/knob builds the layer stack (body + pointer); <see cref="ComponentType.Button"/>
    /// builds discrete state frames (off/on); a fader/slider flattens the cap to the single source so
    /// the linear renderer translates it (the layer stack is knob/button-only). Replaces the manual
    /// base/pointer slots and any previous import.
    /// </summary>
    public async Task ImportLayeredFromPathAsync(string path, ComponentType? asType = null)
    {
        // Parsing (SVG rasterize / PSD decode) is CPU-bound, so keep it off the UI thread.
        var result = await Task.Run(() => _layeredImport.Import(path));
        if (result is null || result.Layers.Count == 0)
        {
            StatusMessage = "Could not read any layers from that file (expected SVG groups or PSD layers).";
            return;
        }

        // Fader/slider caps are a single static image, not a layer stack: load the flattened art as the
        // normal single source so the linear renderer translates it (BuildLayerArt is knob/button-only).
        if (asType is ComponentType.VerticalFader or ComponentType.HorizontalSlider)
        {
            AdoptFlattenedSource(result, asType.Value, path);
            return;
        }

        if (asType == ComponentType.Meter)
        {
            AdoptMeterArt(result, path);
            return;
        }

        // The file picker passes no explicit type, so infer one: honour an already-selected Button/
        // Toggle; otherwise two+ off/on state groups (a toggle SVG) become a Toggle, and anything else
        // is a layered knob (body + pointer). An explicit asType (the Generate handoff) always wins.
        var resolvedType = asType
            ?? (IsStateFrames ? ComponentType
                : LooksLikeStateFrames(result) ? ComponentType.Toggle
                : ComponentType.RotaryKnob);
        bool stateFrames = resolvedType is ComponentType.Button or ComponentType.Toggle;

        DiscardBasePointer();       // the import replaces the base/pointer slots
        DiscardImportedLayers();    // dispose any previous import

        foreach (var layer in result.Layers)
        {
            var row = new ImportedLayerRow(layer.Name, layer.Art, layer.SuggestedBehavior);
            row.PropertyChanged += OnImportedLayerChanged;
            ImportedLayers.Add(row);
        }
        HasImportedLayers = true;
        _importPath = path;
        ImportInfo = $"{Path.GetFileName(path)} — {result.Layers.Count} layer{(result.Layers.Count == 1 ? "" : "s")} ({result.SourceFormat})";

        int rotateCount = result.Layers.Count(l => l.SuggestedBehavior == LayerBehavior.Rotate);

        _suspendRefresh = true;
        if (stateFrames)
        {
            // Discrete state frames (off/on, …): one frame per layer, no rotation axis, keep the canvas size.
            ComponentType = resolvedType;
            FrameWidth = result.CanvasWidth;
            FrameHeight = result.CanvasHeight;
            FrameCount = Math.Max(2, ImportedLayers.Count);
        }
        else
        {
            // Knob (body + pointer) — the default, also used by the file picker.
            if (ComponentType != ComponentType.RotaryKnob)
                ComponentType = ComponentType.RotaryKnob;
            // Square the frame to the document canvas; seed the rotation axis from the whole knob's centre.
            FrameWidth = Math.Max(result.CanvasWidth, result.CanvasHeight);
            FrameHeight = FrameWidth;
            var (cx, cy) = DetectImportedCenter(result.CanvasWidth, result.CanvasHeight);
            SourceCenterX = cx;
            SourceCenterY = cy;
        }
        _suspendRefresh = false;

        string stateNoun = resolvedType == ComponentType.Toggle ? "toggle" : "button";
        StatusMessage = stateFrames
            ? $"Imported {ImportedLayers.Count} {stateNoun} state layer(s) from {Path.GetFileName(path)} "
              + "(frame 0 = off, frame 1 = on). Scrub to check each state."
            : $"Imported {result.Layers.Count} layers from {Path.GetFileName(path)} "
              + $"({rotateCount} set to rotate). Verify each layer's behaviour and scrub to check the sweep.";
        UpdateReadouts();
        RefreshPreview();
    }

    /// <summary>True when an imported file looks like discrete on/off state art (≥2 layers tagged
    /// Frame — e.g. groups named off/on), so the file picker adopts it as a Toggle rather than a knob.</summary>
    private static bool LooksLikeStateFrames(LayeredImportResult result) =>
        result.Layers.Count >= 2 && result.Layers.Count(l => l.SuggestedBehavior == LayerBehavior.Frame) >= 2;

    /// <summary>Flattens an imported layer stack into one bitmap and adopts it as the single source for
    /// a linear control (a generated fader/slider cap). The linear renderer translates a source image —
    /// it does not consume the layer stack — so this routes a generated cap through the same path a
    /// loaded PNG would take.</summary>
    private void AdoptFlattenedSource(LayeredImportResult result, ComponentType type, string path)
    {
        int w = result.CanvasWidth, h = result.CanvasHeight;
        var flat = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(flat))
        {
            c.Clear(SKColors.Transparent);
            foreach (var layer in result.Layers)
                c.DrawBitmap(layer.Art, 0, 0);
        }
        foreach (var layer in result.Layers)
            layer.Art.Dispose();   // the flattened copy is all we keep

        DiscardBasePointer();
        DiscardImportedLayers();
        _source?.Dispose();
        _source = flat;
        _sourcePath = path;
        HasSource = true;
        SourceInfo = $"{Path.GetFileName(path)} — {w}×{h}px (generated cap)";

        _suspendRefresh = true;
        ComponentType = type;
        // Linear frame defaults (mirrors ApplyTypeDefaults; set directly to stay inside the suspend block).
        if (type == ComponentType.VerticalFader) { FrameWidth = 40; FrameHeight = 128; }
        else { FrameWidth = 128; FrameHeight = 32; }
        SourceCenterX = 0.5;
        SourceCenterY = 0.5;
        _suspendRefresh = false;

        StatusMessage = $"Imported a generated {(type == ComponentType.VerticalFader ? "fader" : "slider")} cap "
                      + $"from {Path.GetFileName(path)}. Scrub to see it travel, then Export.";
        UpdateReadouts();
        RefreshPreview();
    }

    /// <summary>Adopts a generated meter's layers as the meter's off/on art: the renderer draws the
    /// off-state full (background) and reveals the on-state up to the value (source). Routes the layer
    /// named "off" (or, failing a name, the first/bottom layer) to the background and "on" (or the
    /// last/top layer) to the source; degrades to a source-only meter when only one layer is present.
    /// The meter render path consumes a single source + background — not the layer stack — so this
    /// routes a generated meter through the same path a hand-loaded on/off pair would take.</summary>
    private void AdoptMeterArt(LayeredImportResult result, string path)
    {
        int w = result.CanvasWidth, h = result.CanvasHeight;

        var on = result.Layers.FirstOrDefault(l => l.Name.Trim().Equals("on", StringComparison.OrdinalIgnoreCase))
                 ?? result.Layers[^1];                              // last/top = lit, by convention
        var off = result.Layers.FirstOrDefault(l => l.Name.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
                  ?? (result.Layers.Count > 1 ? result.Layers[0] : null);   // first/bottom = unlit

        var onArt = CopyCanvas(on.Art, w, h);
        var offArt = off is not null && !ReferenceEquals(off, on) ? CopyCanvas(off.Art, w, h) : null;
        foreach (var layer in result.Layers) layer.Art.Dispose();   // the copies are all we keep

        DiscardBasePointer();
        DiscardImportedLayers();
        _source?.Dispose();
        _background?.Dispose();
        _source = onArt;            // on-state, revealed up to the value
        _background = offArt;       // off-state, drawn full behind (null → procedural-less plain reveal)
        _sourcePath = path;
        HasSource = true;
        HasBackground = offArt is not null;
        SourceInfo = $"{Path.GetFileName(path)} — {w}×{h}px (generated meter, lit)";
        BackgroundInfo = offArt is not null ? $"{Path.GetFileName(path)} — off-state" : "None.";

        _suspendRefresh = true;
        ComponentType = ComponentType.Meter;
        FrameWidth = w;
        FrameHeight = h;
        ContinuousFill = true;                       // generated art reveals smoothly, not in steps
        // Orientation follows the generated art's shape: a wide meter fills left→right, a tall one
        // fills bottom→top (low value at the start edge in both).
        FillDirection = w > h ? MeterFillDirection.LeftToRight : MeterFillDirection.Up;
        _suspendRefresh = false;

        StatusMessage = offArt is not null
            ? $"Imported a generated meter from {Path.GetFileName(path)} (off → background, on → fill). Scrub to see it fill, then Export."
            : $"Imported a generated meter from {Path.GetFileName(path)}. Scrub to see it fill, then Export.";
        UpdateReadouts();
        RefreshPreview();
    }

    /// <summary>A canvas-sized RGBA copy of one imported layer's art (so we can keep it after the rest
    /// of the import set is disposed).</summary>
    private static SKBitmap CopyCanvas(SKBitmap src, int w, int h)
    {
        var copy = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(copy);
        c.Clear(SKColors.Transparent);
        c.DrawBitmap(src, 0, 0);
        return copy;
    }

    [RelayCommand]
    private void ClearImportedLayers()
    {
        DiscardImportedLayers();
        RefreshPreview();
    }

    /// <summary>Detects the knob's rotation axis from the merged imported layers (all canvas-sized,
    /// so the normalized centre is shared by every Rotate layer's pivot).</summary>
    private (double cx, double cy) DetectImportedCenter(int w, int h)
    {
        if (ImportedLayers.Count == 0 || w <= 0 || h <= 0) return (0.5, 0.5);
        using var merged = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(merged))
        {
            c.Clear(SKColors.Transparent);
            foreach (var row in ImportedLayers)
                c.DrawBitmap(row.Art, 0, 0);
        }
        return ContentAnalysis.DetectContentCenter(merged);
    }

    private void OnImportedLayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportedLayerRow.Behavior))
            RefreshPreview();   // re-render live when the user re-tags a layer
    }

    private void DiscardImportedLayers()
    {
        foreach (var row in ImportedLayers)
        {
            row.PropertyChanged -= OnImportedLayerChanged;
            row.DisposeArt();
        }
        ImportedLayers.Clear();
        HasImportedLayers = false;
        ImportInfo = "None.";
        _importPath = null;
    }

    private void DiscardBasePointer()
    {
        _baseLayer?.Dispose();
        _pointer?.Dispose();
        _baseLayer = null;
        _pointer = null;
        _baseLayerPath = null;
        HasBaseLayer = false;
        HasPointer = false;
        BaseLayerInfo = "None.";
        PointerInfo = "None.";
    }

    [RelayCommand] private void SetFrames32() => FrameCount = 32;
    [RelayCommand] private void SetFrames64() => FrameCount = 64;
    [RelayCommand] private void SetFrames128() => FrameCount = 128;

    [RelayCommand] private void Sweep270() => SweepDegrees = 270;
    [RelayCommand] private void Sweep300() => SweepDegrees = 300;
    [RelayCommand] private void Sweep360() => SweepDegrees = 360;

    [RelayCommand]
    private void MatchFrameToSource()
    {
        if (_source is null) return;
        var source = _source;

        _suspendRefresh = true;
        if (IsRotary)
        {
            FrameWidth = Math.Max(source.Width, source.Height);
            FrameHeight = FrameWidth;
        }
        else if (ComponentType == ComponentType.VerticalFader)
        {
            FrameWidth = source.Width + 8;
            FrameHeight = source.Height * 6;
        }
        else // horizontal slider
        {
            FrameWidth = source.Width * 6;
            FrameHeight = source.Height + 8;
        }
        _suspendRefresh = false;

        UpdateReadouts();
        RefreshPreview();
    }

    [RelayCommand]
    private void AutoCenter()
    {
        if (_source is null) return;
        var (cx, cy) = ContentAnalysis.DetectContentCenter(_source);
        _suspendRefresh = true;
        SourceCenterX = cx;
        SourceCenterY = cy;
        _suspendRefresh = false;
        StatusMessage = $"Centred on content ({cx * 100:0}%, {cy * 100:0}%).";
        UpdateReadouts();
        RefreshPreview();
    }

    [RelayCommand]
    private void ResetCenter()
    {
        _suspendRefresh = true;
        SourceCenterX = 0.5;
        SourceCenterY = 0.5;
        _suspendRefresh = false;
        StatusMessage = "Centre reset to the image centre.";
        UpdateReadouts();
        RefreshPreview();
    }

    /// <summary>The crosshair is a persistent toggle. The preview shows the same frame with
    /// or without it (the knob is never moved and Play keeps animating), so toggling it never
    /// shifts the image — only the crosshair overlay appears/disappears.</summary>
    partial void OnShowCenterGuideChanged(bool value) =>
        StatusMessage = value ? "Crosshair on — drag it onto the knob's centre." : "Crosshair off.";

    /// <summary>Sets the spin centre in one batch (a single preview render). The live crosshair
    /// drag calls this so a fast drag triggers one render per move, not one per axis.</summary>
    public void SetSourceCenter(double x, double y)
    {
        _suspendRefresh = true;
        SourceCenterX = Math.Clamp(x, 0.0, 1.0);
        SourceCenterY = Math.Clamp(y, 0.0, 1.0);
        _suspendRefresh = false;
        RefreshPreview();
    }

    // A procedural meter, a layered knob (base body or an imported stack), and a button with
    // imported state layers all render without a flat source image.
    private bool CanExport() => HasSource || IsMeter || HasBaseLayer || HasImportedLayers;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_source is null && !IsMeter && _baseLayer is null && !HasImportedLayers) return;

        var baseName = Path.GetFileNameWithoutExtension(_sourcePath ?? _baseLayerPath ?? _importPath) ?? "filmstrip";
        var suggested = $"{baseName}_{FrameCount}frames.png";

        var path = await _dialogs.SavePngAsync(suggested);
        if (path is null) return;

        try
        {
            StatusMessage = "Rendering…";

            var settings = BuildSettings();
            var src = _source;        // captured for the background render thread
            var bg = _background;
            var layerArt = BuildLayerArt();   // base + pointer for a layered knob, else null

            // Rendering is CPU-bound and pure (no UI types), so run it off-thread.
            using (var strip = await Task.Run(() => _renderer.RenderStrip(settings, src, bg, 1.0, layerArt)))
                await _export.SavePngAsync(strip, path);

            if (ExportAt2x)
            {
                var path2x = AppendSuffix(path, "@2x");
                using var strip2x = await Task.Run(() => _renderer.RenderStrip(settings, src, bg, 2.0, layerArt));
                await _export.SavePngAsync(strip2x, path2x);
            }

            bool wroteManifest = false;
            if (ExportManifest)
            {
                var asset = Path.GetFileName(path);
                var asset2x = ExportAt2x ? Path.GetFileName(AppendSuffix(path, "@2x")) : null;
                var controlId = Path.GetFileNameWithoutExtension(path);
                var parameterId = string.IsNullOrWhiteSpace(ParameterId) ? controlId : ParameterId.Trim();

                var manifest = _manifest.BuildSingleControl(settings, asset, asset2x, controlId, parameterId);
                var manifestPath = Path.Combine(Path.GetDirectoryName(path) ?? "", controlId + ".skin.json");
                await _manifest.SaveAsync(manifest, manifestPath);
                wroteManifest = true;
            }

            int wroteCode = 0;
            if (ExportCode)
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                var asset = Path.GetFileName(path);
                var asset2x = ExportAt2x ? Path.GetFileName(AppendSuffix(path, "@2x")) : null;
                var controlId = Path.GetFileNameWithoutExtension(path);
                var parameterId = string.IsNullOrWhiteSpace(ParameterId) ? controlId : ParameterId.Trim();

                var request = new CodeSnippetRequest(ComponentType, FrameCount, FrameWidth, FrameHeight,
                                                     StackDirection, asset, asset2x, controlId, parameterId);
                foreach (var target in SelectedCodeTargets())
                {
                    await _codeSnippets.SaveAsync(target, request, dir);
                    wroteCode++;
                }
            }

            StatusMessage = $"Exported {FrameCount}-frame filmstrip → {Path.GetFileName(path)}"
                          + (ExportAt2x ? " (+@2x)" : "")
                          + (wroteManifest ? " (+skin.json)" : "")
                          + (wroteCode > 0 ? $" (+{wroteCode} code file{(wroteCode == 1 ? "" : "s")})" : "");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during export: {ex.Message}";
        }
    }

    private static string AppendSuffix(string path, string suffix)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return Path.Combine(dir, $"{name}{suffix}{ext}");
    }

    private void RefreshPreview()
    {
        if (_source is null && !IsMeter && _baseLayer is null && !HasImportedLayers)
        {
            PreviewImage = null;
            return;
        }

        var source = _source;

        try
        {
            var settings = BuildSettings();
            // Alignment is interactive (live crosshair drag + playback), so drop supersampling
            // while the crosshair is on to keep it snappy; the normal preview keeps quality and
            // export always uses the full supersample setting.
            settings.Supersample = ShowCenterGuide ? 1 : Math.Min(settings.Supersample, 2);

            // While the crosshair is on, render the art at its neutral (rectangle-centred) position
            // so dragging the crosshair marks a point on a STATIONARY knob. The chosen centre is
            // applied once the guide is turned off — and the crosshair overlay maps to exactly this
            // rectangle-centred art (see ArtRectOnScreen), so the mark lands where you drop it.
            if (ShowCenterGuide)
            {
                settings.SourceCenterX = 0.5;
                settings.SourceCenterY = 0.5;
            }

            // Continuous preview: a fixed, fine virtual frame resolution (1024) so the preview
            // and the aligned position are smooth and NOT quantised to the (possibly coarse)
            // export frame steps. Export still uses the sidebar frame count — this is preview-only.
            int previewN = Math.Max(FrameCount, 1024);
            settings.FrameCount = previewN;
            int idx = Math.Clamp((int)Math.Round(PreviewValue * (previewN - 1)), 0, previewN - 1);

            double scale = PreviewDisplaySize / Math.Max(1, Math.Max(FrameWidth, FrameHeight));

            // A layered knob renders from its base + pointer; everything else from the single
            // source (kept on the exact 5-arg call so the render path is unchanged).
            var layerArt = BuildLayerArt();
            using var frame = layerArt is not null
                ? _renderer.RenderFrame(settings, source, _background, idx, scale, layerArt)
                : _renderer.RenderFrame(settings, source, _background, idx, scale);
            PreviewImage = SkiaImageInterop.ToAvaloniaBitmap(frame);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview error: {ex.Message}";
        }
    }
}
