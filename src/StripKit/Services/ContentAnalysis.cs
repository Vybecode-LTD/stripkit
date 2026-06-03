using SkiaSharp;

namespace StripKit.Services;

/// <summary>
/// Pure pixel analysis backing the alignment tools — finds where the opaque content of
/// a source image actually sits, so a knob/cap can be centred on its real centre rather
/// than on the raw image rectangle. No Avalonia dependency, so it is unit-testable and
/// reusable by any host.
/// </summary>
public static class ContentAnalysis
{
    /// <summary>
    /// Returns the normalized (0..1 per axis) centre of the bounding box of pixels whose
    /// alpha exceeds <paramref name="alphaThreshold"/>. Falls back to (0.5, 0.5) for a
    /// null/empty/fully-transparent image. The bounding-box centre is more stable than an
    /// alpha-weighted centroid for knobs that carry a bright indicator on one side.
    /// </summary>
    public static (double X, double Y) DetectContentCenter(SKBitmap? bitmap, byte alphaThreshold = 8)
    {
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            return (0.5, 0.5);

        int w = bitmap.Width, h = bitmap.Height;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        var span = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;

        // Fast path: for both RGBA8888 and BGRA8888 the alpha byte is the 4th in each
        // pixel, so we can scan the raw buffer without per-pixel interop. Anything else
        // (rare for loaded PNGs) falls back to GetPixel.
        if (bitmap.BytesPerPixel == 4 && span.Length >= h * rowBytes)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * rowBytes;
                for (int x = 0; x < w; x++)
                {
                    if (span[row + x * 4 + 3] > alphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > alphaThreshold)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0) // nothing opaque
            return (0.5, 0.5);

        double cx = (minX + maxX + 1) / 2.0 / w;
        double cy = (minY + maxY + 1) / 2.0 / h;
        return (cx, cy);
    }
}
