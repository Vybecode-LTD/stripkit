using System.Net;
using System.Text;
using FluentAssertions;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The vision path (DescribeImageAsync) per provider: each must send the reference image in its own
/// wire format (Claude image block, OpenAI image_url data URI, Gemini inline_data) and read the text
/// description back. Only the network is faked; we assert the outgoing request body's shape.
/// </summary>
public class VisionProviderTests
{
    sealed class BodyCapture(string reply) : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    static readonly byte[] Img = [1, 2, 3, 4];
    static string B64 => Convert.ToBase64String(Img);

    [Fact]
    public async Task Claude_sends_a_base64_image_block_and_reads_the_text()
    {
        var h = new BodyCapture("{\"content\":[{\"type\":\"text\",\"text\":\"desc\"}]}");
        var text = await new ClaudeProvider(new HttpClient(h))
            .DescribeImageAsync(Img, "image/png", "describe", "k", "claude-x", default);

        text.Should().Be("desc");
        h.Body.Should().Contain("\"type\":\"image\"").And.Contain(B64);
    }

    [Fact]
    public async Task OpenAi_sends_an_image_url_data_uri_and_reads_the_text()
    {
        var h = new BodyCapture("{\"choices\":[{\"message\":{\"content\":\"desc\"}}]}");
        var text = await new OpenAiProvider(new HttpClient(h))
            .DescribeImageAsync(Img, "image/png", "describe", "k", "gpt-4o", default);

        text.Should().Be("desc");
        h.Body.Should().Contain("image_url").And.Contain($"data:image/png;base64,{B64}");
    }

    [Fact]
    public async Task Gemini_sends_inline_data_and_reads_the_text()
    {
        var h = new BodyCapture("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"desc\"}]}}]}");
        var text = await new GeminiProvider(new HttpClient(h))
            .DescribeImageAsync(Img, "image/png", "describe", "k", "gemini-2.5-flash", default);

        text.Should().Be("desc");
        h.Body.Should().Contain("inline_data").And.Contain(B64);
    }
}
