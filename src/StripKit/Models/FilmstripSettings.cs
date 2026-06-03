namespace StripKit.Models;

/// <summary>
/// The complete description of a filmstrip render. This is a plain data object
/// with no UI or Skia dependencies, so it can be unit-tested and reused by any
/// host (e.g. a CLI or a build step) — not just this app.
/// </summary>
public sealed class FilmstripSettings
{
    public ComponentType ComponentType { get; set; } = ComponentType.RotaryKnob;

    /// <summary>Number of frames in the strip. 64 is standard; 32 small, 128 large.</summary>
    public int FrameCount { get; set; } = 64;

    /// <summary>Width of a single frame cell, in 1x pixels.</summary>
    public int FrameWidth { get; set; } = 80;

    /// <summary>Height of a single frame cell, in 1x pixels.</summary>
    public int FrameHeight { get; set; } = 80;

    // ---- Rotary knob ----

    /// <summary>Rotation of frame 0 (the minimum value), in degrees, clockwise.</summary>
    public double StartAngleDegrees { get; set; } = -135.0;

    /// <summary>Rotation of the final frame (the maximum value), in degrees, clockwise.</summary>
    public double EndAngleDegrees { get; set; } = 135.0;

    /// <summary>Pivot offset from the frame centre, in 1x pixels.</summary>
    public double PivotOffsetX { get; set; }
    public double PivotOffsetY { get; set; }

    // ---- Linear fader / slider ----

    /// <summary>Gap, in 1x pixels, left at each end of the cap's travel.</summary>
    public double EdgeMargin { get; set; } = 4.0;

    /// <summary>Offset of the cap on its non-travel (cross) axis, in 1x pixels.</summary>
    public double CapCrossOffset { get; set; }

    // ---- Quality / output ----

    /// <summary>Internal oversampling factor for anti-aliasing (1, 2, 4, or 8).</summary>
    public int Supersample { get; set; } = 4;

    public StackDirection StackDirection { get; set; } = StackDirection.Vertical;

    // ---- Meter ----

    /// <summary>Number of segments (LED bars). Used by meters; the fill snaps to these
    /// unless <see cref="ContinuousFill"/> is set.</summary>
    public int SegmentCount { get; set; } = 12;

    /// <summary>Which way a meter fills as the value rises from 0 to 1.</summary>
    public MeterFillDirection FillDirection { get; set; } = MeterFillDirection.Up;

    /// <summary>When true a meter fills smoothly; when false it snaps to whole segments.</summary>
    public bool ContinuousFill { get; set; }

    /// <summary>Gap between procedural meter segments, in 1x pixels.</summary>
    public double SegmentGap { get; set; } = 3.0;

    /// <summary>Procedural meter lit-segment colour, packed <c>0xAARRGGBB</c> (no Skia dep here).</summary>
    public uint OnColorArgb { get; set; } = 0xFFE8440A;   // house accent

    /// <summary>Procedural meter unlit-segment colour, packed <c>0xAARRGGBB</c>.</summary>
    public uint OffColorArgb { get; set; } = 0xFF2A2A2A;   // dim

    public FilmstripSettings Clone() => (FilmstripSettings)MemberwiseClone();
}
