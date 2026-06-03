using FluentAssertions;
using SkiaSharp;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Unit tests for the opaque-content centre detection that backs the alignment tools.
/// </summary>
public class ContentAnalysisTests
{
    // Bitmap with one opaque rectangle (left/top/right/bottom), rest transparent.
    static SKBitmap WithBox(int w, int h, int l, int t, int r, int b)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        c.DrawRect(new SKRect(l, t, r, b), p);
        return bmp;
    }

    [Fact]
    public void Centered_content_detects_near_half()
    {
        using var bmp = WithBox(100, 100, 40, 40, 60, 60); // centre (50,50)
        var (x, y) = ContentAnalysis.DetectContentCenter(bmp);
        x.Should().BeApproximately(0.5, 0.02);
        y.Should().BeApproximately(0.5, 0.02);
    }

    [Fact]
    public void Offset_content_detects_offset_center()
    {
        using var bmp = WithBox(100, 100, 10, 10, 30, 50); // centre (20,30)
        var (x, y) = ContentAnalysis.DetectContentCenter(bmp);
        x.Should().BeApproximately(0.20, 0.03);
        y.Should().BeApproximately(0.30, 0.03);
    }

    [Fact]
    public void Fully_transparent_falls_back_to_half()
    {
        using var bmp = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.Transparent);

        var (x, y) = ContentAnalysis.DetectContentCenter(bmp);
        x.Should().Be(0.5);
        y.Should().Be(0.5);
    }

    [Fact]
    public void Null_bitmap_falls_back_to_half()
    {
        var (x, y) = ContentAnalysis.DetectContentCenter(null);
        x.Should().Be(0.5);
        y.Should().Be(0.5);
    }
}
