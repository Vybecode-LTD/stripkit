using SkiaSharp;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class ImageLoadService : IImageLoadService
{
    /// <summary>Reject sources whose pixel count would balloon memory — a "decompression bomb"
    /// (tiny file on disk, enormous dimensions). Real control art is a few hundred to a few thousand
    /// px per side; 64 MP (≈ 8192×8192) is a generous ceiling for a single source image.</summary>
    private const long MaxPixels = 64L * 1024 * 1024;

    public SKBitmap? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);

        // Peek the header dimensions before decoding so a decompression-bomb image can't allocate
        // gigabytes of pixels before we ever see them. SkiaSharp decodes PNG/WebP/etc. to
        // premultiplied RGBA by default — exactly the layout the renderer composites in.
        using var codec = SKCodec.Create(stream, out _);
        if (codec is null) return null;
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            return null;

        return SKBitmap.Decode(codec);
    }
}
