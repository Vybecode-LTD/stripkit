using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// End-to-end through the REAL service + provider + sanitizer + importer — only the network is faked —
/// driving the actual <see cref="GenerateViewModel"/> command. This is the path that only runs with a
/// real key (a unit test with a mocked service skips it), so it's where a "crashes on Generate" bug
/// would hide. Asserts the wired command neither throws nor crashes for a realistic success OR an API
/// error.
/// </summary>
public class GenerateIntegrationTests
{
    sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    const string LayeredKnobSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\" viewBox=\"0 0 512 512\">" +
        "<g id=\"body\"><circle cx=\"256\" cy=\"256\" r=\"200\" fill=\"#333\"/></g>" +
        "<g id=\"pointer\"><line x1=\"256\" y1=\"256\" x2=\"256\" y2=\"80\" stroke=\"#e8440a\" stroke-width=\"24\"/></g>" +
        "</svg>";

    static GenerateViewModel BuildVm(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var service = new AssetGenerationService(
            [new ClaudeProvider(http), new OpenAiProvider(http), new GeminiProvider(http)]);
        return new GenerateViewModel(service, TestFakes.TempSecrets(), TestFakes.TempSettings(),
                                     new LayeredImportService(), Substitute.For<IFileDialogService>());
    }

    [Fact]
    public async Task Full_wired_success_path_produces_a_result_without_throwing()
    {
        // A realistic Anthropic success: a chatty, fenced reply wrapping the layered SVG.
        var reply = $"Here is your knob:\n```svg\n{LayeredKnobSvg}\n```";
        var body = "{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(reply) + "}]}";

        var vm = BuildVm(new StubHandler(body, HttpStatusCode.OK));
        vm.SelectedProvider = AiProvider.Claude;
        vm.ApiKey = "sk-ant-fake";

        await vm.GenerateCommand.ExecuteAsync(null);

        vm.HasResult.Should().BeTrue(vm.StatusMessage);
        vm.GeneratedSvg.Should().Contain("pointer");
    }

    [Fact]
    public async Task Full_wired_api_error_surfaces_a_message_and_does_not_crash()
    {
        var vm = BuildVm(new StubHandler("{\"error\":{\"message\":\"invalid x-api-key\"}}", HttpStatusCode.Unauthorized));
        vm.SelectedProvider = AiProvider.Claude;
        vm.ApiKey = "sk-ant-bad";

        await vm.GenerateCommand.ExecuteAsync(null);

        vm.HasResult.Should().BeFalse();
        vm.StatusMessage.Should().Contain("authentication failed").And.Contain("invalid x-api-key");
    }
}
