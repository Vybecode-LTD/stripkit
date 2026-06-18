using CommunityToolkit.Mvvm.ComponentModel;
using StripKit.Models;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace StripKit.ViewModels;

/// <summary>A checkable control type in the matching-set picker: which types to include when
/// generating a consistent family in one go.</summary>
public partial class SetTypeOption : ObservableObject
{
    public SetTypeOption(ComponentType type, string label, bool include)
    {
        Type = type;
        Label = label;
        _include = include;
    }

    public ComponentType Type { get; }
    public string Label { get; }

    [ObservableProperty] private bool _include;
}

/// <summary>One control in a generated matching set, ready to bind in the results grid: its type +
/// label, the at-rest preview bitmap, the temp SVG path (for the "Use in Create" handoff), the raw
/// SVG (for "Save"), and whether it generated cleanly. Immutable — a fresh instance replaces it when
/// that single control is regenerated.</summary>
public sealed class GeneratedSetResult
{
    public required ComponentType ComponentType { get; init; }
    public required string Label { get; init; }
    public string? SvgPath { get; init; }
    public AvBitmap? PreviewImage { get; init; }
    public string? Svg { get; init; }
    public bool IsSuccess { get; init; }
    public string StatusMessage { get; init; } = "";

    public bool HasPreview => PreviewImage is not null;
}
