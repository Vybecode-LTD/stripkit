using System.Text.Json;
using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// End-to-end through the REAL <see cref="KitBuilder"/> + renderer + exporter + importer + manifest:
/// generate-style layered SVGs on disk → one-click kit build → filmstrip PNGs + a multi-control
/// skin.json. This is the "Build kit" endgame the Generate tab drives; it exercises all four per-type
/// render paths (layered knob, state-frame button/toggle, flattened fader/slider, meter off/on).
/// </summary>
public class KitBuilderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"stripkit_kit_{Guid.NewGuid():N}");

    public KitBuilderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static KitBuilder Builder() =>
        new(new LayeredImportService(), new SkiaFilmstripRenderer(), new ExportService(), new ManifestService());

    private string WriteSvg(string name, string svg)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, svg);
        return path;
    }

    // A layered knob: a static body group + a "pointer" group (the name → Rotate).
    private const string KnobSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\" viewBox=\"0 0 200 200\">" +
        "<g id=\"body\"><circle cx=\"100\" cy=\"100\" r=\"80\" fill=\"#333\"/></g>" +
        "<g id=\"pointer\"><rect x=\"96\" y=\"24\" width=\"8\" height=\"76\" fill=\"#e8440a\"/></g>" +
        "</svg>";

    // Discrete on/off state art (the group names → Frame).
    private const string ButtonSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"120\" viewBox=\"0 0 120 120\">" +
        "<g id=\"off\"><rect x=\"10\" y=\"10\" width=\"100\" height=\"100\" rx=\"12\" fill=\"#222\"/></g>" +
        "<g id=\"on\"><rect x=\"10\" y=\"10\" width=\"100\" height=\"100\" rx=\"12\" fill=\"#e8440a\"/></g>" +
        "</svg>";

    // A single-group cap the linear renderer translates.
    private const string FaderSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" height=\"120\" viewBox=\"0 0 48 120\">" +
        "<g id=\"body\"><rect x=\"8\" y=\"40\" width=\"32\" height=\"40\" rx=\"6\" fill=\"#888\"/></g>" +
        "</svg>";

    // A tall meter off/on pair (unlit background + lit source revealed up to value).
    private const string MeterSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"160\" viewBox=\"0 0 40 160\">" +
        "<g id=\"off\"><rect x=\"8\" y=\"8\" width=\"24\" height=\"144\" fill=\"#222\"/></g>" +
        "<g id=\"on\"><rect x=\"8\" y=\"8\" width=\"24\" height=\"144\" fill=\"#38e06a\"/></g>" +
        "</svg>";

    [Fact]
    public async Task Builds_a_mixed_kit_of_filmstrips_and_a_skin_json()
    {
        var sources = new[]
        {
            new KitControlSource(ComponentType.RotaryKnob, WriteSvg("knob.svg", KnobSvg)),
            new KitControlSource(ComponentType.Button, WriteSvg("button.svg", ButtonSvg)),
            new KitControlSource(ComponentType.VerticalFader, WriteSvg("fader.svg", FaderSvg)),
            new KitControlSource(ComponentType.Meter, WriteSvg("meter.svg", MeterSvg)),
        };
        var outDir = Path.Combine(_dir, "out");
        var options = new KitBuildOptions { OutputDirectory = outDir, KitName = "Modern kit", FilePrefix = "modern", FrameCount = 32 };

        var result = await Builder().BuildAsync(sources, options);

        result.SuccessCount.Should().Be(4);
        result.Controls.Should().OnlyContain(c => c.Success);
        result.SkinJsonPath.Should().NotBeNull();
        File.Exists(result.SkinJsonPath!).Should().BeTrue();

        // Every control produced a 1x + @2x PNG under the prefix.
        foreach (var slug in new[] { "knob", "button", "fader", "meter" })
        {
            File.Exists(Path.Combine(outDir, $"modern-{slug}.png")).Should().BeTrue($"{slug} 1x asset");
            File.Exists(Path.Combine(outDir, $"modern-{slug}@2x.png")).Should().BeTrue($"{slug} @2x asset");
        }
    }

    [Fact]
    public async Task Skin_json_binds_each_control_with_the_right_type_frames_and_a_non_overlapping_row()
    {
        var sources = new[]
        {
            new KitControlSource(ComponentType.RotaryKnob, WriteSvg("k.svg", KnobSvg)),
            new KitControlSource(ComponentType.Button, WriteSvg("b.svg", ButtonSvg)),
            new KitControlSource(ComponentType.Meter, WriteSvg("m.svg", MeterSvg)),
        };
        var outDir = Path.Combine(_dir, "skin");
        var options = new KitBuildOptions { OutputDirectory = outDir, FilePrefix = "kit", FrameCount = 48, ExportAt2x = false };

        var result = await Builder().BuildAsync(sources, options);
        result.SkinJsonPath.Should().NotBeNull();

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(result.SkinJsonPath!));
        var root = doc.RootElement;
        root.GetProperty("manifestVersion").GetInt32().Should().Be(1);

        var controls = root.GetProperty("controls").EnumerateArray().ToList();
        controls.Should().HaveCount(3);
        controls[0].GetProperty("type").GetString().Should().Be("knob");
        controls[1].GetProperty("type").GetString().Should().Be("button");
        controls[2].GetProperty("type").GetString().Should().Be("meter");

        // Continuously-swept controls honour the requested frame count; a button uses its state count (2).
        controls[0].GetProperty("frames").GetInt32().Should().Be(48);
        controls[1].GetProperty("frames").GetInt32().Should().Be(2);
        controls[2].GetProperty("frames").GetInt32().Should().Be(48);

        // No @2x was requested → the asset2x key is omitted (WhenWritingNull).
        controls[0].TryGetProperty("asset2x", out _).Should().BeFalse();

        // Laid out left-to-right: each control's X sits past the previous control's right edge.
        double prevRight = 0;
        foreach (var c in controls)
        {
            var b = c.GetProperty("bounds");
            double x = b.GetProperty("x").GetDouble();
            double w = b.GetProperty("w").GetDouble();
            x.Should().BeGreaterThanOrEqualTo(prevRight, "controls must not overlap in the row");
            prevRight = x + w;
        }
    }

    [Fact]
    public async Task Knob_frame_is_squared_to_the_source_and_the_strip_stacks_vertically()
    {
        var sources = new[] { new KitControlSource(ComponentType.RotaryKnob, WriteSvg("knob.svg", KnobSvg)) };
        var outDir = Path.Combine(_dir, "knob");
        var options = new KitBuildOptions { OutputDirectory = outDir, FilePrefix = "k", FrameCount = 16, ExportAt2x = false };

        var result = await Builder().BuildAsync(sources, options);
        result.SuccessCount.Should().Be(1);

        using var codec = SKCodec.Create(Path.Combine(outDir, "k-knob.png"));
        codec.Should().NotBeNull();
        // 200x200 SVG → square 200 frame, 16 frames stacked vertically.
        codec!.Info.Width.Should().Be(200);
        codec.Info.Height.Should().Be(200 * 16);
    }

    [Fact]
    public async Task A_bad_source_fails_only_itself_and_the_rest_of_the_kit_still_builds()
    {
        var sources = new[]
        {
            new KitControlSource(ComponentType.RotaryKnob, WriteSvg("ok.svg", KnobSvg)),
            new KitControlSource(ComponentType.Button, Path.Combine(_dir, "does-not-exist.svg")),
        };
        var outDir = Path.Combine(_dir, "partial");
        var options = new KitBuildOptions { OutputDirectory = outDir, FilePrefix = "p", ExportAt2x = false };

        var result = await Builder().BuildAsync(sources, options);

        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.Controls.Single(c => c.Type == ComponentType.Button).Success.Should().BeFalse();
        result.Controls.Single(c => c.Type == ComponentType.Button).Error.Should().NotBeNullOrEmpty();
        // The good control still wrote, and a skin.json was still produced from the survivors.
        File.Exists(Path.Combine(outDir, "p-knob.png")).Should().BeTrue();
        result.SkinJsonPath.Should().NotBeNull();
    }

    [Fact]
    public async Task A_knob_with_no_rotating_layer_still_builds_but_warns()
    {
        // Both groups are static ("base"/"ring" → no Rotate hint).
        const string flatKnob =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\" viewBox=\"0 0 100 100\">" +
            "<g id=\"base\"><circle cx=\"50\" cy=\"50\" r=\"40\" fill=\"#333\"/></g>" +
            "<g id=\"ring\"><circle cx=\"50\" cy=\"50\" r=\"44\" fill=\"none\" stroke=\"#888\" stroke-width=\"3\"/></g>" +
            "</svg>";
        var sources = new[] { new KitControlSource(ComponentType.RotaryKnob, WriteSvg("flat.svg", flatKnob)) };
        var options = new KitBuildOptions { OutputDirectory = Path.Combine(_dir, "warn"), FilePrefix = "w", ExportAt2x = false };

        var result = await Builder().BuildAsync(sources, options);

        var knob = result.Controls.Single();
        knob.Success.Should().BeTrue("a static knob still renders a valid — if unanimated — strip");
        knob.Warning.Should().Contain("rotating");
    }

    [Fact]
    public async Task No_successful_controls_writes_no_skin_json()
    {
        var sources = new[] { new KitControlSource(ComponentType.RotaryKnob, Path.Combine(_dir, "nope.svg")) };
        var options = new KitBuildOptions { OutputDirectory = Path.Combine(_dir, "empty"), FilePrefix = "e" };

        var result = await Builder().BuildAsync(sources, options);

        result.SuccessCount.Should().Be(0);
        result.SkinJsonPath.Should().BeNull();
    }
}
