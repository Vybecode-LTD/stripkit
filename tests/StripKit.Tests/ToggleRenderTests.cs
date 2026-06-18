using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Toggle rendering: a Toggle is its own control type but renders exactly like a 2-state Button —
/// each Frame-tagged layer shows only on its matching frame index (frame 0 = off, frame 1 = on),
/// with no position transform. This locks the renderer's Button/Toggle state-frame path for Toggle.
/// </summary>
public class ToggleRenderTests
{
    readonly SkiaFilmstripRenderer _renderer = new();

    static SKBitmap Solid(SKColor c, int size = 40)
    {
        var b = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(b);
        canvas.Clear(c);
        return b;
    }

    static FilmstripSettings ToggleSettings() => new()
    {
        ComponentType = ComponentType.Toggle,
        FrameCount = 2,
        FrameWidth = 40,
        FrameHeight = 40,
        Supersample = 1,
        Layers = { new RenderLayer { Behavior = LayerBehavior.Frame }, new RenderLayer { Behavior = LayerBehavior.Frame } },
    };

    static SKColor Center(SKBitmap b) => b.GetPixel(b.Width / 2, b.Height / 2);

    [Fact]
    public void Frame_0_shows_the_off_state_and_frame_1_shows_the_on_state()
    {
        using var off = Solid(new SKColor(0x22, 0x22, 0x22));
        using var on = Solid(new SKColor(0x00, 0xff, 0x00));
        var art = new[] { off, on };

        using var f0 = _renderer.RenderFrame(ToggleSettings(), null, null, 0, 1.0, art);
        using var f1 = _renderer.RenderFrame(ToggleSettings(), null, null, 1, 1.0, art);

        Center(f0).Green.Should().BeLessThan(80, "frame 0 = the off (dark) state");
        Center(f1).Green.Should().BeGreaterThan(180, "frame 1 = the on (lit) state");
    }
}
