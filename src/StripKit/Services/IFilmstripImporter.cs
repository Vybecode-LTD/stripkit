using StripKit.Models;
using SkiaSharp;

namespace StripKit.Services;

/// <summary>
/// Detects the layout of an existing filmstrip and splits or re-stacks it.
/// UI-agnostic (SkiaSharp only), like <see cref="IFilmstripRenderer"/>.
/// </summary>
public interface IFilmstripImporter
{
    /// <summary>Infers the layout (count, frame size, orientation, kind) from a strip bitmap.</summary>
    StripDetection Detect(SKBitmap strip);

    /// <summary>Infers the layout from raw dimensions (useful for tests).</summary>
    StripDetection Detect(int width, int height);

    /// <summary>
    /// Extracts a single frame as a new bitmap. The caller owns and must dispose it.
    /// </summary>
    SKBitmap ExtractFrame(SKBitmap strip, StripDetection layout, int index);

    /// <summary>
    /// Re-emits every frame in a new orientation (same count). The caller owns and
    /// must dispose the returned strip.
    /// </summary>
    SKBitmap Restack(SKBitmap strip, StripDetection layout, StackDirection destination);

    /// <summary>
    /// Re-times the strip to <paramref name="destinationCount"/> frames by picking the nearest
    /// source frame for each output frame (<c>round(j·(N−1)/(M−1))</c>, so the first/last frames
    /// land exactly on the source min/max). The output keeps the source orientation and frame
    /// size; downsampling is **lossy** (frames are dropped, not blended). The caller owns and
    /// must dispose the returned strip.
    /// </summary>
    SKBitmap Resample(SKBitmap strip, StripDetection layout, int destinationCount);
}
