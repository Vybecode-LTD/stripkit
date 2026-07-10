namespace StripKit.Models;

/// <summary>One control to include in a built kit: which component type, and the layered SVG/PSD art
/// on disk that produces it (a generated matching-set member, or any layered file). Pure data.</summary>
public sealed record KitControlSource(ComponentType Type, string SourcePath);

/// <summary>Options for a one-click kit build: where to write, how to name, frame count, quality, and
/// whether to emit an @Nx asset. Pure data — no UI/Skia deps.</summary>
public sealed record KitBuildOptions
{
    /// <summary>The folder the filmstrips + skin.json are written into (created if missing).</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Display name written into the skin.json's <c>name</c>.</summary>
    public string KitName { get; init; } = "kit";

    /// <summary>Optional author, written into the skin.json.</summary>
    public string? Author { get; init; }

    /// <summary>Filename prefix for every emitted asset and the manifest (e.g. <c>modern</c> →
    /// <c>modern-knob.png</c>, <c>modern.skin.json</c>). Slugified before use.</summary>
    public string FilePrefix { get; init; } = "kit";

    /// <summary>Frames for continuously-swept controls (knob / fader / slider / meter). Button &amp;
    /// toggle always use their own on/off state-layer count and ignore this.</summary>
    public int FrameCount { get; init; } = 64;

    /// <summary>Renderer oversampling (1 / 2 / 4 / 8), clamped by the builder.</summary>
    public int Supersample { get; init; } = 4;

    /// <summary>Also emit an @Nx asset per control (see <see cref="HiDpiScale"/>).</summary>
    public bool ExportAt2x { get; init; } = true;

    /// <summary>The HiDPI multiplier for the @Nx asset (2 / 3 / 4). Ignored when
    /// <see cref="ExportAt2x"/> is false or this is &le; 1.</summary>
    public int HiDpiScale { get; init; } = 2;
}

/// <summary>The outcome of building one control in a kit: success/failure plus the written 1x asset,
/// an optional non-fatal warning (e.g. a knob with no rotating layer — the asset still wrote), or the
/// failure reason.</summary>
public sealed record KitControlResult
{
    public required ComponentType Type { get; init; }
    public required bool Success { get; init; }

    /// <summary>The 1x PNG path on success.</summary>
    public string? AssetPath { get; init; }

    /// <summary>A non-fatal note surfaced to the user; the asset still wrote successfully.</summary>
    public string? Warning { get; init; }

    /// <summary>The failure reason when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }
}

/// <summary>The outcome of a whole kit build: the per-control results, the written skin.json (null when
/// nothing succeeded), and the output folder.</summary>
public sealed record KitBuildResult(
    IReadOnlyList<KitControlResult> Controls, string? SkinJsonPath, string OutputDirectory)
{
    public int SuccessCount => Controls.Count(c => c.Success);
    public int TotalCount => Controls.Count;
}
