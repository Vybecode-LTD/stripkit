using System.Text.Json.Nodes;
using FluentAssertions;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Tests the manifest builder/serializer: component-type mapping, field carry-over,
/// optional-field omission, and conformance to the JSON Schema from the
/// plugin-asset-manifest skill (required keys, enums, types).
/// </summary>
public class ManifestServiceTests
{
    readonly ManifestService _svc = new();

    static FilmstripSettings Settings(ComponentType type, int frames = 64, int fw = 80, int fh = 80,
                                      StackDirection stack = StackDirection.Vertical)
        => new() { ComponentType = type, FrameCount = frames, FrameWidth = fw, FrameHeight = fh, StackDirection = stack };

    [Theory]
    [InlineData(ComponentType.RotaryKnob, "knob")]
    [InlineData(ComponentType.VerticalFader, "vfader")]
    [InlineData(ComponentType.HorizontalSlider, "hslider")]
    public void BuildSingleControl_maps_the_component_type(ComponentType type, string expected)
    {
        var m = _svc.BuildSingleControl(Settings(type), "gain_64.png", null, "gain", "outputGain");

        m.Controls.Should().HaveCount(1);
        m.Controls[0].Type.Should().Be(expected);
    }

    [Fact]
    public void BuildSingleControl_carries_frames_size_stack_and_assets()
    {
        var s = Settings(ComponentType.HorizontalSlider, frames: 128, fw: 128, fh: 32, stack: StackDirection.Horizontal);

        var c = _svc.BuildSingleControl(s, "slider_128.png", "slider_128@2x.png", "pan", "panParam").Controls[0];

        c.Frames.Should().Be(128);
        c.FrameWidth.Should().Be(128);
        c.FrameHeight.Should().Be(32);
        c.Stack.Should().Be("horizontal");
        c.Asset.Should().Be("slider_128.png");
        c.Asset2x.Should().Be("slider_128@2x.png");
        c.ParameterId.Should().Be("panParam");
        c.Bounds.W.Should().Be(128);
        c.Bounds.H.Should().Be(32);
    }

    [Fact]
    public void Serialized_manifest_conforms_to_the_skill_schema()
    {
        var m = _svc.BuildSingleControl(Settings(ComponentType.RotaryKnob), "knob_64.png", "knob_64@2x.png", "cutoff", "filterCutoff");

        var json = _svc.Serialize(m);

        AssertConformsToSchema(json);
        json.Should().Contain("\"manifestVersion\"")
            .And.Contain("\"frameWidth\"")
            .And.Contain("\"parameterId\"")
            .And.Contain("\"asset2x\"");   // camelCase keys, matching the schema
    }

    [Fact]
    public void Optional_fields_are_omitted_when_absent()
    {
        var m = _svc.BuildSingleControl(Settings(ComponentType.RotaryKnob), "knob_64.png", null, "cutoff", "filterCutoff");

        var json = _svc.Serialize(m);

        json.Should().NotContain("asset2x");   // no @2x exported → omitted
        json.Should().NotContain("author");    // null metadata → omitted
        AssertConformsToSchema(json);
    }

    [Fact]
    public void BuildManifest_assembles_multiple_controls_and_global_metadata()
    {
        var controls = new[]
        {
            new ManifestControl
            {
                Id = "cutoff", Type = "knob", ParameterId = "filterCutoff",
                Asset = "cutoff_64.png", Frames = 64, FrameWidth = 80, FrameHeight = 80,
                Bounds = new ManifestBounds(10, 20, 80, 80),
            },
            new ManifestControl
            {
                Id = "gain", Type = "vfader", ParameterId = "outGain",
                Asset = "gain_100.png", Asset2x = "gain_100@2x.png", Frames = 100, FrameWidth = 40, FrameHeight = 128,
                Stack = "vertical", Bounds = new ManifestBounds(120, 0, 40, 128),
                ValueMin = 0, ValueMax = 1, ValueDefault = 0.7,
            },
        };

        var m = _svc.BuildManifest(controls, "Synth Skin", "VybeCode", 320, 200, "panel.png");

        m.Name.Should().Be("Synth Skin");
        m.Author.Should().Be("VybeCode");
        m.BaseWidth.Should().Be(320);
        m.BaseHeight.Should().Be(200);
        m.Background.Should().Be("panel.png");
        m.Controls.Should().HaveCount(2);

        var json = _svc.Serialize(m);
        AssertConformsToSchema(json);
        json.Should().Contain("\"background\"").And.Contain("\"author\"").And.Contain("\"valueDefault\"");
    }

    [Fact]
    public void BuildManifest_defaults_a_blank_name_and_omits_blank_author_and_background()
    {
        var controls = new[]
        {
            new ManifestControl
            {
                Id = "k", Type = "knob", ParameterId = "k", Asset = "k.png",
                Frames = 8, FrameWidth = 40, FrameHeight = 40, Bounds = new ManifestBounds(0, 0, 40, 40),
            },
        };

        var m = _svc.BuildManifest(controls, "   ", "   ", 40, 40, "   ");

        m.Name.Should().Be("skin");   // blank name → default
        m.Author.Should().BeNull();
        m.Background.Should().BeNull();
        _svc.Serialize(m).Should().NotContain("author").And.NotContain("background");
    }

    // Mirrors the required/enum/type rules of the JSON Schema in plugin-asset-manifest.
    static void AssertConformsToSchema(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();

        foreach (var key in new[] { "manifestVersion", "name", "baseWidth", "baseHeight", "controls" })
            root.ContainsKey(key).Should().BeTrue($"top-level '{key}' is required");

        root["manifestVersion"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
        root["name"]!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.String);
        root["baseWidth"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
        root["baseHeight"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);

        var controls = root["controls"]!.AsArray();
        controls.Count.Should().BeGreaterThan(0);

        foreach (var node in controls)
        {
            var c = node!.AsObject();
            foreach (var key in new[] { "id", "type", "parameterId", "asset", "frames", "frameWidth", "frameHeight", "bounds" })
                c.ContainsKey(key).Should().BeTrue($"control '{key}' is required");

            new[] { "knob", "vfader", "hslider", "button", "meter" }
                .Should().Contain(c["type"]!.GetValue<string>());
            if (c.ContainsKey("stack"))
                new[] { "vertical", "horizontal" }.Should().Contain(c["stack"]!.GetValue<string>());

            c["frames"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
            c["frameWidth"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
            c["frameHeight"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);

            var bounds = c["bounds"]!.AsObject();
            foreach (var key in new[] { "x", "y", "w", "h" })
                bounds.ContainsKey(key).Should().BeTrue($"bounds '{key}' is required");
        }
    }
}
