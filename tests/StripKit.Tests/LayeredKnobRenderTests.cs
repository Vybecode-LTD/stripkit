using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Layer-aware knob rendering (a static base body + a separate rotating pointer). Golden
/// baselines lock the look; pixel-logic locks the behaviour: the pointer rotates while the
/// static base never moves, an empty layer stack falls back to the single-source path, the
/// pointer pivot is honoured, and layers are ignored for non-knob components.
/// </summary>
public class LayeredKnobRenderTests
{
    readonly SkiaFilmstripRenderer _renderer = new();

    // A layered knob: layer 0 = static body, layer 1 = rotating pointer (its own pivot).
    static FilmstripSettings LayeredKnob(int frames = 64, double pivotX = 0.5, double pivotY = 0.5, int ss = 1)
    {
        var s = new FilmstripSettings
        {
            ComponentType = ComponentType.RotaryKnob,
            FrameCount = frames,
            FrameWidth = 80,
            FrameHeight = 80,
            StartAngleDegrees = -135,
            EndAngleDegrees = 135,
            Supersample = ss,
        };
        s.Layers.Add(new RenderLayer { Behavior = LayerBehavior.Static });
        s.Layers.Add(new RenderLayer { Behavior = LayerBehavior.Rotate, PivotX = pivotX, PivotY = pivotY });
        return s;
    }

    static IReadOnlyList<SKBitmap> Art(SKBitmap body, SKBitmap pointer) => new[] { body, pointer };

    // The pointer is the only near-white element; this rejects the dark body and accent ring.
    static bool IsPointer(SKColor c) => c.Red > 180 && c.Green > 180 && c.Blue > 180 && c.Alpha > 120;

    // ---- golden-image baselines ----

    [Fact]
    public void Layered_knob_min() // frame 0 — pointer rotated to the start angle
    {
        using var body = TestImages.KnobBody();
        using var ptr = TestImages.Pointer();
        using var frame = _renderer.RenderFrame(LayeredKnob(ss: 4), null, null, 0, 1.0, Art(body, ptr));
        ImageAssert.MatchesBaseline(frame, "layered_knob_min");
    }

    [Fact]
    public void Layered_knob_mid()
    {
        using var body = TestImages.KnobBody();
        using var ptr = TestImages.Pointer();
        using var frame = _renderer.RenderFrame(LayeredKnob(ss: 4), null, null, 32, 1.0, Art(body, ptr));
        ImageAssert.MatchesBaseline(frame, "layered_knob_mid");
    }

    [Fact]
    public void Layered_knob_max() // last frame — pointer at the end angle
    {
        using var body = TestImages.KnobBody();
        using var ptr = TestImages.Pointer();
        using var frame = _renderer.RenderFrame(LayeredKnob(ss: 4), null, null, 63, 1.0, Art(body, ptr));
        ImageAssert.MatchesBaseline(frame, "layered_knob_max");
    }

    // ---- pixel-logic ----

    [Fact]
    public void The_pointer_rotates_to_the_top_at_mid_travel()
    {
        using var body = TestImages.KnobBody();
        using var ptr = TestImages.Pointer();
        // 65 frames so frame 32 is exactly t = 0.5 → angle 0 → the pointer is straight up.
        using var f0 = _renderer.RenderFrame(LayeredKnob(frames: 65), null, null, 0, 1.0, Art(body, ptr));
        using var fUp = _renderer.RenderFrame(LayeredKnob(frames: 65), null, null, 32, 1.0, Art(body, ptr));

        // (40,20) sits on the upright needle (centre 40,40 → tip near 40,10).
        IsPointer(fUp.GetPixel(40, 20)).Should().BeTrue("the pointer points straight up at mid-travel");
        IsPointer(f0.GetPixel(40, 20)).Should().BeFalse("at frame 0 the pointer is rotated down to the start angle");
    }

    [Fact]
    public void A_static_base_layer_is_identical_in_every_frame()
    {
        using var body = TestImages.KnobBody();
        var s = new FilmstripSettings
        {
            ComponentType = ComponentType.RotaryKnob,
            FrameCount = 64, FrameWidth = 80, FrameHeight = 80, Supersample = 1,
        };
        s.Layers.Add(new RenderLayer { Behavior = LayerBehavior.Static });

        using var f0 = _renderer.RenderFrame(s, null, null, 0, 1.0, new[] { body });
        using var f63 = _renderer.RenderFrame(s, null, null, 63, 1.0, new[] { body });
        ImagesEqual(f0, f63).Should().BeTrue("a static layer must not change across frames");
    }

    [Fact]
    public void The_body_under_a_rotating_pointer_does_not_move()
    {
        using var body = TestImages.KnobBody();
        using var ptr = TestImages.Pointer();
        using var f0 = _renderer.RenderFrame(LayeredKnob(frames: 65), null, null, 0, 1.0, Art(body, ptr));
        using var fUp = _renderer.RenderFrame(LayeredKnob(frames: 65), null, null, 32, 1.0, Art(body, ptr));

        // The bottom of the body ring (6 o'clock) is outside the pointer's 270° sweep, so it is
        // pure static body in every frame.
        f0.GetPixel(40, 66).Should().Be(fUp.GetPixel(40, 66), "the base body is drawn identically every frame");
    }

    [Fact]
    public void An_empty_layer_stack_falls_back_to_the_single_source()
    {
        using var src = TestImages.Knob();
        var s = new FilmstripSettings
        {
            ComponentType = ComponentType.RotaryKnob,
            FrameCount = 64, FrameWidth = 80, FrameHeight = 80,
            StartAngleDegrees = -135, EndAngleDegrees = 135, Supersample = 1,
        };

        using var legacy = _renderer.RenderFrame(s, src, null, 20);                       // single-source path
        // Empty Layers + provided art must STILL render the single source (the gate is Layers.Count > 0).
        using var withArtButNoLayers = _renderer.RenderFrame(s, src, null, 20, 1.0, new[] { src });
        ImagesEqual(legacy, withArtButNoLayers).Should().BeTrue("an empty layer list ignores layer art");
    }

    [Fact]
    public void The_pointer_pivot_changes_the_render()
    {
        using var body = TestImages.KnobBody();
        using var ptr = TestImages.Pointer();
        using var centred = _renderer.RenderFrame(LayeredKnob(pivotX: 0.5, pivotY: 0.5), null, null, 10, 1.0, Art(body, ptr));
        using var offset = _renderer.RenderFrame(LayeredKnob(pivotX: 0.2, pivotY: 0.8), null, null, 10, 1.0, Art(body, ptr));
        ImagesEqual(centred, offset).Should().BeFalse("the pointer pivot moves where the needle swings");
    }

    [Fact]
    public void Layers_are_ignored_for_non_knob_components()
    {
        using var cap = TestImages.Cap(20, 14);
        using var ptr = TestImages.Pointer(20);
        var s = new FilmstripSettings
        {
            ComponentType = ComponentType.VerticalFader,
            FrameCount = 64, FrameWidth = 40, FrameHeight = 128, EdgeMargin = 4, Supersample = 1,
        };
        s.Layers.Add(new RenderLayer { Behavior = LayerBehavior.Static });
        s.Layers.Add(new RenderLayer { Behavior = LayerBehavior.Rotate });

        var noLayers = s.Clone();   // Clone deep-copies Layers…
        noLayers.Layers.Clear();    // …so clearing this one does not touch s.Layers

        using var a = _renderer.RenderFrame(s, cap, null, 32, 1.0, Art(cap, ptr));
        using var b = _renderer.RenderFrame(noLayers, cap, null, 32);
        ImagesEqual(a, b).Should().BeTrue("the layer stack is a rotary-only feature");
    }

    static bool ImagesEqual(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
