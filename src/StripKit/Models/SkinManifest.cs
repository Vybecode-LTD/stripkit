namespace StripKit.Models;

/// <summary>
/// A <c>skin.json</c> manifest that binds exported filmstrips to plugin parameters
/// for a data-driven GUI skinning engine (see the <c>plugin-asset-manifest</c>
/// skill). Serialized with camelCase property names to match the schema. Paths are
/// relative to the manifest's folder.
/// </summary>
public sealed record SkinManifest
{
    /// <summary>Bumped on breaking changes; the loader checks it.</summary>
    public int ManifestVersion { get; init; } = 1;

    public required string Name { get; init; }
    public string? Author { get; init; }
    public string? SkinVersion { get; init; }

    /// <summary>Design resolution the control <c>bounds</c> are authored against.</summary>
    public required int BaseWidth { get; init; }
    public required int BaseHeight { get; init; }

    /// <summary>Optional whole-window background image (relative path).</summary>
    public string? Background { get; init; }

    public required IReadOnlyList<ManifestControl> Controls { get; init; }
}

/// <summary>One control binding within a <see cref="SkinManifest"/>.</summary>
public sealed record ManifestControl
{
    public required string Id { get; init; }

    /// <summary>One of: knob, vfader, hslider, button, meter.</summary>
    public required string Type { get; init; }

    /// <summary>The host/APVTS parameter id this control reads and writes.</summary>
    public required string ParameterId { get; init; }

    /// <summary>Relative path to the 1x filmstrip PNG.</summary>
    public required string Asset { get; init; }

    /// <summary>Optional relative path to the @2x PNG for HiDPI displays.</summary>
    public string? Asset2x { get; init; }

    public required int Frames { get; init; }
    public required int FrameWidth { get; init; }
    public required int FrameHeight { get; init; }

    /// <summary>Frame layout in the PNG: vertical or horizontal.</summary>
    public string Stack { get; init; } = "vertical";

    /// <summary>Optional per-control static layer drawn behind the strip.</summary>
    public string? Background { get; init; }

    /// <summary>On-screen rectangle in base-resolution pixels.</summary>
    public required ManifestBounds Bounds { get; init; }

    public double? ValueMin { get; init; }
    public double? ValueMax { get; init; }
    public double? ValueDefault { get; init; }
}

/// <summary>A control's placement rectangle in base-resolution pixels.</summary>
public sealed record ManifestBounds(double X, double Y, double W, double H);
