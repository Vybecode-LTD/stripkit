using CommunityToolkit.Mvvm.ComponentModel;
using StripKit.Models;

namespace StripKit.ViewModels;

/// <summary>
/// One editable control binding in the Skin tab's multi-control manifest builder. This is the
/// mutable, observable counterpart of the immutable <see cref="ManifestControl"/> record — the
/// list and the detail editor bind to it; <see cref="SkinViewModel"/> maps it to a record on
/// export. Value range fields are strings so blank means "omit" (the manifest leaves them null).
/// </summary>
public partial class SkinControlEntry : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _id = "control";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private ComponentType _type = ComponentType.RotaryKnob;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _parameterId = "param";

    [ObservableProperty] private string _asset = "";
    [ObservableProperty] private string _asset2x = "";

    [ObservableProperty] private int _frames = 64;
    [ObservableProperty] private int _frameWidth = 80;
    [ObservableProperty] private int _frameHeight = 80;
    [ObservableProperty] private StackDirection _stack = StackDirection.Vertical;

    // On-screen rectangle in base-resolution pixels.
    [ObservableProperty] private double _boundsX;
    [ObservableProperty] private double _boundsY;
    [ObservableProperty] private double _boundsW = 80;
    [ObservableProperty] private double _boundsH = 80;

    // Optional value range — blank means omit from the manifest.
    [ObservableProperty] private string _valueMin = "";
    [ObservableProperty] private string _valueMax = "";
    [ObservableProperty] private string _valueDefault = "";

    /// <summary>Compact one-line summary shown in the controls list.</summary>
    public string Display => $"{Id}   ·   {Type}   ·   {ParameterId}";
}
