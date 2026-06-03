namespace StripKit.Models;

/// <summary>
/// The direction a meter fills as its value rises from 0 (frame 0) to 1 (last frame).
/// </summary>
public enum MeterFillDirection
{
    /// <summary>Fills from the bottom edge upward (the VU/level-meter default).</summary>
    Up,

    /// <summary>Fills from the top edge downward.</summary>
    Down,

    /// <summary>Fills from the left edge rightward.</summary>
    LeftToRight,

    /// <summary>Fills from the right edge leftward.</summary>
    RightToLeft,
}
