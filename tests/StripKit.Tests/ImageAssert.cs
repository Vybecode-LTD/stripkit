using System.Runtime.CompilerServices;
using SkiaSharp;
using Xunit.Sdk;

namespace StripKit.Tests;

/// <summary>
/// Golden-image assertion with a tolerant pixel diff, per the
/// image-regression-testing skill. Baselines live next to the test sources under
/// <c>baselines/</c>; on mismatch, expected/actual/diff PNGs are written to
/// <c>output/</c>. Regenerate intentionally with <c>UPDATE_BASELINES=1 dotnet test</c>.
/// </summary>
public static class ImageAssert
{
    const int PerPixelThreshold = 2;            // per-channel delta tolerated (swallows AA fringe)
    const double MaxFractionDiffering = 0.001;  // 0.1% of pixels may differ

    public static void MatchesBaseline(SKBitmap actual, string name, [CallerFilePath] string callerFile = "")
    {
        var dir = Path.GetDirectoryName(callerFile)!;
        var baselinePath = Path.Combine(dir, "baselines", name + ".png");
        var outDir = Path.Combine(dir, "output");

        bool update = Environment.GetEnvironmentVariable("UPDATE_BASELINES") == "1";
        if (!File.Exists(baselinePath) || update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            Save(actual, baselinePath);
            if (!update)
                throw new XunitException($"New baseline written: {baselinePath} — review and commit it, then re-run.");
            return;
        }

        using var baseline = SKBitmap.Decode(baselinePath);
        if (baseline.Width != actual.Width || baseline.Height != actual.Height)
            throw new XunitException(
                $"{name}: size changed — baseline {baseline.Width}x{baseline.Height} vs actual {actual.Width}x{actual.Height}");

        long differing = 0;
        int maxDelta = 0;
        using var diff = new SKBitmap(actual.Width, actual.Height);
        for (int y = 0; y < actual.Height; y++)
        for (int x = 0; x < actual.Width; x++)
        {
            var a = actual.GetPixel(x, y);
            var b = baseline.GetPixel(x, y);
            int d = Math.Max(
                Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
            maxDelta = Math.Max(maxDelta, d);
            bool differs = d > PerPixelThreshold;
            if (differs) differing++;
            diff.SetPixel(x, y, differs ? new SKColor(255, 0, 0) : new SKColor(0, 0, 0, 40));
        }

        double frac = (double)differing / ((long)actual.Width * actual.Height);
        if (frac > MaxFractionDiffering)
        {
            Directory.CreateDirectory(outDir);
            Save(actual, Path.Combine(outDir, name + ".actual.png"));
            Save(baseline, Path.Combine(outDir, name + ".expected.png"));
            Save(diff, Path.Combine(outDir, name + ".diff.png"));
            throw new XunitException(
                $"{name}: {frac:P3} of pixels differ (max channel delta {maxDelta}). See output/{name}.diff.png");
        }
    }

    static void Save(SKBitmap bmp, string path)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
