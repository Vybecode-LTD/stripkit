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

/// <summary>
/// Backs the "Assemble" tab: take a folder (or drag-drop) of individually-rendered frames — e.g. a
/// path-traced PNG sequence from Blender / KeyShot / Octane — natural-sort them, and pack them into
/// one stacked filmstrip with the usual @2x / skin.json / loader-code exports. The procedural
/// renderer isn't involved; this is the import-side bridge for offline-3D art. Holds no Avalonia UI
/// types beyond the preview <see cref="AvBitmap"/>.
/// </summary>
public partial class FrameSequenceViewModel : ViewModelBase
{
    private static readonly string[] AcceptedExtensions = [".png", ".webp", ".bmp", ".jpg", ".jpeg"];

    private readonly IImageLoadService _imageLoad;
    private readonly IFrameSequenceAssembler _assembler;
    private readonly IFileDialogService _dialogs;
    private readonly IExportService _export;
    private readonly IManifestService _manifest;
    private readonly ICodeSnippetService _codeSnippets;
    private readonly IRenderRecipeService _recipes;

    private CancellationTokenSource? _cts;

    public FrameSequenceViewModel(IImageLoadService imageLoad, IFrameSequenceAssembler assembler,
                                  IFileDialogService dialogs, IExportService export,
                                  IManifestService manifest, ICodeSnippetService codeSnippets,
                                  IRenderRecipeService recipes)
    {
        _imageLoad = imageLoad;
        _assembler = assembler;
        _dialogs = dialogs;
        _export = export;
        _manifest = manifest;
        _codeSnippets = codeSnippets;
        _recipes = recipes;
        UpdateRecipePreview();
    }

    /// <summary>The ordered frames that will be stacked (1 = first/min, N = last/max).</summary>
    public ObservableCollection<FrameItemRow> Frames { get; } = [];

    // ---- combo choices (shared shape with the other tabs) ----
    public ComponentType[] ComponentTypes { get; } =
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider,
         ComponentType.Meter, ComponentType.Button, ComponentType.Toggle];
    public StackDirection[] StackDirections { get; } = [StackDirection.Vertical, StackDirection.Horizontal];
    public CellFit[] CellFits { get; } = [CellFit.PadToLargest, CellFit.CropToSmallest, CellFit.Strict];
    public FrameInterpolation[] Interpolations { get; } = [FrameInterpolation.Nearest, FrameInterpolation.Crossfade];

    // ---- sequence state ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckFramesCommand))]
    private bool _hasFrames;

    [ObservableProperty] private string _sequenceInfo = "No frames yet — choose a folder or drop a rendered sequence here.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string _warningText = "";

    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningText);

    // ---- assembly options ----
    [ObservableProperty] private ComponentType _componentType = ComponentType.RotaryKnob;
    [ObservableProperty] private StackDirection _stackDirection = StackDirection.Vertical;
    [ObservableProperty] private CellFit _cellFit = CellFit.PadToLargest;
    [ObservableProperty] private bool _recenterOnContent;
    [ObservableProperty] private bool _unpremultiplyAlpha;

    [ObservableProperty] private bool _resampleEnabled;
    [ObservableProperty] private int _targetFrameCount = 64;

    /// <summary>Nearest source frame (default, never ghosts) or crossfade blend (P4 — synthesize
    /// in-betweens so a few expensive path-traced frames ship as a standard count).</summary>
    [ObservableProperty] private FrameInterpolation _interpolation = FrameInterpolation.Nearest;

    // ---- export options (mirror the Create tab) ----
    [ObservableProperty] private bool _exportAt2x = true;
    [ObservableProperty] private bool _exportManifest = true;
    [ObservableProperty] private string _parameterId = "";
    [ObservableProperty] private bool _exportCode;
    [ObservableProperty] private bool _emitCodeJuce = true;
    [ObservableProperty] private bool _emitCodeCss;
    [ObservableProperty] private bool _emitCodeIPlug2;
    [ObservableProperty] private bool _emitCodeHise;

    // ---- render recipe (plan the offline render that feeds this tab) ----
    public RenderRecipeTarget[] RecipeTargets { get; } =
        [RenderRecipeTarget.Blender, RenderRecipeTarget.Csv, RenderRecipeTarget.Json];
    [ObservableProperty] private RenderRecipeTarget _recipePreviewTarget = RenderRecipeTarget.Blender;
    [ObservableProperty] private int _recipeFrameCount = 64;
    [ObservableProperty] private double _recipeSweepDegrees = 270;
    [ObservableProperty] private int _recipeResolution = 256;
    [ObservableProperty] private string _generatedRecipe = "";

    // ---- preview ----
    [ObservableProperty] private double _previewValue;
    [ObservableProperty] private AvBitmap? _previewImage;
    [ObservableProperty] private string _previewReadout = "Frame —/—";

    // ---- run state ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckFramesCommand))]
    private bool _isRunning;

    [ObservableProperty] private double _progressValue;   // 0..100 for a ProgressBar
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _statusMessage = "Point at a folder of rendered frames to begin.";

    /// <summary>Single funnel (mirrors the Import tab): scrubbing the preview slider re-renders the
    /// previewed frame. Everything else only updates derived labels, so it's filtered out.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(PreviewValue))
            RefreshPreview();
        else if (e.PropertyName is nameof(ComponentType) or nameof(RecipeFrameCount)
                 or nameof(RecipeSweepDegrees) or nameof(RecipeResolution) or nameof(RecipePreviewTarget))
            UpdateRecipePreview();
    }

    // ---- render recipe ----

    /// <summary>Build the recipe request from the recipe inputs (clockwise sweep, square render size).</summary>
    private RenderRecipeRequest BuildRecipeRequest()
    {
        double half = RecipeSweepDegrees / 2.0;
        return new RenderRecipeRequest(ComponentType, RecipeFrameCount, -half, half,
                                       RecipeResolution, RecipeResolution, SuggestBaseName());
    }

    private void UpdateRecipePreview() =>
        GeneratedRecipe = _recipes.Generate(RecipePreviewTarget, BuildRecipeRequest());

    [RelayCommand]
    private async Task SaveRecipeAsync()
    {
        var dir = await _dialogs.OpenFolderAsync("Choose a folder for the render recipe");
        if (string.IsNullOrEmpty(dir)) return;
        var path = await _recipes.SaveAsync(RecipePreviewTarget, BuildRecipeRequest(), dir);
        StatusMessage = $"Saved render recipe → {Path.GetFileName(path)}";
    }

    // ---- frame-list management ----

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var folder = await _dialogs.OpenFolderAsync("Choose the folder of rendered frames");
        if (folder is null) return;

        var paths = Directory.EnumerateFiles(folder)
            .Where(f => AcceptedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();
        SetFrames(paths);
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var paths = await _dialogs.OpenImagesAsync();
        if (paths.Count == 0) return;
        AddFrames(paths);
    }

    /// <summary>Shared with the view's drag-and-drop handler: add dropped images (or all the images
    /// inside a dropped folder, already expanded by the view) to the sequence.</summary>
    public void AddDroppedPaths(IReadOnlyList<string> paths)
    {
        var images = paths
            .Where(p => AcceptedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (images.Count == 0) return;
        if (Frames.Count == 0) SetFrames(images);
        else AddFrames(images);
    }

    [RelayCommand]
    private void ClearFrames()
    {
        Frames.Clear();
        HasFrames = false;
        WarningText = "";
        SequenceInfo = "No frames yet — choose a folder or drop a rendered sequence here.";
        PreviewImage = null;
        PreviewReadout = "Frame —/—";
        StatusMessage = "Cleared.";
    }

    [RelayCommand]
    private void RemoveFrame(FrameItemRow? row)
    {
        if (row is null) return;
        Frames.Remove(row);
        Renumber();
    }

    [RelayCommand]
    private void MoveFrameUp(FrameItemRow? row)
    {
        if (row is null) return;
        int i = Frames.IndexOf(row);
        if (i > 0) { Frames.Move(i, i - 1); Renumber(); }
    }

    [RelayCommand]
    private void MoveFrameDown(FrameItemRow? row)
    {
        if (row is null) return;
        int i = Frames.IndexOf(row);
        if (i >= 0 && i < Frames.Count - 1) { Frames.Move(i, i + 1); Renumber(); }
    }

    private void SetFrames(IReadOnlyList<string> paths)
    {
        Frames.Clear();
        AddFrames(paths);
    }

    private void AddFrames(IReadOnlyList<string> paths)
    {
        // Combine existing + new (de-duplicated), then natural-sort the whole set into render order.
        var seen = new HashSet<string>(Frames.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var combined = Frames.Select(f => f.Path).Concat(paths.Where(p => seen.Add(p))).ToList();

        var probe = _assembler.Probe(combined, _imageLoad);
        RebuildFrames(probe.OrderedPaths);

        WarningText = string.Join("\n", probe.Warnings);
        SequenceInfo = probe.HasFrames
            ? $"{probe.OrderedPaths.Count} frame(s) · "
              + (probe.Uniform ? $"{probe.MaxWidth}×{probe.MaxHeight}px · uniform"
                               : $"{probe.MinWidth}×{probe.MinHeight}–{probe.MaxWidth}×{probe.MaxHeight}px · mixed — will reconcile")
            : "No readable image frames found.";
        HasFrames = Frames.Count >= 2;
        StatusMessage = HasFrames
            ? $"{Frames.Count} frames ready. Scrub to check the order, then Assemble."
            : "Add at least two frames to assemble a filmstrip.";
        RefreshPreview();
    }

    private void RebuildFrames(IReadOnlyList<string> orderedPaths)
    {
        Frames.Clear();
        for (int i = 0; i < orderedPaths.Count; i++)
            Frames.Add(new FrameItemRow(orderedPaths[i]) { Position = i + 1 });
    }

    private void Renumber()
    {
        for (int i = 0; i < Frames.Count; i++)
            Frames[i].Position = i + 1;
        HasFrames = Frames.Count >= 2;
        SequenceInfo = Frames.Count == 0
            ? "No frames yet — choose a folder or drop a rendered sequence here."
            : $"{Frames.Count} frame(s) ordered.";
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (Frames.Count == 0)
        {
            PreviewImage = null;
            PreviewReadout = "Frame —/—";
            return;
        }

        int n = Frames.Count;
        int idx = Math.Clamp((int)Math.Round(PreviewValue * (n - 1)), 0, n - 1);

        var bmp = _imageLoad.Load(Frames[idx].Path);
        if (bmp is null)
        {
            PreviewReadout = $"Frame {idx + 1}/{n} — unreadable";
            return;
        }
        using (bmp)
            PreviewImage = SkiaImageInterop.ToAvaloniaBitmap(bmp);
        PreviewReadout = $"Frame {idx + 1}/{n}";
    }

    // ---- target-count presets ----
    [RelayCommand] private void SetTarget32() => TargetFrameCount = 32;
    [RelayCommand] private void SetTarget64() => TargetFrameCount = 64;
    [RelayCommand] private void SetTarget128() => TargetFrameCount = 128;

    // ---- assemble + export ----

    private bool CanExport() => HasFrames && !IsRunning;

    /// <summary>Render-QC pre-flight: decode the frames off the UI thread and report the path-tracer
    /// failure modes (drift, missing transparency, blank or premultiplied frames) without assembling.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task CheckFramesAsync()
    {
        var paths = Frames.Select(f => f.Path).ToList();
        StatusMessage = "Checking render quality…";

        var report = await Task.Run(() =>
        {
            var bitmaps = new List<SKBitmap>(paths.Count);
            try
            {
                foreach (var p in paths)
                {
                    var bmp = _imageLoad.Load(p);
                    if (bmp is not null) bitmaps.Add(bmp);
                }
                return bitmaps.Count >= 1 ? FrameSequenceAssembler.AnalyzeQc(bitmaps) : null;
            }
            finally
            {
                foreach (var b in bitmaps) b.Dispose();
            }
        });

        if (report is null)
        {
            StatusMessage = "No readable frames to check.";
            return;
        }

        WarningText = report.IsClean ? "" : string.Join("\n", report.Messages);
        StatusMessage = report.IsClean
            ? $"Render QC: {report.FrameCount} frames look clean — no drift, transparency present."
            : $"Render QC flagged {report.Messages.Count} thing(s) to check (see above).";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var path = await _dialogs.SavePngAsync($"{SuggestBaseName()}.png");
        if (path is null) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressValue = 0;
        ProgressText = "";
        StatusMessage = "Assembling…";

        var paths = Frames.Select(f => f.Path).ToList();
        var options = new FrameSequenceOptions
        {
            Direction = StackDirection,
            Fit = CellFit,
            RecenterOnContent = RecenterOnContent,
            UnpremultiplyAlpha = UnpremultiplyAlpha,
            ResampleTo = ResampleEnabled ? TargetFrameCount : null,
            Interpolation = Interpolation,
        };

        // Created on the UI thread → Report callbacks marshal back to the UI thread.
        var progress = new Progress<int>(done =>
        {
            ProgressValue = paths.Count > 0 ? (double)done / paths.Count * 100.0 : 0;
            ProgressText = $"Loaded {done}/{paths.Count}";
        });

        try
        {
            var ct = _cts.Token;
            var result = await Task.Run(() => AssembleFromPaths(paths, options, progress, ct));
            if (result is null)
            {
                StatusMessage = "Cancelled.";
                return;
            }

            using (result.Strip)
            {
                await _export.SavePngAsync(result.Strip, path);

                if (ExportAt2x)
                {
                    var path2x = AppendSuffix(path, "@2x");
                    using var strip2x = UpscaleStrip(result.Strip, 2);
                    await _export.SavePngAsync(strip2x, path2x);
                }

                var controlId = Path.GetFileNameWithoutExtension(path);
                var parameterId = string.IsNullOrWhiteSpace(ParameterId) ? controlId : ParameterId.Trim();
                var asset = Path.GetFileName(path);
                var asset2x = ExportAt2x ? Path.GetFileName(AppendSuffix(path, "@2x")) : null;

                var settings = new FilmstripSettings
                {
                    ComponentType = ComponentType,
                    FrameCount = result.FrameCount,
                    FrameWidth = result.FrameWidth,
                    FrameHeight = result.FrameHeight,
                    StackDirection = result.Direction,
                };

                bool wroteManifest = false;
                if (ExportManifest)
                {
                    var manifest = _manifest.BuildSingleControl(settings, asset, asset2x, controlId, parameterId);
                    var manifestPath = Path.Combine(Path.GetDirectoryName(path) ?? "", controlId + ".skin.json");
                    await _manifest.SaveAsync(manifest, manifestPath);
                    wroteManifest = true;
                }

                int wroteCode = 0;
                if (ExportCode)
                {
                    var dir = Path.GetDirectoryName(path) ?? "";
                    var request = new CodeSnippetRequest(ComponentType, result.FrameCount, result.FrameWidth,
                                                         result.FrameHeight, result.Direction, asset, asset2x,
                                                         controlId, parameterId);
                    foreach (var target in SelectedCodeTargets())
                    {
                        await _codeSnippets.SaveAsync(target, request, dir);
                        wroteCode++;
                    }
                }

                WarningText = string.Join("\n", result.Warnings);
                StatusMessage = $"Assembled {result.FrameCount}-frame filmstrip → {Path.GetFileName(path)}"
                              + (ExportAt2x ? " (+@2x)" : "")
                              + (wroteManifest ? " (+skin.json)" : "")
                              + (wroteCode > 0 ? $" (+{wroteCode} code file{(wroteCode == 1 ? "" : "s")})" : "");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Assemble error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Stream-decode each frame (reporting progress, honouring cancel) then hand the set to
    /// the assembler. Bitmaps are disposed as soon as the strip is built, so peak memory stays near
    /// one decoded sequence rather than that plus the output. Returns null if cancelled.</summary>
    private FrameSequenceResult? AssembleFromPaths(IReadOnlyList<string> paths, FrameSequenceOptions options,
                                                   IProgress<int> progress, CancellationToken ct)
    {
        var bitmaps = new List<SKBitmap>(paths.Count);
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (ct.IsCancellationRequested) return null;
                var bmp = _imageLoad.Load(paths[i]);
                if (bmp is not null) bitmaps.Add(bmp);
                progress.Report(i + 1);
            }

            if (bitmaps.Count < 2)
                throw new InvalidOperationException("Need at least two readable frames to assemble.");

            return _assembler.Assemble(bitmaps, options);
        }
        finally
        {
            foreach (var b in bitmaps) b.Dispose();
        }
    }

    // @2x is a high-quality upscale of the assembled strip. For best results a user should render
    // their sequence at 2× instead, but this keeps parity with the Create tab's @2x toggle.
    private static SKBitmap UpscaleStrip(SKBitmap strip, int factor)
    {
        var big = new SKBitmap(strip.Width * factor, strip.Height * factor, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(big);
        canvas.Clear(SKColors.Transparent);
        using var img = SKImage.FromBitmap(strip);
        canvas.DrawImage(img, SKRect.Create(0, 0, big.Width, big.Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return big;
    }

    private IEnumerable<CodeTarget> SelectedCodeTargets()
    {
        if (EmitCodeJuce) yield return CodeTarget.Juce;
        if (EmitCodeCss) yield return CodeTarget.Css;
        if (EmitCodeIPlug2) yield return CodeTarget.IPlug2;
        if (EmitCodeHise) yield return CodeTarget.Hise;
    }

    /// <summary>A clean base name for the output, derived from the first frame's name with its trailing
    /// frame number stripped (e.g. <c>knob_0001</c> → <c>knob</c>).</summary>
    private string SuggestBaseName()
    {
        if (Frames.Count == 0) return "filmstrip";
        var first = Path.GetFileNameWithoutExtension(Frames[0].Path);
        var trimmed = first.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_', '-', ' ', '.');
        return string.IsNullOrWhiteSpace(trimmed) ? "filmstrip" : trimmed;
    }

    private static string AppendSuffix(string path, string suffix)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return Path.Combine(dir, $"{name}{suffix}{ext}");
    }
}
