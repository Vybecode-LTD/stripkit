using System;
using System.Collections.Generic;
using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Tests for the frame-sequence assembler: stacking, the cell-fit policies, content re-centring,
/// the resample hand-off to the importer, and the safety guards. Synthetic in-memory frames keep
/// these deterministic and free of golden baselines.
/// </summary>
public class FrameSequenceAssemblerTests
{
    readonly FrameSequenceAssembler _assembler = new(new FilmstripImporter());
    readonly FilmstripImporter _importer = new();

    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(b);
        canvas.Clear(c);
        return b;
    }

    static List<SKBitmap> Frames(int n, int w, int h)
    {
        var list = new List<SKBitmap>();
        for (int i = 0; i < n; i++)
            list.Add(Solid(w, h, new SKColor((byte)(20 + i * 25), 80, 160)));
        return list;
    }

    [Fact]
    public void Stacks_uniform_frames_vertically()
    {
        var frames = Frames(4, 20, 20);
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions { Direction = StackDirection.Vertical });
            using (result.Strip)
            {
                result.FrameCount.Should().Be(4);
                result.FrameWidth.Should().Be(20);
                result.FrameHeight.Should().Be(20);
                result.Strip.Width.Should().Be(20);
                result.Strip.Height.Should().Be(80);
                result.Resampled.Should().BeFalse();
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Stacks_uniform_frames_horizontally()
    {
        var frames = Frames(4, 20, 20);
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions { Direction = StackDirection.Horizontal });
            using (result.Strip)
            {
                result.Strip.Width.Should().Be(80);
                result.Strip.Height.Should().Be(20);
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Placed_cells_match_the_source_frames_pixel_for_pixel()
    {
        var frames = Frames(3, 16, 16);
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions { Direction = StackDirection.Vertical });
            using (result.Strip)
            {
                var layout = new StripDetection(true, 3, 16, 16, null, false, new[] { 3 });
                for (int i = 0; i < 3; i++)
                {
                    using var cell = _importer.ExtractFrame(result.Strip, layout, i);
                    PixelsEqual(cell, frames[i]).Should().BeTrue($"cell {i} should equal source frame {i}");
                }
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Pad_to_largest_pads_mismatched_frames_and_warns()
    {
        var frames = new List<SKBitmap> { Solid(20, 20, SKColors.Red), Solid(30, 30, SKColors.Green) };
        try
        {
            var result = _assembler.Assemble(frames,
                new FrameSequenceOptions { Fit = CellFit.PadToLargest, Direction = StackDirection.Vertical });
            using (result.Strip)
            {
                result.FrameWidth.Should().Be(30);
                result.FrameHeight.Should().Be(30);
                result.Strip.Height.Should().Be(60);
                result.Warnings.Should().NotBeEmpty();
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Crop_to_smallest_uses_the_smallest_cell()
    {
        var frames = new List<SKBitmap> { Solid(20, 20, SKColors.Red), Solid(30, 30, SKColors.Green) };
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions { Fit = CellFit.CropToSmallest });
            using (result.Strip)
            {
                result.FrameWidth.Should().Be(20);
                result.FrameHeight.Should().Be(20);
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Strict_fit_throws_on_a_size_mismatch()
    {
        var frames = new List<SKBitmap> { Solid(20, 20, SKColors.Red), Solid(30, 30, SKColors.Green) };
        try
        {
            var act = () => _assembler.Assemble(frames, new FrameSequenceOptions { Fit = CellFit.Strict });
            act.Should().Throw<InvalidOperationException>();
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Fewer_than_two_frames_throws()
    {
        var frames = new List<SKBitmap> { Solid(20, 20, SKColors.Red) };
        try
        {
            var act = () => _assembler.Assemble(frames, new FrameSequenceOptions());
            act.Should().Throw<InvalidOperationException>();
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Resample_retimes_to_the_target_count_and_keeps_the_endpoints()
    {
        var frames = Frames(8, 16, 16);
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions
            {
                Direction = StackDirection.Vertical,
                ResampleTo = 4,
            });
            using (result.Strip)
            {
                result.FrameCount.Should().Be(4);
                result.Resampled.Should().BeTrue();
                result.Strip.Height.Should().Be(16 * 4);

                var layout = new StripDetection(true, 4, 16, 16, null, false, new[] { 4 });
                using var d0 = _importer.ExtractFrame(result.Strip, layout, 0);
                using var d3 = _importer.ExtractFrame(result.Strip, layout, 3);
                PixelsEqual(d0, frames[0]).Should().BeTrue("dest 0 maps to source 0 (min)");
                PixelsEqual(d3, frames[7]).Should().BeTrue("dest last maps to source last (max)");
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Recenter_moves_off_centre_content_to_the_cell_centre()
    {
        // Two 40x40 frames with a 10x10 opaque block in the top-left corner (content centre ~0.125).
        var frames = new List<SKBitmap>();
        for (int i = 0; i < 2; i++)
        {
            var b = new SKBitmap(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var c = new SKCanvas(b))
            {
                c.Clear(SKColors.Transparent);
                using var p = new SKPaint { Color = SKColors.White };
                c.DrawRect(SKRect.Create(0, 0, 10, 10), p);
            }
            frames.Add(b);
        }
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions { RecenterOnContent = true });
            using (result.Strip)
            {
                var layout = new StripDetection(true, 2, 40, 40, null, false, new[] { 2 });
                using var cell = _importer.ExtractFrame(result.Strip, layout, 0);
                var (cx, cy) = ContentAnalysis.DetectContentCenter(cell);
                cx.Should().BeApproximately(0.5, 0.05);
                cy.Should().BeApproximately(0.5, 0.05);
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Crossfade_resample_hits_the_target_count_and_keeps_the_endpoints_exact()
    {
        var frames = Frames(8, 16, 16);
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions
            {
                Direction = StackDirection.Vertical,
                ResampleTo = 4,
                Interpolation = FrameInterpolation.Crossfade,
            });
            using (result.Strip)
            {
                result.FrameCount.Should().Be(4);
                result.Resampled.Should().BeTrue();
                result.Strip.Height.Should().Be(16 * 4);

                var layout = new StripDetection(true, 4, 16, 16, null, false, new[] { 4 });
                using var d0 = _importer.ExtractFrame(result.Strip, layout, 0);
                using var d3 = _importer.ExtractFrame(result.Strip, layout, 3);
                PixelsEqual(d0, frames[0]).Should().BeTrue("crossfade endpoint 0 is the real first frame");
                PixelsEqual(d3, frames[7]).Should().BeTrue("crossfade endpoint N-1 is the real last frame");
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Crossfade_midpoint_blends_the_two_bracketing_frames()
    {
        // Two distinct opaque frames; upsample to 3 → the middle frame must be a ~50/50 blend, i.e. an
        // in-between that equals neither endpoint (nearest resampling could never produce this).
        var frames = new List<SKBitmap> { Solid(16, 16, SKColors.Red), Solid(16, 16, SKColors.Blue) };
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions
            {
                Direction = StackDirection.Vertical,
                ResampleTo = 3,
                Interpolation = FrameInterpolation.Crossfade,
            });
            using (result.Strip)
            {
                result.FrameCount.Should().Be(3);
                var layout = new StripDetection(true, 3, 16, 16, null, false, new[] { 3 });
                using var mid = _importer.ExtractFrame(result.Strip, layout, 1);
                var p = mid.GetPixel(8, 8);

                ((int)p.Red).Should().BeInRange(110, 145, "half of red 255");
                ((int)p.Blue).Should().BeInRange(110, 145, "half of blue 255");
                ((int)p.Green).Should().BeLessThan(20);
                p.Alpha.Should().BeGreaterThan(250);
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Emission_pass_additively_brightens_the_beauty_frames()
    {
        var beauty = new List<SKBitmap> { Solid(16, 16, new SKColor(40, 40, 40)), Solid(16, 16, new SKColor(40, 40, 40)) };
        var emission = new List<SKBitmap> { Solid(16, 16, new SKColor(200, 0, 0)), Solid(16, 16, new SKColor(200, 0, 0)) };
        try
        {
            var result = _assembler.Assemble(beauty, new FrameSequenceOptions
            {
                EmissionFrames = emission,
                EmissionIntensity = 1.0,
            });
            using (result.Strip)
            {
                var layout = new StripDetection(true, 2, 16, 16, null, false, new[] { 2 });
                using var cell = _importer.ExtractFrame(result.Strip, layout, 0);
                var p = cell.GetPixel(8, 8);
                ((int)p.Red).Should().BeGreaterThan(200, "beauty 40 + additive emission 200");
                ((int)p.Green).Should().BeInRange(30, 60, "emission has no green, so green is unchanged");
                result.Warnings.Should().Contain(w => w.Contains("emission"));
            }
        }
        finally { foreach (var b in beauty) b.Dispose(); foreach (var b in emission) b.Dispose(); }
    }

    [Fact]
    public void A_mismatched_emission_pass_is_ignored_with_a_warning()
    {
        var beauty = Frames(3, 16, 16);
        var emission = new List<SKBitmap> { Solid(16, 16, new SKColor(200, 0, 0)) };   // 1 vs 3 beauty frames
        try
        {
            var result = _assembler.Assemble(beauty, new FrameSequenceOptions { EmissionFrames = emission });
            using (result.Strip)
            {
                result.Warnings.Should().Contain(w => w.Contains("Emission pass ignored"));
                var layout = new StripDetection(true, 3, 16, 16, null, false, new[] { 3 });
                using var cell = _importer.ExtractFrame(result.Strip, layout, 0);
                PixelsEqual(cell, beauty[0]).Should().BeTrue("a mismatched emission pass leaves the beauty untouched");
            }
        }
        finally { foreach (var b in beauty) b.Dispose(); foreach (var b in emission) b.Dispose(); }
    }

    static bool PixelsEqual(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
