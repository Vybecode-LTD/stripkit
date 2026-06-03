namespace StripKit.Models;

/// <summary>
/// How frames are laid out in the exported PNG. Vertical (frames stacked
/// top-to-bottom) is the convention most JUCE LookAndFeel filmstrip loaders and
/// KnobMan exports expect.
/// </summary>
public enum StackDirection
{
    Vertical,
    Horizontal,
}
