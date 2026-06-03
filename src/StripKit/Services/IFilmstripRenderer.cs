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
    /// <paramref name="source"/> may be null only for a procedural meter; it is
    /// required for every other component type. The caller owns and must dispose the result.
    /// </summary>
    SKBitmap RenderFrame(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, int frameIndex, double scale = 1.0);

    /// <summary>
    /// Renders the full stacked filmstrip. <paramref name="source"/> may be null only
    /// for a procedural meter. The caller owns and must dispose the returned bitmap.
    /// </summary>
    SKBitmap RenderStrip(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, double scale = 1.0);
}
