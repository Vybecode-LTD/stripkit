namespace StripKit.Models;

/// <summary>
/// The kind of control a filmstrip drives. Determines how each frame is composed:
/// a rotary knob rotates the source about a pivot, faders and sliders translate the
/// source (the "cap") along an axis, and a meter progressively fills segments as the
/// value rises (procedural, or by revealing on-state art over off-state art).
/// </summary>
public enum ComponentType
{
    RotaryKnob,
    VerticalFader,
    HorizontalSlider,
    Meter,

    /// <summary>A discrete-state button: typically 2 frames (off / on), or more for
    /// hover, pressed, and disabled states. Each frame renders the art for that state; no
    /// position transform is applied.</summary>
    Button,

    /// <summary>An on/off toggle switch — 2 frames (off / on). Rendered exactly like a 2-state
    /// <see cref="Button"/> (the discrete state-frame path), but surfaced as its own control:
    /// switch/rocker-style generated art and a latching (boolean) code-export binding.</summary>
    Toggle,
}
