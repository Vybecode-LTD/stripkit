using ImageMagick;
using SkiaSharp;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class ImageLoadService : IImageLoadService
{
    /// <summary>Reject sources whose pixel count would balloon memory — a "decompression bomb"
    /// (tiny file on disk, enormous dimensions). Real control art is a few hundred to a few thousand
    /// px per side; 64 MP (≈ 8192×8192) is a generous ceiling for a single source image.</summary>
    private const long MaxPixels = 64L * 1024 * 1024;

    /// <summary>Formats SkiaSharp can't decode — routed through Magick.NET (Q16-HDRI, path-tracing P3b).
    /// EXR/HDR are linear high-dynamic-range and get a tone-map; 16-bit TIFF is treated as
    /// display-referred (dithered down to 8-bit only).</summary>
    private static readonly string[] HdrExtensions = [".exr", ".hdr", ".tif", ".tiff"];

    private static bool IsHdr(string path) =>
        HdrExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public SKBitmap? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (IsHdr(path))
            return LoadHdr(path);

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

    public (int Width, int Height) Probe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (0, 0);

        if (IsHdr(path))
            return ProbeHdr(path);

        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream, out _);
        if (codec is null) return (0, 0);

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            return (0, 0);

        return (info.Width, info.Height);
    }

    // ---- HDR / EXR ingest (path-tracing P3b, Magick.NET Q16-HDRI) ----

    /// <summary>
    /// Decode a high-dynamic-range or 16-bit frame via Magick.NET: tone-map a linear EXR/HDR to sRGB,
    /// dither in 16-bit space, then hand an 8-bit RGBA PNG to SkiaSharp. Best-effort (null on any
    /// failure), header-capped against a decompression bomb like the SkiaSharp path.
    /// </summary>
    private static SKBitmap? LoadHdr(string path)
    {
        try
        {
            var (pw, ph) = ProbeHdr(path);
            if (pw <= 0 || ph <= 0) return null;

            using var img = new MagickImage(path);

            var ext = Path.GetExtension(path);
            bool linearHdr = ext.Equals(".exr", StringComparison.OrdinalIgnoreCase)
                          || ext.Equals(".hdr", StringComparison.OrdinalIgnoreCase);
            if (linearHdr)
                // Linear scene-referred → sRGB display. (A filmic knee via SigmoidalContrast could be
                // layered in here later; plain sRGB + Clamp is a correct, safe display transform.)
                img.ColorSpace = ColorSpace.sRGB;

            img.Clamp();                 // HDRI allows values past the quantum max — clamp before quantizing
            img.Alpha(AlphaOption.Set);  // guarantee an RGBA channel
            img.Depth = 8;               // reduce 16-bit → 8-bit (the PNG32 write does the quantization)

            // (A dithered 16→8 reduction to kill banding on smooth gradients is a future refinement —
            // Magick's OrderedDither("o8x8") posterizes to 2 levels per channel, so it isn't the right
            // tool here; a per-channel error-diffusion pass would be.)
            byte[] png = img.ToByteArray(MagickFormat.Png32);   // 8-bit RGBA
            return SKBitmap.Decode(png);
        }
        catch
        {
            return null;   // unreadable / unsupported HDR — the caller surfaces a friendly error
        }
    }

    private static (int Width, int Height) ProbeHdr(string path)
    {
        try
        {
            var info = new MagickImageInfo(path);
            int w = (int)info.Width, h = (int)info.Height;
            if (w <= 0 || h <= 0 || (long)w * h > MaxPixels) return (0, 0);
            return (w, h);
        }
        catch
        {
            return (0, 0);
        }
    }
}
