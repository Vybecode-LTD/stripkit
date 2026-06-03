---
name: avalonia-skia-interop
description: >-
  Render with SkiaSharp inside an Avalonia 11 app and move pixels between
  SkiaSharp and Avalonia correctly. Use when drawing custom visuals on the
  Avalonia Skia canvas, when an SKBitmap or SKImage must be shown in an Image
  control, when building a live preview that updates many times per second, when
  rotated or scaled images look jagged, when colours look wrong or edges show
  dark fringing, or when a heavy render blocks the UI thread. Covers the
  ICustomDrawOperation lease pattern for zero-copy drawing, SKBitmap to Avalonia
  Bitmap via PNG round-trip versus a reused WriteableBitmap pixel copy, SkiaSharp
  3.x SKSamplingOptions and supersampling, premultiplied-alpha and Bgra8888
  versus Rgba8888 matching, disposal, and off-thread rendering. Triggers on
  SkiaSharp Avalonia, SKBitmap to Bitmap, WriteableBitmap, ICustomDrawOperation,
  SKSamplingOptions, premultiplied alpha, jagged rotation, render off UI thread.
---

# Avalonia + SkiaSharp Interop

Avalonia 11 renders through Skia, so SkiaSharp and Avalonia can share the same
engine — but they are two different object worlds, and the seam between them is
where most bugs live: wrong pixel format, premultiplied-alpha fringing, jagged
transforms, or a UI freeze. Pick the right one of two patterns and the rest is
easy.

## Decide which pattern you need

- **Drawing custom visuals live (a waveform, a meter, a canvas)** → draw directly
  on Avalonia's Skia canvas with an `ICustomDrawOperation`. Zero copy, no encode.
- **Producing a finished raster to show in an `Image` (a generated thumbnail, an
  exported frame, a preview)** → render to an `SKBitmap`, then convert to an
  Avalonia `Bitmap`. Use a **PNG round-trip** for occasional updates, or a reused
  **WriteableBitmap** with a direct pixel copy for many-times-per-second updates.

## Pattern A — draw on Avalonia's canvas (zero copy)

Lease the underlying `SKCanvas` inside a custom draw operation. This is the
fastest path for live custom rendering because nothing is copied or encoded.

```csharp
public sealed class SkiaView : Control
{
    public override void Render(DrawingContext context)
        => context.Custom(new SkiaDrawOp(Bounds));
}

public sealed class SkiaDrawOp(Rect bounds) : ICustomDrawOperation
{
    public Rect Bounds => bounds;
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            return; // not running on the Skia backend
        using var lease = feature.Lease();
        SKCanvas canvas = lease.SkCanvas;
        // draw with SkiaSharp here; the canvas is Avalonia's own surface
    }
}
```

Call `InvalidateVisual()` when the data changes to schedule a redraw.

## Pattern B1 — SKBitmap to Avalonia Bitmap, PNG round-trip (simple)

Good for previews that update at human pace (a slider drag, a button press).
Encoding a small frame is sub-millisecond; do not use it for 60 FPS.

```csharp
public static Bitmap ToAvaloniaBitmap(SKBitmap sk)
{
    using var image = SKImage.FromBitmap(sk);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var ms = new MemoryStream();
    data.SaveTo(ms);
    ms.Position = 0;
    return new Bitmap(ms);
}
```

## Pattern B2 — reused WriteableBitmap, direct copy (fast)

For high-frame-rate updates, allocate one `WriteableBitmap` and copy raw pixels
into it each frame — no encode, no per-frame allocation. **The formats must
match** or red and blue channels swap. Avalonia's `Bgra8888` pairs with
SkiaSharp's `SKColorType.Bgra8888`; both should be `Premul`. Requires
`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.

```csharp
// Render the SKBitmap as Bgra8888/Premul so it matches Avalonia 1:1.
static unsafe void CopyInto(SKBitmap src, WriteableBitmap dst)
{
    using var fb = dst.Lock();                 // ILockedFramebuffer
    int rowBytes = src.Width * 4;
    byte* s = (byte*)src.GetPixels().ToPointer();
    byte* d = (byte*)fb.Address.ToPointer();
    for (int y = 0; y < src.Height; y++)       // copy row-by-row: dst rows may be padded
        Buffer.MemoryCopy(s + y * rowBytes, d + y * fb.RowBytes, rowBytes, rowBytes);
}
// Create once: new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
//                                   PixelFormat.Bgra8888, AlphaFormat.Premul);
```

After copying, raise a change notification on the bound `Bitmap` property (or
bind to the `WriteableBitmap` and call `InvalidateVisual` on the host Image).

## Quality: anti-aliasing rotated and scaled images

SkiaSharp 3.x replaced the old `SKFilterQuality` with `SKSamplingOptions`. For
clean edges use a cubic resampler, and **supersample** when rotating: render into
an oversampled surface, then downsample once.

```csharp
static readonly SKSamplingOptions Cubic = new(SKCubicResampler.Mitchell);
// draw rotated/scaled content with: canvas.DrawImage(img, src, dst, Cubic);
// supersample: render at size*ss into an SKSurface, then DrawImage it down to size.
```

## Format and alpha gotchas

- **Channel swap** — a raw memcpy from `Rgba8888` Skia into `Bgra8888` Avalonia
  turns blue knobs orange. Match the color types, or convert.
- **Dark fringing on transparent edges** — caused by mixing premultiplied and
  unpremultiplied alpha. Keep everything `Premul`; `SKBitmap.Decode` and Skia
  surfaces default to premultiplied, and Avalonia expects premultiplied.
- **Blurry upscaled preview** — an `Image` upscaling a tiny frame uses bilinear.
  Render the preview near display size, and set
  `RenderOptions.BitmapInterpolationMode="HighQuality"`.

## Threading

Rendering an `SKBitmap` is CPU-bound and uses no UI types, so run it on a
background thread (`await Task.Run(...)`) for large outputs. Then marshal the
resulting Avalonia `Bitmap` back to the UI thread (`Dispatcher.UIThread.Post`)
before assigning it to a bound property. Never lease the Avalonia canvas
(Pattern A) off the render thread.

## Anti-patterns

- Encoding a PNG every frame for a 60 FPS animation — use Pattern B2.
- Allocating a new `WriteableBitmap` per frame — allocate once, reuse.
- Mismatched pixel formats between Skia and Avalonia (red/blue swap).
- Mixing premultiplied and straight alpha (dark edge halos).
- Forgetting to dispose `SKSurface`, `SKImage`, and `SKData`.
- Doing a heavy render on the UI thread and freezing the window.
- Using `SKFilterQuality` (removed in 3.x) instead of `SKSamplingOptions`.
