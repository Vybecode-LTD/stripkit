using SkiaSharp;

namespace StripKit.Services;

/// <summary>Loads an image file from disk into an <see cref="SKBitmap"/>.</summary>
public interface IImageLoadService
{
    /// <summary>Returns the decoded bitmap, or <c>null</c> if the file is missing or undecodable.</summary>
    SKBitmap? Load(string path);

    /// <summary>Peeks an image's pixel dimensions from its header without decoding the pixels;
    /// returns <c>(0, 0)</c> if the file is missing, undecodable, or over the size cap.</summary>
    (int Width, int Height) Probe(string path);
}
