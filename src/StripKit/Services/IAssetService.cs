namespace StripKit.Services;

/// <summary>
/// Access to bundled (avares) assets that need to reach a file path — currently the sample knob the
/// tutorial loads. The implementation lives in the app layer (it uses Avalonia's asset loader); view
/// models depend only on this interface, so they stay free of Avalonia types.
/// </summary>
public interface IAssetService
{
    /// <summary>Extracts the bundled sample knob PNG to a temp file and returns its path (cached
    /// across calls), or <c>null</c> if the asset is unavailable.</summary>
    string? GetSampleKnobPath();
}
