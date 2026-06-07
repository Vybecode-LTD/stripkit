using System.Text.Json;
using StripKit.Models;

namespace StripKit.Services;

/// <summary>OpenAI via the Chat Completions API (<c>POST /v1/chat/completions</c>, Bearer auth).</summary>
/// <remarks>The body is kept to <c>model</c> + <c>messages</c> only (no <c>max_tokens</c>/<c>temperature</c>)
/// so it stays compatible across the chat models a user might pin, including reasoning models that
/// reject those fields. Default temperature gives enough variety on Regenerate.</remarks>
public sealed class OpenAiProvider(HttpClient http) : HttpAssetGenerationProvider(http)
{
    private const string Url = "https://api.openai.com/v1/chat/completions";

    public override AiProvider Provider => AiProvider.OpenAI;
    public override string DefaultModel => "gpt-4o";
    public override IReadOnlyList<string> SuggestedModels =>
        ["gpt-4o", "gpt-4.1", "gpt-4.1-mini"];

    public override async Task<string> CompleteAsync(string systemPrompt, string userPrompt, string apiKey, string model, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = JsonBody(new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        });

        using var doc = await SendAsync(request, ct);

        // choices: [ { "message": { "content": "…" } }, … ]
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.GetString() is { Length: > 0 } text)
        {
            return text;
        }

        throw new GenerationException("OpenAI returned no message content.");
    }
}
