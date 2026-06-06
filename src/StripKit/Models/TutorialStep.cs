namespace StripKit.Models;

/// <summary>
/// One step of the Getting Started overlay: a title, the instruction body, an optional extra tip,
/// and whether this step offers the "load a sample knob" shortcut (so a brand-new user with no art
/// can run the whole load → preview → export loop immediately).
/// </summary>
public sealed class TutorialStep
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? Tip { get; init; }
    public bool OffersSample { get; init; }
}
