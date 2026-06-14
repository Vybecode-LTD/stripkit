using FluentAssertions;
using NSubstitute;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The Generate tab's view model, with a mocked generation service and a real encrypted secret store
/// + real importer. Covers the key-gating, per-provider key save/reload, the success path (which
/// validates by importing the SVG and then enables the Create handoff), and the two failure paths
/// (service error, and a reply that won't import). The Avalonia preview bitmap can't render under a
/// plain unit test, so it's best-effort null — the result state is what's asserted.
/// </summary>
public class GenerateViewModelTests
{
    const string LayeredKnobSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="body"><circle cx="50" cy="50" r="40" fill="#333"/></g>
          <g id="pointer"><line x1="50" y1="50" x2="50" y2="12" stroke="#fff" stroke-width="6"/></g>
        </svg>
        """;

    static (GenerateViewModel vm, IAssetGenerationService gen, DpapiSecretStore secrets, string[] temps) Build()
    {
        var secretsPath = Path.Combine(Path.GetTempPath(), $"stripkit_secrets_{Guid.NewGuid():N}.dat");
        var settingsPath = Path.Combine(Path.GetTempPath(), $"stripkit_settings_{Guid.NewGuid():N}.json");

        var gen = Substitute.For<IAssetGenerationService>();
        gen.AvailableProviders.Returns([AiProvider.Claude, AiProvider.OpenAI, AiProvider.Gemini]);
        gen.DefaultModelFor(Arg.Any<AiProvider>()).Returns(ci => $"default-{ci.Arg<AiProvider>()}");
        gen.ModelsFor(Arg.Any<AiProvider>()).Returns(["test-model", "test-alt"]);

        var secrets = new DpapiSecretStore(secretsPath);
        var vm = new GenerateViewModel(gen, secrets, new SettingsService(settingsPath),
                                       new LayeredImportService(), Substitute.For<IFileDialogService>());
        return (vm, gen, secrets, [secretsPath, settingsPath]);
    }

    static void Cleanup(string[] paths)
    {
        foreach (var p in paths)
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
    }

    static void StubReply(IAssetGenerationService gen, GenerationResult result) =>
        gen.GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                          Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);

    [Fact]
    public void Generate_is_gated_on_a_present_api_key()
    {
        var (vm, _, _, temps) = Build();
        try
        {
            vm.GenerateCommand.CanExecute(null).Should().BeFalse("no key entered yet");
            vm.ApiKey = "sk-something";
            vm.GenerateCommand.CanExecute(null).Should().BeTrue();
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public void Saving_a_key_persists_it_and_switching_provider_reloads_per_provider()
    {
        var (vm, _, secrets, temps) = Build();
        try
        {
            vm.SelectedProvider = AiProvider.Claude;
            vm.ApiKey = "claude-key";
            vm.SaveKeyCommand.Execute(null);
            vm.HasKey.Should().BeTrue();
            secrets.Get(AiProvider.Claude).Should().Be("claude-key");

            vm.SelectedProvider = AiProvider.OpenAI;
            vm.ApiKey.Should().BeEmpty("no key is stored for OpenAI yet");
            vm.HasKey.Should().BeFalse();
            vm.Model.Should().Be("default-OpenAI", "the model resets to the provider default");

            vm.SelectedProvider = AiProvider.Claude;
            vm.ApiKey.Should().Be("claude-key", "switching back reloads the per-provider key");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_successful_generation_produces_a_result_and_enables_the_create_handoff()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Ok(LayeredKnobSvg));
            vm.ApiKey = "sk-test";

            string? handedOff = null;
            ComponentType handedType = default;
            vm.UseInCreateRequested += (p, t) => { handedOff = p; handedType = t; };

            await vm.GenerateCommand.ExecuteAsync(null);

            vm.HasResult.Should().BeTrue(vm.StatusMessage);
            vm.GeneratedSvg.Should().Contain("pointer");
            vm.UseInCreateCommand.CanExecute(null).Should().BeTrue();
            vm.SaveSvgCommand.CanExecute(null).Should().BeTrue();

            vm.UseInCreateCommand.Execute(null);
            handedOff.Should().NotBeNull("the handoff fires with a path");
            handedType.Should().Be(ComponentType.RotaryKnob, "the knob default carries its type to Create");
            File.Exists(handedOff!).Should().BeTrue("the path is a real temp SVG the Create tab can import");
            File.ReadAllText(handedOff!).Should().Contain("<svg");
            File.Delete(handedOff!);
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_thrown_service_error_is_caught_and_surfaced_not_propagated()
    {
        // The broad catch in GenerateAsync exists so a generation can NEVER take the app down.
        // Make the service throw a raw exception and assert the command swallows it gracefully.
        var (vm, gen, _, temps) = Build();
        try
        {
            gen.GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                              Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task<GenerationResult>>(_ => throw new InvalidOperationException("boom"));
            vm.ApiKey = "sk-test";

            var act = async () => await vm.GenerateCommand.ExecuteAsync(null);

            await act.Should().NotThrowAsync("a generation failure must never crash the app");
            vm.HasResult.Should().BeFalse();
            vm.StatusMessage.Should().Contain("Generation failed");
            vm.LastRawResponse.Should().Contain("boom", "the detail is kept for diagnosis");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_custom_model_id_not_in_the_suggestions_is_honored()
    {
        // The model input is free-text (editable), so a model the user types — or a pinned one that
        // the provider later delists — must still be sent, not silently dropped to a suggestion.
        var (vm, gen, _, temps) = Build();
        try
        {
            string? sentModel = null;
            gen.GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                              Arg.Do<string>(m => sentModel = m), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Fail("stop here"));
            vm.ApiKey = "sk-test";
            vm.Model = "my-private-model-9000";   // not in SuggestedModels

            await vm.GenerateCommand.ExecuteAsync(null);

            sentModel.Should().Be("my-private-model-9000", "a typed/delisted model id is sent verbatim");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Generate_passes_the_type_body_accent_and_effects_into_the_request()
    {
        // VM-layer lock: the new colour + effect + control-type fields must reach the GenerationRequest
        // (the service test proves they reach the prompt; this proves the VM populates them at all).
        var (vm, gen, _, temps) = Build();
        try
        {
            GenerationRequest? captured = null;
            gen.GenerateAsync(Arg.Do<GenerationRequest>(r => captured = r), Arg.Any<AiProvider>(),
                              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Fail("stop here"));   // fail early — we only assert the request
            vm.ApiKey = "sk-test";
            vm.GenerateControlType = ComponentType.Button;
            vm.BodyColorHex = "#102030";
            vm.AccentColorHex = "#A0B0C0";
            vm.HasDropShadow = true;
            vm.HasMetallicSheen = true;
            vm.CanvasSize = 768;
            vm.Style = GenerationStyle.Vintage;

            await vm.GenerateCommand.ExecuteAsync(null);

            captured.Should().NotBeNull();
            captured!.ComponentType.Should().Be(ComponentType.Button);
            captured.BodyColor.Should().Be("#102030");
            captured.AccentColor.Should().Be("#A0B0C0");
            captured.HasDropShadow.Should().BeTrue();
            captured.HasMetallicSheen.Should().BeTrue();
            captured.HasOuterGlow.Should().BeFalse();
            captured.CanvasSize.Should().Be(768);
            captured.Style.Should().Be(GenerationStyle.Vintage);
            captured.Layered.Should().BeTrue("a button is layered (off/on state groups)");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_failed_generation_shows_the_error_and_produces_no_result()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Fail("Claude: rate limited or out of quota."));
            vm.ApiKey = "sk-test";

            await vm.GenerateCommand.ExecuteAsync(null);

            vm.HasResult.Should().BeFalse();
            vm.StatusMessage.Should().Contain("rate limited");
            vm.UseInCreateCommand.CanExecute(null).Should().BeFalse();
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_reply_that_will_not_import_does_not_become_a_result()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Ok("this is not really an svg"));
            vm.ApiKey = "sk-test";

            await vm.GenerateCommand.ExecuteAsync(null);

            vm.HasResult.Should().BeFalse();
            vm.StatusMessage.Should().Contain("couldn't be read");
        }
        finally { Cleanup(temps); }
    }
}
