using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// An OpenAI-compatible chat-completions endpoint at a user-supplied base URL — OpenRouter, a local
/// Ollama / LM Studio server, or any service that speaks the OpenAI wire format with Bearer auth.
/// Reuses <see cref="OpenAiProvider"/>'s request/response handling unchanged; the only difference is the
/// URL, read from settings at call time so the user can change it without a restart. App-only.
/// </summary>
public sealed class CustomOpenAiProvider(HttpClient http, ISettingsService settings) : OpenAiProvider(http)
{
    public override AiProvider Provider => AiProvider.Custom;

    // No built-in default / suggestions — the user types the model id their own endpoint expects
    // (e.g. "openai/gpt-4o" on OpenRouter, "llama3.1" on Ollama).
    public override string DefaultModel => "";
    public override IReadOnlyList<string> SuggestedModels => Array.Empty<string>();

    protected override string EndpointUrl
    {
        get
        {
            var baseUrl = settings.Settings.GenerateCustomBaseUrl?.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new GenerationException("Enter the custom endpoint's base URL first (e.g. https://openrouter.ai/api/v1).");

            baseUrl = baseUrl.TrimEnd('/');
            // Accept either a base ("…/v1") or a full chat-completions URL; normalise to the full path.
            return baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : baseUrl + "/chat/completions";
        }
    }
}
