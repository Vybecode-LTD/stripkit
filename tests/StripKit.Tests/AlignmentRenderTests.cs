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
    public void Auto_centered_offcenter_knob_stays_centered_across_frames()
    {
        using var src = OffCenterDisc(120, 0.30f, 0.35f, 0.18f);
        var (cx, cy) = ContentAnalysis.DetectContentCenter(src);

        var renderer = new SkiaFilmstripRenderer();
        var settings = KnobSettings();
        settings.SourceCenterX = cx;
        settings.SourceCenterY = cy;

        foreach (int i in new[] { 0, 5, 10, 15 })
        {
            using var frame = renderer.RenderFrame(settings, src, null, i, 1.0);
            var (fx, fy) = ContentCenter(frame);
            fx.Should().BeApproximately(40, 2.5); // frame centre X
            fy.Should().BeApproximately(40, 2.5); // frame centre Y
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
