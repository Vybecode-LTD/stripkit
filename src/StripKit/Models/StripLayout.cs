namespace StripKit.Models;

/// <summary>
/// How frames are packed into the exported filmstrip PNG.
/// </summary>
public enum StripLayout
{
    /// <summary>A single 1×N (or N×1) strip along <see cref="StackDirection"/> — the classic
    /// filmstrip. Default, so existing exports are byte-identical.</summary>
    Strip,

    /// <summary>An R×C grid: <see cref="FilmstripSettings.GridColumns"/> wide, rows =
    /// ceil(FrameCount / GridColumns) — a 2D sprite atlas for loaders that expect one.</summary>
    Grid,
}
