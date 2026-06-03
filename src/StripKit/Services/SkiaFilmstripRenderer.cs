using StripKit.Models;
using SkiaSharp;

namespace StripKit.Services;

/// <summary>
/// Renders animated control filmstrips with SkiaSharp.
///
/// <para>The whole tool reduces to one idea: for each of N frames, place the
/// source art inside a fixed-size frame cell under a per-frame transform, then
/// stack the cells into a single PNG. Rotary knobs rotate the art about a pivot;
/// faders/sliders translate the art (the "cap") along an axis.</para>
///
/// <para>Quality comes from two things: a Mitchell cubic resampler (low ringing,
/// smooth edges) and optional supersampling — each frame is rendered into an
/// oversampled surface and downsampled once, which is what keeps a rotated knob's
/// edge crisp instead of jagged.</para>
/// </summary>
public sealed class SkiaFilmstripRenderer : IFilmstripRenderer
{
    // Mitchell cubic is a good default for both rotation and downscale.
    private static readonly SKSamplingOptions Cubic = new(SKCubicResampler.Mitchell);

    public FrameTransform ComputeTransform(FilmstripSettings settings, SKBitmap source, int frameIndex)
    {
        int n = Math.Max(1, settings.FrameCount);

        // t runs 0 -> 1 across the strip. The (n - 1) divisor lands the final
        // frame exactly on the maximum position; using n would fall short.
        double t = n > 1 ? (double)frameIndex / (n - 1) : 0.0;

        float fw = settings.FrameWidth;
        float fh = settings.FrameHeight;

        switch (settings.ComponentType)
        {
            case ComponentType.RotaryKnob:
            {
                // The art is fit (aspect-preserving, centred) into the frame, then
                // rotated about the pivot. Give knob art ~10% transparent margin
                // so its corners don't clip as it rotates.
                var (drawW, drawH) = Contain(source.Width, source.Height, fw, fh);
                float drawX = (fw - drawW) / 2f;
                float drawY = (fh - drawH) / 2f;

                double angle = settings.StartAngleDegrees
                             + (settings.EndAngleDegrees - settings.StartAngleDegrees) * t;

                float pivotX = fw / 2f + (float)settings.PivotOffsetX;
                float pivotY = fh / 2f + (float)settings.PivotOffsetY;

                return new FrameTransform(drawX, drawY, drawW, drawH, (float)angle, pivotX, pivotY);
            }

            case ComponentType.VerticalFader:
            {
                // The source is the cap. It keeps its native size and slides
                // vertically: frame 0 (min) at the bottom, last frame (max) at top.
                float capW = source.Width;
                float capH = source.Height;
                float x = (fw - capW) / 2f + (float)settings.CapCrossOffset;

                float yBottom = fh - (float)settings.EdgeMargin - capH; // min
                float yTop = (float)settings.EdgeMargin;                 // max
                float y = (float)(yBottom + (yTop - yBottom) * t);

                return new FrameTransform(x, y, capW, capH, 0f, 0f, 0f);
            }

            case ComponentType.HorizontalSlider:
            {
                // Cap slides horizontally: min at the left, max at the right.
                float capW = source.Width;
                float capH = source.Height;
                float y = (fh - capH) / 2f + (float)settings.CapCrossOffset;

                float xLeft = (float)settings.EdgeMargin;                 // min
                float xRight = fw - (float)settings.EdgeMargin - capW;     // max
                float x = (float)(xLeft + (xRight - xLeft) * t);

                return new FrameTransform(x, y, capW, capH, 0f, 0f, 0f);
            }

            case ComponentType.Meter:
                // Meters are composed in RenderFrame's own segment-fill path, not via
                // a per-frame transform; return an identity full-frame transform.
                return new FrameTransform(0f, 0f, fw, fh, 0f, fw / 2f, fh / 2f);

            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.ComponentType, "Unknown component type.");
        }
    }

    public SKBitmap RenderFrame(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, int frameIndex, double scale = 1.0)
    {
        int ss = Math.Clamp(settings.Supersample, 1, 8);

        // Work in oversampled, export-scaled pixels, then downsample once at the end.
        double px = scale * ss;

        int targetW = Math.Max(1, (int)Math.Round(settings.FrameWidth * scale));
        int targetH = Math.Max(1, (int)Math.Round(settings.FrameHeight * scale));
        int workW = Math.Max(1, (int)Math.Round(settings.FrameWidth * px));
        int workH = Math.Max(1, (int)Math.Round(settings.FrameHeight * px));

        var info = new SKImageInfo(workW, workH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var work = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create the render surface (out of memory?).");

        var canvas = work.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Static background (a knob well, a fader track, or a meter's off-state art) —
        // drawn once, never transformed.
        if (background is not null)
        {
            using var bgImage = SKImage.FromBitmap(background);
            canvas.DrawImage(bgImage, new SKRect(0, 0, workW, workH), Cubic);
        }

        if (settings.ComponentType == ComponentType.Meter)
        {
            // Meters fill segments as the value rises; source (if any) is the
            // on-state art, revealed up to the fill; otherwise segments are procedural.
            RenderMeterFrame(canvas, settings, source, frameIndex, px, workW, workH);
        }
        else
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source), "A source image is required for non-meter components.");

            var tf = ComputeTransform(settings, source, frameIndex);
            using var srcImage = SKImage.FromBitmap(source);
            canvas.Save();

            // Rotate about the pivot (a no-op when RotateDegrees == 0).
            float pivotX = tf.PivotX * (float)px;
            float pivotY = tf.PivotY * (float)px;
            canvas.Translate(pivotX, pivotY);
            canvas.RotateDegrees(tf.RotateDegrees);
            canvas.Translate(-pivotX, -pivotY);

            var srcRect = new SKRect(0, 0, source.Width, source.Height);
            var dstRect = new SKRect(
                tf.TranslateX * (float)px,
                tf.TranslateY * (float)px,
                (tf.TranslateX + tf.DrawWidth) * (float)px,
                (tf.TranslateY + tf.DrawHeight) * (float)px);

            canvas.DrawImage(srcImage, srcRect, dstRect, Cubic);
            canvas.Restore();
        }

        // Downsample the oversampled frame to the target size. When ss == 1 this
        // is simply a 1:1 copy and costs almost nothing.
        using var workImage = work.Snapshot();
        var result = new SKBitmap(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var resultCanvas = new SKCanvas(result))
        {
            resultCanvas.Clear(SKColors.Transparent);
            resultCanvas.DrawImage(workImage, new SKRect(0, 0, targetW, targetH), Cubic);
        }
        return result;
    }

    public SKBitmap RenderStrip(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, double scale = 1.0)
    {
        int n = Math.Max(1, settings.FrameCount);
        int fw = Math.Max(1, (int)Math.Round(settings.FrameWidth * scale));
        int fh = Math.Max(1, (int)Math.Round(settings.FrameHeight * scale));

        bool vertical = settings.StackDirection == StackDirection.Vertical;
        int stripW = vertical ? fw : fw * n;
        int stripH = vertical ? fh * n : fh;

        var strip = new SKBitmap(stripW, stripH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(strip))
        {
            canvas.Clear(SKColors.Transparent);

            // Render frame-by-frame so only one oversampled frame is in memory at a
            // time — important for large XL strips that can be tens of thousands of
            // pixels tall.
            for (int i = 0; i < n; i++)
            {
                using var frame = RenderFrame(settings, source, background, i, scale);
                int x = vertical ? 0 : i * fw;
                int y = vertical ? i * fh : 0;
                canvas.DrawBitmap(frame, x, y); // 1:1 blit, no resampling needed
            }
        }
        return strip;
    }

    /// <summary>Aspect-preserving "contain" fit of a source into a box.</summary>
    private static (float Width, float Height) Contain(float srcW, float srcH, float boxW, float boxH)
    {
        if (srcW <= 0 || srcH <= 0) return (boxW, boxH);
        float scale = Math.Min(boxW / srcW, boxH / srcH);
        return (srcW * scale, srcH * scale);
    }

    // ---- meter ----

    /// <summary>
    /// Composes one meter frame into the (already background-filled) work canvas.
    /// With <paramref name="onArt"/> present it reveals that on-state art up to the
    /// fill level (layered); otherwise it draws procedural on/off segment bars.
    /// </summary>
    private static void RenderMeterFrame(SKCanvas canvas, FilmstripSettings settings, SKBitmap? onArt,
                                         int frameIndex, double px, int workW, int workH)
    {
        int n = Math.Max(1, settings.FrameCount);
        double t = n > 1 ? (double)frameIndex / (n - 1) : 0.0;
        int segments = Math.Max(1, settings.SegmentCount);

        // Fraction of the axis that is lit. Discrete snaps to whole segments.
        double fill = settings.ContinuousFill ? t : Math.Round(t * segments) / segments;
        fill = Math.Clamp(fill, 0.0, 1.0);
        var fillRect = FillRect(settings.FillDirection, (float)fill, workW, workH);

        if (onArt is not null)
        {
            // Layered: reveal the on-state art up to the fill. The off-state art, if
            // supplied, was already drawn full as the background.
            using var onImage = SKImage.FromBitmap(onArt);
            canvas.Save();
            canvas.ClipRect(fillRect);
            canvas.DrawImage(onImage, new SKRect(0, 0, workW, workH), Cubic);
            canvas.Restore();
            return;
        }

        // Procedural: draw every segment off, then overlay the on colour clipped to
        // the fill region (whole segments snap when discrete; the boundary segment is
        // partially lit when continuous). Gaps stay transparent.
        bool vertical = settings.FillDirection is MeterFillDirection.Up or MeterFillDirection.Down;
        float axisLen = vertical ? workH : workW;
        float gap = (float)(settings.SegmentGap * px);
        float segLen = Math.Max(1f, (axisLen - gap * (segments - 1)) / segments);

        using var offPaint = new SKPaint { Color = FromArgb(settings.OffColorArgb), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var onPaint = new SKPaint { Color = FromArgb(settings.OnColorArgb), IsAntialias = true, Style = SKPaintStyle.Fill };

        for (int k = 0; k < segments; k++)
            canvas.DrawRect(SegmentRect(k, vertical, segLen, gap, workW, workH), offPaint);

        canvas.Save();
        canvas.ClipRect(fillRect);
        for (int k = 0; k < segments; k++)
            canvas.DrawRect(SegmentRect(k, vertical, segLen, gap, workW, workH), onPaint);
        canvas.Restore();
    }

    /// <summary>The lit region of the frame for a given fill fraction and direction.</summary>
    private static SKRect FillRect(MeterFillDirection direction, float fill, int workW, int workH) => direction switch
    {
        MeterFillDirection.Up => new SKRect(0, workH * (1 - fill), workW, workH),
        MeterFillDirection.Down => new SKRect(0, 0, workW, workH * fill),
        MeterFillDirection.LeftToRight => new SKRect(0, 0, workW * fill, workH),
        MeterFillDirection.RightToLeft => new SKRect(workW * (1 - fill), 0, workW, workH),
        _ => new SKRect(0, 0, workW, workH),
    };

    /// <summary>The rectangle of procedural segment <paramref name="k"/> along the fill axis.</summary>
    private static SKRect SegmentRect(int k, bool vertical, float segLen, float gap, int workW, int workH)
    {
        float start = k * (segLen + gap);
        return vertical
            ? new SKRect(0, start, workW, start + segLen)
            : new SKRect(start, 0, start + segLen, workH);
    }

    /// <summary>Unpacks a <c>0xAARRGGBB</c> value into an <see cref="SKColor"/>.</summary>
    private static SKColor FromArgb(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
