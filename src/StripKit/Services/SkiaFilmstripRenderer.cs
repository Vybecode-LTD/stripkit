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
                // The art keeps its natural (aspect-fit, rectangle-centred) position in the
                // cell — it is NOT moved. We only rotate about its *content* centre
                // (SourceCenterX/Y within the drawn art) so an off-centre knob spins in
                // place instead of orbiting. (0.5, 0.5) pivots at the frame centre = the
                // classic behaviour. Give knob art ~10% transparent margin so corners
                // don't clip as it rotates.
                var (drawW, drawH) = Contain(source.Width, source.Height, fw, fh);
                float drawX = (fw - drawW) / 2f;
                float drawY = (fh - drawH) / 2f;

                double angle = settings.StartAngleDegrees
                             + (settings.EndAngleDegrees - settings.StartAngleDegrees) * t;

                // Pivot = the marked content point in frame coordinates (+ manual nudge).
                float pivotX = drawX + (float)settings.SourceCenterX * drawW + (float)settings.PivotOffsetX;
                float pivotY = drawY + (float)settings.SourceCenterY * drawH + (float)settings.PivotOffsetY;

                return new FrameTransform(drawX, drawY, drawW, drawH, (float)angle, pivotX, pivotY);
            }

            case ComponentType.VerticalFader:
            {
                // The source is the cap. It keeps its native size and slides
                // vertically: frame 0 (min) at the bottom, last frame (max) at top.
                // SourceCenterX centres the cap's content on the cross (X) axis.
                float capW = source.Width;
                float capH = source.Height;
                float x = fw / 2f - (float)settings.SourceCenterX * capW + (float)settings.CapCrossOffset;

                float yBottom = fh - (float)settings.EdgeMargin - capH; // min
                float yTop = (float)settings.EdgeMargin;                 // max
                float y = (float)(yBottom + (yTop - yBottom) * t);

                return new FrameTransform(x, y, capW, capH, 0f, 0f, 0f);
            }

            case ComponentType.HorizontalSlider:
            {
                // Cap slides horizontally: min at the left, max at the right.
                // SourceCenterY centres the cap's content on the cross (Y) axis.
                float capW = source.Width;
                float capH = source.Height;
                float y = fh / 2f - (float)settings.SourceCenterY * capH + (float)settings.CapCrossOffset;

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

    public SKBitmap RenderFrame(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, int frameIndex,
                                double scale = 1.0, IReadOnlyList<SKBitmap>? layerArt = null)
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
        else if (settings.ComponentType == ComponentType.RotaryKnob
                 && settings.Layers.Count > 0 && layerArt is { Count: > 0 })
        {
            // Layer-aware knob: a static base body + a rotating pointer (and any further
            // tagged layers), composited bottom-first. The single `source` is unused here.
            var arcTf = RenderLayers(canvas, settings, layerArt, frameIndex, px);
            if (settings.ShowValueArc)
                RenderValueArc(canvas, settings, arcTf, frameIndex, px, workW, workH);
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

            // Composite the value arc on top of the rotated art (knobs only).
            if (settings.ShowValueArc && settings.ComponentType == ComponentType.RotaryKnob)
                RenderValueArc(canvas, settings, tf, frameIndex, px, workW, workH);
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

    public SKBitmap RenderStrip(FilmstripSettings settings, SKBitmap? source, SKBitmap? background,
                                double scale = 1.0, IReadOnlyList<SKBitmap>? layerArt = null)
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
                using var frame = RenderFrame(settings, source, background, i, scale, layerArt);
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

    // ---- layered knob (base + pointer) ----

    /// <summary>
    /// Composites a layered knob into the work canvas: each layer is "contain"-fit and
    /// centred in the cell like a single knob source; a <see cref="LayerBehavior.Static"/>
    /// layer (the body) is drawn fixed, a <see cref="LayerBehavior.Rotate"/> layer (the
    /// pointer) spins about its own pivot by the per-frame angle. Returns a transform
    /// carrying the knob centre (the static body's content centre) so an optional value arc
    /// stays concentric with it. <paramref name="layerArt"/> is index-matched to
    /// <see cref="FilmstripSettings.Layers"/>; both are guaranteed non-empty by the caller.
    /// </summary>
    private FrameTransform RenderLayers(SKCanvas canvas, FilmstripSettings settings,
                                        IReadOnlyList<SKBitmap> layerArt, int frameIndex, double px)
    {
        float fw = settings.FrameWidth;
        float fh = settings.FrameHeight;

        int n = Math.Max(1, settings.FrameCount);
        double t = n > 1 ? (double)frameIndex / (n - 1) : 0.0;
        float angle = (float)(settings.StartAngleDegrees
                              + (settings.EndAngleDegrees - settings.StartAngleDegrees) * t);

        // Default knob centre (for the value arc) is the cell centre; a static base layer
        // overrides it with the body's content centre below.
        float knobCx = fw / 2f, knobCy = fh / 2f;

        int count = Math.Min(settings.Layers.Count, layerArt.Count);
        for (int i = 0; i < count; i++)
        {
            var layer = settings.Layers[i];
            var art = layerArt[i];
            if (art is null || art.Width <= 0 || art.Height <= 0) continue;

            // Same contain-fit + centring the single source gets, so layers authored at the
            // same canvas size overlay exactly (and a differently-sized one stays undistorted).
            var (drawW, drawH) = Contain(art.Width, art.Height, fw, fh);
            float drawX = (fw - drawW) / 2f;
            float drawY = (fh - drawH) / 2f;

            using var img = SKImage.FromBitmap(art);
            var srcRect = new SKRect(0, 0, art.Width, art.Height);
            var dstRect = new SKRect(drawX * (float)px, drawY * (float)px,
                                     (drawX + drawW) * (float)px, (drawY + drawH) * (float)px);

            if (layer.Behavior == LayerBehavior.Rotate)
            {
                // Spin about this layer's own pivot (normalized within its drawn art).
                float pivotX = (drawX + (float)layer.PivotX * drawW) * (float)px;
                float pivotY = (drawY + (float)layer.PivotY * drawH) * (float)px;
                canvas.Save();
                canvas.Translate(pivotX, pivotY);
                canvas.RotateDegrees(angle);
                canvas.Translate(-pivotX, -pivotY);
                canvas.DrawImage(img, srcRect, dstRect, Cubic);
                canvas.Restore();
            }
            else // Static body — drawn fixed; it defines the knob centre for the value arc.
            {
                knobCx = drawX + (float)settings.SourceCenterX * drawW;
                knobCy = drawY + (float)settings.SourceCenterY * drawH;
                canvas.DrawImage(img, srcRect, dstRect, Cubic);
            }
        }

        return new FrameTransform(0f, 0f, fw, fh, angle, knobCx, knobCy);
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

    // ---- value arc ----

    /// <summary>
    /// Composites a value-tracking fill arc onto a knob frame (Serum/Vital style). The lit
    /// arc sweeps from the start angle to the current frame's angle, concentric with the
    /// rotation pivot; an optional dim track shows the unfilled remainder, with optional
    /// sweep gradient and glow. Drawn into the oversampled work surface, so it stays crisp.
    /// </summary>
    private static void RenderValueArc(SKCanvas canvas, FilmstripSettings settings, FrameTransform tf,
                                       int frameIndex, double px, int workW, int workH)
    {
        int n = Math.Max(1, settings.FrameCount);
        double t = n > 1 ? (double)frameIndex / (n - 1) : 0.0;

        // Concentric with the knob's rotation pivot (its content centre + any nudge).
        float cx = tf.PivotX * (float)px;
        float cy = tf.PivotY * (float)px;

        float inscribed = Math.Min(workW, workH) / 2f;
        float radius = (float)settings.ArcRadius * inscribed;
        if (radius <= 0f) return;

        float thickness = Math.Max(0.5f, (float)(settings.ArcThickness * px));
        var oval = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        var cap = settings.ArcRoundCaps ? SKStrokeCap.Round : SKStrokeCap.Butt;

        // App angles: 0 = up (12 o'clock), positive = clockwise. Skia arc angles: 0 = 3
        // o'clock. Convert by -90; the -90 cancels in the sweep delta, so the lit fill is
        // the same fraction of the rotation range the frame represents.
        float fullSweep = (float)(settings.EndAngleDegrees - settings.StartAngleDegrees);
        float skiaStart = (float)settings.StartAngleDegrees - 90f;
        float litSweep = (float)(fullSweep * t);

        // Dim full-sweep track behind the lit fill.
        if (settings.ArcTrack)
        {
            using var trackPaint = StrokePaint(FromArgb(settings.ArcTrackColorArgb), thickness, cap);
            canvas.DrawArc(oval, skiaStart, fullSweep, false, trackPaint);
        }

        // Glow: a blurred under-stroke of the lit portion in the arc colour.
        if (settings.ArcGlow && settings.ArcGlowSize > 0)
        {
            float sigma = (float)(settings.ArcGlowSize * px) * 0.5f;
            using var glowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
            using var glowPaint = StrokePaint(FromArgb(settings.ArcColorArgb), thickness, cap);
            glowPaint.MaskFilter = glowBlur;
            canvas.DrawArc(oval, skiaStart, litSweep, false, glowPaint);
        }

        // The lit fill: a solid colour, or a sweep gradient across the rotation range.
        using var litPaint = StrokePaint(FromArgb(settings.ArcColorArgb), thickness, cap);
        if (settings.ArcGradient)
        {
            float gA = skiaStart, gB = skiaStart + fullSweep;
            if (gB < gA) (gA, gB) = (gB, gA);  // CreateSweepGradient wants ascending angles
            litPaint.Shader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                new[] { FromArgb(settings.ArcColorArgb), FromArgb(settings.ArcColor2Argb) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp, gA, gB);
        }

        canvas.DrawArc(oval, skiaStart, litSweep, false, litPaint);
        litPaint.Shader?.Dispose();
    }

    /// <summary>A stroked, antialiased paint with the given colour, width and end cap.</summary>
    private static SKPaint StrokePaint(SKColor color, float thickness, SKStrokeCap cap) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = thickness,
        StrokeCap = cap,
    };

    /// <summary>Unpacks a <c>0xAARRGGBB</c> value into an <see cref="SKColor"/>.</summary>
    private static SKColor FromArgb(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
