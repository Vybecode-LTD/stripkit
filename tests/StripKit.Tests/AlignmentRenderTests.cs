using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Proves the alignment fix: an off-centre knob, once centred on its content, spins in
/// place (its content centre stays on the frame centre across frames) instead of orbiting.
/// </summary>
public class AlignmentRenderTests
{
    // An opaque disc sitting off-centre within a transparent square source.
    static SKBitmap OffCenterDisc(int size, float cxFrac, float cyFrac, float rFrac)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        c.DrawCircle(size * cxFrac, size * cyFrac, size * rFrac, p);
        return bmp;
    }

    // Bounding-box centre of the opaque pixels in a rendered frame.
    static (double X, double Y) ContentCenter(SKBitmap bmp, byte threshold = 16)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
            if (bmp.GetPixel(x, y).Alpha > threshold)
            { if (x < minX) minX = x; if (y < minY) minY = y; if (x > maxX) maxX = x; if (y > maxY) maxY = y; }
        return ((minX + maxX + 1) / 2.0, (minY + maxY + 1) / 2.0);
    }

    static FilmstripSettings KnobSettings() => new()
    {
        ComponentType = ComponentType.RotaryKnob,
        FrameCount = 16,
        FrameWidth = 80,
        FrameHeight = 80,
        Supersample = 1,
    };

    [Fact]
    public void Pivoting_on_content_centre_keeps_an_offcenter_knob_spinning_in_place()
    {
        using var src = OffCenterDisc(120, 0.30f, 0.35f, 0.18f);
        var (cx, cy) = ContentAnalysis.DetectContentCenter(src);

        var renderer = new SkiaFilmstripRenderer();
        var settings = KnobSettings();
        settings.SourceCenterX = cx;
        settings.SourceCenterY = cy;

        // The art is NOT moved; pivoting on its content centre means the disc's centre
        // stays put across the whole sweep (spins in place, no orbit).
        using var f0 = renderer.RenderFrame(settings, src, null, 0, 1.0);
        var (x0, y0) = ContentCenter(f0);
        foreach (int i in new[] { 4, 8, 12, 15 })
        {
            using var frame = renderer.RenderFrame(settings, src, null, i, 1.0);
            var (x, y) = ContentCenter(frame);
            x.Should().BeApproximately(x0, 1.5);
            y.Should().BeApproximately(y0, 1.5);
        }
    }

    [Fact]
    public void Without_centering_the_offcenter_knob_orbits()
    {
        // Sanity: the default (0.5, 0.5) centre leaves an off-centre disc orbiting, so
        // the test above is actually exercising the fix and would catch a regression.
        using var src = OffCenterDisc(120, 0.30f, 0.35f, 0.18f);
        var renderer = new SkiaFilmstripRenderer();
        var settings = KnobSettings(); // SourceCenterX/Y left at the default 0.5

        using var f0 = renderer.RenderFrame(settings, src, null, 0, 1.0);
        using var f8 = renderer.RenderFrame(settings, src, null, 8, 1.0);
        var c0 = ContentCenter(f0);
        var c8 = ContentCenter(f8);

        (Math.Abs(c0.X - c8.X) + Math.Abs(c0.Y - c8.Y)).Should().BeGreaterThan(5.0);
    }
}
