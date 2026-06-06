using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// Loads and persists the small <see cref="AppSettings"/> JSON (app-data folder). App-only (file
/// I/O, no Avalonia). Settings are best-effort — a missing/corrupt file yields defaults, and a
/// failed write never crashes the app.
/// </summary>
public interface ISettingsService
{
    /// <summary>The live settings instance (mutate, then call <see cref="Save"/>).</summary>
    AppSettings Settings { get; }

    /// <summary>Persists the current settings to disk (best-effort).</summary>
    void Save();
}
