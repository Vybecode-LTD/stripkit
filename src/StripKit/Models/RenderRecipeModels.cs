namespace StripKit.Models;

/// <summary>
/// A format StripKit can emit an offline-render <em>recipe</em> in, so a path-traced frame
/// sequence (Blender / KeyShot / Octane / C4D) lines up exactly with the filmstrip's runtime
/// frame law before it is stacked back into a strip on the Assemble tab.
/// </summary>
public enum RenderRecipeTarget
{
    /// <summary>A Blender <c>bpy</c> script: transparent film, a keyframe on every one of the N frames.</summary>
    Blender,

    /// <summary>An engine-agnostic <c>frame,value,angle_deg</c> CSV table (KeyShot / Octane / C4D).</summary>
    Csv,

    /// <summary>The same table as JSON, with metadata — machine-readable for any render scripting.</summary>
    Json,
}

/// <summary>
/// Everything a <see cref="StripKit.Services.IRenderRecipeService"/> needs to emit a render
/// recipe for one control. Pure data — no UI or Skia dependency. The angle fields drive a
/// rotary knob's render; every other type renders a value-driven sequence (angle stays 0 and
/// the baked 0..1 <c>value</c> property drives the rig).
/// </summary>
public sealed record RenderRecipeRequest(
    ComponentType ComponentType,
    int FrameCount,
    double StartAngleDegrees,
    double EndAngleDegrees,
    int FrameWidth,
    int FrameHeight,
    string ControlId)
{
    /// <summary>True for a rotary knob — the only type whose render bakes a rotation sweep.</summary>
    public bool IsRotary => ComponentType == ComponentType.RotaryKnob;
}

/// <summary>
/// One row of a render recipe: the filmstrip frame index (0-based), its normalized value
/// (0..1), and — for a rotary knob — the absolute rotation in degrees for that frame.
/// </summary>
public readonly record struct RecipeFrame(int Frame, double Value, double AngleDegrees);
