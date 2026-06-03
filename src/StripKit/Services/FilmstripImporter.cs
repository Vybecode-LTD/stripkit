using StripKit.Models;
using SkiaSharp;

namespace StripKit.Services;

/// <summary>
/// SkiaSharp implementation of the filmstrip importer (see the
/// <c>filmstrip-importer-engine</c> skill). Detection infers the frame count from
/// the image dimensions because a PNG does not store it — the result is a guess
/// the UI must let the user verify and edit.
/// </summary>
public sealed class FilmstripImporter : IFilmstripImporter
{
    // Tried in this order against the strip's total dimension; the first that
    // divides evenly wins. Biased toward the largest plausible count. Includes the
    // odd "+ centre frame" variants (127/101/63) seen in real exports.
    private static readonly int[] Candidates = { 128, 127, 101, 100, 64, 63, 48, 32, 24, 16, 12, 8, 4, 3, 2 };

    // 1:1 blits only — no scaling, so nearest sampling is exact and cheap.
    private static readonly SKSamplingOptions Blit = new(SKFilterMode.Nearest, SKMipmapMode.None);

    public StripDetection Detect(SKBitmap strip) => Detect(strip.Width, strip.Height);

    public StripDetection Detect(int width, int height)
    {
        bool vertical = height >= width;
        int total = vertical ? height : width;

        var dividing = new List<int>();
        foreach (int c in Candidates)
            if (c <= total && total % c == 0)
                dividing.Add(c);

        int n = dividing.Count > 0 ? dividing[0] : 1;
        int frameW = vertical ? width : width / n;
        int frameH = vertical ? height / n : height;

        ComponentType? kind = Classify(frameW, frameH);

        // Ambiguity flag: a square frame whose total also divides by an adjacent
        // count (e.g. 64 and 63) may include an extra centre frame — verify.
        bool square = kind == ComponentType.RotaryKnob;
        bool lowConfidence = square &&
            ((n > 1 && total % (n - 1) == 0) || total % (n + 1) == 0);

        return new StripDetection(vertical, n, frameW, frameH, kind, lowConfidence, dividing);
    }

    private static ComponentType? Classify(int frameW, int frameH)
    {
        if (Math.Abs(frameH - frameW) <= 0.2 * Math.Max(frameW, frameH))
            return ComponentType.RotaryKnob;          // (or a square button)
        if (frameH > frameW * 2)
            return ComponentType.VerticalFader;
        if (frameW > frameH * 2)
            return ComponentType.HorizontalSlider;
        return null;                                   // unknown aspect
    }

    public SKBitmap ExtractFrame(SKBitmap strip, StripDetection layout, int index)
    {
        int n = Math.Max(1, layout.FrameCount);
        int idx = Math.Clamp(index, 0, n - 1);
        int fw = layout.FrameWidth;
        int fh = layout.FrameHeight;

        int sx = layout.Vertical ? 0 : idx * fw;
        int sy = layout.Vertical ? idx * fh : 0;

        var frame = new SKBitmap(fw, fh, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(frame);
        canvas.Clear(SKColors.Transparent);
        using var img = SKImage.FromBitmap(strip);
        canvas.DrawImage(img, SKRect.Create(sx, sy, fw, fh), SKRect.Create(0, 0, fw, fh), Blit);
        return frame;
    }

    public SKBitmap Restack(SKBitmap strip, StripDetection layout, StackDirection destination)
    {
        int n = Math.Max(1, layout.FrameCount);
        int fw = layout.FrameWidth;
        int fh = layout.FrameHeight;

        bool destVertical = destination == StackDirection.Vertical;
        int outW = destVertical ? fw : fw * n;
        int outH = destVertical ? fh * n : fh;

        var outBmp = new SKBitmap(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(outBmp);
        canvas.Clear(SKColors.Transparent);
        using var img = SKImage.FromBitmap(strip);

        for (int i = 0; i < n; i++)
        {
            int sx = layout.Vertical ? 0 : i * fw;
            int sy = layout.Vertical ? i * fh : 0;
            int dx = destVertical ? 0 : i * fw;
            int dy = destVertical ? i * fh : 0;
            canvas.DrawImage(img, SKRect.Create(sx, sy, fw, fh), SKRect.Create(dx, dy, fw, fh), Blit);
        }
        return outBmp;
    }
}
