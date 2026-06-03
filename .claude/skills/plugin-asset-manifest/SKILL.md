---
name: plugin-asset-manifest
description: >-
  Define and consume a JSON manifest that binds exported control filmstrips to
  plugin parameters for a GUI skinning engine. Use when wiring StripKit or
  KnobMan output into a JUCE LookAndFeel or a VybeForge-style skin, when designing
  a skin.json that maps each control to its asset, frame count, geometry and
  parameter, when a skinning system must auto-load knobs, faders, sliders,
  buttons and meters without hard-coded paths, or when versioning a skin so a
  loader stays compatible. Covers the manifest schema, relative-path and HiDPI
  conventions, a JSON Schema with a worked example, and how a loader picks the
  frame from a normalized value. Triggers on skin manifest, skin.json, plugin
  asset manifest, bind filmstrip to parameter, LookAndFeel from JSON, asset
  manifest schema, frame count, HiDPI at2x asset, skin versioning.
---

# Plugin Asset Manifest

A filmstrip on disk is useless to a skinning engine until something tells it
*which parameter the strip drives, how many frames it has, and where it sits in
the GUI*. A manifest is that contract: one JSON file the engine reads to load and
bind every control, with no hard-coded paths or pixel positions in the C++.

## Core principle

The engine should be **data-driven**: it reads `skin.json`, loads each declared
asset, and binds it to a parameter by id. Adding or restyling a control becomes a
manifest edit plus a new PNG — not a recompile. Keep all paths relative to the
manifest, declare HiDPI variants explicitly, and version the manifest so a newer
skin can refuse to load on an older host instead of crashing.

## Schema

Top-level keys describe the skin; `controls` is an array of bindings.

| Field | Meaning |
|-------|---------|
| `manifestVersion` | Integer. Bump on breaking changes; the loader checks it. |
| `name`, `author`, `skinVersion` | Human metadata. |
| `baseWidth`, `baseHeight` | Design resolution the `bounds` are authored against. |
| `background` | Optional whole-window background image (relative path). |
| `controls[]` | One entry per control (see below). |

Each control:

| Field | Meaning |
|-------|---------|
| `id` | Unique control id within the skin. |
| `type` | `knob` \| `vfader` \| `hslider` \| `button` \| `meter`. |
| `parameterId` | The host/APVTS parameter id this control reads and writes. |
| `asset` | Relative path to the 1x filmstrip PNG. |
| `asset2x` | Optional relative path to the @2x PNG for HiDPI displays. |
| `frames` | Frame count in the strip. |
| `frameWidth`, `frameHeight` | One frame's 1x pixel size. |
| `stack` | `vertical` \| `horizontal` — frame layout in the PNG. |
| `bounds` | `{ x, y, w, h }` in base-resolution pixels. |
| `background` | Optional per-control static layer drawn behind the strip. |
| `valueMin`, `valueMax`, `valueDefault` | Parameter range mapping (optional; many hosts already normalize). |

## Conventions (do not break)

- **Relative paths only.** Resolve every asset relative to the manifest's folder
  so the skin is portable and embeddable as BinaryData.
- **Transparent RGBA frames.** The control's background is the manifest
  `background`/per-control `background`; the strip draws only the control.
- **Vertical stack, frame 0 = minimum.** Match what the exporter produced.
- **HiDPI by naming.** Ship `asset2x` (e.g. `gain_64.png` + `gain_64@2x.png`);
  the loader picks by display scale, never by guessing.
- **`bounds` are in base resolution.** The loader scales them by the actual
  display/window scale — do not bake device pixels into the manifest.
- **`frames` must match the PNG.** A mismatch shows the wrong frame; validate on
  load (`png_height / frames == frameHeight` for a vertical strip).

## JSON Schema (draft)

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["manifestVersion", "name", "baseWidth", "baseHeight", "controls"],
  "properties": {
    "manifestVersion": { "type": "integer", "minimum": 1 },
    "name": { "type": "string" },
    "author": { "type": "string" },
    "skinVersion": { "type": "string" },
    "baseWidth": { "type": "integer", "minimum": 1 },
    "baseHeight": { "type": "integer", "minimum": 1 },
    "background": { "type": "string" },
    "controls": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "type", "parameterId", "asset", "frames",
                     "frameWidth", "frameHeight", "bounds"],
        "properties": {
          "id": { "type": "string" },
          "type": { "enum": ["knob", "vfader", "hslider", "button", "meter"] },
          "parameterId": { "type": "string" },
          "asset": { "type": "string" },
          "asset2x": { "type": "string" },
          "frames": { "type": "integer", "minimum": 1 },
          "frameWidth": { "type": "integer", "minimum": 1 },
          "frameHeight": { "type": "integer", "minimum": 1 },
          "stack": { "enum": ["vertical", "horizontal"], "default": "vertical" },
          "background": { "type": "string" },
          "bounds": {
            "type": "object",
            "required": ["x", "y", "w", "h"],
            "properties": {
              "x": { "type": "number" }, "y": { "type": "number" },
              "w": { "type": "number" }, "h": { "type": "number" }
            }
          },
          "valueMin": { "type": "number" },
          "valueMax": { "type": "number" },
          "valueDefault": { "type": "number" }
        }
      }
    }
  }
}
```

## Worked example

```json
{
  "manifestVersion": 1,
  "name": "Petrol Synth Skin",
  "author": "Vibrant Mindz",
  "skinVersion": "1.0.0",
  "baseWidth": 800,
  "baseHeight": 480,
  "background": "bg/panel.png",
  "controls": [
    {
      "id": "cutoff",
      "type": "knob",
      "parameterId": "filterCutoff",
      "asset": "knobs/big_64.png",
      "asset2x": "knobs/big_64@2x.png",
      "frames": 64,
      "frameWidth": 80,
      "frameHeight": 80,
      "stack": "vertical",
      "bounds": { "x": 120, "y": 90, "w": 80, "h": 80 }
    },
    {
      "id": "level",
      "type": "vfader",
      "parameterId": "outputGain",
      "asset": "faders/cap_64.png",
      "frames": 64,
      "frameWidth": 40,
      "frameHeight": 128,
      "stack": "vertical",
      "background": "faders/track.png",
      "bounds": { "x": 700, "y": 60, "w": 40, "h": 128 }
    }
  ]
}
```

## How a loader consumes it

For a control bound to a parameter whose normalized value is `value01` in `[0,1]`:

```
frameIndex = round(value01 * (frames - 1))     // (frames-1): last frame = max
srcRect    = vertical ? (0, frameIndex*frameHeight, frameWidth, frameHeight)
                      : (frameIndex*frameWidth, 0, frameWidth, frameHeight)
drawScale  = displayScale                        // 1x asset on a 2x display => 0.5 source-to-dest, or load asset2x
```

A JUCE `LookAndFeel` reads the manifest once at construction, loads each asset
(from BinaryData or file), and in `drawRotarySlider` / `drawLinearSlider` blits
`srcRect` into the control's scaled `bounds`. Pick `asset2x` when
`Desktop::getInstance().getDisplays().getPrimaryDisplay()->scale >= 2`.

## Anti-patterns

- Absolute paths — the skin breaks the moment it moves or is embedded.
- Baking device pixels into `bounds` instead of base-resolution + scale.
- Omitting `manifestVersion` — an old loader then silently mis-reads a new skin.
- `frames` that disagrees with the PNG height — every control shows wrong frames.
- Flattened/opaque assets — the control paints a visible rectangle.
- Shipping only `asset` and stretching it on Retina instead of providing `asset2x`.
