using System.Text;
using System.Text.Json;
using StripKit.Models;

namespace StripKit.Services;

/// <summary>Anthropic Claude via the Messages API (<c>POST /v1/messages</c>, <c>x-api-key</c> auth).</summary>
public sealed class ClaudeProvider(HttpClient http) : HttpAssetGenerationProvider(http)
{
    private const string Url = "https://api.anthropic.com/v1/messages";

    public override AiProvider Provider => AiProvider.Claude;
    public override string DefaultModel => "claude-sonnet-4-6";
    public override IReadOnlyList<string> SuggestedModels =>
        ["claude-sonnet-4-6", "claude-opus-4-8", "claude-haiku-4-5-20251001"];

    public override async Task<string> CompleteAsync(string systemPrompt, string userPrompt, string apiKey, string model, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonBody(new
        {
            model,
            max_tokens = 8192,
            temperature = 0.9,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        });

        using var doc = await SendAsync(request, ct);

        // content: [ { "type": "text", "text": "…" }, … ]
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var text))
                    sb.Append(text.GetString());
            }
            if (sb.Length > 0) return sb.ToString();
        }

        throw new GenerationException("Claude returned no text content.");
    }
}
