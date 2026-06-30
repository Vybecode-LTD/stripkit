using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Emits an offline-render <em>recipe</em> for one control — a Blender script or a
/// <c>frame,value,angle</c> CSV/JSON table — so a path-traced sequence matches StripKit's
/// runtime frame law (<c>angle_i = start + (end − start)·i/(N−1)</c>) and stacks cleanly on
/// the Assemble tab. Pure string generation; the only I/O is <see cref="SaveAsync"/>.
/// </summary>
public interface IRenderRecipeService
{
    /// <summary>Generate the recipe text for one target.</summary>
    string Generate(RenderRecipeTarget target, RenderRecipeRequest request);

    /// <summary>The on-disk file name a target's recipe is saved as (control id + extension).</summary>
    string FileName(RenderRecipeTarget target, string controlId);

    /// <summary>Write the recipe into <paramref name="directory"/> and return the full path.</summary>
    Task<string> SaveAsync(RenderRecipeTarget target, RenderRecipeRequest request, string directory);
}
