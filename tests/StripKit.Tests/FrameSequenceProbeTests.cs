using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using SkiaSharp;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Integration coverage for the probe path: a real <see cref="ImageLoadService"/> reads on-disk PNGs
/// and the assembler natural-sorts them and reports the dimension spread (no full decode).
/// </summary>
public class FrameSequenceProbeTests
{
    readonly FrameSequenceAssembler _assembler = new(new FilmstripImporter());
    readonly ImageLoadService _loader = new();

    static string WritePng(string dir, string name, int w, int h)
    {
        var path = Path.Combine(dir, name);
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.Blue);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
        return path;
    }

    [Fact]
    public void Probe_natural_sorts_paths_and_reports_uniform_size()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stripkit_seq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            WritePng(dir, "f_10.png", 40, 40);
            WritePng(dir, "f_2.png", 40, 40);
            WritePng(dir, "f_1.png", 40, 40);
            var scrambled = new[]
            {
                Path.Combine(dir, "f_10.png"),
                Path.Combine(dir, "f_1.png"),
                Path.Combine(dir, "f_2.png"),
            };

            var probe = _assembler.Probe(scrambled, _loader);

            probe.OrderedPaths.Select(Path.GetFileName).Should().Equal("f_1.png", "f_2.png", "f_10.png");
            probe.Uniform.Should().BeTrue();
            probe.MaxWidth.Should().Be(40);
            probe.MaxHeight.Should().Be(40);
            probe.Warnings.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Probe_warns_when_frame_sizes_differ()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stripkit_seq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var a = WritePng(dir, "a_1.png", 40, 40);
            var b = WritePng(dir, "a_2.png", 48, 48);

            var probe = _assembler.Probe(new[] { a, b }, _loader);

            probe.Uniform.Should().BeFalse();
            probe.MinWidth.Should().Be(40);
            probe.MaxWidth.Should().Be(48);
            probe.Warnings.Should().NotBeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
