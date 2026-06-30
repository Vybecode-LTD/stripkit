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

    static bool PixelsEqual(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
