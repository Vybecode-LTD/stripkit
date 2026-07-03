namespace StripKit.Models;

/// <summary>
/// How a frame's linear position in the strip (<c>i / (FrameCount - 1)</c>) is remapped before
/// it drives the rotation angle, meter fill, or layer pivot — so the strip's visual sweep can
/// match a plugin's actual parameter law (e.g. a logarithmic frequency taper) instead of a
/// straight linear divisor. The frame count and spacing are unchanged either way; only the
/// value assigned to each frame index moves.
/// </summary>
public enum FrameMappingCurve
{
    /// <summary>No remapping — frame <c>i</c> gets exactly <c>i / (FrameCount - 1)</c>. Default,
    /// so existing renders are byte-identical.</summary>
    Linear,

    /// <summary>A power-law skew, the same convention as JUCE's <c>NormalisableRange</c> skew
    /// factor: <c>t' = t ^ MappingSkew</c>. Skew &lt; 1 front-loads resolution toward the low end;
    /// skew &gt; 1 front-loads it toward the high end.</summary>
    Skew,

    /// <summary>A true logarithmic taper (frequency-style parameters):
    /// <c>t' = log(1 + t·(MappingLogBase − 1)) / log(MappingLogBase)</c> — concave, so it front-loads
    /// visual sweep at the low end of the strip. Higher <see cref="FilmstripSettings.MappingLogBase"/>
    /// makes the curve more aggressive.</summary>
    Logarithmic,
}
