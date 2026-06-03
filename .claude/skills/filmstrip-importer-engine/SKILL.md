---
name: filmstrip-importer-engine
description: >-
  Detect the frame layout of an existing control filmstrip (sprite sheet) and
  split, re-slice, or re-stack it. Use when importing a KnobMan export or a
  purchased plugin-asset pack, when you have a filmstrip PNG of unknown frame
  count, when extracting a single frame from a strip, when converting a vertical
  strip to horizontal or changing frame count, or when adding a filmstrip
  importer to a tool like StripKit. Covers the frame-count detection
  algorithm (test plausible counts, height or width divisibility, aspect-ratio
  classification into knob vs vertical fader vs horizontal slider), confidence
  and ambiguity handling, single-frame extraction, and clean re-export. Triggers
  on filmstrip import, sprite sheet detect frames, KnobMan strip, how many
  frames, frame count detection, split filmstrip, re-slice filmstrip, extract one
  frame, knob strip, fader strip, purchased asset pack, detect frame height.
---

# Filmstrip Importer Engine

Take a filmstrip PNG you did not generate — a KnobMan export, a purchased pack,
or an old strip with lost metadata — figure out how it is laid out, and split or
re-stack it cleanly. The hard part is that the frame count is not stored in the
file; it must be **inferred** from the image dimensions and then **verified**.

## Core principle

A vertical filmstrip is `W × (H_frame × N)`. You do not know `N`, but you know
that `total_height` must divide evenly by `N`. Test the plausible counts in
descending order and take the first that divides evenly — then sanity-check the
resulting frame shape against the control type. Never trust a filename for the
count, and never skip the visual verification step.

## Detection procedure

1. Read `width` and `height` of the PNG.
2. Decide the strip axis. If `height > width`, assume a **vertical** strip
   (frames stacked top-to-bottom) and test against `height`. If `width > height`
   and the image is very wide, also test a **horizontal** strip against `width`.
   When unsure, test both axes and prefer the candidate whose frame is closest to
   square or to a known control aspect.
3. Test these frame counts, in this order, against the chosen total dimension:
   `[128, 127, 101, 100, 64, 63, 48, 32, 24, 16, 12, 8, 4, 3, 2]`.
   The first count `n` where `total % n == 0` is the best candidate.
   (Both `N` and `N+1`/odd counts appear in the wild — e.g. KnobMan defaults to
   128, while some hand-built strips are 127 or 101 because they include the
   exact midpoint frame.)
4. Compute `frame = total / n`. For a vertical strip `frame_height = height / n`,
   `frame_width = width`.
5. Classify the control from the frame aspect ratio:
   - `frame_height ≈ frame_width` (within ~20%) → **rotary knob** or button.
   - `frame_height > frame_width * 2` → **vertical fader**.
   - `frame_width > frame_height * 2` → **horizontal slider**.

## Confidence and ambiguity

Many totals divide evenly by several candidates (e.g. height 4096 divides by 64,
32, 16, 8, 4, 2). The ordered list biases toward the largest plausible count,
which is usually correct, but **it is a guess**. Always:

- Render the first and last detected frames and the midpoint, and let a human
  confirm the control sweeps correctly (frame 0 = min, last = max).
- Expose the detected count as an editable value, not a fixed result.
- Flag low confidence when two adjacent candidates (e.g. 64 and 63) both divide
  evenly and the frame is square — the strip may include an extra centre frame.

## Reference implementation (Python / Pillow)

Pillow matches the established asset pipeline; the same logic ports directly to
SkiaSharp (`SKBitmap.ExtractSubset` for the crop, `SKImageInfo` for sizes).

```python
from PIL import Image

CANDIDATES = [128, 127, 101, 100, 64, 63, 48, 32, 24, 16, 12, 8, 4, 3, 2]

def detect_layout(path):
    img = Image.open(path).convert("RGBA")
    w, h = img.size
    vertical = h >= w
    total = h if vertical else w
    n = next((c for c in CANDIDATES if total % c == 0), 1)
    fw = w if vertical else w // n
    fh = h // n if vertical else h
    if abs(fh - fw) <= 0.2 * max(fw, fh):
        kind = "knob_or_button"
    elif fh > fw * 2:
        kind = "vertical_fader"
    elif fw > fh * 2:
        kind = "horizontal_slider"
    else:
        kind = "unknown"
    return {"vertical": vertical, "frames": n,
            "frame_w": fw, "frame_h": fh, "kind": kind}

def extract_frame(path, index, frames, vertical=True):
    img = Image.open(path).convert("RGBA")
    w, h = img.size
    if vertical:
        fh = h // frames
        return img.crop((0, index * fh, w, (index + 1) * fh))
    fw = w // frames
    return img.crop((index * fw, 0, (index + 1) * fw, h))

def restack(path, frames, src_vertical=True, dst_vertical=True):
    """Re-emit every frame in a new orientation (same count)."""
    img = Image.open(path).convert("RGBA")
    fr = [extract_frame(path, i, frames, src_vertical) for i in range(frames)]
    fw, fh = fr[0].size
    out = Image.new("RGBA", (fw, fh * frames) if dst_vertical
                    else (fw * frames, fh), (0, 0, 0, 0))
    for i, f in enumerate(fr):
        out.paste(f, (0, i * fh) if dst_vertical else (i * fw, 0), f)
    return out
```

To change frame *count* (e.g. downsample a 128-frame strip to 64), do not drop
frames — that aliases the animation. Re-render from the source art if you have
it; if you only have the strip, resample by picking evenly spaced source frames
`round(j * (N_src - 1) / (N_dst - 1))` and accept the loss.

## Verifying an imported strip before use

- Confirm 32-bit RGBA with real transparency (not a white/black matte). Zoom to
  400% and check for compression halos at frame edges.
- Confirm the frame count matches what the consuming LookAndFeel expects.
- For purchased packs, confirm the licence permits use in a shipping plugin.

## Anti-patterns

- Reading the frame count from the filename instead of the dimensions.
- Returning the detected count as final with no visual verification.
- Forgetting horizontal strips — testing only `height`.
- Treating a square frame as definitely a knob; small buttons are square too.
- Changing frame count by deleting frames rather than re-rendering or resampling.
- Assuming a JPEG/flattened strip is fine — a non-transparent matte will paint a
  visible box around every control.
