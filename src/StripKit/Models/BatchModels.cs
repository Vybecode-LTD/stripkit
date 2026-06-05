namespace StripKit.Models;

/// <summary>
/// Inputs for a batch run: a set of source images, an output folder, and the render
/// template applied to every one of them. Pure data, no UI/Skia deps.
/// </summary>
public sealed record BatchOptions
{
    public required IReadOnlyList<string> InputFiles { get; init; }
    public required string OutputDirectory { get; init; }

    /// <summary>The render settings applied to each source (a template).</summary>
    public required FilmstripSettings Settings { get; init; }

    /// <summary>For knobs, square each frame to that source's larger side (per file).</summary>
    public bool MatchKnobFrameToSource { get; init; } = true;

    /// <summary>Meters only: when true, each source is a housing/backdrop and procedural LED
    /// segments are drawn over it (source → background, procedural meter); when false, each
    /// source is the lit on-state art revealed up to the fill (source → on-art, layered meter).</summary>
    public bool MeterSourceIsBackdrop { get; init; }

    public bool ExportAt2x { get; init; }
    public bool ExportManifest { get; init; }
}

/// <summary>Progress after each processed item (<paramref name="Completed"/> of <paramref name="Total"/>).</summary>
public sealed record BatchProgress(int Completed, int Total, string CurrentFile);

/// <summary>Outcome of one source in the batch.</summary>
public sealed record BatchItemResult(string InputFile, bool Success, string? OutputFile, string? Error);

/// <summary>Aggregate outcome of a batch run.</summary>
public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items, bool Cancelled)
{
    public int SucceededCount => Items.Count(i => i.Success);
    public int FailedCount => Items.Count(i => !i.Success);
}
