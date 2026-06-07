namespace StripKit.Models;

/// <summary>
/// Persisted user preferences — a small JSON in the app-data folder. Kept deliberately minimal;
/// StripKit is otherwise stateless. Read once at startup, written on change.
/// </summary>
public sealed class AppSettings
{
    /// <summary>True once the user has finished or skipped the first-run Getting Started tutorial,
    /// so it does not auto-open again (it stays re-openable from the Help button).</summary>
    public bool HasSeenTutorial { get; set; }

    /// <summary>The last AI provider chosen on the Generate tab (round-trips the dropdown).
    /// API keys are NOT stored here — they live encrypted in the secret store.</summary>
    public AiProvider GenerateProvider { get; set; } = AiProvider.Claude;

    /// <summary>Per-provider model id overrides keyed by provider name (e.g. "Claude" → a model id).
    /// Empty ⇒ the provider's built-in default. Lets a user pin a specific model without code changes.</summary>
    public Dictionary<string, string> GenerateModels { get; set; } = new();
}
