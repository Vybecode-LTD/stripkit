using FluentAssertions;
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
}
