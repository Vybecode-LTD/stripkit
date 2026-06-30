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

        int maxW = frames.Max(f => f.Width), maxH = frames.Max(f => f.Height);
        int minW = frames.Min(f => f.Width), minH = frames.Min(f => f.Height);
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
                if (!uniform) warnings.Add($"Cropped {frames.Count} frames to the smallest cell ({minW}×{minH}).");
                break;

            default: // PadToLargest
                cellW = maxW; cellH = maxH;
                if (!uniform) warnings.Add($"Padded mismatched frames up to the largest cell ({maxW}×{maxH}).");
                break;
        }

        int n = frames.Count;
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
                DrawIntoCell(canvas, frames[i], dx, dy, cellW, cellH, options.RecenterOnContent);
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
}
