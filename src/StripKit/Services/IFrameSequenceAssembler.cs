using StripKit.Models;
using SkiaSharp;

namespace StripKit.Services;

/// <summary>
/// Assembles a sequence of individually-rendered frames (e.g. a path-traced PNG sequence from
/// Blender / KeyShot / Octane) into a single stacked filmstrip — the import-side bridge the
/// procedural renderer doesn't cover. Pure SkiaSharp, no Avalonia dependency (like
/// <see cref="IFilmstripImporter"/>); not mirrored into the standalone <c>FilmstripEngine.cs</c>
/// because it changes no renderer math.
/// </summary>
public interface IFrameSequenceAssembler
{
    /// <summary>
    /// Probe candidate frame paths without decoding them all: natural-sort the image files and
    /// report the dimension spread, so the UI can warn about size mismatches before a large assemble.
    /// </summary>
    SequenceProbe Probe(IReadOnlyList<string> paths, IImageLoadService loader);

    /// <summary>
    /// Pack already-decoded frames into one stacked strip (the golden-testable core). Throws
    /// <see cref="InvalidOperationException"/> on fewer than two frames, a <see cref="CellFit.Strict"/>
    /// size mismatch, or an output larger than the safety cap.
    /// </summary>
    FrameSequenceResult Assemble(IReadOnlyList<SKBitmap> frames, FrameSequenceOptions options);
}
