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

    private SKBitmap? _source;
    private SKBitmap? _background;
    private string? _sourcePath;
    private AvBitmap? _sourceAv;      // cached Avalonia copy of _source for the align overlay
    private SKBitmap? _sourceAvOf;    // the _source that _sourceAv was built from

    // Suppresses the preview/recompute funnel while we set several properties at
    // once (e.g. applying type defaults), so we refresh only once afterwards.
    private bool _suspendRefresh;

    // The preview frame is rendered at roughly this many pixels on its long edge,
    // so it stays sharp regardless of the (often small) real frame size.
    private const double PreviewDisplaySize = 380.0;

    public MainWindowViewModel(IImageLoadService imageLoad, IFilmstripRenderer renderer,
                               IFileDialogService dialogs, IExportService export,
                               IManifestService manifest, ImporterViewModel importer,
                               BatchViewModel batch)
    {
        _imageLoad = imageLoad;
        _renderer = renderer;
        _dialogs = dialogs;
        _export = export;
        _manifest = manifest;
        Importer = importer;
        Batch = batch;

        SourceInfo = "No image loaded.";
        BackgroundInfo = "None.";
        StatusMessage = "Load a source image to begin.";
        UpdateReadouts();
    }

    /// <summary>The "Import" tab's view model (hosted in a second tab in the window).</summary>
    public ImporterViewModel Importer { get; }

    /// <summary>The "Batch" tab's view model (hosted in a third tab in the window).</summary>
    public BatchViewModel Batch { get; }

    // ---- combo box choices ----
    public ComponentType[] ComponentTypes { get; } =
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider, ComponentType.Meter];

    public StackDirection[] StackDirections { get; } =
        [StackDirection.Vertical, StackDirection.Horizontal];

    public MeterFillDirection[] FillDirections { get; } =
        [MeterFillDirection.Up, MeterFillDirection.Down, MeterFillDirection.LeftToRight, MeterFillDirection.RightToLeft];

    public int[] SupersampleOptions { get; } = [1, 2, 4, 8];

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
    /// a procedural meter renders without a source.</summary>
    public bool ShowLoadHint => !HasSource && !IsMeter;

    /// <summary>
    /// Single funnel: any meaningful input change recomputes derived values and
    /// refreshes the preview. Outputs are filtered out to avoid feedback loops.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

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
            case nameof(ParameterId):
                return;
        }

        if (_suspendRefresh)
            return;

        if (e.PropertyName is nameof(SweepDegrees) or nameof(RotationClockwise))
            RecomputeAnglesFromSweep();

        if (e.PropertyName == nameof(ComponentType))
            ApplyTypeDefaults();

        UpdateReadouts();
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
                if (_source is not null)
                {
                    FrameWidth = Math.Max(_source.Width, _source.Height);
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

    private FilmstripSettings BuildSettings() => new()
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
    };

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
        _sourceAv = null;             // invalidate the cached align-overlay bitmap
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

    private bool CanExport() => HasSource || IsMeter;   // a procedural meter needs no source

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_source is null && !IsMeter) return;

        var baseName = Path.GetFileNameWithoutExtension(_sourcePath) ?? "filmstrip";
        var suggested = $"{baseName}_{FrameCount}frames.png";

        var path = await _dialogs.SavePngAsync(suggested);
        if (path is null) return;

        try
        {
            StatusMessage = "Rendering…";

            var settings = BuildSettings();
            var src = _source;        // captured for the background render thread
            var bg = _background;

            // Rendering is CPU-bound and pure (no UI types), so run it off-thread.
            using (var strip = await Task.Run(() => _renderer.RenderStrip(settings, src, bg, 1.0)))
                await _export.SavePngAsync(strip, path);

            if (ExportAt2x)
            {
                var path2x = AppendSuffix(path, "@2x");
                using var strip2x = await Task.Run(() => _renderer.RenderStrip(settings, src, bg, 2.0));
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

            StatusMessage = $"Exported {FrameCount}-frame filmstrip → {Path.GetFileName(path)}"
                          + (ExportAt2x ? " (+@2x)" : "")
                          + (wroteManifest ? " (+skin.json)" : "");
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
        if (_source is null && !IsMeter)
        {
            PreviewImage = null;
            return;
        }

        var source = _source;

        try
        {
            // Alignment mode: show the raw source so the view can overlay a draggable
            // centre crosshair. Cache the Avalonia copy so dragging doesn't re-encode.
            if (ShowCenterGuide && source is not null)
            {
                if (_sourceAv is null || !ReferenceEquals(_sourceAvOf, source))
                {
                    _sourceAv = SkiaImageInterop.ToAvaloniaBitmap(source);
                    _sourceAvOf = source;
                }
                PreviewImage = _sourceAv;
                return;
            }

            int n = Math.Max(1, FrameCount);
            int idx = Math.Clamp((int)Math.Round(PreviewValue * (n - 1)), 0, n - 1);

            // Render the preview at display size for crispness, and cap supersample
            // at 2 so scrubbing and playback stay responsive (export uses the full
            // supersample setting).
            var settings = BuildSettings();
            settings.Supersample = Math.Min(settings.Supersample, 2);

            double scale = PreviewDisplaySize / Math.Max(1, Math.Max(FrameWidth, FrameHeight));

            using var frame = _renderer.RenderFrame(settings, source, _background, idx, scale);
            PreviewImage = SkiaImageInterop.ToAvaloniaBitmap(frame);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview error: {ex.Message}";
        }
    }
}
