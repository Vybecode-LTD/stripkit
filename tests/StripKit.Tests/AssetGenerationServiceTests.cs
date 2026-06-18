using FluentAssertions;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The orchestrator above the providers: it builds a StripKit-aware prompt, dispatches to the chosen
/// provider, and reduces the reply to a clean SVG. We use a fake provider (which records the prompts
/// it receives) so the prompt content, model fallback, success-to-importable-SVG path, and the
/// failure paths are all covered without a network.
/// </summary>
public class AssetGenerationServiceTests
{
    sealed class FakeProvider(AiProvider provider, Func<string> reply) : IAssetGenerationProvider
    {
        public string? LastSystem, LastUser, LastModel, LastKey, LastImagePrompt;
        public byte[]? LastImage;
        public AiProvider Provider { get; } = provider;
        public string DefaultModel => "fake-default";
        public IReadOnlyList<string> SuggestedModels => ["fake-default", "fake-alt"];

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, string apiKey, string model, CancellationToken ct)
        {
            LastSystem = systemPrompt; LastUser = userPrompt; LastModel = model; LastKey = apiKey;
            return Task.FromResult(reply());
        }

        public Task<string> DescribeImageAsync(byte[] image, string mediaType, string prompt, string apiKey, string model, CancellationToken ct)
        {
            LastImage = image; LastImagePrompt = prompt; LastKey = apiKey; LastModel = model;
            return Task.FromResult("amber knurled knob, brushed metal, warm LED ring");
        }
    }

    const string LayeredKnob =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="body"><circle cx="50" cy="50" r="40" fill="#333"/></g>
          <g id="pointer"><line x1="50" y1="50" x2="50" y2="12" stroke="#fff" stroke-width="6"/></g>
        </svg>
        """;

    const string LayeredMeter =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="48" height="160" viewBox="0 0 48 160">
          <g id="off"><rect x="8" y="0" width="32" height="160" fill="#222"/></g>
          <g id="on"><rect x="8" y="0" width="32" height="160" fill="#0f0"/></g>
        </svg>
        """;

    const string LayeredToggle =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="off"><rect x="10" y="35" width="80" height="30" rx="15" fill="#333"/></g>
          <g id="on"><rect x="10" y="35" width="80" height="30" rx="15" fill="#0f0"/></g>
        </svg>
        """;

    [Fact]
    public async Task A_chatty_reply_is_reduced_to_a_clean_importable_layered_svg()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => $"Here you go!\n```svg\n{LayeredKnob}\n```");
        var svc = new AssetGenerationService([fake]);

        var result = await svc.GenerateAsync(new GenerationRequest(), AiProvider.Claude, "KEY", "", default);

        result.Success.Should().BeTrue(result.Error);
        result.Svg.Should().StartWith("<svg").And.Contain("pointer");

        // The cleaned SVG must round-trip through the real importer as a tagged layer stack — that's
        // what guarantees the Create-tab handoff works.
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_gen_{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, result.Svg!);
        try
        {
            var import = new LayeredImportService().Import(path)!;
            import.Layers.Should().HaveCount(2);
            import.Layers.Single(l => l.Name == "pointer").SuggestedBehavior.Should().Be(LayerBehavior.Rotate);
            import.Layers.Single(l => l.Name == "body").SuggestedBehavior.Should().Be(LayerBehavior.Static);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task The_prompt_encodes_stripkit_conventions_and_uses_the_default_model()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        await svc.GenerateAsync(new GenerationRequest { AccentColor = "#00FF00", CanvasSize = 256 },
                                AiProvider.Claude, "KEY", "", default);

        fake.LastModel.Should().Be("fake-default", "an empty model falls back to the provider default");
        fake.LastSystem.Should().Contain("id=\"body\"").And.Contain("id=\"pointer\"");
        fake.LastSystem.Should().Contain("10%", "the ~10% rotation-margin convention is in the prompt");
        fake.LastSystem.Should().Contain("256", "the canvas size flows into the prompt");
        fake.LastUser.Should().Contain("#00FF00", "the accent colour flows into the prompt");
    }

    [Fact]
    public async Task A_meter_prompt_asks_for_off_on_groups_spanning_the_full_height()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredMeter);
        var svc = new AssetGenerationService([fake]);

        var result = await svc.GenerateAsync(new GenerationRequest { ComponentType = ComponentType.Meter },
                                             AiProvider.Claude, "KEY", "", default);

        result.Success.Should().BeTrue(result.Error);
        fake.LastSystem.Should().Contain("id=\"off\"").And.Contain("id=\"on\"",
            "a meter is generated as an unlit + lit pair");
        fake.LastSystem.Should().Contain("full height",
            "the reveal needs the meter to span the height with no vertical margin");
        fake.LastUser.Should().Contain("meter");
    }

    [Fact]
    public async Task A_horizontal_meter_prompt_uses_a_landscape_canvas_filling_left_to_right()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredMeter);
        var svc = new AssetGenerationService([fake]);

        await svc.GenerateAsync(new GenerationRequest { ComponentType = ComponentType.Meter, MeterHorizontal = true },
                                AiProvider.Claude, "KEY", "", default);

        fake.LastSystem.Should().Contain("full width", "a horizontal meter spans the width, not the height");
        fake.LastSystem.Should().Contain("LEFT", "low values sit at the left for a horizontal meter");
        fake.LastUser.Should().Contain("horizontal meter");
    }

    [Fact]
    public async Task The_avoid_text_is_folded_into_the_user_prompt()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        await svc.GenerateAsync(new GenerationRequest { Avoid = "no text, no numbers" },
                                AiProvider.Claude, "KEY", "", default);

        fake.LastUser.Should().Contain("no text, no numbers", "the avoid list shapes the prompt");
    }

    [Fact]
    public async Task GenerateSetAsync_returns_one_result_per_type_in_order()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);
        var types = new[] { ComponentType.RotaryKnob, ComponentType.Button, ComponentType.Meter, ComponentType.VerticalFader };

        var items = await svc.GenerateSetAsync(
            new GenerationRequest { Style = GenerationStyle.Vintage, AccentColor = "#ABCDEF" },
            types, AiProvider.Claude, "KEY", "", default);

        items.Should().HaveCount(4);
        items.Select(i => i.ComponentType).Should().Equal(types, "one item per requested type, in order");
        items.Should().OnlyContain(i => i.Result.Success);
    }

    [Fact]
    public async Task GenerateSetAsync_surfaces_a_per_item_failure_without_sinking_the_rest()
    {
        // First call fails, the rest succeed — the set must report each item independently.
        int n = 0;
        var fake = new FakeProvider(AiProvider.Claude, () => Interlocked.Increment(ref n) == 1 ? "not an svg at all" : LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        var items = await svc.GenerateSetAsync(new GenerationRequest(),
            new[] { ComponentType.RotaryKnob, ComponentType.Button, ComponentType.Meter },
            AiProvider.Claude, "KEY", "", default);

        items.Should().HaveCount(3);
        items.Count(i => i.Result.Success).Should().Be(2, "one reply was not an SVG");
        items.Count(i => !i.Result.Success).Should().Be(1);
    }

    [Fact]
    public async Task GenerateVariationsAsync_returns_the_requested_number_of_takes()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        var results = await svc.GenerateVariationsAsync(new GenerationRequest { ComponentType = ComponentType.RotaryKnob },
                                                        5, AiProvider.Claude, "KEY", "", default);

        results.Should().HaveCount(5);
        results.Should().OnlyContain(r => r.Success);
    }

    [Fact]
    public async Task RefineAsync_hands_the_current_svg_and_the_instruction_to_the_model()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        var result = await svc.RefineAsync(new GenerationRequest { ComponentType = ComponentType.RotaryKnob },
            "<svg id=\"old\"/>", "make the pointer thicker", AiProvider.Claude, "KEY", "", default);

        result.Success.Should().BeTrue(result.Error);
        fake.LastUser.Should().Contain("<svg id=\"old\"/>", "the current SVG is handed back for revision");
        fake.LastUser.Should().Contain("make the pointer thicker", "the instruction drives the change");
    }

    [Fact]
    public async Task RefineAsync_requires_an_instruction()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        var result = await svc.RefineAsync(new GenerationRequest(), "<svg/>", "   ", AiProvider.Claude, "KEY", "", default);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("change");
    }

    [Fact]
    public async Task DescribeReferenceAsync_returns_the_vision_description()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        var result = await svc.DescribeReferenceAsync(new byte[] { 1, 2, 3 }, "image/png", AiProvider.Claude, "KEY", "", default);

        result.Success.Should().BeTrue(result.Error);
        result.Text.Should().Contain("amber");
        fake.LastImage.Should().NotBeNull();
        fake.LastImagePrompt.Should().Contain("SVG", "the vision prompt asks for an SVG-oriented style description");
    }

    [Fact]
    public async Task DescribeReferenceAsync_fails_cleanly_without_a_key()
    {
        var svc = new AssetGenerationService([new FakeProvider(AiProvider.Claude, () => LayeredKnob)]);

        var result = await svc.DescribeReferenceAsync(new byte[] { 1 }, "image/png", AiProvider.Claude, "  ", "", default);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_toggle_prompt_asks_for_off_on_switch_groups()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredToggle);
        var svc = new AssetGenerationService([fake]);

        var result = await svc.GenerateAsync(new GenerationRequest { ComponentType = ComponentType.Toggle },
                                             AiProvider.Claude, "KEY", "", default);

        result.Success.Should().BeTrue(result.Error);
        fake.LastSystem.Should().Contain("id=\"off\"").And.Contain("id=\"on\"", "a toggle is an off/on pair");
        fake.LastSystem.Should().Contain("switch", "a toggle is drawn as a switch, not a push button");
        fake.LastUser.Should().Contain("toggle switch");
    }

    [Fact]
    public async Task A_reply_without_an_svg_fails_gracefully_and_keeps_the_raw_text()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => "I'm sorry, I can't draw that.");
        var svc = new AssetGenerationService([fake]);

        var result = await svc.GenerateAsync(new GenerationRequest(), AiProvider.Claude, "KEY", "", default);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.RawResponse.Should().Contain("can't draw", "the raw reply is kept for the diagnostic panel");
    }

    [Fact]
    public async Task A_provider_failure_surfaces_its_message()
    {
        var fake = new FakeProvider(AiProvider.OpenAI, () => throw new GenerationException("OpenAI: rate limited."));
        var svc = new AssetGenerationService([fake]);

        var result = await svc.GenerateAsync(new GenerationRequest(), AiProvider.OpenAI, "KEY", "", default);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("OpenAI: rate limited.");
    }

    [Fact]
    public async Task A_missing_key_fails_before_the_provider_is_called()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        var result = await svc.GenerateAsync(new GenerationRequest(), AiProvider.Claude, "   ", "", default);

        result.Success.Should().BeFalse();
        fake.LastUser.Should().BeNull("the provider is never called without a key");
    }

    [Fact]
    public void Available_providers_are_reported_in_a_stable_display_order()
    {
        var svc = new AssetGenerationService(
        [
            new FakeProvider(AiProvider.Gemini, () => ""),
            new FakeProvider(AiProvider.Claude, () => ""),
        ]);

        svc.AvailableProviders.Should().Equal(AiProvider.Claude, AiProvider.Gemini);
        svc.DefaultModelFor(AiProvider.Claude).Should().Be("fake-default");
    }

    [Fact]
    public void ModelsFor_returns_the_provider_suggestions()
    {
        var svc = new AssetGenerationService([new FakeProvider(AiProvider.Claude, () => LayeredKnob)]);

        svc.ModelsFor(AiProvider.Claude).Should().BeEquivalentTo(["fake-default", "fake-alt"]);
        svc.ModelsFor(AiProvider.Gemini).Should().BeEmpty("Gemini is not wired in this service instance");
    }

    [Fact]
    public async Task Body_color_and_effects_appear_in_the_user_prompt()
    {
        var fake = new FakeProvider(AiProvider.Claude, () => LayeredKnob);
        var svc = new AssetGenerationService([fake]);

        var req = new GenerationRequest
        {
            BodyColor = "#1A1A2E",
            HasDropShadow = true,
            HasMetallicSheen = true,
        };
        await svc.GenerateAsync(req, AiProvider.Claude, "key", "fake-default", default);

        fake.LastUser.Should().Contain("#1A1A2E", "body colour is included in the prompt");
        fake.LastUser.Should().Contain("drop shadow");
        fake.LastUser.Should().Contain("metallic sheen");
    }
}
