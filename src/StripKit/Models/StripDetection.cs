namespace StripKit.Models;

/// <summary>
/// The inferred layout of an <i>existing</i> filmstrip (one we did not generate).
/// The frame count is not stored in a PNG, so it is guessed from the dimensions
/// and must be verified visually — see <c>FilmstripImporter</c> and the
/// <c>filmstrip-importer-engine</c> skill.
/// </summary>
/// <param name="Vertical">True if frames stack top-to-bottom; false for left-to-right.</param>
/// <param name="FrameCount">Best-guess number of frames.</param>
/// <param name="FrameWidth">Width of one frame cell, in px.</param>
/// <param name="FrameHeight">Height of one frame cell, in px.</param>
/// <param name="Kind">Classified control type from the frame aspect, or null if unknown.</param>
/// <param name="LowConfidence">True when the guess is ambiguous (e.g. a square frame
/// whose total also divides by an adjacent count — the strip may include an extra
/// centre frame). Prompt the user to verify.</param>
/// <param name="CandidateCounts">All tested counts that divide the total evenly, best first.</param>
public sealed record StripDetection(
    bool Vertical,
    int FrameCount,
    int FrameWidth,
    int FrameHeight,
    ComponentType? Kind,
    bool LowConfidence,
    IReadOnlyList<int> CandidateCounts)
{
    public StackDirection Direction => Vertical ? StackDirection.Vertical : StackDirection.Horizontal;

    public string KindLabel => Kind switch
    {
        ComponentType.RotaryKnob => "Rotary knob / button",
        ComponentType.VerticalFader => "Vertical fader",
        ComponentType.HorizontalSlider => "Horizontal slider",
        _ => "Unknown",
    };
}
