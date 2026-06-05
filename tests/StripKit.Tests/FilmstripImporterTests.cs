using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Tests for the importer engine: dimension-based frame-count detection, aspect
/// classification, ambiguity flagging, single-frame extraction, and re-stacking.
/// Dimensions are chosen so the ordered-candidate heuristic lands unambiguously.
/// </summary>
public class FilmstripImporterTests
{
    readonly FilmstripImporter _importer = new();
    readonly SkiaFilmstripRenderer _renderer = new();

    [Theory]
    [InlineData(64, 6500, 100, true, ComponentType.RotaryKnob)]      // ~square frame → knob
    [InlineData(40, 9000, 100, true, ComponentType.VerticalFader)]   // tall frame   → vfader
    [InlineData(9000, 40, 100, false, ComponentType.HorizontalSlider)] // wide frame → hslider
    public void Detect_infers_count_orientation_and_kind(int w, int h, int count, bool vertical, ComponentType kind)
    {
        var det = _importer.Detect(w, h);

        det.FrameCount.Should().Be(count);
        det.Vertical.Should().Be(vertical);
        det.Kind.Should().Be(kind);
        det.LowConfidence.Should().BeFalse();
    }

    [Fact]
    public void Detect_flags_low_confidence_when_a_square_strip_also_divides_by_an_adjacent_count()
    {
        // 63 x 4032: 4032 divides by 64 (chosen) AND 63 (adjacent) and the frame is
        // square — the classic "extra centre frame" ambiguity.
        var det = _importer.Detect(63, 4032);

        det.FrameCount.Should().Be(64);
        det.Kind.Should().Be(ComponentType.RotaryKnob);
        det.LowConfidence.Should().BeTrue();
    }

    [Fact]
    public void ExtractFrame_returns_one_cell_and_frames_differ_across_the_sweep()
    {
        using var src = TestImages.Knob();
        using var strip = _renderer.RenderStrip(KnobStrip(8), src, null, 1.0); // 80 x 640
        var layout = new StripDetection(true, 8, 80, 80, ComponentType.RotaryKnob, false, new[] { 8 });

        using var first = _importer.ExtractFrame(strip, layout, 0);
        using var last = _importer.ExtractFrame(strip, layout, 7);

        first.Width.Should().Be(80);
        first.Height.Should().Be(80);
        PixelsEqual(first, last).Should().BeFalse("frame 0 and 7 are at different rotations");
    }

    [Fact]
    public void Restack_flips_a_vertical_strip_to_horizontal_preserving_frames()
    {
        using var src = TestImages.Knob();
        using var vertical = _renderer.RenderStrip(KnobStrip(8), src, null, 1.0); // 80 x 640
        var vLayout = new StripDetection(true, 8, 80, 80, ComponentType.RotaryKnob, false, new[] { 8 });

        using var horizontal = _importer.Restack(vertical, vLayout, StackDirection.Horizontal);

        horizontal.Width.Should().Be(80 * 8);
        horizontal.Height.Should().Be(80);

        // Frame 3 must survive the re-stack byte-for-byte (lossless 1:1 blit).
        var hLayout = new StripDetection(false, 8, 80, 80, ComponentType.RotaryKnob, false, new[] { 8 });
        using var fromVertical = _importer.ExtractFrame(vertical, vLayout, 3);
        using var fromHorizontal = _importer.ExtractFrame(horizontal, hLayout, 3);
        PixelsEqual(fromVertical, fromHorizontal).Should().BeTrue();
    }

    [Fact]
    public void Resample_retimes_the_frame_count_with_nearest_frame_mapping()
    {
        using var srcArt = TestImages.Knob();
        using var strip = _renderer.RenderStrip(KnobStrip(8), srcArt, null, 1.0); // 80 x 640, 8 frames
        var layout = new StripDetection(true, 8, 80, 80, ComponentType.RotaryKnob, false, new[] { 8 });

        using var resampled = _importer.Resample(strip, layout, 4);

        resampled.Width.Should().Be(80);
        resampled.Height.Should().Be(80 * 4);

        // The (N-1)/(M-1) law lands the endpoints exactly: dest 0 = source 0 (min),
        // dest 3 = source 7 (max). Nearest-frame, so each output frame equals a source frame.
        var dstLayout = new StripDetection(true, 4, 80, 80, ComponentType.RotaryKnob, false, new[] { 4 });
        using var d0 = _importer.ExtractFrame(resampled, dstLayout, 0);
        using var d3 = _importer.ExtractFrame(resampled, dstLayout, 3);
        using var s0 = _importer.ExtractFrame(strip, layout, 0);
        using var s7 = _importer.ExtractFrame(strip, layout, 7);
        PixelsEqual(d0, s0).Should().BeTrue("the first output frame maps to the source min");
        PixelsEqual(d3, s7).Should().BeTrue("the last output frame maps to the source max");
    }

    [Fact]
    public void Resample_to_the_same_count_reproduces_every_frame()
    {
        using var srcArt = TestImages.Knob();
        using var strip = _renderer.RenderStrip(KnobStrip(8), srcArt, null, 1.0);
        var layout = new StripDetection(true, 8, 80, 80, ComponentType.RotaryKnob, false, new[] { 8 });

        using var same = _importer.Resample(strip, layout, 8);

        same.Width.Should().Be(80);
        same.Height.Should().Be(640);
        using var a = _importer.ExtractFrame(strip, layout, 5);
        using var b = _importer.ExtractFrame(same, layout, 5);
        PixelsEqual(a, b).Should().BeTrue("an N->N resample reproduces every frame");
    }

    static FilmstripSettings KnobStrip(int frames) => new()
    {
        ComponentType = ComponentType.RotaryKnob,
        FrameCount = frames, FrameWidth = 80, FrameHeight = 80,
        StartAngleDegrees = -135, EndAngleDegrees = 135,
        Supersample = 1, StackDirection = StackDirection.Vertical,
    };

    static bool PixelsEqual(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
