using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripKit.Models;
using StripKit.Services;
using SkiaSharp;

namespace StripKit.ViewModels;

/// <summary>
/// Backs the "Batch" tab: pick a folder of source images and an output folder, set a
/// render template, then export a filmstrip for each source off the UI thread with a
/// progress bar and a working cancel. Holds no Avalonia UI types.
/// </summary>
public partial class BatchViewModel : ViewModelBase
{
    private static readonly string[] AcceptedExtensions = [".png", ".webp", ".bmp", ".jpg", ".jpeg"];

    private readonly IFileDialogService _dialogs;
    private readonly IBatchProcessor _batch;

    private List<string> _inputFiles = [];
    private CancellationTokenSource? _cts;

    public BatchViewModel(IFileDialogService dialogs, IBatchProcessor batch)
    {
        _dialogs = dialogs;
        _batch = batch;
    }

    // ---- combo choices (shared shape with the Create tab) ----
    public ComponentType[] ComponentTypes { get; } =
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider, ComponentType.Meter];
    public StackDirection[] StackDirections { get; } = [StackDirection.Vertical, StackDirection.Horizontal];
    public MeterFillDirection[] FillDirections { get; } =
        [MeterFillDirection.Up, MeterFillDirection.Down, MeterFillDirection.LeftToRight, MeterFillDirection.RightToLeft];
    public int[] SupersampleOptions { get; } = [1, 2, 4, 8];

    // ---- folders ----
    [ObservableProperty] private string _inputFolder = "";
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _inputSummary = "No input folder chosen.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _hasInputFiles;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _hasOutput;

    // ---- render template ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRotary), nameof(IsMeter))]
    private ComponentType _componentType = ComponentType.RotaryKnob;

    [ObservableProperty] private int _frameCount = 64;
    [ObservableProperty] private int _frameWidth = 80;
    [ObservableProperty] private int _frameHeight = 80;
    [ObservableProperty] private double _sweepDegrees = 270;
    [ObservableProperty] private bool _rotationClockwise = true;
    [ObservableProperty] private int _supersample = 4;
    [ObservableProperty] private StackDirection _stackDirection = StackDirection.Vertical;
    [ObservableProperty] private bool _matchKnobFrameToSource = true;
    [ObservableProperty] private bool _exportAt2x = true;
    [ObservableProperty] private bool _exportManifest;

    // ---- meter template (used when ComponentType is Meter) ----
    [ObservableProperty] private int _segmentCount = 12;
    [ObservableProperty] private MeterFillDirection _fillDirection = MeterFillDirection.Up;
    [ObservableProperty] private bool _continuousFill;
    [ObservableProperty] private string _onColorHex = "#FFE8440A";
    [ObservableProperty] private string _offColorHex = "#FF2A2A2A";

    /// <summary>When true each source is a backdrop and procedural LED segments are drawn over
    /// it; when false each source is the lit on-state art revealed up to the fill.</summary>
    [ObservableProperty] private bool _meterSourceIsBackdrop;

    // ---- run state ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty] private double _progressValue;   // 0..100 for a ProgressBar
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _statusMessage = "Choose an input folder and an output folder.";
    [ObservableProperty] private string _resultSummary = "";

    public bool IsRotary => ComponentType == ComponentType.RotaryKnob;
    public bool IsMeter => ComponentType == ComponentType.Meter;

    // ---- commands ----

    [RelayCommand]
    private async Task ChooseInputFolderAsync()
    {
        var folder = await _dialogs.OpenFolderAsync("Choose the folder of source images");
        if (folder is null) return;

        InputFolder = folder;
        _inputFiles = Directory.EnumerateFiles(folder)
            .Where(f => AcceptedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HasInputFiles = _inputFiles.Count > 0;
        InputSummary = _inputFiles.Count == 0
            ? "No PNG/image files found in that folder."
            : $"{_inputFiles.Count} image(s) found.";
    }

    [RelayCommand]
    private async Task ChooseOutputFolderAsync()
    {
        var folder = await _dialogs.OpenFolderAsync("Choose the output folder");
        if (folder is null) return;

        OutputFolder = folder;
        HasOutput = true;
    }

    private bool CanRun() => HasInputFiles && HasOutput && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ResultSummary = "";
        ProgressValue = 0;
        ProgressText = $"0/{_inputFiles.Count}";
        StatusMessage = "Processing…";

        var options = new BatchOptions
        {
            InputFiles = _inputFiles.ToArray(),
            OutputDirectory = OutputFolder,
            Settings = BuildSettings(),
            MatchKnobFrameToSource = MatchKnobFrameToSource,
            MeterSourceIsBackdrop = MeterSourceIsBackdrop,
            ExportAt2x = ExportAt2x,
            ExportManifest = ExportManifest,
        };

        // Created on the UI thread → Report callbacks marshal back to the UI thread.
        var progress = new Progress<BatchProgress>(p =>
        {
            ProgressValue = p.Total > 0 ? (double)p.Completed / p.Total * 100.0 : 0;
            ProgressText = $"{p.Completed}/{p.Total} — {p.CurrentFile}";
        });

        try
        {
            var result = await _batch.ProcessAsync(options, progress, _cts.Token);
            StatusMessage = result.Cancelled
                ? $"Cancelled — {result.SucceededCount} of {options.InputFiles.Count} written."
                : $"Done — {result.SucceededCount} written, {result.FailedCount} failed.";
            ResultSummary = BuildSummary(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Batch error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private FilmstripSettings BuildSettings()
    {
        double half = SweepDegrees / 2.0;
        return new FilmstripSettings
        {
            ComponentType = ComponentType,
            FrameCount = FrameCount,
            FrameWidth = FrameWidth,
            FrameHeight = FrameHeight,
            StartAngleDegrees = RotationClockwise ? -half : half,
            EndAngleDegrees = RotationClockwise ? half : -half,
            Supersample = Supersample,
            StackDirection = StackDirection,
            // Meter fields (used only when ComponentType is Meter; harmless otherwise).
            SegmentCount = SegmentCount,
            FillDirection = FillDirection,
            ContinuousFill = ContinuousFill,
            OnColorArgb = ParseArgb(OnColorHex, 0xFFE8440A),
            OffColorArgb = ParseArgb(OffColorHex, 0xFF2A2A2A),
        };
    }

    /// <summary>Parses a "#AARRGGBB"/"#RRGGBB" colour to packed ARGB, or the fallback.</summary>
    private static uint ParseArgb(string hex, uint fallback) =>
        SKColor.TryParse(hex, out var c)
            ? ((uint)c.Alpha << 24) | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue
            : fallback;

    private static string BuildSummary(BatchResult result)
    {
        var failures = result.Items.Where(i => !i.Success).ToList();
        if (failures.Count == 0)
            return $"All {result.SucceededCount} strips exported.";

        var lines = failures.Take(8).Select(f => $"  • {Path.GetFileName(f.InputFile)}: {f.Error}");
        return $"{result.SucceededCount} exported, {failures.Count} failed:\n"
             + string.Join("\n", lines)
             + (failures.Count > 8 ? $"\n  …and {failures.Count - 8} more" : "");
    }
}
