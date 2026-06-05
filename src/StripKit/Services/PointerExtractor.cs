using SkiaSharp;

namespace StripKit.Services;

/// <summary>The two layers split out of a flat knob image, plus a confidence score.</summary>
public sealed class PointerExtractionResult
{
    /// <summary>The symmetric body with the indicator erased (the static base layer). Caller-owned.</summary>
    public required SKBitmap BaseLayer { get; init; }

    /// <summary>The extracted indicator on a transparent canvas (the rotating pointer layer). Caller-owned.</summary>
    public required SKBitmap PointerLayer { get; init; }

    /// <summary>0..1 — how cleanly the indicator separated (a small, concentrated residual scores high;
    /// a residual spread across the whole body — an asymmetric/textured knob — scores low).</summary>
    public double Confidence { get; init; }

    /// <summary>True when the extraction is unreliable (an asymmetric body), so the user should verify
    /// or fall back to loading the base + pointer by hand.</summary>
    public bool LowConfidence => Confidence < 0.5;
}

/// <summary>
/// Splits a single FLAT knob image (body + indicator baked together) into a static base layer and a
/// rotating pointer layer, for the layer-aware renderer. The principle: a knob body is rotationally
/// symmetric about its centre, so the indicator is whatever **breaks** that symmetry. We compute the
/// rotational average per radius (robustly, so the indicator's own pixels don't pollute it) — that is
/// the symmetric body, the **base** — and the residual that deviates from each radial ring's average
/// is the **pointer**. Pure SkiaSharp + BCL (no Avalonia), like <see cref="ContentAnalysis"/>; runs
/// once on load, not per frame. Best for the common round-body-with-one-indicator case.
/// </summary>
public static class PointerExtractor
{
    /// <summary>
    /// Extracts the base + pointer from <paramref name="flat"/>, rotating about the normalized centre
    /// (<paramref name="centerX"/>, <paramref name="centerY"/>) — typically the
    /// <see cref="ContentAnalysis.DetectContentCenter"/> result. Returns null for a null/empty image.
    /// </summary>
    /// <param name="colorThreshold">Euclidean RGB distance (0..441) beyond which a pixel counts as
    /// indicator rather than body. Lower = more sensitive.</param>
    public static PointerExtractionResult? Extract(SKBitmap? flat, double centerX, double centerY,
                                                   double colorThreshold = 36, byte alphaThreshold = 8)
    {
        if (flat is null || flat.Width <= 0 || flat.Height <= 0) return null;

        int w = flat.Width, h = flat.Height;
        float cx = (float)(centerX * w), cy = (float)(centerY * h);

        // Cache pixels + each pixel's integer radius from the centre (one GetPixel pass).
        var px = new SKColor[w * h];
        var rad = new int[w * h];
        int maxR = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            px[i] = flat.GetPixel(x, y);
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            int r = (int)MathF.Round(MathF.Sqrt(dx * dx + dy * dy));
            rad[i] = r;
            if (r > maxR) maxR = r;
        }
        int bins = maxR + 1;

        // Pass 1: per-radius mean over opaque pixels (includes the indicator — a first estimate).
        var s1R = new double[bins]; var s1G = new double[bins]; var s1B = new double[bins]; var n1 = new int[bins];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.Alpha <= alphaThreshold) continue;
            int r = rad[i];
            s1R[r] += c.Red; s1G[r] += c.Green; s1B[r] += c.Blue; n1[r]++;
        }
        var m1R = new double[bins]; var m1G = new double[bins]; var m1B = new double[bins];
        for (int r = 0; r < bins; r++)
            if (n1[r] > 0) { m1R[r] = s1R[r] / n1[r]; m1G[r] = s1G[r] / n1[r]; m1B[r] = s1B[r] / n1[r]; }

        // Pass 2 (robust): re-mean excluding pixels far from the pass-1 mean — i.e. drop the indicator
        // outliers, so the body estimate is the true symmetric body.
        var s2R = new double[bins]; var s2G = new double[bins]; var s2B = new double[bins]; var n2 = new int[bins];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.Alpha <= alphaThreshold) continue;
            int r = rad[i];
            if (ColorDist(c, m1R[r], m1G[r], m1B[r]) <= colorThreshold)
            { s2R[r] += c.Red; s2G[r] += c.Green; s2B[r] += c.Blue; n2[r]++; }
        }
        var mR = new double[bins]; var mG = new double[bins]; var mB = new double[bins];
        for (int r = 0; r < bins; r++)
        {
            if (n2[r] > 0) { mR[r] = s2R[r] / n2[r]; mG[r] = s2G[r] / n2[r]; mB[r] = s2B[r] / n2[r]; }
            else if (n1[r] > 0) { mR[r] = m1R[r]; mG[r] = m1G[r]; mB[r] = m1B[r]; }
        }

        // Build the base (radial mean, original silhouette) + pointer (soft residual mask × original).
        // Output bitmaps are straight-alpha (Unpremul) so SetPixel stores colours verbatim; the renderer
        // converts on DrawImage.
        var baseBmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var ptrBmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        double lowT = colorThreshold;            // mask ramp start
        double highT = colorThreshold * 3.0;     // fully indicator at/above this distance
        double residual = 0; long bodyCount = 0;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            var c = px[i];
            if (c.Alpha <= alphaThreshold)
            {
                baseBmp.SetPixel(x, y, SKColors.Transparent);
                ptrBmp.SetPixel(x, y, SKColors.Transparent);
                continue;
            }

            int r = rad[i];
            // Base: the radial-mean colour, keeping this pixel's original alpha (so the body silhouette
            // is unchanged but the indicator's colour is erased).
            baseBmp.SetPixel(x, y, new SKColor((byte)Math.Round(mR[r]), (byte)Math.Round(mG[r]),
                                               (byte)Math.Round(mB[r]), c.Alpha));

            // Pointer: how far this pixel deviates from the body → a soft 0..1 mask × the original colour.
            double d = ColorDist(c, mR[r], mG[r], mB[r]);
            double wgt = Math.Clamp((d - lowT) / Math.Max(1.0, highT - lowT), 0.0, 1.0);
            bodyCount++;
            residual += wgt;
            ptrBmp.SetPixel(x, y, wgt > 0
                ? new SKColor(c.Red, c.Green, c.Blue, (byte)Math.Round(c.Alpha * wgt))
                : SKColors.Transparent);
        }

        // A clean single indicator is a small fraction of the body; a fraction spread wide means the body
        // itself is asymmetric (low confidence). Map ~1% → 1.0 down to ~25% → 0.0.
        double fraction = bodyCount > 0 ? residual / bodyCount : 0;
        double confidence = 1.0 - Math.Clamp((fraction - 0.01) / 0.24, 0.0, 1.0);

        return new PointerExtractionResult { BaseLayer = baseBmp, PointerLayer = ptrBmp, Confidence = confidence };
    }

    private static double ColorDist(SKColor c, double r, double g, double b)
    {
        double dr = c.Red - r, dg = c.Green - g, db = c.Blue - b;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}
