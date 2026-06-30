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

    /// <summary>When set, re-time the assembled strip to this many output frames via the importer's
    /// nearest-frame law; <c>null</c> keeps the native (input) frame count.</summary>
    public int? ResampleTo { get; init; }
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
