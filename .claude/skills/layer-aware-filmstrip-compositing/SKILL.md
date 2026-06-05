---
name: layer-aware-filmstrip-compositing
description: >-
  Composite a control filmstrip (sprite sheet) from layers: a static body plus a
  separately-animated overlay (a rotating pointer, a sliding cap, an opacity-ramped
  glow), so only the moving part transforms while the body stays crisp and
  re-renderable at any size. Use when building or extending a filmstrip / knob /
  sprite generator (e.g. StripKit), when a baked single-image knob looks soft or
  can't be re-skinned, or when adding a base+pointer or layered PSD/SVG input mode.
  Covers the
  render-lib-free layer model (behaviour + per-layer pivot), contain-fit +
  supersample-once compositing, the N-1 per-frame angle law, the
  pivot-is-the-knob-axis-not-the-overlay-centre rule, and gating so empty layers
  stay byte-identical to single-source output. Triggers on layered knob, base plus
  pointer, only the pointer rotates, layer-aware animation, filmstrip compositing,
  sprite-sheet layers, pointer pivot, per-layer behaviour.
---

# Layer-Aware Filmstrip Compositing

A control filmstrip animates by showing one pre-baked frame per value. Bake the
*whole* control into each frame and you get two problems: the body is
re-rasterised (and softened) at every angle, and you can never re-skin or
re-colour just the body without re-rendering everything. Layer-aware compositing
fixes both — keep the body as one static layer and transform only the part that
actually moves.

## Core principle

Treat a frame as a **stack of layers**, each with a *behaviour*, not as one image
under one transform. The body is a `Static` layer drawn once per frame; the
pointer is a `Rotate` layer that spins; a fader cap is a `Translate` layer; a glow
is an `OpacityRamp`. Only the moving layer is transformed, so the body stays
pixel-for-pixel identical across the strip and can be swapped or recoloured
independently.

Keep the **layer spec** (behaviour + pivot — plain data) separate from the
**layer art** (the bitmap). The spec stays free of your rendering library so it can
be unit-tested and serialized; the bitmaps are passed alongside to the renderer,
index-matched to the specs. This mirrors how a good renderer already separates
settings from pixels.

## The layer model

```
enum LayerBehavior { Static, Rotate, Translate, OpacityRamp }

class RenderLayer {
    LayerBehavior Behavior      // how this layer animates
    double PivotX, PivotY        // normalized (0..1) rotation axis within the layer's art
    // (Translate / OpacityRamp add their own params as you grow the model)
}

// On the render contract, an ORDERED list, bottom-first:
List<RenderLayer> Layers        // empty => single-source render (see "Gate it")
```

The art lives outside this model — the renderer takes `layerArt[i]` paired with
`Layers[i]`. That keeps the model render-lib-free (no `SKBitmap` / `juce::Image` /
`HTMLCanvasElement` leaking into your data layer).

## The composite

For each frame `i` of `N`:

1. `t = N > 1 ? i / (N - 1) : 0`. **Use `N − 1`, not `N`** — it lands the last
   frame exactly on the maximum (a 64-frame, 270° sweep must reach +135° on frame
   63, not fall short). This is the same law a single-image knob uses; layers don't
   change it.
2. For each layer, bottom-first: **contain-fit** the art into the frame cell and
   centre it — *the same fit every layer gets*, so layers authored at one canvas
   size overlay pixel-perfectly.
3. Apply the behaviour:
   - **Static** → draw at the fit rect, no transform. (The body. Identical every frame.)
   - **Rotate** → rotate by `angle = Start + (End − Start)·t` about the layer's
     **own** pivot, then draw.
   - **Translate / OpacityRamp** → slide along an axis by `t`, or ramp alpha by `t`.
4. Composite **into an oversampled work surface** and downsample **once** at the
   end (supersample-once) — rotating into an oversampled buffer is what keeps the
   needle's edge smooth instead of jagged.

## The pivot is the knob axis, not the overlay's centre

The single most common bug. A pointer/needle drawn on a transparent canvas has its
*bounding-box centre* up near the tip — but it must rotate about the **knob's
axis** (where it attaches to the body), which is usually the body's content centre
/ the shared canvas centre. So:

- **Do not** auto-detect the pointer layer's own opaque-content centre and use that
  as its pivot — you get a needle that orbits instead of spinning.
- **Do** seed each rotating layer's pivot from the **body's** detected content
  centre (the knob axis), then expose it for manual nudging. A needle authored on
  the same canvas as the body then spins correctly out of the box.
- Give each rotating layer its **own** pivot. The axis is a property of how the art
  was drawn, not a global — two overlays can rotate about different points.

## Gate it: empty layers must equal single-source output

This is what makes the feature safe to add to a *shipping* renderer. Make the
layered path **purely additive and gated on the layer list being non-empty**:

- Add an *optional* `layerArt` parameter to `RenderFrame` / `RenderStrip` (append
  it; default null) so every existing call site and signature is unchanged.
- Branch: `if (isKnob && Layers.Count > 0 && layerArt present) → composite layers;
  else → the existing single-source path, untouched`.
- Result: an empty `Layers` list renders the single source **byte-for-byte** as
  before, so every committed golden baseline still passes and you have genuinely
  *extended* the renderer rather than rewritten it.

This is the same discipline that lets you add a value arc (`ShowArc = false`
default) or content-centring (`0.5,0.5` default) without disturbing existing
output. Reach for it whenever you bolt a new path onto a tested renderer.

## Authoring & UI conventions

- **Same canvas size.** Tell users to export the base and the overlay at identical
  pixel dimensions, both centred on the knob axis. Then both contain-fit to the same
  rect and overlay exactly. A differently-sized overlay still renders undistorted
  (its own contain-fit) and anchored at the shared pivot — just not co-scaled.
- **Explicit named slots, not an overloaded "source".** Surface a **Base** slot and
  a **Pointer** slot, each with its own load/clear. Don't silently change what
  "source" means when a second layer appears — it confuses users. Keep the flat
  single-image path working alongside; an empty layer stack is the legacy mode.
- **Future layer sources fill the same slots.** Auto-extracting a pointer from flat
  art, or importing PSD/SVG layers, just *populates this same model* — the render
  path and the UI never change shape again. Design the slots once.

## Reference implementation (C# / SkiaSharp)

The math is library-agnostic — it ports directly to JUCE `Graphics`, an HTML
canvas, or Pillow. Only the draw/rotate calls change.

```csharp
// Returns the knob centre (body's content centre) so an optional value arc can sit concentric.
FrameTransform RenderLayers(SKCanvas canvas, Settings s,
                            IReadOnlyList<SKBitmap> art, int frame, double px)
{
    float fw = s.FrameWidth, fh = s.FrameHeight;
    double t = s.FrameCount > 1 ? (double)frame / (s.FrameCount - 1) : 0;   // N-1 divisor
    float angle = (float)(s.StartAngle + (s.EndAngle - s.StartAngle) * t);
    float knobCx = fw / 2f, knobCy = fh / 2f;

    int n = Math.Min(s.Layers.Count, art.Count);
    for (int i = 0; i < n; i++)
    {
        var (lw, lh) = Contain(art[i].Width, art[i].Height, fw, fh);        // same fit every layer
        float lx = (fw - lw) / 2f, ly = (fh - lh) / 2f;
        var dst = new SKRect(lx*(float)px, ly*(float)px, (lx+lw)*(float)px, (ly+lh)*(float)px);
        using var img = SKImage.FromBitmap(art[i]);

        if (s.Layers[i].Behavior == LayerBehavior.Rotate)
        {
            float pvx = (lx + (float)s.Layers[i].PivotX * lw) * (float)px;   // the layer's OWN pivot
            float pvy = (ly + (float)s.Layers[i].PivotY * lh) * (float)px;
            canvas.Save();
            canvas.Translate(pvx, pvy); canvas.RotateDegrees(angle); canvas.Translate(-pvx, -pvy);
            canvas.DrawImage(img, dst, Cubic);
            canvas.Restore();
        }
        else // Static — fixed; defines the knob axis for the arc.
        {
            knobCx = lx + (float)s.SourceCenterX * lw;
            knobCy = ly + (float)s.SourceCenterY * lh;
            canvas.DrawImage(img, dst, Cubic);
        }
    }
    return new FrameTransform(pivotX: knobCx, pivotY: knobCy, rotate: angle);
}
```

Call it from `RenderFrame` **only when the gate passes**; otherwise run the
existing single-source draw. `RenderStrip` just threads `layerArt` through to each
`RenderFrame`. Mirror the math into any standalone copy of the engine if you keep one.

## Testing

Layered output is deterministic, so lock it the way you lock any renderer:

- **Golden baselines** at min / mid / max — eyeball them once (body crisp and
  static; only the overlay moves) and commit them.
- **Pixel-logic** that survives a baseline regen:
  - a `Static`-only stack is **identical across frames** (the body never moves);
  - a probe on the upright needle is lit at mid-travel and dark at frame 0 (the
    overlay rotates);
  - an **empty layer stack renders identically to the single source** (the gate holds);
  - changing a layer's pivot changes the output (the pivot is wired);
  - layers are a **no-op for component types that don't support them** (this also
    proves your settings deep-copy the layer list in `Clone`).

## Anti-patterns

- Baking the whole control into each frame — the body softens at every angle and
  can't be re-skinned. That's the problem this pattern exists to solve.
- Using the moving layer's bounding-box centre as its rotation pivot — it orbits
  instead of spinning. Pivot on the knob axis (the body centre).
- Rewriting the single-source path instead of gating an additive layered branch —
  you break committed baselines and lose the "extend, don't rewrite" guarantee.
- Putting the bitmap type (`SKBitmap` / `juce::Image`) in the layer *model* — it
  stops being unit-testable and serializable. Keep art beside the spec, index-matched.
- Downsampling per layer instead of compositing into one oversampled surface and
  resampling once — soft, and slower.
- Stretching every layer to a shared rect — a differently-sized overlay distorts.
  Contain-fit each layer on its own; rely on same-canvas authoring for exact overlay.
- A shallow `Clone()` that shares the layer list — a per-item batch clone then
  mutates the original. Deep-copy the list.
- Changing the `N − 1` divisor to `N` — the last frame stops landing on the maximum.
