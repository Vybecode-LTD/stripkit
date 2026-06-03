---
name: image-regression-testing
description: >-
  Lock down the pixel output of rendering code with golden-image (snapshot) tests
  and perceptual diffing, so a refactor cannot silently change what gets drawn.
  Use when testing a renderer, an image exporter, a thumbnail or chart generator,
  a filmstrip or sprite tool, or any code whose output is an image, when a change
  altered output and you must catch it, when deciding how to store and update
  baselines, or when pixel comparisons go flaky on anti-aliasing or platform
  differences. Covers capturing a baseline PNG, tolerant pixel diff with a
  per-pixel and fraction budget, determinism tips, baseline storage and an
  approve-baseline workflow, and emitting a visual diff on failure. Triggers on
  golden image test, snapshot test image, visual regression, pixel diff, image
  comparison test, baseline image, perceptual diff, SSIM, render output changed,
  anti-alias tolerance.
---

# Image Regression Testing

A renderer's contract is the pixels it produces. Unit tests on its helper methods
will not notice when a refactor shifts every knob frame by one pixel or drops the
anti-aliasing — only a test that looks at the *output image* will. A golden-image
test renders, compares against a committed baseline, and fails (with a visual
diff) when the picture changes.

## Core principle

Assert on the output image, but **compare with a tolerance, not byte equality**.
Identical-looking renders differ by a few least-significant bits across machines,
GPU/CPU rasterizers, and library versions. A test that demands exact bytes is
flaky; a test that allows a small, defined deviation is stable and still catches
real regressions. Reserve exact equality for output you can guarantee is
bit-deterministic.

## The workflow

1. **Render** the thing under test into an in-memory image at a fixed, small size.
2. **No baseline yet?** Write the current image as the baseline, and **fail the
   test** with a clear "new baseline created — review and commit it" message. A
   silently-passing first run lets a wrong baseline get blessed.
3. **Baseline exists?** Load it and compare under the tolerance budget.
4. **Match** → pass. **Mismatch** → write `expected`, `actual`, and `diff` PNGs to
   a test-output folder and fail with the numbers (max delta, % pixels differing).

## Comparison methods, weakest to strongest tolerance

- **Exact hash** (SHA of pixels) — only for guaranteed-deterministic output.
  Fast, but flaky the moment a rasterizer or library version nudges a pixel.
- **Per-pixel max-channel delta + count budget** — the workhorse. A pixel
  "differs" if any channel differs by more than `perPixelThreshold` (e.g. 2/255);
  the test fails if more than `maxFractionDiffering` of pixels differ (e.g.
  0.1%). Absorbs anti-aliasing jitter while catching real changes.
- **Mean absolute difference** — one number across the image; good as a secondary
  guard, poor at localizing.
- **Perceptual (SSIM)** — structural similarity, closer to what an eye notices;
  use when AA/resampling makes per-pixel budgets noisy. Heavier to implement.

## Tolerance budget

Pick two knobs per test and tune them: a **per-pixel channel threshold** (small,
to swallow AA fringe) and a **fraction-of-pixels-allowed-to-differ** (small, to
swallow a few edge pixels). Start strict (e.g. threshold 2, fraction 0.1%) and
loosen only with a recorded reason. A test that needs a 10% fraction to pass is
not testing anything — re-examine determinism instead of loosening.

## Determinism: remove the jitter at the source

- **Pin the rendering library version.** SkiaSharp/Skia AA differs across
  versions; an unpinned bump will redden every baseline. Pin it in the csproj.
- Render at a **fixed canvas size** and fixed inputs; never depend on screen DPI,
  system fonts, or wall-clock/random seeds inside the render.
- Use a **fixed pixel format** (e.g. `Rgba8888` premultiplied) so the comparison
  and the baseline agree.
- Prefer a **CPU raster surface** for tests, not a GPU surface, for repeatability.

## Baseline management

- Commit baselines to the repo under `tests/baselines/<name>.png`. They are
  reviewable in a PR — a changed baseline shows up as a visual diff.
- Regenerate intentionally behind a flag/env var, never on every run:
  `UPDATE_BASELINES=1 dotnet test`. The test code checks the flag and overwrites.
- Keep baselines **small** (render a 64–128px canvas, not a 4K one) so the repo
  stays light and diffs are quick to eyeball.
- A baseline change in a PR must be reviewed like code — it is the assertion.

## C# example (xUnit + SkiaSharp)

```csharp
public static class ImageAssert
{
    const int PerPixelThreshold = 2;          // channel delta tolerated
    const double MaxFractionDiffering = 0.001; // 0.1% of pixels

    public static void MatchesBaseline(SKBitmap actual, string name)
    {
        var baselinePath = Path.Combine("tests", "baselines", name + ".png");
        var outDir = Path.Combine("tests", "output");
        Directory.CreateDirectory(outDir);

        if (!File.Exists(baselinePath) || Environment.GetEnvironmentVariable("UPDATE_BASELINES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            Save(actual, baselinePath);
            if (Environment.GetEnvironmentVariable("UPDATE_BASELINES") != "1")
                throw new Xunit.Sdk.XunitException($"New baseline written: {baselinePath} — review and commit it.");
            return;
        }

        using var baseline = SKBitmap.Decode(baselinePath);
        if (baseline.Width != actual.Width || baseline.Height != actual.Height)
            throw new Xunit.Sdk.XunitException($"Size changed: baseline {baseline.Width}x{baseline.Height} vs actual {actual.Width}x{actual.Height}");

        long differing = 0; int maxDelta = 0;
        using var diff = new SKBitmap(actual.Width, actual.Height);
        for (int y = 0; y < actual.Height; y++)
        for (int x = 0; x < actual.Width; x++)
        {
            var a = actual.GetPixel(x, y); var b = baseline.GetPixel(x, y);
            int d = Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                             Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
            maxDelta = Math.Max(maxDelta, d);
            bool diff1 = d > PerPixelThreshold;
            if (diff1) differing++;
            diff.SetPixel(x, y, diff1 ? new SKColor(255, 0, 0) : new SKColor(0, 0, 0, 40));
        }

        double frac = (double)differing / (actual.Width * actual.Height);
        if (frac > MaxFractionDiffering)
        {
            Save(actual, Path.Combine(outDir, name + ".actual.png"));
            Save(baseline, Path.Combine(outDir, name + ".expected.png"));
            Save(diff, Path.Combine(outDir, name + ".diff.png"));
            throw new Xunit.Sdk.XunitException(
                $"{name}: {frac:P3} pixels differ (max channel delta {maxDelta}). See tests/output/{name}.diff.png");
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

// Usage:
//   var strip = renderer.RenderStrip(settings, source, null, 1.0);
//   ImageAssert.MatchesBaseline(strip, "knob_64_default");
```

## What to baseline for a filmstrip/control renderer

- One strip per component type at default settings (knob, vfader, hslider).
- Edge cases that are easy to break: frame 0 and the last frame (min/max), an
  odd frame count, a non-square source fit into a square frame, an off-centre
  pivot, and a strip with a static background composited behind.
- A single extracted mid frame, to localize a regression to one frame quickly.

## Anti-patterns

- Byte-exact comparison across platforms or library versions — flaky by design.
- Committing no baselines, or letting the first run pass silently and bless a
  wrong image.
- Auto-updating baselines on every run — the test then asserts nothing.
- Not pinning the rendering library version, so an AA change reddens everything.
- Giant baselines (full-resolution) that bloat the repo and slow diffs.
- Failing with only "images differ" and no diff artifact — give the numbers and
  write expected/actual/diff PNGs so a human can see what moved.
- Loosening the tolerance until it passes instead of fixing the nondeterminism.
