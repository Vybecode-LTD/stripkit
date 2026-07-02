using System.Net;
using System.Text;
using System.Text.Json;
using StripKit.Models;

namespace StripKit.Services;

/// <summary>
/// One AI provider's text/chat endpoint, used to generate SVG art. Each implementation knows its own
/// URL, auth header, request body, and response shape; all return the model's plain-text completion
/// (the SVG extraction/sanitization happens above, in <see cref="IAssetGenerationService"/>). App-only.
/// </summary>
public interface IAssetGenerationProvider
{
    /// <summary>Which provider this is (for selection + key lookup).</summary>
    AiProvider Provider { get; }

    /// <summary>The model id used when the user hasn't pinned one.</summary>
    string DefaultModel { get; }

    /// <summary>The model ids shown in the model picker, in recommended order. The first entry
    /// must equal <see cref="DefaultModel"/>.</summary>
    IReadOnlyList<string> SuggestedModels { get; }

    /// <summary>Sends the prompts to the provider and returns the raw text completion. Throws
    /// <see cref="GenerationException"/> with a user-facing message on any auth/network/API failure.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, string apiKey, string model, CancellationToken ct);

    /// <summary>Vision: describes a reference image as text (for "match this style"). Returns the model's
    /// plain-text description. <paramref name="mediaType"/> is e.g. "image/png". Throws
    /// <see cref="GenerationException"/> on failure (including providers/models without image support).</summary>
    Task<string> DescribeImageAsync(byte[] image, string mediaType, string prompt, string apiKey, string model, CancellationToken ct);
}

/// <summary>A provider failure carrying a message safe to show the user (no secrets, no stack noise).</summary>
public sealed class GenerationException(string message) : Exception(message);

/// <summary>
/// Shared HTTP plumbing for the providers: JSON body building, sending on the shared
/// <see cref="HttpClient"/>, and turning non-2xx responses / transport faults into friendly
/// <see cref="GenerationException"/>s (with the provider's own <c>error.message</c> folded in).
/// </summary>
public abstract class HttpAssetGenerationProvider(HttpClient http) : IAssetGenerationProvider
{
    protected HttpClient Http { get; } = http;

    public abstract AiProvider Provider { get; }
    public abstract string DefaultModel { get; }
    public abstract IReadOnlyList<string> SuggestedModels { get; }
    public abstract Task<string> CompleteAsync(string systemPrompt, string userPrompt, string apiKey, string model, CancellationToken ct);

    /// <summary>Vision support is opt-in per provider; the default reports it as unsupported so a new
    /// provider compiles without it. The three shipped providers override this.</summary>
    public virtual Task<string> DescribeImageAsync(byte[] image, string mediaType, string prompt, string apiKey, string model, CancellationToken ct) =>
        Task.FromException<string>(new GenerationException($"{Provider} does not support image input in StripKit."));

    /// <summary>Serializes an anonymous payload to a JSON request body. Default naming is verbatim,
    /// so member names must match the API exactly (e.g. <c>max_tokens</c>, <c>maxOutputTokens</c>).</summary>
    protected static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    /// <summary>Sends a request and returns the parsed JSON response, or throws a friendly
    /// <see cref="GenerationException"/> on timeout, transport error, or non-success status.</summary>
    protected async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GenerationException($"The {Provider} request timed out — try again.");
        }
        catch (HttpRequestException ex)
        {
            throw new GenerationException($"Couldn't reach {Provider} ({ex.Message}). Check your connection.");
        }

        // Dispose the response (and its content) on every return/throw path — the body is fully buffered
        // to a string below, so a plain using is sufficient.
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new GenerationException(FriendlyError(response.StatusCode, body));

            try
            {
                return JsonDocument.Parse(body);
            }
            catch
            {
                throw new GenerationException($"{Provider} returned an unreadable response.");
            }
        }
    }

    /// <summary>Maps an HTTP failure to a friendly sentence, appending the API's own error message
    /// when one is present (all three nest it at <c>error.message</c>).</summary>
    private string FriendlyError(HttpStatusCode code, string body)
    {
        var detail = ApiErrorMessage(body);
        var prefix = (int)code switch
        {
            401 => "authentication failed — check your API key",
            403 => "access denied — your key may not have access to this model",
            404 => "not found — the model id may be wrong",
            429 => "rate limited or out of quota — wait a moment and retry",
            >= 500 and < 600 => $"server error ({(int)code})",
            _ => $"request failed ({(int)code})",
        };
        return detail is null ? $"{Provider}: {prefix}." : $"{Provider}: {prefix} — {detail}";
    }

    private static string? ApiErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m))
                    return m.GetString();
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString();
            }
        }
        catch
        {
            // body wasn't JSON — no detail to add
        }
        return null;
    }
}
