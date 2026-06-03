using SkiaSharp;

namespace StripKit.Services;

/// <summary>Loads an image file from disk into an <see cref="SKBitmap"/>.</summary>
public interface IImageLoadService
{
    /// <summary>Returns the decoded bitmap, or <c>null</c> if the file is missing or undecodable.</summary>
    SKBitmap? Load(string path);
}
