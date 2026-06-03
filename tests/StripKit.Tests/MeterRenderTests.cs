using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Meter rendering: procedural segment fill (all four directions, discrete) and the
/// layered on/off-art reveal. Golden baselines lock the look; pixel-logic tests lock
/// the fill direction and the layered behaviour.
/// </summary>
public class MeterRenderTests
{
    readonly SkiaFilmstripRenderer _renderer = new();

    static FilmstripSettings Meter(MeterFillDirection dir, int frames = 64)
    {
        bool vertical = dir is MeterFillDirection.Up or MeterFillDirection.Down;
        return new FilmstripSettings
        {
            ComponentType = ComponentType.Meter,
            FrameCount = frames,
            FrameWidth = vertical ? 48 : 160,
            FrameHeight = vertical ? 160 : 48,
            SegmentCount = 12,
            FillDirection = dir,
            ContinuousFill = false,
            SegmentGap = 3,
            OnColorArgb = 0xFFE8440A,
            OffColorArgb = 0xFF2A2A2A,
            Supersample = 1,
        };
    }

    static SKBitmap Solid(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(color);
        return bmp;
    }

    // ---- golden-image baselines ----

    [Fact]
    public void Procedural_up_empty() // frame 0
    {
        using var frame = _renderer.RenderFrame(Meter(MeterFillDirection.Up), null, null, 0);
        ImageAssert.MatchesBaseline(frame, "meter_proc_up_empty");
    }

    [Fact]
    public void Procedural_up_mid()
    {
        using var frame = _renderer.RenderFrame(Meter(MeterFillDirection.Up), null, null, 32);
        ImageAssert.MatchesBaseline(frame, "meter_proc_up_mid");
    }

    [Fact]
    public void Procedural_up_full() // last frame
    {
        using var frame = _renderer.RenderFrame(Meter(MeterFillDirection.Up), null, null, 63);
        ImageAssert.MatchesBaseline(frame, "meter_proc_up_full");
    }

    [Fact]
    public void Procedural_left_to_right_mid()
    {
        using var frame = _renderer.RenderFrame(Meter(MeterFillDirection.LeftToRight), null, null, 32);
        ImageAssert.MatchesBaseline(frame, "meter_proc_lr_mid");
    }

    [Fact]
    public void Layered_mid_reveals_on_over_off()
    {
        using var on = Solid(48, 160, new SKColor(0xE8, 0x44, 0x0A));   // on art
        using var off = Solid(48, 160, new SKColor(0x22, 0x22, 0x22));  // off art (background)
        using var frame = _renderer.RenderFrame(Meter(MeterFillDirection.Up), on, off, 32);
        ImageAssert.MatchesBaseline(frame, "meter_layered_up_mid");
    }

    // ---- pixel-logic ----

    static bool IsOn(SKColor c) => c.Red > 150;   // accent on ≈ R 232
    static bool IsOff(SKColor c) => c.Red < 100;  // dim off ≈ R 42

    [Fact]
    public void Procedural_up_fills_from_the_bottom()
    {
        var s = Meter(MeterFillDirection.Up);
        int xMid = s.FrameWidth / 2;

        using var empty = _renderer.RenderFrame(s, null, null, 0);
        IsOff(empty.GetPixel(xMid, s.FrameHeight - 6)).Should().BeTrue("nothing is lit at frame 0");

        using var full = _renderer.RenderFrame(s, null, null, 63);
        IsOn(full.GetPixel(xMid, s.FrameHeight - 6)).Should().BeTrue("everything is lit at the last frame");

        using var mid = _renderer.RenderFrame(s, null, null, 32);
        IsOn(mid.GetPixel(xMid, s.FrameHeight - 6)).Should().BeTrue("the bottom is lit at mid");
        IsOff(mid.GetPixel(xMid, 6)).Should().BeTrue("the top is unlit at mid");
    }

    [Fact]
    public void Procedural_down_fills_from_the_top()
    {
        var s = Meter(MeterFillDirection.Down);
        int xMid = s.FrameWidth / 2;
        using var mid = _renderer.RenderFrame(s, null, null, 32);

        IsOn(mid.GetPixel(xMid, 6)).Should().BeTrue("the top is lit when filling downward");
        IsOff(mid.GetPixel(xMid, s.FrameHeight - 6)).Should().BeTrue("the bottom is unlit at mid");
    }

    [Fact]
    public void Procedural_left_to_right_fills_from_the_left()
    {
        var s = Meter(MeterFillDirection.LeftToRight);
        int yMid = s.FrameHeight / 2;
        using var mid = _renderer.RenderFrame(s, null, null, 32);

        IsOn(mid.GetPixel(6, yMid)).Should().BeTrue("the left is lit when filling rightward");
        IsOff(mid.GetPixel(s.FrameWidth - 6, yMid)).Should().BeTrue("the right is unlit at mid");
    }

    [Fact]
    public void Layered_reveals_on_art_up_to_the_fill()
    {
        var s = Meter(MeterFillDirection.Up);
        using var on = Solid(48, 160, new SKColor(0xE8, 0x44, 0x0A));
        using var off = Solid(48, 160, new SKColor(0x22, 0x22, 0x22));

        using var empty = _renderer.RenderFrame(s, on, off, 0);
        empty.GetPixel(24, 150).Red.Should().BeLessThan(80, "frame 0 shows the off art at the bottom");

        using var full = _renderer.RenderFrame(s, on, off, 63);
        IsOn(full.GetPixel(24, 150)).Should().BeTrue("the last frame reveals the on art");
    }
}
