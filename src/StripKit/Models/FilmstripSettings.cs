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

    /// <summary>Pivot offset from the frame centre, in 1x pixels. An advanced nudge on
    /// top of the content centre below (use it for deliberately eccentric rotation).</summary>
    public double PivotOffsetX { get; set; }
    public double PivotOffsetY { get; set; }

    // ---- Content alignment (all art types) ----

    /// <summary>
    /// Normalized location (0..1 per axis) of the art's visual centre within the source
    /// image; (0.5, 0.5) is the image centre and reproduces plain rectangle centring.
    /// The app auto-detects this from the opaque pixels. For a rotary knob this point is
    /// re-centred in the frame and used as the rotation pivot (so an off-centre knob spins
    /// in place rather than orbiting); for a fader/slider cap it sets the cross-axis
    /// centring. Keep the defaults to preserve existing output.
    /// </summary>
    public double SourceCenterX { get; set; } = 0.5;
    public double SourceCenterY { get; set; } = 0.5;

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

    /// <summary>Highlight the topmost lit segment in <see cref="PeakColorArgb"/> as a peak marker (the
    /// filmstrip form of a peak indicator; true temporal peak-hold is a runtime/loader concern). Off by
    /// default so existing meter output is byte-identical. Procedural meters only.</summary>
    public bool ShowMeterPeak { get; set; }

    /// <summary>Peak-marker colour, packed <c>0xAARRGGBB</c> (used only when <see cref="ShowMeterPeak"/> is set).</summary>
    public uint PeakColorArgb { get; set; } = 0xFFFFFFFF;   // bright peak tick

    // ---- Value arc / fill ring (rotary knob only) ----

    /// <summary>When true, a Serum/Vital-style fill arc that tracks the value is composited
    /// onto each knob frame (it sweeps from the start angle to the current frame's angle).
    /// Off by default, so existing output is unchanged. Ignored for non-knob types.</summary>
    public bool ShowValueArc { get; set; }

    /// <summary>Arc radius as a fraction of the frame's inscribed radius (½·min(width,height)).
    /// ~0.88 sits just outside a typical knob body; 1.0 rings the frame edge.</summary>
    public double ArcRadius { get; set; } = 0.88;

    /// <summary>Arc stroke thickness, in 1x pixels.</summary>
    public double ArcThickness { get; set; } = 4.0;

    /// <summary>Round (true) vs butt (false) arc end caps.</summary>
    public bool ArcRoundCaps { get; set; } = true;

    /// <summary>Lit-arc colour, packed <c>0xAARRGGBB</c> (no Skia dep here).</summary>
    public uint ArcColorArgb { get; set; } = 0xFFE8440A;   // house accent

    /// <summary>When true the lit arc is a sweep gradient from <see cref="ArcColorArgb"/> to
    /// <see cref="ArcColor2Argb"/> across the rotation sweep.</summary>
    public bool ArcGradient { get; set; }

    /// <summary>Far end of the arc gradient, packed <c>0xAARRGGBB</c> (used when <see cref="ArcGradient"/>).</summary>
    public uint ArcColor2Argb { get; set; } = 0xFFFFC107;  // amber

    /// <summary>When true a dim full-sweep track is drawn behind the lit fill (shows the unfilled remainder).</summary>
    public bool ArcTrack { get; set; } = true;

    /// <summary>Track colour, packed <c>0xAARRGGBB</c> (used when <see cref="ArcTrack"/>).</summary>
    public uint ArcTrackColorArgb { get; set; } = 0x33FFFFFF;  // faint white

    /// <summary>When true the lit arc gets a soft glow (a blurred under-stroke in the arc colour).</summary>
    public bool ArcGlow { get; set; }

    /// <summary>Glow blur size, in 1x pixels (used when <see cref="ArcGlow"/>).</summary>
    public double ArcGlowSize { get; set; } = 6.0;

    // ---- Layered animation (knob: base + pointer) ----

    /// <summary>
    /// Ordered layer stack (bottom-first) for layered knob rendering. Empty (the default)
    /// renders the single <c>source</c> exactly as before, so existing output is unchanged.
    /// When non-empty for a rotary knob, the renderer composites these layers — their
    /// bitmaps passed alongside, index-matched — instead of rotating the single source:
    /// a <see cref="LayerBehavior.Static"/> base body stays fixed while a
    /// <see cref="LayerBehavior.Rotate"/> pointer follows the angle sweep. Knob-only for now.
    /// </summary>
    public List<RenderLayer> Layers { get; set; } = new();

    /// <summary>A deep copy — the <see cref="Layers"/> list is cloned, not shared, so a
    /// cloned settings (e.g. a per-file batch clone) can be mutated independently.</summary>
    public FilmstripSettings Clone()
    {
        var copy = (FilmstripSettings)MemberwiseClone();
        copy.Layers = Layers.Select(l => l.Clone()).ToList();
        return copy;
    }
}
