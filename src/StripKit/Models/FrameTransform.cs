namespace StripKit.Models;

/// <summary>
/// The placement of the source layer inside a single frame cell, in <b>frame</b>
/// units (i.e. 1x, before any export scale or supersampling is applied). The
/// renderer multiplies these by the working pixel scale.
/// </summary>
/// <param name="TranslateX">Top-left X at which the source layer is drawn.</param>
/// <param name="TranslateY">Top-left Y at which the source layer is drawn.</param>
/// <param name="DrawWidth">Width to draw the source layer at.</param>
/// <param name="DrawHeight">Height to draw the source layer at.</param>
/// <param name="RotateDegrees">Clockwise rotation applied about the pivot (0 = none).</param>
/// <param name="PivotX">Rotation pivot X, in frame units.</param>
/// <param name="PivotY">Rotation pivot Y, in frame units.</param>
public readonly record struct FrameTransform(
    float TranslateX,
    float TranslateY,
    float DrawWidth,
    float DrawHeight,
    float RotateDegrees,
    float PivotX,
    float PivotY);
