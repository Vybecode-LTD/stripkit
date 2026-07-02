using System.Runtime.InteropServices;
using FluentAssertions;
using NSubstitute;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Render QC + the un-premultiply fix (path-tracing P3): catch the path-tracer failure modes on
/// import (object drift, a missing transparent background, blank or premultiplied frames) and clean
/// premultiplied edge halos.
/// </summary>
public class RenderQcTests
{
    // ---- un-premultiply ----

    [Fact]
    public void UnpremultiplyAlpha_recovers_straight_colour_from_premultiplied_bytes()
    {
        // Straight (200,100,50) premultiplied at 50% alpha ≈ (100,50,25,128); dividing back recovers it.
        using var src = MakeRgba(100, 50, 25, 128);
        using var outp = FrameSequenceAssembler.UnpremultiplyAlpha(src);

        var (r, g, b, a) = PixelOf(outp);
        a.Should().Be(128);
        r.Should().BeInRange(196, 203);   // 100·255/128 ≈ 199
        g.Should().BeInRange(97, 103);    //  50·255/128 ≈ 100
        b.Should().BeInRange(47, 53);     //  25·255/128 ≈ 50
    }

    [Fact]
    public void UnpremultiplyAlpha_leaves_fully_opaque_and_fully_transparent_pixels_unchanged()
    {
        using var opaque = MakeRgba(200, 100, 50, 255);
        using var o2 = FrameSequenceAssembler.UnpremultiplyAlpha(opaque);
        PixelOf(o2).Should().Be(((byte)200, (byte)100, (byte)50, (byte)255));

        using var clear = MakeRgba(10, 20, 30, 0);   // a == 0 → stays fully transparent (colour irrelevant)
        using var c2 = FrameSequenceAssembler.UnpremultiplyAlpha(clear);
        PixelOf(c2).A.Should().Be(0);
    }

    // ---- QC analysis ----

    [Fact]
    public void AnalyzeQc_detects_object_drift_between_frames()
    {
        using var a = SquareAt(64, 64, 10, 28, 8);   // content centre ≈ x14
        using var b = SquareAt(64, 64, 30, 28, 8);   // content centre ≈ x34 → ~20px drift

        var report = FrameSequenceAssembler.AnalyzeQc(new[] { a, b });

        report.DriftXPx.Should().BeApproximately(20.0, 1.5);
        report.MaxDriftPx.Should().BeGreaterThan(2.0);
        report.Messages.Should().Contain(m => m.Contains("drift"));
    }

    [Fact]
    public void AnalyzeQc_flags_frames_with_no_transparency()
    {
        using var solid = Filled(32, 32, SKColors.Red);     // fully opaque — no transparent background
        using var normal = SquareAt(32, 32, 8, 8, 16);

        var report = FrameSequenceAssembler.AnalyzeQc(new[] { solid, normal });

        report.OpaqueFrames.Should().Be(1);
        report.Messages.Should().Contain(m => m.Contains("no transparency"));
    }

    [Fact]
    public void AnalyzeQc_flags_blank_frames()
    {
        using var blank = Filled(32, 32, SKColors.Transparent);
        using var n1 = SquareAt(32, 32, 8, 8, 16);
        using var n2 = SquareAt(32, 32, 8, 8, 16);

        var report = FrameSequenceAssembler.AnalyzeQc(new[] { blank, n1, n2 });

        report.EmptyFrames.Should().Be(1);
        report.Messages.Should().Contain(m => m.Contains("fully transparent"));
    }

    [Fact]
    public void AnalyzeQc_reports_clean_for_a_well_behaved_sequence()
    {
        using var a = SquareAt(32, 32, 8, 8, 16);
        using var b = SquareAt(32, 32, 8, 8, 16);

        var report = FrameSequenceAssembler.AnalyzeQc(new[] { a, b });

        report.IsClean.Should().BeTrue();
        report.OpaqueFrames.Should().Be(0);
        report.EmptyFrames.Should().Be(0);
        report.PremultipliedSuspected.Should().BeFalse();
    }

    [Fact]
    public void Assemble_surfaces_render_qc_warnings_in_the_result()
    {
        var asm = new FrameSequenceAssembler(Substitute.For<IFilmstripImporter>());
        using var solid = Filled(16, 16, SKColors.Red);     // opaque → QC warning
        using var normal = SquareAt(16, 16, 4, 4, 8);

        var result = asm.Assemble(new[] { solid, normal }, new FrameSequenceOptions());

        result.Warnings.Should().Contain(w => w.Contains("no transparency"));
        result.Strip.Dispose();
    }

    [Fact]
    public void UnpremultiplyAlpha_returns_an_unpremultiplied_bitmap_that_survives_a_premultiplied_roundtrip()
    {
        // A real decoded frame is PREMULTIPLIED. Store straight (200,100,50) at 50% alpha in a Premul
        // bitmap (Skia writes premultiplied bytes ≈ 100,50,25,128). The result must be TAGGED Unpremul so
        // its straight colour reads back ≈ (200,100,50) — via GetPixel and through a real PNG encode/decode,
        // not just via raw bytes (a Premul-tagged result reads and encodes as corrupted colour).
        using var premulFrame = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        premulFrame.Erase(new SKColor(200, 100, 50, 128));

        using var outp = FrameSequenceAssembler.UnpremultiplyAlpha(premulFrame);

        outp.AlphaType.Should().Be(SKAlphaType.Unpremul, "straight bytes must not carry a premultiplied tag");

        var direct = outp.GetPixel(0, 0);
        direct.Alpha.Should().Be(128);
        ((int)direct.Red).Should().BeInRange(190, 210);
        ((int)direct.Green).Should().BeInRange(90, 110);
        ((int)direct.Blue).Should().BeInRange(40, 60);

        // End-to-end: encode to PNG and decode — the disk pixel must still be the straight colour.
        using var img = SKImage.FromBitmap(outp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(data);
        var disk = decoded.GetPixel(0, 0);
        disk.Alpha.Should().Be(128);
        ((int)disk.Red).Should().BeInRange(185, 215);
        ((int)disk.Green).Should().BeInRange(85, 115);
        ((int)disk.Blue).Should().BeInRange(35, 65);
    }

    [Fact]
    public void UnpremultiplyAlpha_recovers_colour_across_a_multi_pixel_frame_including_later_rows_and_columns()
    {
        // A multi-pixel frame exercises the row = y*RowBytes and x*4 stepping the raw-byte loop exists for
        // (the 1×1 tests never leave x=0,y=0). SetPixel on a Premul bitmap stores premultiplied bytes.
        using var frame = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        frame.SetPixel(0, 0, new SKColor(200, 100, 50, 128));
        frame.SetPixel(2, 0, new SKColor(60, 180, 240, 128));   // x > 0
        frame.SetPixel(1, 1, new SKColor(240, 40, 90, 200));    // last row

        using var outp = FrameSequenceAssembler.UnpremultiplyAlpha(frame);

        Near(outp.GetPixel(0, 0), 200, 100, 50, 128);
        Near(outp.GetPixel(2, 0), 60, 180, 240, 128);
        Near(outp.GetPixel(1, 1), 240, 40, 90, 200);
    }

    [Fact]
    public void AnalyzeQc_does_not_report_phantom_drift_for_mixed_size_frames_with_content_at_the_same_pixel()
    {
        // The same object at the same ABSOLUTE pixel (24,24 centre) in a 64² and a 128² frame — nothing
        // moved, so drift must read ~0. The old metric scaled a per-frame-normalized spread by the largest
        // cell and reported ~24px of phantom drift, nudging the user to "Re-centre" when nothing had moved.
        using var small = SquareAt(64, 64, 20, 20, 8);
        using var big = SquareAt(128, 128, 20, 20, 8);

        var report = FrameSequenceAssembler.AnalyzeQc(new[] { small, big });

        report.DriftXPx.Should().BeLessThan(2.0);
        report.DriftYPx.Should().BeLessThan(2.0);
    }

    [Fact]
    public void AnalyzeQc_flags_a_sequence_whose_edges_are_all_premultiplied()
    {
        using var a = RawPartialFrame(8, 8, 100, 100, 100, 150);   // raw RGB ≤ A → premultiplied edge
        using var b = RawPartialFrame(8, 8, 100, 100, 100, 150);

        var report = FrameSequenceAssembler.AnalyzeQc(new[] { a, b });

        report.PremultipliedSuspected.Should().BeTrue();
        report.Messages.Should().Contain(m => m.Contains("premultiplied"));
    }

    [Fact]
    public void AnalyzeQc_does_not_flag_premultiplied_when_any_edge_is_straight_alpha()
    {
        using var a = RawPartialFrame(8, 8, 100, 100, 100, 150);   // premultiplied-looking (RGB ≤ A)
        using var b = RawPartialFrame(8, 8, 200, 200, 200, 120);   // straight-alpha edge (RGB > A)

        var report = FrameSequenceAssembler.AnalyzeQc(new[] { a, b });

        report.PremultipliedSuspected.Should().BeFalse("one frame's edge is straight-alpha, so the vote isn't unanimous");
    }

    // ---- helpers ----

    private static void Near(SKColor c, byte r, byte g, byte b, byte a)
    {
        c.Alpha.Should().Be(a);
        ((int)c.Red).Should().BeInRange(r - 3, r + 3);
        ((int)c.Green).Should().BeInRange(g - 3, g + 3);
        ((int)c.Blue).Should().BeInRange(b - 3, b + 3);
    }

    // A frame with a 2×2 block of a given RAW RGBA value near the centre (rest transparent). AnalyzeQc's
    // premultiplied heuristic reads raw bytes, so the RGB≤A relationship of the block is what it inspects.
    private static SKBitmap RawPartialFrame(int w, int h, byte r, byte g, byte b, byte a)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var buf = new byte[bmp.RowBytes * h];
        for (int y = h / 2; y < h / 2 + 2; y++)
        for (int x = w / 2; x < w / 2 + 2; x++)
        {
            int i = y * bmp.RowBytes + x * 4;
            buf[i] = r; buf[i + 1] = g; buf[i + 2] = b; buf[i + 3] = a;
        }
        Marshal.Copy(buf, 0, bmp.GetPixels(), buf.Length);
        return bmp;
    }

    private static SKBitmap MakeRgba(byte r, byte g, byte b, byte a)
    {
        var bmp = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var buf = new byte[bmp.RowBytes];
        buf[0] = r; buf[1] = g; buf[2] = b; buf[3] = a;
        Marshal.Copy(buf, 0, bmp.GetPixels(), buf.Length);
        return bmp;
    }

    private static (byte R, byte G, byte B, byte A) PixelOf(SKBitmap bmp, int x = 0, int y = 0)
    {
        var buf = bmp.Bytes;
        int i = y * bmp.RowBytes + x * 4;
        return (buf[i], buf[i + 1], buf[i + 2], buf[i + 3]);
    }

    private static SKBitmap Filled(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(color);
        return bmp;
    }

    private static SKBitmap SquareAt(int w, int h, int sx, int sy, int size)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Color = SKColors.White, IsAntialias = false };
        c.DrawRect(SKRect.Create(sx, sy, size, size), p);
        return bmp;
    }
}
