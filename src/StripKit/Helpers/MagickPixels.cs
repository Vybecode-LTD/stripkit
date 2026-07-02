namespace StripKit.Helpers;

/// <summary>
/// Normalizes the raw RGBA byte buffer returned by Magick.NET's <c>IPixelCollection.ToByteArray</c> to
/// 8-bit RGBA (one byte per channel), regardless of the ImageMagick build's quantum depth.
/// </summary>
/// <remarks>
/// <c>ToByteArray</c> has no storage-type overload — it always emits pixels at the build's quantum depth.
/// At <b>Q8</b> that's 1 byte/channel (4 bytes/pixel); at <b>Q16-HDRI</b> (what StripKit ships, so an EXR
/// can be tone-mapped) it's 2 bytes/channel, little-endian (8 bytes/pixel). Downshifting to the high byte
/// of each 16-bit channel is exact for an 8-bit source (255 → 65535 → 255) and the correct display value
/// for a depth-8-quantized 16-bit one. Inferring bytes-per-channel from the buffer length keeps callers
/// correct whether the app is built at Q8 or Q16-HDRI.
/// </remarks>
public static class MagickPixels
{
    /// <summary>Returns an <c>w*h*4</c> 8-bit RGBA buffer from Magick's <paramref name="src"/> RGBA bytes
    /// (1 or 2 bytes per channel). Returns <paramref name="src"/> unchanged when it is already 8-bit or
    /// the dimensions don't line up.</summary>
    public static byte[] ToRgba8888(byte[] src, int width, int height)
    {
        long pixels = (long)width * height;
        if (pixels <= 0) return src;

        long channels = pixels * 4;
        if (channels == 0 || src.Length % channels != 0) return src;

        int bytesPerChannel = (int)(src.Length / channels);
        if (bytesPerChannel <= 1) return src;   // already 8-bit

        var dst = new byte[channels];
        // Little-endian: the high (most-significant) byte of each channel sits at the last sub-byte.
        int hi = bytesPerChannel - 1;
        for (int i = 0; i < channels; i++)
            dst[i] = src[i * bytesPerChannel + hi];
        return dst;
    }

    // Standard 8×8 Bayer ordered-dither threshold matrix, values 0..63.
    private static readonly int[] Bayer8 =
    {
         0, 32,  8, 40,  2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44,  4, 36, 14, 46,  6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
         3, 35, 11, 43,  1, 33,  9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47,  7, 39, 13, 45,  5, 37,
        63, 31, 55, 23, 61, 29, 53, 21,
    };

    /// <summary>
    /// Reduce a 16-bit RGBA buffer (Magick's <c>ToByteArray(RGBA)</c> at Q16-HDRI) to 8-bit RGBA with an
    /// ordered (Bayer 8×8) dither on the RGB channels — spreads the rounding spatially so smooth gradients
    /// (metal, glass) don't band into 8-bit steps (path-tracing P3b de-band). Alpha is truncated, not
    /// dithered, to avoid speckling anti-aliased edges. An already-8-bit buffer is returned unchanged.
    /// </summary>
    public static byte[] DitherDownTo8(byte[] src, int width, int height)
    {
        long pixelCount = (long)width * height;
        if (pixelCount <= 0) return src;

        long channels = pixelCount * 4;
        if (channels == 0 || src.Length % channels != 0) return src;

        int bytesPerChannel = (int)(src.Length / channels);
        if (bytesPerChannel <= 1) return src;   // already 8-bit — nothing to dither

        var dst = new byte[channels];
        for (int y = 0; y < height; y++)
        {
            int row = (y & 7) * 8;
            for (int x = 0; x < width; x++)
            {
                int threshold = Bayer8[row + (x & 7)] * 4;   // 0..252
                long p = (long)y * width + x;
                int si = (int)(p * 4 * bytesPerChannel);
                int di = (int)(p * 4);
                for (int c = 0; c < 4; c++)
                {
                    int lo = src[si + c * bytesPerChannel];                    // low byte (LE remainder)
                    int hi = src[si + c * bytesPerChannel + bytesPerChannel - 1];  // high byte = truncated 8-bit
                    dst[di + c] = c == 3
                        ? (byte)hi                                            // alpha: no dither
                        : (byte)(lo > threshold ? Math.Min(255, hi + 1) : hi);
                }
            }
        }
        return dst;
    }
}
