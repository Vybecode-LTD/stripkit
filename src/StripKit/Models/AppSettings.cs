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

    /// <summary>Base URL for the OpenAI-compatible custom provider — anything that speaks the OpenAI
    /// chat-completions wire format with Bearer auth (e.g. https://openrouter.ai/api/v1, or
    /// http://localhost:11434/v1 for Ollama, or an LM Studio server). Used only when the Custom provider
    /// is selected; the key still lives in the secret store.</summary>
    public string? GenerateCustomBaseUrl { get; set; }

    /// <summary>The user's saved prompt seeds (named style bundles) on the Generate tab. Built-in seeds
    /// are not stored here — only ones the user saved.</summary>
    public List<GenerationSeed> GenerateSeeds { get; set; } = new();

    /// <summary>The main window's last size, restored on the next launch (null = use the default).</summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    /// <summary>The tab the app was on when it last closed, reopened on the next launch.</summary>
    public int LastTabIndex { get; set; }
}
