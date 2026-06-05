using FluentAssertions;
using SkiaSharp;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Auto-pointer extraction (★ #3 step 2): splitting a flat knob into a symmetric base body + the
/// indicator (the rotating pointer) via the radial-symmetry residual. `TestImages.Knob` is itself a
/// flat knob (dark disc + accent ring + a white indicator at 12 o'clock), so it is the natural input.
/// </summary>
public class PointerExtractorTests
{
    static bool IsOpaque(SKColor c) => c.Alpha > 128;
    static bool IsWhitish(SKColor c) => c.Red > 180 && c.Green > 180 && c.Blue > 180;

    [Fact]
    public void Extract_splits_a_flat_knob_into_a_symmetric_body_and_the_indicator()
    {
        using var knob = TestImages.Knob(100);
        var result = PointerExtractor.Extract(knob, 0.5, 0.5);

        result.Should().NotBeNull();
        using var body = result!.BaseLayer;
        using var pointer = result.PointerLayer;
        body.Width.Should().Be(100);
        pointer.Width.Should().Be(100);

        // The white indicator runs up from the centre; (50,25) sits on it.
        IsOpaque(pointer.GetPixel(50, 25)).Should().BeTrue("the indicator goes into the pointer layer");
        IsWhitish(pointer.GetPixel(50, 25)).Should().BeTrue("the pointer keeps the indicator's colour");

        // A body-only region (the bottom of the disc) yields no pointer.
        pointer.GetPixel(50, 75).Alpha.Should().BeLessThan(40, "a body-only region has no residual");

        // The base has the indicator erased — where the white was, the base is the dark body, matching
        // the symmetric opposite point at the same radius.
        IsWhitish(body.GetPixel(50, 25)).Should().BeFalse("the indicator is erased from the base");
        int atIndicator = body.GetPixel(50, 25).Red;
        int opposite = body.GetPixel(50, 75).Red;     // same radius (25), no indicator
        Math.Abs(atIndicator - opposite).Should().BeLessThan(40, "the base is rotationally symmetric");

        result.LowConfidence.Should().BeFalse("a clean single indicator on a round body is high-confidence");
    }

    [Fact]
    public void Extract_returns_null_for_a_missing_image()
    {
        PointerExtractor.Extract(null, 0.5, 0.5).Should().BeNull();
    }

    [Fact]
    public void A_plain_symmetric_disc_yields_an_essentially_empty_pointer()
    {
        using var disc = Disc(100);
        var result = PointerExtractor.Extract(disc, 0.5, 0.5);

        result.Should().NotBeNull();
        using var pointer = result!.PointerLayer;
        int opaque = 0;
        for (int y = 0; y < 100; y++)
        for (int x = 0; x < 100; x++)
            if (pointer.GetPixel(x, y).Alpha > 40) opaque++;
        opaque.Should().BeLessThan(80, "a rotationally symmetric disc has nothing to extract");
    }

    static SKBitmap Disc(int size)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true, Style = SKPaintStyle.Fill };
        c.DrawCircle(size / 2f, size / 2f, size * 0.4f, p);
        return bmp;
    }
}
