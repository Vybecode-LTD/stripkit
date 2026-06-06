using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// End-to-end: a parsed layered source (★ #3 step 3) renders through the existing layer-aware path.
/// The service turns an SVG's groups into tagged layers; mapping them onto <see
/// cref="FilmstripSettings.Layers"/> exactly as the view model does must produce a layered knob —
/// the body stays put, only the indicator-named group rotates. One golden locks the look; pixel-logic
/// locks the behaviour.
/// </summary>
public class LayeredImportRenderTests
{
    readonly SkiaFilmstripRenderer _renderer = new();
    readonly LayeredImportService _import = new();

    const string KnobSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="body"><circle cx="50" cy="50" r="40" fill="#333333"/></g>
          <g id="pointer"><line x1="50" y1="50" x2="50" y2="12" stroke="#ffffff" stroke-width="6"/></g>
        </svg>
        """;

    static bool IsPointer(SKColor c) => c.Red > 180 && c.Green > 180 && c.Blue > 180 && c.Alpha > 120;

    static string WriteTempSvg(string svg)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_render_{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, svg);
        return path;
    }

    // Map the imported layers onto a knob settings + art list, the way MainWindowViewModel does
    // (each layer's guessed behaviour; a centred body → pivot 0.5,0.5).
    (FilmstripSettings s, IReadOnlyList<SKBitmap> art) Import(int frames, int ss = 1)
    {
        var path = WriteTempSvg(KnobSvg);
        try
        {
            var result = _import.Import(path)!;
            var s = new FilmstripSettings
            {
                ComponentType = ComponentType.RotaryKnob,
                FrameCount = frames,
                FrameWidth = Math.Max(result.CanvasWidth, result.CanvasHeight),
                FrameHeight = Math.Max(result.CanvasWidth, result.CanvasHeight),
                StartAngleDegrees = -135,
                EndAngleDegrees = 135,
                Supersample = ss,
                SourceCenterX = 0.5,
                SourceCenterY = 0.5,
            };
            foreach (var l in result.Layers)
                s.Layers.Add(new RenderLayer { Behavior = l.SuggestedBehavior, PivotX = 0.5, PivotY = 0.5 });
            return (s, result.Layers.Select(l => l.Art).ToList());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Imported_svg_knob_mid_golden()
    {
        var (s, art) = Import(64, ss: 4);
        using var frame = _renderer.RenderFrame(s, null, null, 32, 1.0, art);
        ImageAssert.MatchesBaseline(frame, "imported_svg_knob_mid");
        foreach (var a in art) a.Dispose();
    }

    [Fact]
    public void Imported_indicator_group_rotates_while_the_body_stays_put()
    {
        // 65 frames so frame 32 is exactly t = 0.5 → angle 0 → the needle points straight up.
        var (s, art) = Import(65);
        using var f0 = _renderer.RenderFrame(s, null, null, 0, 1.0, art);
        using var fUp = _renderer.RenderFrame(s, null, null, 32, 1.0, art);

        // The pointer group rotates: straight up at mid-travel, elsewhere at frame 0.
        IsPointer(fUp.GetPixel(50, 25)).Should().BeTrue("the imported pointer group points up at mid-travel");
        IsPointer(f0.GetPixel(50, 25)).Should().BeFalse("at frame 0 the pointer has swung to the start angle");

        // Straight down (6 o'clock) sits in the 90° gap of the 270° sweep — pure static body always.
        f0.GetPixel(50, 85).Should().Be(fUp.GetPixel(50, 85), "the body group is static across frames");

        foreach (var a in art) a.Dispose();
    }
}
