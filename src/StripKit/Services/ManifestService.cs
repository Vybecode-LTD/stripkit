using System.Text.Json;
using System.Text.Json.Serialization;
using StripKit.Models;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class ManifestService : IManifestService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SkinManifest BuildSingleControl(FilmstripSettings settings, string assetName,
                                           string? asset2xName, string controlId, string parameterId)
    {
        var control = new ManifestControl
        {
            Id = controlId,
            Type = MapType(settings.ComponentType),
            ParameterId = parameterId,
            Asset = assetName,
            Asset2x = asset2xName,
            Frames = settings.FrameCount,
            FrameWidth = settings.FrameWidth,
            FrameHeight = settings.FrameHeight,
            Stack = settings.StackDirection == StackDirection.Vertical ? "vertical" : "horizontal",
            Layout = settings.Layout == StripLayout.Grid ? "grid" : null,
            // Clamp defensively (mirrors the renderer's own Math.Max(1, ...) guard) so an unclamped
            // upstream value (a hand-built FilmstripSettings, or a preset saved before validation
            // existed) can never serialize a non-positive gridColumns, which would violate the
            // plugin-asset-manifest JSON Schema's minimum: 1.
            GridColumns = settings.Layout == StripLayout.Grid ? Math.Max(1, settings.GridColumns) : null,
            // Default the on-screen bounds to one frame at the origin; the skin author
            // repositions in their real layout (bounds are base-resolution pixels).
            Bounds = new ManifestBounds(0, 0, settings.FrameWidth, settings.FrameHeight),
        };

        return new SkinManifest
        {
            ManifestVersion = 1,
            Name = $"{controlId} skin",
            BaseWidth = settings.FrameWidth,
            BaseHeight = settings.FrameHeight,
            Controls = new[] { control },
        };
    }

    public SkinManifest BuildManifest(IReadOnlyList<ManifestControl> controls, string name, string? author,
                                      int baseWidth, int baseHeight, string? background) =>
        new()
        {
            ManifestVersion = 1,
            Name = string.IsNullOrWhiteSpace(name) ? "skin" : name.Trim(),
            Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
            BaseWidth = Math.Max(1, baseWidth),
            BaseHeight = Math.Max(1, baseHeight),
            Background = string.IsNullOrWhiteSpace(background) ? null : background.Trim(),
            Controls = controls,
        };

    public string Serialize(SkinManifest manifest) => JsonSerializer.Serialize(manifest, Options);

    public async Task SaveAsync(SkinManifest manifest, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(path, Serialize(manifest));
    }

    private static string MapType(ComponentType type) => type switch
    {
        ComponentType.RotaryKnob => "knob",
        ComponentType.VerticalFader => "vfader",
        ComponentType.HorizontalSlider => "hslider",
        ComponentType.Meter => "meter",
        ComponentType.Button => "button",
        ComponentType.Toggle => "toggle",
        _ => "knob",
    };
}
