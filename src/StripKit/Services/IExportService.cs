using SkiaSharp;

namespace StripKit.Services;

/// <summary>Encodes a rendered bitmap to a PNG file on disk.</summary>
public interface IExportService
{
    Task SavePngAsync(SKBitmap bitmap, string path);
}
