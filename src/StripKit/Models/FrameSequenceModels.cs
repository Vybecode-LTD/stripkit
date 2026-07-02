using SkiaSharp;

namespace StripKit.Models;

/// <summary>
/// How to reconcile frames whose pixel dimensions don't all match — the common case when a
/// path-traced render sequence has a stray off-size frame.
/// </summary>
public enum CellFit
{
    /// <summary>Any size mismatch is an error — refuse to assemble (the sequence must be uniform).</summary>
    Strict,

    /// <summary>Transparent-pad every frame up to the largest frame's W×H cell, centred (default).</summary>
    PadToLargest,

    /// <summary>Centre-crop every frame down to the smallest frame's W×H cell.</summary>
    CropToSmallest,
}

/// <summary>
/// How the assembler synthesizes frames when re-timing to a different count (path-tracing P4 —
/// "render fewer, ship more").
/// </summary>
public enum FrameInterpolation
{
    /// <summary>Pick the nearest source frame for each output frame — no blending (the importer's law).
    /// Correct for a filmstrip in general: a moving pointer never ghosts. The default.</summary>
    Nearest,

    /// <summary>Cross-dissolve the two bracketing source frames by their fractional distance, so a
    /// handful of expensive path-traced frames can be shipped as a standard 64/128. Good for slow, smooth
    /// motion (a gently rotating knob); it can ghost on fast motion, where Nearest is safer.</summary>
    Crossfade,
}

/// <summary>
/// Options for assembling a sequence of individually-rendered frames into one stacked filmstrip.
/// Pure data — no UI or Avalonia dependency.
/// </summary>
public sealed record FrameSequenceOptions
{
    /// <summary>Stack the frames top-to-bottom (vertical) or left-to-right (horizontal).</summary>
    public StackDirection Direction { get; init; } = StackDirection.Vertical;

    /// <summary>How to handle frames whose dimensions differ from the others.</summary>
    public CellFit Fit { get; init; } = CellFit.PadToLargest;

    /// <summary>Re-centre each frame on its opaque content before packing — fixes a 3D object that
    /// drifts off-centre between rendered frames.</summary>
    public bool RecenterOnContent { get; init; }

    /// <summary>Un-premultiply each frame's alpha before packing — divides RGB by alpha to remove the
    /// dark edge halos a premultiplied path-traced render leaves on its anti-aliased edges. A no-op for
    /// fully opaque / fully transparent pixels. Off by default (only premultiplied renders need it).</summary>
    public bool UnpremultiplyAlpha { get; init; }

    /// <summary>When set, re-time the assembled strip to this many output frames; <c>null</c> keeps the
    /// native (input) frame count.</summary>
    public int? ResampleTo { get; init; }

    /// <summary>How to synthesize frames when <see cref="ResampleTo"/> re-times the sequence: pick the
    /// nearest source frame (default), or cross-dissolve the two bracketing frames (P4 — "render fewer,
    /// ship more"). Ignored when <see cref="ResampleTo"/> is null.</summary>
    public FrameInterpolation Interpolation { get; init; } = FrameInterpolation.Nearest;
}

/// <summary>
/// A render-QC report on a decoded frame sequence — the path-tracer failure modes StripKit can catch
/// on import: object drift between frames, frames with no transparency (a missing transparent
/// background) or none at all (a failed render), and premultiplied edges. Pure metrics; the UI shows
/// <see cref="Messages"/>. Drift is the content-centre spread in pixels (of the largest cell).
/// </summary>
public sealed record RenderQcReport(
    int FrameCount,
    double DriftXPx,
    double DriftYPx,
    int OpaqueFrames,
    int EmptyFrames,
    bool PremultipliedSuspected)
{
    /// <summary>The larger of the two drift axes, in pixels.</summary>
    public double MaxDriftPx => Math.Max(DriftXPx, DriftYPx);

    /// <summary>True when nothing looked wrong.</summary>
    public bool IsClean => Messages.Count == 0;

    /// <summary>Human-readable QC advisories (empty when the sequence looks clean).</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            var m = new List<string>();
            if (MaxDriftPx >= 2.0)
                m.Add($"Frames drift up to {MaxDriftPx:0.#}px between renders — tick \"Re-centre each frame\" to stabilise the object.");
            if (OpaqueFrames > 0)
                m.Add($"{OpaqueFrames} frame(s) have no transparency — the render may be missing a transparent background (render RGBA with transparent film).");
            if (EmptyFrames > 0)
                m.Add($"{EmptyFrames} frame(s) are fully transparent — likely a failed or empty render.");
            if (PremultipliedSuspected)
                m.Add("Edges look premultiplied (dark fringe) — tick \"Un-premultiply alpha\" to clean them.");
            return m;
        }
    }
}

/// <summary>The outcome of assembling a frame sequence: the stacked strip plus what happened.</summary>
public sealed record FrameSequenceResult(
    SKBitmap Strip,
    int FrameCount,
    int FrameWidth,
    int FrameHeight,
    StackDirection Direction,
    bool Resampled,
    IReadOnlyList<string> Warnings);

/// <summary>
/// A non-decoding probe of a candidate frame sequence: the natural-sorted paths and a dimension
/// report, so the UI can list the frames and warn about size mismatches before the (potentially
/// large) decode-and-assemble.
/// </summary>
public sealed record SequenceProbe(
    IReadOnlyList<string> OrderedPaths,
    int MaxWidth,
    int MaxHeight,
    int MinWidth,
    int MinHeight,
    bool Uniform,
    IReadOnlyList<string> Warnings)
{
    public bool HasFrames => OrderedPaths.Count > 0;
}
