using StripKit.Models;
using SkiaSharp;

namespace StripKit.Services;

/// <inheritdoc cref="IFrameSequenceAssembler"/>
public sealed class FrameSequenceAssembler : IFrameSequenceAssembler
{
    // Same ceiling philosophy as ImageLoadService's decompression-bomb guard, applied to the whole
    // assembled strip: a 512 MP output (e.g. 2048²×128 frames) is already ~2 GB of RGBA — refuse past it.
    private const long MaxOutputPixels = 512L * 1024 * 1024;

    private static readonly string[] FrameExtensions = [".png", ".webp", ".bmp", ".jpg", ".jpeg"];

    // 1:1 placement: nearest sampling, exact and cheap (frames are blitted at integer offsets).
    private static readonly SKSamplingOptions Blit = new(SKFilterMode.Nearest, SKMipmapMode.None);

    private readonly IFilmstripImporter _importer;

    public FrameSequenceAssembler(IFilmstripImporter importer) => _importer = importer;

    public SequenceProbe Probe(IReadOnlyList<string> paths, IImageLoadService loader)
    {
        var ordered = paths
            .Where(p => FrameExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetFileName(p) ?? string.Empty, NaturalFileNameComparer.Instance)
            .ToList();

        var warnings = new List<string>();
        int maxW = 0, maxH = 0, minW = int.MaxValue, minH = int.MaxValue, readable = 0;

        foreach (var p in ordered)
        {
            var (w, h) = loader.Probe(p);
            if (w <= 0 || h <= 0)
            {
                warnings.Add($"{Path.GetFileName(p)}: not a readable image — it will be skipped.");
                continue;
            }
            readable++;
            maxW = Math.Max(maxW, w); maxH = Math.Max(maxH, h);
            minW = Math.Min(minW, w); minH = Math.Min(minH, h);
        }

        if (readable == 0)
            return new SequenceProbe(ordered, 0, 0, 0, 0, true, warnings);

        bool uniform = maxW == minW && maxH == minH;
        if (!uniform)
            warnings.Add($"Frames vary in size ({minW}×{minH} to {maxW}×{maxH}) — they'll be reconciled to a common cell.");

        return new SequenceProbe(ordered, maxW, maxH, minW, minH, uniform, warnings);
    }

    public FrameSequenceResult Assemble(IReadOnlyList<SKBitmap> frames, FrameSequenceOptions options)
    {
        if (frames.Count < 2)
            throw new InvalidOperationException("A filmstrip needs at least two frames.");

        var warnings = new List<string>();

        // Render QC on the frames as rendered (before any fix) — advisory warnings only.
        warnings.AddRange(AnalyzeQc(frames).Messages);

        // Optional fix: un-premultiply alpha to remove dark edge halos. We own (and dispose) the copies.
        List<SKBitmap>? owned = options.UnpremultiplyAlpha ? frames.Select(UnpremultiplyAlpha).ToList() : null;
        var working = owned ?? frames;

        try
        {
            int maxW = working.Max(f => f.Width), maxH = working.Max(f => f.Height);
            int minW = working.Min(f => f.Width), minH = working.Min(f => f.Height);
            bool uniform = maxW == minW && maxH == minH;

            int cellW, cellH;
            switch (options.Fit)
            {
                case CellFit.Strict:
                    if (!uniform)
                        throw new InvalidOperationException(
                            $"Frames are not all the same size ({minW}×{minH} to {maxW}×{maxH}). " +
                            "Choose a pad or crop fit, or fix the render.");
                    cellW = maxW; cellH = maxH;
                    break;

                case CellFit.CropToSmallest:
                    cellW = minW; cellH = minH;
                    if (!uniform) warnings.Add($"Cropped {working.Count} frames to the smallest cell ({minW}×{minH}).");
                    break;

                default: // PadToLargest
                    cellW = maxW; cellH = maxH;
                    if (!uniform) warnings.Add($"Padded mismatched frames up to the largest cell ({maxW}×{maxH}).");
                    break;
            }

            int n = working.Count;
            bool vertical = options.Direction == StackDirection.Vertical;
            long outW = vertical ? cellW : (long)cellW * n;
            long outH = vertical ? (long)cellH * n : cellH;

            if (outW * outH > MaxOutputPixels)
                throw new InvalidOperationException(
                    $"That strip would be {outW}×{outH}px — too large to assemble safely. " +
                    "Reduce the frame size or the frame count.");

            var strip = new SKBitmap((int)outW, (int)outH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(strip))
            {
                canvas.Clear(SKColors.Transparent);
                for (int i = 0; i < n; i++)
                {
                    int dx = vertical ? 0 : i * cellW;
                    int dy = vertical ? i * cellH : 0;
                    DrawIntoCell(canvas, working[i], dx, dy, cellW, cellH, options.RecenterOnContent);
                }
            }

            int count = n;
            bool resampled = false;
            if (options.ResampleTo is int target && target >= 2 && target != n)
            {
                var layout = new StripDetection(vertical, n, cellW, cellH, null, false, Array.Empty<int>());
                var retimed = _importer.Resample(strip, layout, target);
                strip.Dispose();
                strip = retimed;
                count = target;
                resampled = true;
                warnings.Add($"Re-timed {n} rendered frames to {target} output frames (nearest-frame).");
            }

            return new FrameSequenceResult(strip, count, cellW, cellH, options.Direction, resampled, warnings);
        }
        finally
        {
            if (owned is not null)
                foreach (var b in owned) b.Dispose();
        }
    }

    // Place one frame inside its cell. Centre it (by content centroid when requested, else by the
    // frame rectangle) and clip to the cell so an oversized frame (crop-to-smallest) is cropped.
    // Uniform same-size frames land at an integer offset → a pixel-exact 1:1 blit.
    private static void DrawIntoCell(SKCanvas canvas, SKBitmap frame, int dx, int dy, int cellW, int cellH, bool recenter)
    {
        double fcx = 0.5, fcy = 0.5;
        if (recenter)
            (fcx, fcy) = ContentAnalysis.DetectContentCenter(frame);

        double left = dx + cellW * 0.5 - frame.Width * fcx;
        double top = dy + cellH * 0.5 - frame.Height * fcy;

        using var img = SKImage.FromBitmap(frame);
        var dest = SKRect.Create((float)Math.Round(left), (float)Math.Round(top), frame.Width, frame.Height);

        canvas.Save();
        canvas.ClipRect(SKRect.Create(dx, dy, cellW, cellH));
        canvas.DrawImage(img, dest, Blit);
        canvas.Restore();
    }

    /// <summary>
    /// Divide each pixel's RGB by its alpha — un-premultiplying a frame whose anti-aliased edges were
    /// left premultiplied by the path tracer (the usual cause of dark edge halos). Fully opaque /
    /// fully transparent pixels are untouched. Operates on a normalized RGBA8888 copy via raw bytes, so
    /// it's independent of Skia's alpha-type bookkeeping; the caller owns the returned bitmap.
    /// </summary>
    public static SKBitmap UnpremultiplyAlpha(SKBitmap src)
    {
        var dst = src.ColorType == SKColorType.Rgba8888 ? src.Copy() : src.Copy(SKColorType.Rgba8888);
        int w = dst.Width, h = dst.Height, rb = dst.RowBytes;
        var buf = dst.Bytes;
        for (int y = 0; y < h; y++)
        {
            int row = y * rb;
            for (int x = 0; x < w; x++)
            {
                int i = row + x * 4;
                int a = buf[i + 3];
                if (a == 0 || a == 255) continue;
                buf[i]     = (byte)Math.Min(255, (buf[i]     * 255 + a / 2) / a);
                buf[i + 1] = (byte)Math.Min(255, (buf[i + 1] * 255 + a / 2) / a);
                buf[i + 2] = (byte)Math.Min(255, (buf[i + 2] * 255 + a / 2) / a);
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, dst.GetPixels(), buf.Length);
        return dst;
    }

    /// <summary>
    /// Inspect a decoded frame sequence for the path-tracer failure modes StripKit can catch on import:
    /// object drift (the content-centre spread, in px of the largest cell), frames with no transparency
    /// (a missing transparent background) or none at all (a failed render), and premultiplied edges.
    /// Pure analysis; no bitmap is modified.
    /// </summary>
    public static RenderQcReport AnalyzeQc(IReadOnlyList<SKBitmap> frames)
    {
        int refW = 0, refH = 0;
        foreach (var f in frames) { refW = Math.Max(refW, f.Width); refH = Math.Max(refH, f.Height); }

        double minCx = double.MaxValue, maxCx = double.MinValue, minCy = double.MaxValue, maxCy = double.MinValue;
        int opaque = 0, empty = 0, premultVotes = 0, withContent = 0;

        foreach (var f in frames)
        {
            var (minA, maxA, premultEdge) = AlphaStats(f);
            if (minA == 255) opaque++;
            if (maxA == 0)
            {
                empty++;
                continue;
            }
            withContent++;
            var (cx, cy) = ContentAnalysis.DetectContentCenter(f);
            minCx = Math.Min(minCx, cx); maxCx = Math.Max(maxCx, cx);
            minCy = Math.Min(minCy, cy); maxCy = Math.Max(maxCy, cy);
            if (premultEdge) premultVotes++;
        }

        double driftX = withContent > 1 ? (maxCx - minCx) * refW : 0.0;
        double driftY = withContent > 1 ? (maxCy - minCy) * refH : 0.0;
        // Conservative: only flag when EVERY content frame's soft edges are premultiplied-consistent,
        // so we never recommend a fix that would brighten the edges of a straight-alpha render.
        bool premultSuspected = withContent > 0 && premultVotes == withContent;

        return new RenderQcReport(frames.Count, driftX, driftY, opaque, empty, premultSuspected);
    }

    // Per-frame alpha stats: the min/max alpha (opaque/empty detection) and whether every
    // partially-transparent pixel keeps RGB ≤ A — the signature of a premultiplied edge.
    private static (byte MinAlpha, byte MaxAlpha, bool PremultipliedEdge) AlphaStats(SKBitmap f)
    {
        byte minA = 255, maxA = 0;
        long partial = 0, violations = 0;
        int w = f.Width, h = f.Height, rb = f.RowBytes;
        var span = f.GetPixelSpan();

        if (f.BytesPerPixel == 4 && span.Length >= (long)h * rb)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * rb;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    byte c0 = span[i], c1 = span[i + 1], c2 = span[i + 2], a = span[i + 3];
                    if (a < minA) minA = a;
                    if (a > maxA) maxA = a;
                    if (a > 0 && a < 255)
                    {
                        partial++;
                        if (c0 > a || c1 > a || c2 > a) violations++;
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = f.GetPixel(x, y);
                if (p.Alpha < minA) minA = p.Alpha;
                if (p.Alpha > maxA) maxA = p.Alpha;
                if (p.Alpha > 0 && p.Alpha < 255)
                {
                    partial++;
                    if (p.Red > p.Alpha || p.Green > p.Alpha || p.Blue > p.Alpha) violations++;
                }
            }
        }

        return (minA, maxA, partial > 0 && violations == 0);
    }
}
