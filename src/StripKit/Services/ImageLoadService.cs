using SkiaSharp;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class ImageLoadService : IImageLoadService
{
    public SKBitmap? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        // SkiaSharp decodes PNG/WebP/etc. to premultiplied RGBA by default, which
        // is exactly the layout the renderer composites in.
        return SKBitmap.Decode(stream);
    }
}
