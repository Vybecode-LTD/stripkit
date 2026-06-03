using SkiaSharp;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class ExportService : IExportService
{
    public async Task SavePngAsync(SKBitmap bitmap, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        await using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
