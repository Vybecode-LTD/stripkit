using Avalonia.Platform;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class AssetService : IAssetService
{
    private const string SampleKnobUri = "avares://StripKit/Assets/sample-knob.png";
    private string? _cachedSamplePath;

    public string? GetSampleKnobPath()
    {
        if (_cachedSamplePath is not null && File.Exists(_cachedSamplePath))
            return _cachedSamplePath;

        try
        {
            using var asset = AssetLoader.Open(new Uri(SampleKnobUri));
            var dir = Path.Combine(Path.GetTempPath(), "StripKit");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "sample-knob.png");
            using (var fs = File.Create(path))
                asset.CopyTo(fs);
            _cachedSamplePath = path;
            return path;
        }
        catch
        {
            return null;
        }
    }
}
