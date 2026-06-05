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

    private SKBitmap? _source;
    private SKBitmap? _background;
    private string? _sourcePath;

    // Layer-aware knob (base + pointer): a static body layer and a rotating pointer layer.
    // Empty ⇒ the single source above animates as before.
    private SKBitmap? _baseLayer;
    private SKBitmap? _pointer;
    private string? _baseLayerPath;

    // Suppresses the preview/recompute funnel while we set several properties at
    // once (e.g. applying type defaults), so we refresh only once afterwards.
    private bool _suspendRefresh;

    // The preview frame is rendered at roughly this many pixels on its long edge,
    // so it stays sharp regardless of the (often small) real frame size.
    private const double PreviewDisplaySize = 380.0;

    public MainWindowViewModel(IImageLoadService imageLoad, IFilmstripRenderer renderer,
                               IFileDialogService dialogs, IExportService export,
                               IManifestService manifest, ICodeSnippetService codeSnippets,
                               ImporterViewModel importer, BatchViewModel batch, SkinViewModel skin)
    {
        _imageLoad = imageLoad;
        _renderer = renderer;
        _dialogs = dialogs;
        _export = export;
        _manifest = manifest;
        _codeSnippets = codeSnippets;
        Importer = importer;
        Batch = batch;
        Skin = skin;

        SourceInfo = "No image loaded.";
        BackgroundInfo = "None.";
        StatusMessage = "Load a source image to begin.";
        UpdateReadouts();
        UpdateCodePreview();
    }

    /// <summary>The "Import" tab's view model (hosted in a second tab in the window).</summary>
    public ImporterViewModel Importer { get; }

    /// <summary>The "Batch" tab's view model (hosted in a third tab in the window).</summary>
    public BatchViewModel Batch { get; }

    /// <summary>The "Skin" tab's view model (the multi-control manifest builder).</summary>
    public SkinViewModel Skin { get; }

    // ---- combo box choices ----
    public ComponentType[] ComponentTypes { get; } =
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider, ComponentType.Meter];

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
    [NotifyPropertyChangedFor(nameof(IsRotary), nameof(IsLinear), nameof(IsMeter), nameof(ShowLoadHint))]
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

    public bool IsRotary => ComponentType == ComponentType.RotaryKnob;
    public bool IsLinear => ComponentType is ComponentType.VerticalFader or ComponentType.HorizontalSlider;
    public bool IsMeter => ComponentType == ComponentType.Meter;

    /// <summary>The "load a source" overlay shows only when there is nothing to preview;
    /// a procedural meter renders without a source, and a layered knob previews from its
    /// base layer.</summary>
    public bool ShowLoadHint => !HasSource && !IsMeter && !HasBaseLayer;

    /// <summary>True when a layered knob (a base body, optionally a pointer) should drive
    /// the render instead of the single source. Knob-only.</summary>
    private bool IsLayeredKnob => IsRotary && _baseLayer is not null;

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
            case nameof(ShowLoadHint):
            case nameof(IsPlaying):
            case nameof(ExportManifest):
            case nameof(HasBaseLayer):
            case nameof(HasPointer):
            case nameof(BaseLayerInfo):
            case nameof(PointerInfo):
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

        // Layered knob: a static base body + (optionally) a rotating pointer with its own
        // pivot. Left empty for every other case, so the single-source render is unchanged.
        if (IsLayeredKnob)
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

    /// <summary>The layer bitmaps matching <see cref="FilmstripSettings.Layers"/> for a
    /// layered knob — base body first, then the pointer — or null when not layered.</summary>
    private IReadOnlyList<SKBitmap>? BuildLayerArt()
    {
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

    // A procedural meter and a layered knob (base body) both render without a single source.
    private bool CanExport() => HasSource || IsMeter || HasBaseLayer;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_source is null && !IsMeter && _baseLayer is null) return;

        var baseName = Path.GetFileNameWithoutExtension(_sourcePath ?? _baseLayerPath) ?? "filmstrip";
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
        if (_source is null && !IsMeter && _baseLayer is null)
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
