using System.Net;
using System.Text;
using FluentAssertions;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The OpenAI-compatible custom provider: it reuses OpenAI's wire format but POSTs to a user-supplied
/// base URL (read from settings at call time), normalising a base to the full chat-completions path and
/// using Bearer auth. Only the network is faked.
/// </summary>
public class CustomOpenAiProviderTests
{
    sealed class CapturingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? LastUri;
        public string? LastAuth;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            LastAuth = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    const string OkBody = "{\"choices\":[{\"message\":{\"content\":\"<svg/>\"}}]}";

    static ISettingsService Settings(string? baseUrl)
    {
        var ss = new SettingsService(Path.Combine(Path.GetTempPath(), $"stripkit_set_{Guid.NewGuid():N}.json"));
        ss.Settings.GenerateCustomBaseUrl = baseUrl;
        return ss;
    }

    [Fact]
    public async Task Posts_to_the_base_url_with_chat_completions_appended_and_bearer_auth()
    {
        var handler = new CapturingHandler(OkBody);
        var provider = new CustomOpenAiProvider(new HttpClient(handler), Settings("https://openrouter.ai/api/v1"));

        var text = await provider.CompleteAsync("sys", "user", "my-key", "openai/gpt-4o", default);

        text.Should().Be("<svg/>");
        handler.LastUri!.ToString().Should().Be("https://openrouter.ai/api/v1/chat/completions");
        handler.LastAuth.Should().Be("Bearer my-key");
    }

    [Fact]
    public async Task Keeps_a_full_chat_completions_url_as_is()
    {
        var handler = new CapturingHandler(OkBody);
        var provider = new CustomOpenAiProvider(new HttpClient(handler), Settings("http://localhost:11434/v1/chat/completions"));

        await provider.CompleteAsync("s", "u", "k", "llama3", default);

        handler.LastUri!.ToString().Should().Be("http://localhost:11434/v1/chat/completions");
    }

    [Fact]
    public async Task Without_a_base_url_it_fails_with_a_friendly_message()
    {
        var provider = new CustomOpenAiProvider(new HttpClient(new CapturingHandler(OkBody)), Settings(null));

        var act = async () => await provider.CompleteAsync("s", "u", "k", "m", default);

        (await act.Should().ThrowAsync<GenerationException>()).WithMessage("*base URL*");
    }
}
