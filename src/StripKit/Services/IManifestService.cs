using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Builds and serializes a <see cref="SkinManifest"/> for an exported filmstrip.
/// UI-agnostic and pure apart from <see cref="SaveAsync"/>.
/// </summary>
public interface IManifestService
{
    /// <summary>
    /// Builds a one-control manifest for a just-exported strip. Asset paths are
    /// stored as bare file names (relative to the manifest, which is written next
    /// to the PNG). <paramref name="asset2xName"/> is null when no @2x was exported.
    /// </summary>
    SkinManifest BuildSingleControl(FilmstripSettings settings, string assetName,
                                    string? asset2xName, string controlId, string parameterId);

    /// <summary>Serializes a manifest to indented JSON with the schema's camelCase keys.</summary>
    string Serialize(SkinManifest manifest);

    /// <summary>Serializes and writes the manifest to <paramref name="path"/>.</summary>
    Task SaveAsync(SkinManifest manifest, string path);
}
