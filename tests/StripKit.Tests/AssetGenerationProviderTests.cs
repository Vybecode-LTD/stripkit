using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Each provider's HTTP contract, exercised against a fake transport (no network): the right URL,
/// auth header, and request body go out, the right field is parsed back, and a non-2xx becomes a
/// friendly <see cref="GenerationException"/> carrying the API's own error message.
/// </summary>
public class AssetGenerationProviderTests
{
    sealed class CapturingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    static (T provider, CapturingHandler handler) Make<T>(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
        where T : HttpAssetGenerationProvider
    {
        var handler = new CapturingHandler(responseBody, status);
        var provider = (T)Activator.CreateInstance(typeof(T), new HttpClient(handler))!;
        return (provider, handler);
    }

    const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><g id=\"body\"/></svg>";
    static string Json(string s) => JsonSerializer.Serialize(s);

    [Fact]
    public async Task Claude_posts_to_messages_with_x_api_key_and_parses_content_text()
    {
        var (provider, handler) = Make<ClaudeProvider>("{\"content\":[{\"type\":\"text\",\"text\":" + Json(Svg) + "}]}");

        var text = await provider.CompleteAsync("sys", "user", "KEY123", "claude-x", default);

        text.Should().Be(Svg);
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        handler.LastRequest.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("KEY123");
        handler.LastRequest.Headers.GetValues("anthropic-version").Should().ContainSingle();
        handler.LastBody.Should().Contain("\"model\":\"claude-x\"").And.Contain("\"max_tokens\"").And.Contain("\"system\":\"sys\"");
    }

    [Fact]
    public async Task OpenAi_posts_with_bearer_auth_and_parses_choices_message_content()
    {
        var (provider, handler) = Make<OpenAiProvider>("{\"choices\":[{\"message\":{\"content\":" + Json(Svg) + "}}]}");

        var text = await provider.CompleteAsync("sys", "user", "OAKEY", "gpt-x", default);

        text.Should().Be(Svg);
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.openai.com/v1/chat/completions");
        handler.LastRequest.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer OAKEY");
        handler.LastBody.Should().Contain("\"role\":\"system\"").And.Contain("\"role\":\"user\"").And.Contain("\"model\":\"gpt-x\"");
    }

    [Fact]
    public async Task Gemini_puts_the_model_in_the_path_with_a_goog_key_and_parses_parts_text()
    {
        var (provider, handler) = Make<GeminiProvider>("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + Json(Svg) + "}]}}]}");

        var text = await provider.CompleteAsync("sys", "user", "GKEY", "gemini-x", default);

        text.Should().Be(Svg);
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("/models/gemini-x:generateContent");
        handler.LastRequest.Headers.GetValues("x-goog-api-key").Should().ContainSingle().Which.Should().Be("GKEY");
        handler.LastBody.Should().Contain("system_instruction").And.Contain("\"text\":\"user\"");
    }

    [Fact]
    public async Task A_401_becomes_a_friendly_exception_with_the_api_message()
    {
        var (provider, _) = Make<ClaudeProvider>("""{"error":{"message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized);

        var act = async () => await provider.CompleteAsync("s", "u", "bad", "m", default);

        (await act.Should().ThrowAsync<GenerationException>())
            .Which.Message.Should().Contain("authentication failed").And.Contain("invalid x-api-key");
    }

    [Fact]
    public void Each_provider_exposes_its_identity_and_a_default_model()
    {
        var (claude, _) = Make<ClaudeProvider>("{}");
        var (openai, _) = Make<OpenAiProvider>("{}");
        var (gemini, _) = Make<GeminiProvider>("{}");

        claude.Provider.Should().Be(AiProvider.Claude);
        openai.Provider.Should().Be(AiProvider.OpenAI);
        gemini.Provider.Should().Be(AiProvider.Gemini);
        claude.DefaultModel.Should().NotBeNullOrWhiteSpace();
        openai.DefaultModel.Should().NotBeNullOrWhiteSpace();
        gemini.DefaultModel.Should().NotBeNullOrWhiteSpace();

        // Each provider's suggested list is non-empty and starts with the default model.
        claude.SuggestedModels.Should().NotBeEmpty().And.StartWith(claude.DefaultModel);
        openai.SuggestedModels.Should().NotBeEmpty().And.StartWith(openai.DefaultModel);
        gemini.SuggestedModels.Should().NotBeEmpty().And.StartWith(gemini.DefaultModel);

        // The deprecated model must no longer be the Gemini default.
        gemini.DefaultModel.Should().NotBe("gemini-2.0-flash",
            "gemini-2.0-flash was shut down June 1 2026 and must not be used");
    }
}
