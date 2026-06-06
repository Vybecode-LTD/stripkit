using SkiaSharp;
using StripKit.Models;

namespace StripKit.Services;

/// <summary>One layer parsed out of a layered source file (an SVG group or a PSD layer),
/// rasterized onto the full document canvas so every layer registers pixel-for-pixel.</summary>
public sealed class ImportedLayer
{
    /// <summary>The layer's name (SVG <c>inkscape:label</c>/<c>id</c>, or the PSD layer name).</summary>
    public required string Name { get; init; }

    /// <summary>The layer rasterized at the document canvas size (straight alpha). Caller-owned.</summary>
    public required SKBitmap Art { get; init; }

    /// <summary>The behaviour guessed from the layer name (an indicator-like name → <see
    /// cref="LayerBehavior.Rotate"/>, otherwise <see cref="LayerBehavior.Static"/>). A starting
    /// guess the user verifies/overrides per layer, like the pointer-extraction workflow.</summary>
    public LayerBehavior SuggestedBehavior { get; init; }
}

/// <summary>The result of importing a layered source: the ordered layers (bottom-first, the same
/// order the renderer composites <see cref="FilmstripSettings.Layers"/>) and the canvas size.</summary>
public sealed class LayeredImportResult
{
    /// <summary>Layers bottom-first (paint order). Each is canvas-sized so they overlay exactly.</summary>
    public required IReadOnlyList<ImportedLayer> Layers { get; init; }

    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }

    /// <summary>"SVG" or "PSD" — for the status line.</summary>
    public string SourceFormat { get; init; } = "";
}

/// <summary>
/// Parses a real layered source — an <c>.svg</c> (vector groups) or a <c>.psd</c>/<c>.psb</c>
/// (raster layers) — into the layer stack the layer-aware knob renderer already composites
/// (★ #3 step 3). Each parsed layer is rasterized onto the document canvas and tagged with a
/// guessed behaviour; the view model maps them straight onto <see cref="FilmstripSettings.Layers"/>
/// + the index-matched <c>layerArt</c>, so a designer drops a layered knob and gets a layered
/// filmstrip with no hand-splitting. App-only (like <see cref="FilmstripImporter"/> /
/// <see cref="PointerExtractor"/>); never mirrored into the standalone <c>FilmstripEngine.cs</c>,
/// which holds render math only.
/// </summary>
public interface ILayeredImportService
{
    /// <summary>True when the extension is a layered format this service can read (.svg/.psd/.psb).</summary>
    bool CanImport(string path);

    /// <summary>Parses <paramref name="path"/> into its layers, or returns <c>null</c> if the file
    /// is unreadable / unsupported / has no usable layers.</summary>
    LayeredImportResult? Import(string path);
}
