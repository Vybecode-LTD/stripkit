using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StripKit.Models;
using StripKit.Services;

namespace StripKit.ViewModels;

/// <summary>
/// Backs the "Skin" tab: assemble a multi-control <c>skin.json</c> that binds several exported
/// filmstrips to several plugin parameters. Controls are added by picking a strip (its layout is
/// auto-detected via the importer) or blank, edited in a detail panel, and written — with the
/// skin-level metadata (name, author, design resolution, optional window background) — into a
/// chosen folder. Pure of Avalonia UI types; the strips themselves are produced on the Create /
/// Batch tabs and referenced here by relative file name.
/// </summary>
public partial class SkinViewModel : ViewModelBase
{
    private readonly IFileDialogService _dialogs;
    private readonly IImageLoadService _imageLoad;
    private readonly IFilmstripImporter _importer;
    private readonly IManifestService _manifest;

    public SkinViewModel(IFileDialogService dialogs, IImageLoadService imageLoad,
                         IFilmstripImporter importer, IManifestService manifest)
    {
        _dialogs = dialogs;
        _imageLoad = imageLoad;
        _importer = importer;
        _manifest = manifest;
    }

    public ComponentType[] ComponentTypes { get; } =
        [ComponentType.RotaryKnob, ComponentType.VerticalFader, ComponentType.HorizontalSlider, ComponentType.Meter];
    public StackDirection[] StackDirections { get; } = [StackDirection.Vertical, StackDirection.Horizontal];

    /// <summary>The controls bound by this skin (the manifest's <c>controls[]</c>).</summary>
    public ObservableCollection<SkinControlEntry> Controls { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SkinControlEntry? _selectedControl;

    // ---- skin-level metadata ----
    [ObservableProperty] private string _skinName = "My Skin";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private int _baseWidth = 80;
    [ObservableProperty] private int _baseHeight = 80;
    [ObservableProperty] private string _windowBackground = "";   // optional, relative file name

    [ObservableProperty] private string _statusMessage =
        "Add controls (from a strip or blank), then export one skin.json that binds them all.";

    public bool HasControls => Controls.Count > 0;
    public bool HasSelection => SelectedControl is not null;

    // ---- commands ----

    [RelayCommand]
    private async Task AddFromStripAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;

        using var bmp = _imageLoad.Load(path);
        if (bmp is null)
        {
            StatusMessage = "Error: could not load that strip.";
            return;
        }

        var detection = _importer.Detect(bmp);
        var name = Path.GetFileNameWithoutExtension(path) is { Length: > 0 } b ? b : "control";

        AddEntry(new SkinControlEntry
        {
            Id = name,
            ParameterId = name,
            Type = detection.Kind ?? ComponentType.RotaryKnob,
            Asset = Path.GetFileName(path),
            Frames = detection.FrameCount,
            FrameWidth = detection.FrameWidth,
            FrameHeight = detection.FrameHeight,
            Stack = detection.Vertical ? StackDirection.Vertical : StackDirection.Horizontal,
            BoundsW = detection.FrameWidth,
            BoundsH = detection.FrameHeight,
        });

        StatusMessage = detection.LowConfidence
            ? $"Added '{name}' — detected {detection.FrameCount} frames (ambiguous; verify the count)."
            : $"Added '{name}' — {detection.FrameCount} frames, {detection.KindLabel}.";
    }

    [RelayCommand]
    private void AddBlank()
    {
        AddEntry(new SkinControlEntry());
        StatusMessage = "Added a blank control — fill in its fields on the right.";
    }

    private void AddEntry(SkinControlEntry entry)
    {
        // The first control seeds the skin's design resolution (editable afterwards).
        if (Controls.Count == 0)
        {
            BaseWidth = entry.FrameWidth;
            BaseHeight = entry.FrameHeight;
        }
        Controls.Add(entry);
        SelectedControl = entry;
        OnPropertyChanged(nameof(HasControls));
        ExportSkinCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveSelected() => SelectedControl is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        if (SelectedControl is null) return;
        int i = Controls.IndexOf(SelectedControl);
        Controls.Remove(SelectedControl);
        SelectedControl = Controls.Count > 0 ? Controls[Math.Clamp(i, 0, Controls.Count - 1)] : null;
        OnPropertyChanged(nameof(HasControls));
        ExportSkinCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ChooseWindowBackgroundAsync()
    {
        var path = await _dialogs.OpenImageAsync();
        if (path is null) return;
        WindowBackground = Path.GetFileName(path);
    }

    [RelayCommand]
    private void ClearWindowBackground() => WindowBackground = "";

    private bool CanExport() => Controls.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportSkinAsync()
    {
        if (Controls.Count == 0) return;

        var folder = await _dialogs.OpenFolderAsync("Choose where to write the skin.json (next to its assets)");
        if (folder is null) return;

        try
        {
            var controls = Controls.Select(ToManifestControl).ToList();
            var manifest = _manifest.BuildManifest(
                controls, SkinName, Author, BaseWidth, BaseHeight,
                string.IsNullOrWhiteSpace(WindowBackground) ? null : WindowBackground);

            var fileName = SanitizeFileName(SkinName) + ".skin.json";
            var path = Path.Combine(folder, fileName);
            await _manifest.SaveAsync(manifest, path);

            StatusMessage = $"Exported {controls.Count}-control skin → {fileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error exporting skin: {ex.Message}";
        }
    }

    private static ManifestControl ToManifestControl(SkinControlEntry e)
    {
        var id = string.IsNullOrWhiteSpace(e.Id) ? "control" : e.Id.Trim();
        return new ManifestControl
        {
            Id = id,
            Type = MapType(e.Type),
            ParameterId = string.IsNullOrWhiteSpace(e.ParameterId) ? id : e.ParameterId.Trim(),
            Asset = e.Asset.Trim(),
            Asset2x = string.IsNullOrWhiteSpace(e.Asset2x) ? null : e.Asset2x.Trim(),
            Frames = Math.Max(1, e.Frames),
            FrameWidth = Math.Max(1, e.FrameWidth),
            FrameHeight = Math.Max(1, e.FrameHeight),
            Stack = e.Stack == StackDirection.Vertical ? "vertical" : "horizontal",
            Bounds = new ManifestBounds(e.BoundsX, e.BoundsY, Math.Max(0, e.BoundsW), Math.Max(0, e.BoundsH)),
            ValueMin = ParseNullable(e.ValueMin),
            ValueMax = ParseNullable(e.ValueMax),
            ValueDefault = ParseNullable(e.ValueDefault),
        };
    }

    // Mirrors ManifestService.MapType (kept here so the VM doesn't depend on the concrete service).
    private static string MapType(ComponentType type) => type switch
    {
        ComponentType.RotaryKnob => "knob",
        ComponentType.VerticalFader => "vfader",
        ComponentType.HorizontalSlider => "hslider",
        ComponentType.Meter => "meter",
        _ => "knob",
    };

    private static double? ParseNullable(string s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string SanitizeFileName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "skin" : name.Trim();
        var clean = string.Concat(trimmed.Select(ch => Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch));
        return clean.Length == 0 ? "skin" : clean;
    }
}
