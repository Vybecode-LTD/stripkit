using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Golden-image tests that lock the renderer's pixel output so a later refactor
/// cannot silently change what gets drawn. Baselines cover the three component
/// types plus the rotary min/mid/max edge frames and strip stacking.
/// </summary>
public class RendererGoldenTests
{
    readonly SkiaFilmstripRenderer _renderer = new();

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
    public void Knob_min_frame_renders_pointer_at_start_angle()
    {
        using var src = TestImages.Knob();
        using var frame = _renderer.RenderFrame(Knob(), src, null, 0);
        ImageAssert.MatchesBaseline(frame, "knob_default_min");
    }

    [Fact]
    public void Knob_mid_frame_renders_pointer_near_top()
    {
        using var src = TestImages.Knob();
        using var frame = _renderer.RenderFrame(Knob(), src, null, 32);
        ImageAssert.MatchesBaseline(frame, "knob_default_mid");
    }

    [Fact]
    public void Knob_max_frame_renders_pointer_at_end_angle()
    {
        using var src = TestImages.Knob();
        using var frame = _renderer.RenderFrame(Knob(), src, null, 63);
        ImageAssert.MatchesBaseline(frame, "knob_default_max");
    }

    [Fact]
    public void Knob_strip_stacks_eight_frames_vertically()
    {
        using var src = TestImages.Knob();
        using var strip = _renderer.RenderStrip(Knob(8), src, null, 1.0);

        Assert.Equal(80, strip.Width);
        Assert.Equal(80 * 8, strip.Height);
        ImageAssert.MatchesBaseline(strip, "knob_strip8");
    }

    [Fact]
    public void Vertical_fader_mid_frame_centres_the_cap()
    {
        using var src = TestImages.Cap(30, 18);
        var s = new FilmstripSettings
        {
            ComponentType = ComponentType.VerticalFader,
            FrameCount = 64, FrameWidth = 40, FrameHeight = 128,
            EdgeMargin = 4, Supersample = 4,
        };
        using var frame = _renderer.RenderFrame(s, src, null, 32);
        ImageAssert.MatchesBaseline(frame, "vfader_default_mid");
    }

    [Fact]
    public void Horizontal_slider_mid_frame_centres_the_cap()
    {
        using var src = TestImages.Cap(18, 28);
        var s = new FilmstripSettings
        {
            ComponentType = ComponentType.HorizontalSlider,
            FrameCount = 64, FrameWidth = 128, FrameHeight = 32,
            EdgeMargin = 4, Supersample = 4,
        };
        using var frame = _renderer.RenderFrame(s, src, null, 32);
        ImageAssert.MatchesBaseline(frame, "hslider_default_mid");
    }
}
