using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using StripKit.Models;

namespace StripKit.ViewModels;

/// <summary>
/// One row in the Create-tab "import layered file" list: a parsed layer's name, the behaviour the
/// user can override via a dropdown (<see cref="LayerBehavior.Static"/> /
/// <see cref="LayerBehavior.Rotate"/> / <see cref="LayerBehavior.Frame"/>), and the canvas-sized
/// art passed to the renderer (not bound). The owning view model subscribes to
/// <see cref="Behavior"/> changes to re-render live.
/// </summary>
public partial class ImportedLayerRow : ObservableObject
{
    public ImportedLayerRow(string name, SKBitmap art, LayerBehavior behavior)
    {
        Name = name;
        Art = art;
        _behavior = behavior;
    }

    /// <summary>The layer name (read-only display: SVG group label/id or PSD layer name).</summary>
    public string Name { get; }

    /// <summary>The canvas-sized layer bitmap. Owned by the row; disposed when the import is cleared.</summary>
    public SKBitmap Art { get; }

    /// <summary>The behaviour applied to this layer in the render (user-editable; seeded from the
    /// importer's name-based guess).</summary>
    [ObservableProperty] private LayerBehavior _behavior;

    /// <summary>The dropdown choices — all three behaviors apply depending on the control type.</summary>
    public LayerBehavior[] Behaviors { get; } = [LayerBehavior.Static, LayerBehavior.Rotate, LayerBehavior.Frame];

    public void DisposeArt() => Art.Dispose();
}
