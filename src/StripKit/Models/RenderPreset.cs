namespace StripKit.Models;

/// <summary>
/// A named snapshot of the Create tab's full render setup — component type, frames, sweep,
/// resolution, sprite layout, parameter-law curve, meter/value-arc settings, and export
/// preferences — so a control's look can be saved once and reloaded in one click. Deliberately
/// excludes any loaded art (source/background/layers): a preset is a reusable style, not an
/// asset bundle. Plain data, JSON-serialized inside <see cref="AppSettings"/>.
/// </summary>
public sealed class RenderPreset
{
    public required string Name { get; set; }

    // ---- component / frames ----
    public ComponentType ComponentType { get; set; }
    public int FrameCount { get; set; }
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }

    // ---- rotary ----
    public double SweepDegrees { get; set; }
    public bool RotationClockwise { get; set; }
    public double StartAngleDegrees { get; set; }
    public double EndAngleDegrees { get; set; }
    public double PivotOffsetX { get; set; }
    public double PivotOffsetY { get; set; }

    // ---- content alignment ----
    public double SourceCenterX { get; set; }
    public double SourceCenterY { get; set; }

    // ---- linear ----
    public double EdgeMargin { get; set; }
    public double CapCrossOffset { get; set; }

    // ---- quality / output ----
    public int Supersample { get; set; }
    public StackDirection StackDirection { get; set; }
    public StripLayout Layout { get; set; }
    public int GridColumns { get; set; }

    // ---- parameter-law frame mapping ----
    public FrameMappingCurve MappingCurve { get; set; }
    public double MappingSkew { get; set; }
    public double MappingLogBase { get; set; }

    // ---- meter ----
    public int SegmentCount { get; set; }
    public MeterFillDirection FillDirection { get; set; }
    public bool ContinuousFill { get; set; }
    public string OnColorHex { get; set; } = "#FFE8440A";
    public string OffColorHex { get; set; } = "#FF2A2A2A";
    public bool ShowMeterPeak { get; set; }
    public string PeakColorHex { get; set; } = "#FFFFFFFF";

    // ---- value arc ----
    public bool ShowValueArc { get; set; }
    public double ArcRadius { get; set; }
    public double ArcThickness { get; set; }
    public bool ArcRoundCaps { get; set; }
    public string ArcColorHex { get; set; } = "#FFE8440A";
    public bool ArcGradient { get; set; }
    public string ArcColor2Hex { get; set; } = "#FFFFC107";
    public bool ArcTrack { get; set; }
    public string ArcTrackColorHex { get; set; } = "#33FFFFFF";
    public bool ArcGlow { get; set; }
    public double ArcGlowSize { get; set; }

    // ---- export preferences ----
    public bool ExportAt2x { get; set; }
    public int HiDpiScale { get; set; }
    public bool ExportManifest { get; set; }
    public bool ExportCode { get; set; }
    public bool EmitCodeJuce { get; set; }
    public bool EmitCodeCss { get; set; }
    public bool EmitCodeIPlug2 { get; set; }
    public bool EmitCodeHise { get; set; }
    public bool EmitCodeReact { get; set; }
}
