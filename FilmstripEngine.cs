// =============================================================================
//  FilmstripEngine.cs
//
//  A standalone, UI-agnostic engine that turns a single transparent source image
//  into an animated control filmstrip (sprite sheet) for audio-plugin GUIs:
//  rotary knobs, vertical faders, and horizontal sliders.
//
//  This is the reusable core extracted from the StripKit desktop tool. It
//  has NO Avalonia dependency — its only dependency is SkiaSharp 3.x — so it
//  drops straight into a CLI, a build step, a web backend, or another app.
//
//  Dependency:
//      <PackageReference Include="SkiaSharp" Version="3.119.0" />
//
//  The whole engine is one idea: for each of N frames, place the source art in a
//  fixed-size frame cell under a per-frame transform, then stack the cells into a
//  single PNG. Knobs rotate the art about a pivot; faders/sliders translate it.
//  Supersampling + a Mitchell cubic resampler keep rotated edges crisp.
//
//  Minimal usage:
//
//      using StripKit.Engine;
//      using SkiaSharp;
//
//      using var source = SKBitmap.Decode("knob_cap.png");      // pointer art, 12 o'clock
//      var settings = new FilmstripSettings
//      {
//          ComponentType = ComponentType.RotaryKnob,
//          FrameCount    = 64,
//          FrameWidth    = 80,
//          FrameHeight   = 80,
//          Supersample   = 4,
//          // 270-degree sweep, frame 0 = min:
//          StartAngleDegrees = -135,
//          EndAngleDegrees   =  135,
//      };
//
//      var renderer = new SkiaFilmstripRenderer();
//      using var strip = renderer.RenderStrip(settings, source, background: null, scale: 1.0);
//      using var img   = SKImage.FromBitmap(strip);
//      using var data  = img.Encode(SKEncodedImageFormat.Png, 100);
//      using var fs    = File.Create("knob_64frames.png");
//      data.SaveTo(fs);
//
//  License: use freely within your own projects. Origin: VybeCod.ing / StripKit.
// =============================================================================

using SkiaSharp;

namespace StripKit.Engine;

/// <summary>The kind of control a filmstrip drives.</summary>
public enum ComponentType
{
    RotaryKnob,
    VerticalFader,
    HorizontalSlider,
    Meter,
}

/// <summary>The direction a meter fills as its value rises from 0 to 1.</summary>
public enum MeterFillDirection
{
    Up,
    Down,
    LeftToRight,
    RightToLeft,
}

/// <summary>How frames are laid out in the exported PNG.</summary>
public enum StackDirection
{
    Vertical,
    Horizontal,
}

/// <summary>
/// Per-frame placement of the source layer inside one frame cell, in 1x frame
/// units (before export scale / supersampling). The renderer scales these up.
/// </summary>
public readonly record struct FrameTransform(
    float TranslateX,
    float TranslateY,
    float DrawWidth,
    float DrawHeight,
    float RotateDegrees,
    float PivotX,
    float PivotY);

/// <summary>The complete description of a filmstrip render. Pure data, no deps.</summary>
public sealed class FilmstripSettings
{
    public ComponentType ComponentType { get; set; } = ComponentType.RotaryKnob;

    /// <summary>Number of frames. 64 is standard; 32 small, 128 large.</summary>
    public int FrameCount { get; set; } = 64;

    /// <summary>Width of one frame cell, in 1x pixels.</summary>
    public int FrameWidth { get; set; } = 80;

    /// <summary>Height of one frame cell, in 1x pixels.</summary>
    public int FrameHeight { get; set; } = 80;

    // ---- Rotary knob ----
    /// <summary>Rotation of frame 0 (the minimum), in degrees, clockwise.</summary>
    public double StartAngleDegrees { get; set; } = -135.0;

    /// <summary>Rotation of the final frame (the maximum), in degrees, clockwise.</summary>
    public double EndAngleDegrees { get; set; } = 135.0;

    /// <summary>Pivot offset from the frame centre, in 1x pixels.</summary>
    public double PivotOffsetX { get; set; }
    public double PivotOffsetY { get; set; }

    // ---- Linear fader / slider ----
    /// <summary>Gap, in 1x pixels, left at each end of the cap's travel.</summary>
    public double EdgeMargin { get; set; } = 4.0;

    /// <summary>Offset of the cap on its non-travel (cross) axis, in 1x pixels.</summary>
    public double CapCrossOffset { get; set; }

    // ---- Quality / output ----
    /// <summary>Internal oversampling factor for anti-aliasing (1, 2, 4, or 8).</summary>
    public int Supersample { get; set; } = 4;

    public StackDirection StackDirection { get; set; } = StackDirection.Vertical;

    // ---- Meter ----
    /// <summary>Number of segments (LED bars); fill snaps to these unless continuous.</summary>
    public int SegmentCount { get; set; } = 12;
    public MeterFillDirection FillDirection { get; set; } = MeterFillDirection.Up;
    /// <summary>Smooth fill when true; snap to whole segments when false.</summary>
    public bool ContinuousFill { get; set; }
    /// <summary>Gap between procedural segments, in 1x pixels.</summary>
    public double SegmentGap { get; set; } = 3.0;
    /// <summary>Procedural lit-segment colour, 0xAARRGGBB.</summary>
    public uint OnColorArgb { get; set; } = 0xFFE8440A;
    /// <summary>Procedural unlit-segment colour, 0xAARRGGBB.</summary>
    public uint OffColorArgb { get; set; } = 0xFF2A2A2A;

    public FilmstripSettings Clone() => (FilmstripSettings)MemberwiseClone();
}

/// <summary>Computes per-frame transforms and renders filmstrips.</summary>
public interface IFilmstripRenderer
{
    FrameTransform ComputeTransform(FilmstripSettings settings, SKBitmap source, int frameIndex);
    SKBitmap RenderFrame(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, int frameIndex, double scale = 1.0);
    SKBitmap RenderStrip(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, double scale = 1.0);
}

/// <summary>SkiaSharp implementation of the filmstrip renderer.</summary>
public sealed class SkiaFilmstripRenderer : IFilmstripRenderer
{
    // Mitchell cubic: smooth, low-ringing results for both rotation and downscale.
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
                // Fit the art (aspect-preserving, centred) into the frame, then
                // rotate about the pivot. Give knob art ~10% transparent margin
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
                // Meters are composed in RenderFrame's segment-fill path; identity here.
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

        // Static background (a knob well, a fader track, or a meter's off-state art)
        // — drawn once, never transformed.
        if (background is not null)
        {
            using var bgImage = SKImage.FromBitmap(background);
            canvas.DrawImage(bgImage, new SKRect(0, 0, workW, workH), Cubic);
        }

        if (settings.ComponentType == ComponentType.Meter)
        {
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
        // is a 1:1 copy and costs almost nothing.
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

            // Render frame-by-frame so only one oversampled frame is in memory at
            // a time — important for large XL strips tens of thousands of px tall.
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

    private static void RenderMeterFrame(SKCanvas canvas, FilmstripSettings settings, SKBitmap? onArt,
                                         int frameIndex, double px, int workW, int workH)
    {
        int n = Math.Max(1, settings.FrameCount);
        double t = n > 1 ? (double)frameIndex / (n - 1) : 0.0;
        int segments = Math.Max(1, settings.SegmentCount);

        double fill = settings.ContinuousFill ? t : Math.Round(t * segments) / segments;
        fill = Math.Clamp(fill, 0.0, 1.0);
        var fillRect = FillRect(settings.FillDirection, (float)fill, workW, workH);

        if (onArt is not null)
        {
            // Layered: reveal the on-state art up to the fill (off art is the background).
            using var onImage = SKImage.FromBitmap(onArt);
            canvas.Save();
            canvas.ClipRect(fillRect);
            canvas.DrawImage(onImage, new SKRect(0, 0, workW, workH), Cubic);
            canvas.Restore();
            return;
        }

        // Procedural: every segment off, then on-colour clipped to the fill region.
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

    private static SKRect FillRect(MeterFillDirection direction, float fill, int workW, int workH) => direction switch
    {
        MeterFillDirection.Up => new SKRect(0, workH * (1 - fill), workW, workH),
        MeterFillDirection.Down => new SKRect(0, 0, workW, workH * fill),
        MeterFillDirection.LeftToRight => new SKRect(0, 0, workW * fill, workH),
        MeterFillDirection.RightToLeft => new SKRect(workW * (1 - fill), 0, workW, workH),
        _ => new SKRect(0, 0, workW, workH),
    };

    private static SKRect SegmentRect(int k, bool vertical, float segLen, float gap, int workW, int workH)
    {
        float start = k * (segLen + gap);
        return vertical
            ? new SKRect(0, start, workW, start + segLen)
            : new SKRect(start, 0, start + segLen, workH);
    }

    private static SKColor FromArgb(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
