using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Value-arc / fill-ring rendering. Golden baselines lock the look; pixel-logic locks the
/// behaviour: the lit arc grows from the start angle to the full sweep, a dim track shows
/// the remainder, and the feature is inert (output unchanged) when off or for non-knobs.
/// </summary>
public class ValueArcRenderTests
{
    readonly SkiaFilmstripRenderer _renderer = new();

    // A plain knob, value arc off — the default render contract (270° sweep).
    static FilmstripSettings Knob(int frames = 64) => new()
    {
        ComponentType = ComponentType.RotaryKnob,
        FrameCount = frames,
        FrameWidth = 80,
        FrameHeight = 80,
        StartAngleDegrees = -135,
        EndAngleDegrees = 135,
        Supersample = 1,
    };

    // The same knob with the value arc on. Radius 0.95 (ring at 38px from the 40,40 centre)
    // sits outside the test knob's own accent ring (≈0.40·frame), so the probe points read
    // the arc and not the art beneath it.
    static FilmstripSettings ArcKnob(bool track = true, bool gradient = false, bool glow = false, int frames = 64)
    {
        var s = Knob(frames);
        s.ShowValueArc = true;
        s.ArcRadius = 0.95;
        s.ArcThickness = 4;
        s.ArcTrack = track;
        s.ArcGradient = gradient;
        s.ArcGlow = glow;
        return s;
    }

    // The lit accent is clearly red-dominant (≈ R232/G68/B10); this rejects both the white
    // track and transparency.
    static bool IsArc(SKColor c) => c.Red > 150 && c.Red - c.Green > 80;

    // ---- golden-image baselines ----

    [Fact]
    public void Arc_knob_min() // frame 0 — nothing filled
    {
        using var src = TestImages.Knob();
        using var frame = _renderer.RenderFrame(ArcKnob(), src, null, 0);
        ImageAssert.MatchesBaseline(frame, "arc_knob_min");
    }

    [Fact]
    public void Arc_knob_mid()
    {
        using var src = TestImages.Knob();
        using var frame = _renderer.RenderFrame(ArcKnob(), src, null, 32);
        ImageAssert.MatchesBaseline(frame, "arc_knob_mid");
    }

    [Fact]
    public void Arc_knob_max() // last frame — full sweep filled
    {
        using var src = TestImages.Knob();
        using var frame = _renderer.RenderFrame(ArcKnob(), src, null, 63);
        ImageAssert.MatchesBaseline(frame, "arc_knob_max");
    }

    [Fact]
    public void Arc_knob_gradient_glow_mid()
    {
        using var src = TestImages.Knob();
        var s = ArcKnob(gradient: true, glow: true);
        s.Supersample = 4;   // exercise the full-quality path for the gradient + glow look
        using var frame = _renderer.RenderFrame(s, src, null, 40);
        ImageAssert.MatchesBaseline(frame, "arc_knob_gradient_glow_mid");
    }

    // ---- pixel-logic ----

    [Fact]
    public void Arc_off_leaves_the_outer_ring_empty()
    {
        using var src = TestImages.Knob();
        using var frame = _renderer.RenderFrame(Knob(), src, null, 32);
        // The 0.95 ring at 9 o'clock (≈ x=2) is well outside the knob art.
        IsArc(frame.GetPixel(2, 40)).Should().BeFalse("no arc is drawn when ShowValueArc is off");
    }

    [Fact]
    public void Arc_lit_sweep_grows_from_start_to_full()
    {
        using var src = TestImages.Knob();
        var s = ArcKnob(track: false);   // isolate the lit fill from the track

        using var f0 = _renderer.RenderFrame(s, src, null, 0);
        using var fMid = _renderer.RenderFrame(s, src, null, 32);
        using var fMax = _renderer.RenderFrame(s, src, null, 63);

        // Ring probes (centre 40,40, radius 38): left / right / bottom. A −135°→+135° sweep
        // climbs the left side first, reaches the right side only near the end, and never
        // enters the bottom wedge.
        IsArc(f0.GetPixel(2, 40)).Should().BeFalse("almost nothing is lit at frame 0");
        IsArc(fMid.GetPixel(2, 40)).Should().BeTrue("the left of the ring is lit by mid-travel");
        IsArc(fMid.GetPixel(78, 40)).Should().BeFalse("the right is not reached at mid");
        IsArc(fMax.GetPixel(78, 40)).Should().BeTrue("the right of the ring is lit at maximum");
        IsArc(fMax.GetPixel(40, 78)).Should().BeFalse("the bottom wedge is outside the sweep");
    }

    [Fact]
    public void Arc_track_fills_the_unlit_remainder()
    {
        using var src = TestImages.Knob();
        using var fMid = _renderer.RenderFrame(ArcKnob(track: true), src, null, 32);

        // The right side is unlit at mid, but the dim track is drawn across the full sweep.
        var right = fMid.GetPixel(78, 40);
        IsArc(right).Should().BeFalse("the lit fill has not reached the right at mid");
        right.Alpha.Should().BeGreaterThan(0, "the dim track covers the whole sweep");
    }

    [Fact]
    public void Arc_is_ignored_for_non_knob_components()
    {
        using var cap = TestImages.Cap(20, 14);
        var withArc = new FilmstripSettings
        {
            ComponentType = ComponentType.VerticalFader,
            FrameCount = 64, FrameWidth = 40, FrameHeight = 128, EdgeMargin = 4, Supersample = 1,
            ShowValueArc = true, ArcTrack = true,
        };
        var without = withArc.Clone();
        without.ShowValueArc = false;

        using var a = _renderer.RenderFrame(withArc, cap, null, 32);
        using var b = _renderer.RenderFrame(without, cap, null, 32);
        ImagesEqual(a, b).Should().BeTrue("the value arc is a rotary-only overlay");
    }

    static bool ImagesEqual(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
