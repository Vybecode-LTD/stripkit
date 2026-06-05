using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripKit.Helpers;
using StripKit.Models;
using StripKit.Services;
using SkiaSharp;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace StripKit.ViewModels;

/// <summary>
/// Backs the "Import" tab: open an existing filmstrip, detect its layout, scrub
/// the detected frames, and extract a single frame or re-stack the strip in the
/// opposite orientation. The detected frame count is offered as an editable value
/// — detection is a guess that must be verified (see the importer skill). Holds no
/// Avalonia UI types beyond the preview <see cref="AvBitmap"/>.
/// </summary>
public partial class ImporterViewModel : ViewModelBase
{
    private readonly IImageLoadService _imageLoad;
    private readonly IFilmstripImporter _importer;
    private readonly IFileDialogService _dialogs;
    private readonly IExportService _export;

    private SKBitmap? _strip;
    private string _baseName = "strip";
    private bool _detectedVertical = true;
    private int _frameW;
    private int _frameH;
    private bool _suspendRefresh;

    public ImporterViewModel(IImageLoadService imageLoad, IFilmstripImporter importer,
                             IFileDialogService dialogs, IExportService export)
    {
        _imageLoad = imageLoad;
        _importer = importer;
        _dialogs = dialogs;
        _export = export;
    }

    [ObservableProperty] private string _stripInfo = "No strip loaded.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractCurrentFrameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportRestackedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportResampledCommand))]
    private bool _hasStrip;

    [ObservableProperty] private int _frameCount = 1;

    /// <summary>The output frame count for a resample (re-time), independent of the slice count.</summary>
    [ObservableProperty] private int _targetFrameCount = 64;
    [ObservableProperty] private bool _lowConfidence;
    [ObservableProperty] private string _detectedInfo = "";
    [ObservableProperty] private string _frameSizeInfo = "";

    [ObservableProperty] private double _previewValue = 0.5;
    [ObservableProperty] private AvBitmap? _previewImage;
    [ObservableProperty] private string _previewReadout = "";
    [ObservableProperty] private string _statusMessage = "Load or drop a filmstrip to begin.";

    /// <summary>
    /// Single funnel (mirrors MainWindowViewModel): an editable input change
    /// recomputes the frame size and refreshes the previewed frame. Outputs are
    /// filtered out to avoid feedback loops.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(StripInfo):
            case nameof(HasStrip):
            case nameof(LowConfidence):
            case nameof(DetectedInfo):
            case nameof(FrameSizeInfo):
            case nameof(PreviewImage):
            case nameof(PreviewReadout):
            case nameof(StatusMessage):
            case nameof(TargetFrameCount):   // a resample target, not a slice change → no re-preview
                return;
        }

        if (_suspendRefresh)
            return;

        if (e.PropertyName == nameof(FrameCount))
            RecomputeFrameSize();

        RefreshPreview();
    }

    /// <summary>Shared load path used by the button and the drag-and-drop handler.</summary>
    public void LoadStripFromPath(string path)
    {
        var bmp = _imageLoad.Load(path);
        if (bmp is null)
        {
            StatusMessage = "Error: could not load that image.";
            return;
        }

        _strip?.Dispose();
        _strip = bmp;
        _baseName = Path.GetFileNameWithoutExtension(path) is { Length: > 0 } b ? b : "strip";
        StripInfo = $"{Path.GetFileName(path)} — {bmp.Width}×{bmp.Height}px";

        var detection = _importer.Detect(bmp);
        _detectedVertical = detection.Vertical;

        _suspendRefresh = true;
        FrameCount = detection.FrameCount;
        TargetFrameCount = detection.FrameCount;   // default the resample target to the detected count
        _suspendRefresh = false;

        LowConfidence = detection.LowConfidence;
        HasStrip = true;
        DetectedInfo = $"{detection.FrameCount} frames · {detection.FrameWidth}×{detection.FrameHeight}px · "
                     + $"{detection.KindLabel} · {(detection.Vertical ? "vertical" : "horizontal")}";

        RecomputeFrameSize();
        RefreshPreview();

        StatusMessage = detection.LowConfidence
            ? "Loaded — detection is ambiguous; verify the frame count."
            : "Strip loaded.";
    }

    private void RecomputeFrameSize()
    {
        if (_strip is null || FrameCount < 1)
            return;

        int total = _detectedVertical ? _strip.Height : _strip.Width;
        _frameW = _detectedVertical ? _strip.Width : Math.Max(1, _strip.Width / FrameCount);
        _frameH = _detectedVertical ? Math.Max(1, _strip.Height / FrameCount) : _strip.Height;

        FrameSizeInfo = total % FrameCount == 0
            ? $"Frame: {_frameW}×{_frameH}px"
            : $"Frame: {_frameW}×{_frameH}px   ⚠ {total}px doesn't divide evenly by {FrameCount}";
    }

    private StripDetection CurrentLayout() =>
        new(_detectedVertical, Math.Max(1, FrameCount), _frameW, _frameH, null, false, System.Array.Empty<int>());

    private int CurrentFrameIndex()
    {
        int n = Math.Max(1, FrameCount);
        return Math.Clamp((int)Math.Round(PreviewValue * (n - 1)), 0, n - 1);
    }

    private void RefreshPreview()
    {
        if (_strip is null)
        {
            PreviewImage = null;
            return;
        }

        try
        {
            int idx = CurrentFrameIndex();
            using var frame = _importer.ExtractFrame(_strip, CurrentLayout(), idx);
            PreviewImage = SkiaImageInterop.ToAvaloniaBitmap(frame);
            PreviewReadout = $"Frame {idx + 1}/{Math.Max(1, FrameCount)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview error: {ex.Message}";
        }
    }

    // ---- commands ----

    [RelayCommand]
    private async Task OpenStripAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;
        LoadStripFromPath(path);
    }

    private bool CanUseStrip() => HasStrip;

    [RelayCommand(CanExecute = nameof(CanUseStrip))]
    private async Task ExtractCurrentFrameAsync()
    {
        if (_strip is null) return;

        int idx = CurrentFrameIndex();
        var path = await _dialogs.SavePngAsync($"{_baseName}_frame{idx + 1}.png");
        if (path is null) return;

        try
        {
            using var frame = _importer.ExtractFrame(_strip, CurrentLayout(), idx);
            await _export.SavePngAsync(frame, path);
            StatusMessage = $"Extracted frame {idx + 1} → {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error extracting frame: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseStrip))]
    private async Task ExportRestackedAsync()
    {
        if (_strip is null) return;

        var target = _detectedVertical ? StackDirection.Horizontal : StackDirection.Vertical;
        var tag = target == StackDirection.Vertical ? "v" : "h";
        var path = await _dialogs.SavePngAsync($"{_baseName}_{FrameCount}frames_{tag}.png");
        if (path is null) return;

        try
        {
            StatusMessage = "Re-stacking…";
            var strip = _strip;
            var layout = CurrentLayout();
            using var restacked = await Task.Run(() => _importer.Restack(strip, layout, target));
            await _export.SavePngAsync(restacked, path);
            StatusMessage = $"Re-stacked to {(target == StackDirection.Vertical ? "vertical" : "horizontal")} → {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error re-stacking: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseStrip))]
    private async Task ExportResampledAsync()
    {
        if (_strip is null) return;

        int dst = Math.Max(1, TargetFrameCount);
        var tag = _detectedVertical ? "v" : "h";
        var path = await _dialogs.SavePngAsync($"{_baseName}_{dst}frames_{tag}.png");
        if (path is null) return;

        try
        {
            StatusMessage = "Resampling…";
            var strip = _strip;
            var layout = CurrentLayout();
            using var resampled = await Task.Run(() => _importer.Resample(strip, layout, dst));
            await _export.SavePngAsync(resampled, path);
            StatusMessage = $"Resampled {FrameCount} → {dst} frames → {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error resampling: {ex.Message}";
        }
    }
}
