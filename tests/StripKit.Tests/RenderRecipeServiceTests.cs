using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The render-recipe exporter (P2 of the path-tracing pipeline). The whole point is that the
/// recipe's per-frame angles match the renderer's law exactly — <c>angle_i = start +
/// (end − start)·i/(N−1)</c> — so an offline path-traced sequence stacks back cleanly.
/// </summary>
public class RenderRecipeServiceTests
{
    private static RenderRecipeRequest Knob(int n = 64, double start = -135, double end = 135) =>
        new(ComponentType.RotaryKnob, n, start, end, 80, 80, "my knob");

    private readonly RenderRecipeService _svc = new();

    [Fact]
    public void Frame_table_has_N_rows_with_the_endpoints_on_the_extremes()
    {
        var rows = RenderRecipeService.BuildFrameTable(Knob(64));

        rows.Should().HaveCount(64);
        rows[0].Frame.Should().Be(0);
        rows[0].Value.Should().Be(0.0);
        rows[0].AngleDegrees.Should().Be(-135.0);
        rows[^1].Value.Should().BeApproximately(1.0, 1e-12);
        rows[^1].AngleDegrees.Should().BeApproximately(135.0, 1e-9);
    }

    [Fact]
    public void Frame_table_uses_the_deliberate_N_minus_1_divisor()
    {
        var rows = RenderRecipeService.BuildFrameTable(Knob(64));

        // Frame 1 of 64 is 1/63 of the way, NOT 1/64 — the divisor that lands the last frame on max.
        rows[1].Value.Should().BeApproximately(1.0 / 63.0, 1e-12);
        rows[1].AngleDegrees.Should().BeApproximately(-135.0 + 270.0 * (1.0 / 63.0), 1e-9);
    }

    [Fact]
    public void Frame_table_midpoint_is_the_geometric_centre()
    {
        var rows = RenderRecipeService.BuildFrameTable(Knob(65));   // odd count → an exact middle frame

        rows[32].Value.Should().BeApproximately(0.5, 1e-12);
        rows[32].AngleDegrees.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Non_rotary_keeps_the_angle_at_zero_but_the_value_still_ramps()
    {
        var req = new RenderRecipeRequest(ComponentType.VerticalFader, 4, -135, 135, 60, 200, "fader");

        var rows = RenderRecipeService.BuildFrameTable(req);

        rows.Select(r => r.AngleDegrees).Should().OnlyContain(a => a == 0.0);
        rows.Select(r => Math.Round(r.Value, 6))
            .Should().Equal(0.0, Math.Round(1.0 / 3.0, 6), Math.Round(2.0 / 3.0, 6), 1.0);
    }

    [Fact]
    public void A_single_frame_does_not_divide_by_zero()
    {
        var rows = RenderRecipeService.BuildFrameTable(Knob(1));

        rows.Should().ContainSingle();
        rows[0].Value.Should().Be(0.0);
        rows[0].AngleDegrees.Should().Be(-135.0);
    }

    [Fact]
    public void Csv_has_a_header_and_one_row_per_frame()
    {
        var csv = _svc.Generate(RenderRecipeTarget.Csv, Knob(32));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("frame,value,angle_deg");
        lines.Should().HaveCount(1 + 32);                 // header + 32 data rows
        lines[1].Should().Be("0,0.000000,-135.0000");
        lines[^1].Should().StartWith("31,1.000000,135.0000");
    }

    [Fact]
    public void Csv_numbers_are_invariant_culture_even_under_a_comma_decimal_locale()
    {
        var prior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");   // uses ',' as the decimal sep
            var csv = _svc.Generate(RenderRecipeTarget.Csv, Knob(8));

            csv.Should().Contain("0.000000").And.NotContain("0,000000");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prior;
        }
    }

    [Fact]
    public void Blender_script_sets_transparent_film_the_frame_range_and_the_law()
    {
        var py = _svc.Generate(RenderRecipeTarget.Blender, Knob(64));

        py.Should().Contain("film_transparent = True");
        py.Should().Contain("color_mode  = \"RGBA\"");
        py.Should().Contain("N         = 64");
        py.Should().Contain("START_DEG = -135");
        py.Should().Contain("END_DEG   = 135");
        py.Should().Contain("i/(N-1)");                      // the law is documented in-script
        py.Should().Contain("bpy.ops.render.render(animation=True)");
    }

    [Fact]
    public void Blender_script_bakes_rotation_only_for_a_rotary_knob()
    {
        var knob = _svc.Generate(RenderRecipeTarget.Blender, Knob(64));
        var fader = _svc.Generate(RenderRecipeTarget.Blender,
            new RenderRecipeRequest(ComponentType.VerticalFader, 64, -135, 135, 60, 200, "fader"));

        knob.Should().Contain("IS_ROTARY = True").And.Contain("rotation_euler");
        fader.Should().Contain("IS_ROTARY = False");
    }

    [Fact]
    public void Json_parses_with_metadata_and_one_entry_per_frame()
    {
        var json = _svc.Generate(RenderRecipeTarget.Json, Knob(64));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("componentType").GetString().Should().Be("RotaryKnob");
        root.GetProperty("frameCount").GetInt32().Should().Be(64);
        root.GetProperty("startAngleDegrees").GetDouble().Should().Be(-135.0);
        var frames = root.GetProperty("frames");
        frames.GetArrayLength().Should().Be(64);
        frames[63].GetProperty("angleDeg").GetDouble().Should().BeApproximately(135.0, 1e-9);
    }

    [Theory]
    [InlineData(RenderRecipeTarget.Blender, ".blender.py")]
    [InlineData(RenderRecipeTarget.Csv, ".frames.csv")]
    [InlineData(RenderRecipeTarget.Json, ".frames.json")]
    public void FileName_uses_the_right_extension_and_sanitizes_the_id(RenderRecipeTarget target, string ext)
    {
        var name = _svc.FileName(target, "my knob/2");

        name.Should().EndWith(ext);
        name.Should().NotContainAny("/", " ");           // path-separator + space scrubbed
    }

    [Fact]
    public async Task SaveAsync_writes_the_recipe_to_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stripkit_recipe_" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = await _svc.SaveAsync(RenderRecipeTarget.Csv, Knob(16), dir);

            File.Exists(path).Should().BeTrue();
            path.Should().EndWith(".frames.csv");
            (await File.ReadAllTextAsync(path)).Should().Be(_svc.Generate(RenderRecipeTarget.Csv, Knob(16)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
