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
}
