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

    // ---- helpers ----

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
