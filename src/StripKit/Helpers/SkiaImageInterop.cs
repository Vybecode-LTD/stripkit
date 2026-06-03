using Avalonia.Media.Imaging;
using SkiaSharp;

namespace StripKit.Helpers;

/// <summary>Bridges SkiaSharp output to an Avalonia <see cref="Bitmap"/> for display.</summary>
public static class SkiaImageInterop
{
    /// <summary>
    /// Encodes an <see cref="SKBitmap"/> to PNG in memory and wraps it as an
    /// Avalonia <see cref="Bitmap"/> for binding to an <c>Image</c> control.
    /// This is fine for preview-sized frames; for very high frame-rate playback
    /// you could swap to a reused <c>WriteableBitmap</c> with a direct pixel copy.
    /// </summary>
    public static Bitmap ToAvaloniaBitmap(SKBitmap skBitmap)
    {
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
