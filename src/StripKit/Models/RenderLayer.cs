namespace StripKit.Models;

/// <summary>
/// How a single render layer animates across the strip's frames. The MVP covers a
/// static body plus a rotating pointer; <c>Translate</c> / <c>OpacityRamp</c> are
/// reserved for later layer-aware work (faders, fades).
/// </summary>
public enum LayerBehavior
{
    /// <summary>Drawn fixed in every frame (a knob body / well). Never transformed.</summary>
    Static,

    /// <summary>Rotated per-frame about its pivot, following the knob's angle sweep (the pointer).</summary>
    Rotate,
}

/// <summary>
/// One layer of a layered control render: its animation behaviour and, for a
/// <see cref="LayerBehavior.Rotate"/> layer, the normalized pivot within its own drawn
/// art. The layer's bitmap is passed alongside to the renderer (this model stays
/// Skia-free, like the rest of <see cref="FilmstripSettings"/>); layers are composited
/// bottom-first. Empty layer stacks render the single source exactly as before.
/// </summary>
public sealed class RenderLayer
{
    public LayerBehavior Behavior { get; set; } = LayerBehavior.Rotate;

    /// <summary>
    /// Normalized (0..1) horizontal rotation pivot within this layer's own drawn art, used
    /// when <see cref="Behavior"/> is <see cref="LayerBehavior.Rotate"/>. (0.5) is the art
    /// centre; the app seeds it from the body's detected content centre (the knob axis).
    /// </summary>
    public double PivotX { get; set; } = 0.5;

    /// <summary>Normalized (0..1) vertical rotation pivot within this layer's own drawn art.</summary>
    public double PivotY { get; set; } = 0.5;

    public RenderLayer Clone() => (RenderLayer)MemberwiseClone();
}
