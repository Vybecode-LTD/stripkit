using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Builds a whole "matching kit" in one step: renders each supplied layered control (a generated
/// matching-set member) into a filmstrip PNG using the SAME per-type paths the Create tab uses —
/// layered knob (body + pointer), button/toggle state frames, a flattened fader/slider cap, or a
/// meter's off/on reveal — then assembles a multi-control <c>skin.json</c> binding them together.
/// App-only orchestration over <see cref="ILayeredImportService"/>, <see cref="IFilmstripRenderer"/>,
/// <see cref="IExportService"/> and <see cref="IManifestService"/>; it changes no renderer math, so it
/// is NOT mirrored in <c>FilmstripEngine.cs</c>.
/// </summary>
public interface IKitBuilder
{
    /// <summary>Renders every control in <paramref name="sources"/> to a filmstrip (+ optional @Nx) and
    /// writes a single <c>skin.json</c> laying them out in a row. One control failing never sinks the
    /// rest — each carries its own success/error. Cancellation (via <paramref name="ct"/>) propagates as
    /// <see cref="OperationCanceledException"/>.</summary>
    Task<KitBuildResult> BuildAsync(IReadOnlyList<KitControlSource> sources, KitBuildOptions options,
                                    CancellationToken ct = default);
}
