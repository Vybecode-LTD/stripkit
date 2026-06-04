namespace StripKit.Models;

/// <summary>A target framework StripKit can emit ready-to-paste loader code for.</summary>
public enum CodeTarget
{
    /// <summary>JUCE — a <c>LookAndFeel</c> filmstrip <c>Slider</c> (or a meter <c>Component</c>).</summary>
    Juce,

    /// <summary>CSS/HTML — a sprite control driven by <c>background-position</c> (+ a JS value setter).</summary>
    Css,

    /// <summary>iPlug2 — an <c>IBKnobControl</c> / <c>IBSliderControl</c> / <c>IBitmapControl</c>.</summary>
    IPlug2,

    /// <summary>HISE — a <c>ScriptPanel</c> with a filmstrip paint routine.</summary>
    Hise,
}

/// <summary>
/// Everything a <see cref="StripKit.Services.ICodeSnippetService"/> needs to emit a loader
/// snippet for one exported filmstrip. Pure data — no UI or Skia dependency.
/// </summary>
public sealed record CodeSnippetRequest(
    ComponentType ComponentType,
    int FrameCount,
    int FrameWidth,
    int FrameHeight,
    StackDirection Stack,
    string AssetFileName,
    string? Asset2xFileName,
    string ControlId,
    string ParameterId)
{
    /// <summary>True when frames are laid out left-to-right (a horizontal strip).</summary>
    public bool FramesAreHorizontal => Stack == StackDirection.Horizontal;
}
