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
//      <PackageReference Include="SkiaSharp" Version="3.119.2" />
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
//  MIT License — Copyright (c) 2026 VybeCode Software. See LICENSE at https://github.com/Vybecode-LTD/stripkit
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
    /// <summary>Discrete-state button: each frame = one state (off / on / …).</summary>
    Button,
    /// <summary>On/off toggle switch — 2 frames (off / on), rendered like a 2-state Button.</summary>
    Toggle,
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

/// <summary>How a render layer animates across the strip's frames.</summary>
public enum LayerBehavior
{
    /// <summary>Drawn fixed in every frame (a knob body / well). Never transformed.</summary>
    Static,
    /// <summary>Rotated per-frame about its pivot, following the knob's angle sweep (the pointer).</summary>
    Rotate,
    /// <summary>Shown only on the frame whose index matches this layer's index. Used for button states.</summary>
    Frame,
}

/// <summary>One layer of a layered control render: its behaviour and (for a rotating layer) the
/// normalized pivot within its own drawn art. The bitmap is passed alongside to the renderer.</summary>
public sealed class RenderLayer
{
    public LayerBehavior Behavior { get; set; } = LayerBehavior.Rotate;
    /// <summary>Normalized (0..1) horizontal rotation pivot within this layer's drawn art.</summary>
    public double PivotX { get; set; } = 0.5;
    /// <summary>Normalized (0..1) vertical rotation pivot within this layer's drawn art.</summary>
    public double PivotY { get; set; } = 0.5;
    public RenderLayer Clone() => (RenderLayer)MemberwiseClone();
}

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

    /// <summary>Pivot offset from the frame centre, in 1x pixels (advanced nudge).</summary>
    public double PivotOffsetX { get; set; }
    public double PivotOffsetY { get; set; }

    // ---- Content alignment (all art types) ----
    /// <summary>Normalized (0..1) visual centre of the art within the source image;
    /// (0.5, 0.5) = plain rectangle centring. A knob is re-centred on this point and
    /// rotates about it; a fader/slider cap uses it for cross-axis centring.</summary>
    public double SourceCenterX { get; set; } = 0.5;
    public double SourceCenterY { get; set; } = 0.5;

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

    // ---- Value arc / fill ring (rotary knob only) ----
    /// <summary>Composite a Serum/Vital-style value-tracking fill arc onto each knob frame
    /// (off by default; ignored for non-knob types).</summary>
    public bool ShowValueArc { get; set; }
    /// <summary>Arc radius as a fraction of the frame's inscribed radius (½·min(w,h)).</summary>
    public double ArcRadius { get; set; } = 0.88;
    /// <summary>Arc stroke thickness, in 1x pixels.</summary>
    public double ArcThickness { get; set; } = 4.0;
    /// <summary>Round (true) vs butt (false) arc end caps.</summary>
    public bool ArcRoundCaps { get; set; } = true;
    /// <summary>Lit-arc colour, 0xAARRGGBB.</summary>
    public uint ArcColorArgb { get; set; } = 0xFFE8440A;
    /// <summary>Sweep-gradient the lit arc from <see cref="ArcColorArgb"/> to <see cref="ArcColor2Argb"/>.</summary>
    public bool ArcGradient { get; set; }
    /// <summary>Far end of the arc gradient, 0xAARRGGBB.</summary>
    public uint ArcColor2Argb { get; set; } = 0xFFFFC107;
    /// <summary>Draw a dim full-sweep track behind the lit fill.</summary>
    public bool ArcTrack { get; set; } = true;
    /// <summary>Track colour, 0xAARRGGBB.</summary>
    public uint ArcTrackColorArgb { get; set; } = 0x33FFFFFF;
    /// <summary>Give the lit arc a soft glow (a blurred under-stroke).</summary>
    public bool ArcGlow { get; set; }
    /// <summary>Glow blur size, in 1x pixels.</summary>
    public double ArcGlowSize { get; set; } = 6.0;

    // ---- Layered animation (knob: base + pointer) ----
    /// <summary>Ordered layer stack (bottom-first) for a layered knob. Empty (default) renders
    /// the single source as before; when non-empty for a knob the renderer composites these
    /// layers — bitmaps passed alongside, index-matched — a static body + a rotating pointer.</summary>
    public List<RenderLayer> Layers { get; set; } = new();

    /// <summary>A deep copy — the <see cref="Layers"/> list is cloned, not shared.</summary>
    public FilmstripSettings Clone()
    {
        var copy = (FilmstripSettings)MemberwiseClone();
        copy.Layers = Layers.Select(l => l.Clone()).ToList();
        return copy;
    }
}

/// <summary>Computes per-frame transforms and renders filmstrips.</summary>
public interface IFilmstripRenderer
{
    FrameTransform ComputeTransform(FilmstripSettings settings, SKBitmap source, int frameIndex);
    SKBitmap RenderFrame(FilmstripSettings settings, SKBitmap? source, SKBitmap? background, int frameIndex,
                         double scale = 1.0, IReadOnlyList<SKBitmap>? layerArt = null);
    SKBitmap RenderStrip(FilmstripSettings settings, SKBitmap? source, SKBitmap? background,
                         double scale = 1.0, IReadOnlyList<SKBitmap>? layerArt = null);
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
                // Position the art so its content centre (SourceCenterX/Y within the art) lands
                // on the frame centre — an off-centre knob is genuinely centred, not just spun in
                // place. At (0.5, 0.5) this equals the classic rectangle-centred placement, so
                // output is byte-identical; only off-centre sources move. We then pivot on that
                // same point (= the frame centre).
                var (drawW, drawH) = Contain(source.Width, source.Height, fw, fh);
                float drawX = fw / 2f - (float)settings.SourceCenterX * drawW;
                float drawY = fh / 2f - (float)settings.SourceCenterY * drawH;

                double angle = settings.StartAngleDegrees
                             + (settings.EndAngleDegrees - settings.StartAngleDegrees) * t;

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
                // Meters are composed in RenderFrame's segment-fill path; identity here.
                return new FrameTransform(0f, 0f, fw, fh, 0f, fw / 2f, fh / 2f);

            case ComponentType.Button:
            case ComponentType.Toggle:
            {
                // Buttons/toggles render discrete state art per frame (no movement). Center-fit the source.
                var (drawW, drawH) = Contain(source.Width, source.Height, fw, fh);
                float drawX = (fw - drawW) / 2f;
                float drawY = (fh - drawH) / 2f;
                return new FrameTransform(drawX, drawY, drawW, drawH, 0f, fw / 2f, fh / 2f);
            }

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
        else if (settings.ComponentType == ComponentType.RotaryKnob
                 && settings.Layers.Count > 0 && layerArt is { Count: > 0 })
        {
            // Layer-aware knob: a static base body + a rotating pointer, composited
            // bottom-first. The single `source` is unused here.
            var arcTf = RenderLayers(canvas, settings, layerArt, frameIndex, px);
            if (settings.ShowValueArc)
                RenderValueArc(canvas, settings, arcTf, frameIndex, px, workW, workH);
        }
        else if ((settings.ComponentType == ComponentType.Button || settings.ComponentType == ComponentType.Toggle)
                 && settings.Layers.Count > 0 && layerArt is { Count: > 0 })
        {
            RenderButtonLayers(canvas, settings, layerArt, frameIndex, px);
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

            // Render frame-by-frame so only one oversampled frame is in memory at
            // a time — important for large XL strips tens of thousands of px tall.
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

    /// <summary>Composites a layered knob: each layer is "contain"-fit and centred in the cell;
    /// a <see cref="LayerBehavior.Static"/> layer (body) is fixed, a <see cref="LayerBehavior.Rotate"/>
    /// layer (pointer) spins about its own pivot by the per-frame angle. Returns a transform
    /// carrying the knob centre (the static body's content centre) for an optional value arc.</summary>
    private FrameTransform RenderLayers(SKCanvas canvas, FilmstripSettings settings,
                                        IReadOnlyList<SKBitmap> layerArt, int frameIndex, double px)
    {
        float fw = settings.FrameWidth;
        float fh = settings.FrameHeight;

        int n = Math.Max(1, settings.FrameCount);
        double t = n > 1 ? (double)frameIndex / (n - 1) : 0.0;
        float angle = (float)(settings.StartAngleDegrees
                              + (settings.EndAngleDegrees - settings.StartAngleDegrees) * t);

        float knobCx = fw / 2f, knobCy = fh / 2f;

        int count = Math.Min(settings.Layers.Count, layerArt.Count);
        for (int i = 0; i < count; i++)
        {
            var layer = settings.Layers[i];
            var art = layerArt[i];
            if (art is null || art.Width <= 0 || art.Height <= 0) continue;

            var (drawW, drawH) = Contain(art.Width, art.Height, fw, fh);
            float drawX = (fw - drawW) / 2f;
            float drawY = (fh - drawH) / 2f;

            using var img = SKImage.FromBitmap(art);
            var srcRect = new SKRect(0, 0, art.Width, art.Height);
            var dstRect = new SKRect(drawX * (float)px, drawY * (float)px,
                                     (drawX + drawW) * (float)px, (drawY + drawH) * (float)px);

            if (layer.Behavior == LayerBehavior.Rotate)
            {
                float pivotX = (drawX + (float)layer.PivotX * drawW) * (float)px;
                float pivotY = (drawY + (float)layer.PivotY * drawH) * (float)px;
                canvas.Save();
                canvas.Translate(pivotX, pivotY);
                canvas.RotateDegrees(angle);
                canvas.Translate(-pivotX, -pivotY);
                canvas.DrawImage(img, srcRect, dstRect, Cubic);
                canvas.Restore();
            }
            else // Static body — fixed; defines the knob centre for the value arc.
            {
                knobCx = drawX + (float)settings.SourceCenterX * drawW;
                knobCy = drawY + (float)settings.SourceCenterY * drawH;
                canvas.DrawImage(img, srcRect, dstRect, Cubic);
            }
        }

        return new FrameTransform(0f, 0f, fw, fh, angle, knobCx, knobCy);
    }

    // ---- button (discrete states) ----

    private static void RenderButtonLayers(SKCanvas canvas, FilmstripSettings settings,
                                           IReadOnlyList<SKBitmap> layerArt, int frameIndex, double px)
    {
        float fw = settings.FrameWidth;
        float fh = settings.FrameHeight;
        int count = Math.Min(settings.Layers.Count, layerArt.Count);

        for (int i = 0; i < count; i++)
        {
            var layer = settings.Layers[i];
            var art = layerArt[i];
            if (art is null || art.Width <= 0 || art.Height <= 0) continue;

            bool show = layer.Behavior == LayerBehavior.Static
                     || (layer.Behavior == LayerBehavior.Frame && i == frameIndex);
            if (!show) continue;

            var (drawW, drawH) = Contain(art.Width, art.Height, fw, fh);
            float drawX = (fw - drawW) / 2f;
            float drawY = (fh - drawH) / 2f;

            using var img = SKImage.FromBitmap(art);
            canvas.DrawImage(img,
                new SKRect(0, 0, art.Width, art.Height),
                new SKRect(drawX * (float)px, drawY * (float)px,
                           (drawX + drawW) * (float)px, (drawY + drawH) * (float)px),
                Cubic);
        }
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

    // ---- value arc ----

    /// <summary>Composites a value-tracking fill arc onto a knob frame (Serum/Vital style):
    /// the lit arc sweeps from the start angle to the current frame's angle, concentric with
    /// the rotation pivot, with optional dim track, sweep gradient, and glow.</summary>
    private static void RenderValueArc(SKCanvas canvas, FilmstripSettings settings, FrameTransform tf,
                                       int frameIndex, double px, int workW, int workH)
    {
        int n = Math.Max(1, settings.FrameCount);
        double t = n > 1 ? (double)frameIndex / (n - 1) : 0.0;

        float cx = tf.PivotX * (float)px;
        float cy = tf.PivotY * (float)px;

        float inscribed = Math.Min(workW, workH) / 2f;
        float radius = (float)settings.ArcRadius * inscribed;
        if (radius <= 0f) return;

        float thickness = Math.Max(0.5f, (float)(settings.ArcThickness * px));
        var oval = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        var cap = settings.ArcRoundCaps ? SKStrokeCap.Round : SKStrokeCap.Butt;

        // App angles: 0 = up (12 o'clock), + = clockwise. Skia arc: 0 = 3 o'clock. Convert
        // by -90; the -90 cancels in the sweep delta.
        float fullSweep = (float)(settings.EndAngleDegrees - settings.StartAngleDegrees);
        float skiaStart = (float)settings.StartAngleDegrees - 90f;
        float litSweep = (float)(fullSweep * t);

        if (settings.ArcTrack)
        {
            using var trackPaint = StrokePaint(FromArgb(settings.ArcTrackColorArgb), thickness, cap);
            canvas.DrawArc(oval, skiaStart, fullSweep, false, trackPaint);
        }

        if (settings.ArcGlow && settings.ArcGlowSize > 0)
        {
            float sigma = (float)(settings.ArcGlowSize * px) * 0.5f;
            using var glowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
            using var glowPaint = StrokePaint(FromArgb(settings.ArcColorArgb), thickness, cap);
            glowPaint.MaskFilter = glowBlur;
            canvas.DrawArc(oval, skiaStart, litSweep, false, glowPaint);
        }

        using var litPaint = StrokePaint(FromArgb(settings.ArcColorArgb), thickness, cap);
        if (settings.ArcGradient)
        {
            float gA = skiaStart, gB = skiaStart + fullSweep;
            if (gB < gA) (gA, gB) = (gB, gA);
            litPaint.Shader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                new[] { FromArgb(settings.ArcColorArgb), FromArgb(settings.ArcColor2Argb) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp, gA, gB);
        }

        canvas.DrawArc(oval, skiaStart, litSweep, false, litPaint);
        litPaint.Shader?.Dispose();
    }

    private static SKPaint StrokePaint(SKColor color, float thickness, SKStrokeCap cap) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = thickness,
        StrokeCap = cap,
    };
}
