using SkiaSharp;

namespace StripKit.Tests;

/// <summary>
/// Deterministic synthetic source art for the golden-image tests. Fixed geometry,
/// fixed colours, no fonts or randomness — so the rendered baselines are stable.
/// </summary>
internal static class TestImages
{
    /// <summary>Knob art: dark body, accent ring, white pointer at 12 o'clock.</summary>
    public static SKBitmap Knob(int size = 100)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        float cx = size / 2f, cy = size / 2f, r = size * 0.40f;

        using var body = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true, Style = SKPaintStyle.Fill };
        c.DrawCircle(cx, cy, r, body);
        using var ring = new SKPaint { Color = new SKColor(0xE8, 0x44, 0x0A), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5 };
        c.DrawCircle(cx, cy, r, ring);
        using var ptr = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(cx, cy, cx, size * 0.12f, ptr); // straight up = 12 o'clock
        return bmp;
    }

    /// <summary>Fader/slider cap: an accent rounded rectangle with a light border.</summary>
    public static SKBitmap Cap(int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        var rect = new SKRect(1, 1, w - 1, h - 1);
        float rad = Math.Min(w, h) * 0.25f;

        using var fill = new SKPaint { Color = new SKColor(0xE8, 0x44, 0x0A), IsAntialias = true, Style = SKPaintStyle.Fill };
        c.DrawRoundRect(rect, rad, rad, fill);
        using var border = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0xAA), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        c.DrawRoundRect(rect, rad, rad, border);
        return bmp;
    }
}
