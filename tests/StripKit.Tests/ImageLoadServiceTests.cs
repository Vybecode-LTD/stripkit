using FluentAssertions;
using ImageMagick;
using SkiaSharp;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The concrete PNG decode path. Load peeks the header dimensions via <see cref="SKCodec"/> and
/// rejects a decompression-bomb (huge dimensions) before decoding, while still decoding ordinary
/// control-art PNGs to the premultiplied RGBA the renderer composites in.
/// </summary>
public class ImageLoadServiceTests
{
    readonly ImageLoadService _load = new();

    static string WritePng(int w, int h)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_test_{Guid.NewGuid():N}.png");
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.Red);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
        return path;
    }

    [Fact]
    public void Decodes_a_valid_png_at_its_real_dimensions()
    {
        var path = WritePng(120, 80);
        try
        {
            using var bmp = _load.Load(path);
            bmp.Should().NotBeNull();
            bmp!.Width.Should().Be(120);
            bmp.Height.Should().Be(80);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Returns_null_for_a_missing_file() =>
        _load.Load("C:\\does\\not\\exist.png").Should().BeNull();

    [Fact]
    public void Returns_null_for_non_image_content()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_test_{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "this is not a PNG");
        try { _load.Load(path).Should().BeNull("a non-image file has no decodable header"); }
        finally { File.Delete(path); }
    }

    // ---- HDR / 16-bit ingest (path-tracing P3b, Magick.NET Q16-HDRI) ----

    [Fact]
    public void Loads_a_16bit_tiff_frame_and_downshifts_to_8bit_rgba()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_hdr_{Guid.NewGuid():N}.tif");
        try
        {
            using (var img = new MagickImage(new MagickColor("#3366CC"), 12, 10))
            {
                img.Depth = 16;
                img.Write(path);   // a 16-bit TIFF SkiaSharp can't decode
            }

            _load.Probe(path).Should().Be((12, 10), "the header dims come from Magick, not SKCodec");

            using var bmp = _load.Load(path);
            bmp.Should().NotBeNull();
            bmp!.Width.Should().Be(12);
            bmp.Height.Should().Be(10);

            var p = bmp.GetPixel(6, 5);   // display-referred TIFF: colour preserved (dither adds a little)
            ((int)p.Red).Should().BeInRange(0x33 - 24, 0x33 + 24);
            ((int)p.Green).Should().BeInRange(0x66 - 24, 0x66 + 24);
            ((int)p.Blue).Should().BeInRange(0xCC - 24, 0xCC + 24);
        }
        finally { try { File.Delete(path); } catch { /* best-effort */ } }
    }

    [Fact]
    public void Loads_an_exr_frame_and_tone_maps_it_to_an_8bit_bitmap()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_hdr_{Guid.NewGuid():N}.exr");
        try
        {
            using (var img = new MagickImage(new MagickColor("#C0C0C0"), 16, 16))
                img.Write(path);   // EXR (OpenEXR is bundled in the Q16-HDRI native)

            _load.Probe(path).Should().Be((16, 16));

            using var bmp = _load.Load(path);
            bmp.Should().NotBeNull("EXR ingest tone-maps linear HDR down to an 8-bit RGBA bitmap");
            bmp!.Width.Should().Be(16);
            bmp.Height.Should().Be(16);
            bmp.GetPixel(8, 8).Alpha.Should().BeGreaterThan(0, "the frame has content (not blank)");
        }
        finally { try { File.Delete(path); } catch { /* best-effort */ } }
    }
}
