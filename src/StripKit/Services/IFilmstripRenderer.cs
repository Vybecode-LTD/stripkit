using StripKit.Models;
using SkiaSharp;

namespace StripKit.Services;

/// <summary>Computes per-frame transforms and renders filmstrips.</summary>
public interface IFilmstripRenderer
{
    /// <summary>The transform applied to the source layer for a given frame index.</summary>
    FrameTransform ComputeTransform(FilmstripSettings settings, SKBitmap source, int frameIndex);

    /// <summary>
    /// Renders a single frame at the target resolution (frame size × <paramref name="scale"/>).
    /// <paramref name="source"/> may be null only for a procedural meter, or for a layered
    /// knob (see <paramref name="layerArt"/>); it is required for every other case. The
    /// caller owns and must dispose the result.
    /// </summary>
    /// <param name="layerArt">Optional layer bitmaps, index-matched to
    /// <see cref="FilmstripSettings.Layers"/>. When both are non-empty for a rotary knob the
    /// renderer composites the layer stack (a static body + a rotating pointer) instead of
    /// rotating <paramref name="source"/>; otherwise it is ignored and output is unchanged.</param>
    SKBitmap RenderFrame(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, int frameIndex,
                         double scale = 1.0, IReadOnlyList<SKBitmap>? layerArt = null);

    /// <summary>
    /// Renders the full stacked filmstrip. <paramref name="source"/> may be null only for a
    /// procedural meter or a layered knob. The caller owns and must dispose the returned
    /// bitmap. See <see cref="RenderFrame"/> for <paramref name="layerArt"/>.
    /// </summary>
    SKBitmap RenderStrip(FilmstripSettings settings, SKBitmap? source, SKBitmap? background,
                         double scale = 1.0, IReadOnlyList<SKBitmap>? layerArt = null);
}
