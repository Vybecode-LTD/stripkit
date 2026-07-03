using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Parameter-law frame mapping (<see cref="FilmstripSettings.MapT"/>): a frame's linear strip
/// position can be remapped through a skew or logarithmic curve before it drives rotation angle,
/// meter fill, or layer pivot — so the sweep matches a plugin's actual parameter law. Linear is
/// the default and must be a complete no-op (byte-identical existing renders); the math for the
/// other two curves is covered directly, plus one golden image locking the skewed knob sweep.
/// </summary>
public class ParameterLawMappingTests
{
    readonly SkiaFilmstripRenderer _renderer = new();

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Linear_curve_returns_the_input_completely_unchanged(double t)
    {
        var s = new FilmstripSettings(); // MappingCurve defaults to Linear
        s.MapT(t).Should().Be(t);
    }

    [Theory]
    [InlineData(0.5, 2.0, 0.25)]     // t^2
    [InlineData(0.25, 0.5, 0.5)]     // sqrt(t)
    [InlineData(0.0, 2.0, 0.0)]
    [InlineData(1.0, 2.0, 1.0)]
    public void Skew_curve_matches_the_power_law(double t, double skew, double expected)
    {
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Skew, MappingSkew = skew };
        s.MapT(t).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Skew_of_one_is_equivalent_to_linear()
    {
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Skew, MappingSkew = 1.0 };
        s.MapT(0.37).Should().BeApproximately(0.37, 1e-9);
    }

    [Fact]
    public void Skew_non_positive_falls_back_to_one_instead_of_producing_nan_or_inf()
    {
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Skew, MappingSkew = 0.0 };
        s.MapT(0.5).Should().BeApproximately(0.5, 1e-9);
    }

    [Theory]
    [InlineData(0.0, 9.0, 0.0)]
    [InlineData(1.0, 9.0, 1.0)]
    public void Logarithmic_curve_hits_the_exact_endpoints(double t, double logBase, double expected)
    {
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Logarithmic, MappingLogBase = logBase };
        s.MapT(t).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Logarithmic_curve_matches_the_documented_formula()
    {
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Logarithmic, MappingLogBase = 9.0 };
        double expected = Math.Log(1 + 0.5 * 8) / Math.Log(9);   // log(5)/log(9)
        s.MapT(0.5).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Logarithmic_curve_is_concave_and_front_loads_resolution_at_the_low_end()
    {
        // log(1 + t*(k-1))/log(k) is concave (steepest near t=0), so it sits above the
        // linear diagonal everywhere in between — the low end gets more visual sweep per
        // unit of parameter value, matching a log-taper pot's perceptual feel.
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Logarithmic, MappingLogBase = 9.0 };
        s.MapT(0.5).Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void LogBase_at_or_below_one_falls_back_to_nine_instead_of_dividing_by_zero()
    {
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Logarithmic, MappingLogBase = 1.0 };
        var fallback = new FilmstripSettings { MappingCurve = FrameMappingCurve.Logarithmic, MappingLogBase = 9.0 };
        s.MapT(0.5).Should().BeApproximately(fallback.MapT(0.5), 1e-9);
    }

    [Fact]
    public void Clamps_out_of_range_input_for_non_linear_curves()
    {
        var s = new FilmstripSettings { MappingCurve = FrameMappingCurve.Skew, MappingSkew = 2.0 };
        s.MapT(-0.5).Should().Be(0.0);
        s.MapT(1.5).Should().Be(1.0);
    }

    // ---- renderer integration ----

    static FilmstripSettings Knob(int frames = 64) => new()
    {
        ComponentType = ComponentType.RotaryKnob,
        FrameCount = frames,
        FrameWidth = 80,
        FrameHeight = 80,
        StartAngleDegrees = -135,
        EndAngleDegrees = 135,
        Supersample = 4,
        StackDirection = StackDirection.Vertical,
    };

    [Fact]
    public void Skewed_knob_mid_frame_renders_a_different_angle_than_linear()
    {
        using var src = TestImages.Knob();

        var linear = Knob();
        using var linearFrame = _renderer.RenderFrame(linear, src, null, 32);

        var skewed = Knob();
        skewed.MappingCurve = FrameMappingCurve.Skew;
        skewed.MappingSkew = 3.0;
        using var skewedFrame = _renderer.RenderFrame(skewed, src, null, 32);

        ImageAssert.MatchesBaseline(skewedFrame, "knob_skew_mid");
    }

    [Fact]
    public void Skewed_knob_endpoints_match_linear_endpoints_exactly()
    {
        // t=0 and t=1 are fixed points of any curve gated through Math.Clamp/Pow/Log at 0/1 —
        // the min and max frames must land on the exact start/end angle regardless of curve.
        using var src = TestImages.Knob();

        var skewed = Knob();
        skewed.MappingCurve = FrameMappingCurve.Skew;
        skewed.MappingSkew = 3.0;

        using var minFrame = _renderer.RenderFrame(skewed, src, null, 0);
        ImageAssert.MatchesBaseline(minFrame, "knob_default_min");

        using var maxFrame = _renderer.RenderFrame(skewed, src, null, 63);
        ImageAssert.MatchesBaseline(maxFrame, "knob_default_max");
    }

    [Fact]
    public void Logarithmic_meter_fill_differs_from_linear_at_the_same_frame()
    {
        var linear = new FilmstripSettings
        {
            ComponentType = ComponentType.Meter,
            FrameCount = 64,
            FrameWidth = 48,
            FrameHeight = 160,
            SegmentCount = 12,
            ContinuousFill = true,
            Supersample = 1,
        };
        var log = linear.Clone();
        log.MappingCurve = FrameMappingCurve.Logarithmic;
        log.MappingLogBase = 9.0;

        using var linearFrame = _renderer.RenderFrame(linear, null, null, 32);
        using var logFrame = _renderer.RenderFrame(log, null, null, 32);

        // Same frame index, same everything else — only the fill fraction should differ.
        bool anyPixelDiffers = false;
        for (int y = 0; y < linearFrame.Height && !anyPixelDiffers; y++)
        for (int x = 0; x < linearFrame.Width && !anyPixelDiffers; x++)
            if (linearFrame.GetPixel(x, y) != logFrame.GetPixel(x, y))
                anyPixelDiffers = true;

        anyPixelDiffers.Should().BeTrue();
    }
}
