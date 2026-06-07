using System.Text;
using System.Text.Json;
using StripKit.Models;

namespace StripKit.Services;

/// <summary>Google Gemini via the generateContent API (<c>x-goog-api-key</c> auth).</summary>
public sealed class GeminiProvider(HttpClient http) : HttpAssetGenerationProvider(http)
{
    private const string Base = "https://generativelanguage.googleapis.com/v1beta/models/";

    public override AiProvider Provider => AiProvider.Gemini;
    public override string DefaultModel => "gemini-2.5-flash";
    public override IReadOnlyList<string> SuggestedModels =>
        ["gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.5-flash-lite"];

    public override async Task<string> CompleteAsync(string systemPrompt, string userPrompt, string apiKey, string model, CancellationToken ct)
    {
        // Tolerate a "models/…" prefix the user might paste; the path already includes "models/".
        var id = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? model["models/".Length..] : model;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}{Uri.EscapeDataString(id)}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonBody(new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { temperature = 0.9, maxOutputTokens = 8192 },
        });

        using var doc = await SendAsync(request, ct);

        // candidates: [ { "content": { "parts": [ { "text": "…" }, … ] } }, … ]
        if (doc.RootElement.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var text))
                    sb.Append(text.GetString());
            if (sb.Length > 0) return sb.ToString();
        }

        throw new GenerationException("Gemini returned no content (the prompt may have been blocked).");
    }
}
