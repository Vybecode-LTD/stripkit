---
name: live-preview-render-loop
description: >-
  Wire a responsive, settings-driven live preview into a desktop app so dragging
  a control updates the rendered result smoothly without freezing the UI or
  redrawing redundantly. Use when building a tool with a live preview pane (image,
  audio waveform, chart, document), when a preview stutters or the window freezes
  during render, when many settings should each trigger a re-render, when
  scrubbing or auto-playing through a value, or when an expensive render must be
  debounced and run off the UI thread. Covers a single refresh funnel, suppressing
  redundant refreshes during bulk updates, debouncing, off-thread rendering with
  stale-result dropping, capping preview quality while exporting full, and keeping
  the scrub or play loop in the view. Triggers on live preview, preview not
  updating, UI freezes while rendering, debounce render, off-thread render, scrub
  preview, re-render on settings change, stale render result, MVVM preview.
---

# Live Preview Render Loop

A live preview is the difference between a tool that feels alive and one that
feels broken. The user drags a slider and expects the picture to follow. Getting
this right is mostly about three failures to avoid: freezing the window on a
heavy render, redrawing far more often than needed, and letting a slow old render
overwrite a newer one.

## Core principle

Separate **what to render** (the settings, held in the view model) from **the
render itself** (a pure function with no UI types). Then funnel every settings
change through a single refresh path, keep that path cheap enough to feel
instant, and push any heavy work off the UI thread. The view model stays a plain
data object you can unit-test; the rendering is a function you can call from
anywhere.

## The single refresh funnel

Route all observable setting changes to one `RefreshPreview()` rather than
sprinkling refresh calls through every setter. With CommunityToolkit.Mvvm, the
cleanest funnel is to override `OnPropertyChanged` and ignore the *output*
properties so you do not loop:

```csharp
protected override void OnPropertyChanged(PropertyChangedEventArgs e)
{
    base.OnPropertyChanged(e);
    switch (e.PropertyName)
    {
        case nameof(PreviewImage):   // outputs — ignore, or you recurse forever
        case nameof(StatusMessage):
            return;
    }
    if (_suspendRefresh) return;
    RefreshPreview();
}
```

*Why a funnel:* one place to add debouncing, threading, and stale-result
handling later, instead of retrofitting them into a dozen setters.

## Suppress redundant refreshes during bulk updates

When you set several properties at once — loading a file, applying a preset —
each setter fires the funnel. Guard with a flag and refresh once at the end:

```csharp
_suspendRefresh = true;
FrameWidth = src.Width; FrameHeight = src.Height; FrameCount = 64;
_suspendRefresh = false;
RefreshPreview();
```

## Calibrate to render cost

- **Cheap render (sub-millisecond, e.g. a small image frame)** → render
  synchronously in `RefreshPreview` on the UI thread. Do not add threads or
  debouncing; the machinery would cost more than the render. This is the right
  choice for small previews.
- **Expensive render (tens of ms or more)** → debounce and move off-thread (next
  two sections). Match the ceremony to the cost; do not thread a trivial render.

## Debounce rapid changes (expensive renders)

A slider drag fires dozens of changes a second. Do not render each one — render
after a short quiet period. Restart a timer on every change; render on its tick:

```csharp
private readonly System.Timers.Timer _debounce = new(60) { AutoReset = false };
// in ctor: _debounce.Elapsed += (_, _) => StartRender();
private void RefreshPreview() { _debounce.Stop(); _debounce.Start(); }
```

## Render off the UI thread, and drop stale results

The render is pure and uses no UI types, so run it on a background thread and
marshal only the finished result back. Crucially, a fast new request can finish
before a slow old one — tag each request and ignore results that are no longer
the latest, or the preview flickers backwards:

```csharp
private int _renderId;

private async void StartRender()
{
    if (_source is null) return;
    int id = ++_renderId;                 // this request's ticket
    var settings = BuildSettings();
    var src = _source;

    var bitmap = await Task.Run(() => RenderToUiBitmap(settings, src)); // pure work

    if (id != _renderId) { bitmap.Dispose(); return; } // a newer request won; discard
    PreviewImage = bitmap;                 // assign on the UI thread (continuation)
}
```

(`async void` is acceptable here only because this is an event-style entry point.
If your render helper returns a UI bitmap, build it on the background thread and
assign the property in the continuation, which is already on the UI thread when
awaited from it; if you marshal manually, use the framework dispatcher.)

## Preview quality vs export quality

Render the preview cheaply and the export at full fidelity. Cap the expensive
knobs for preview only — for an image tool, render the preview near display size
and cap supersampling (e.g. to 2x), while export uses the full setting. The user
sees a crisp-enough preview that tracks their input, and pays the full cost only
once, at export.

```csharp
var preview = BuildSettings();
preview.Supersample = Math.Min(preview.Supersample, 2);   // export keeps the real value
```

## Scrub and play belong in the view

A value slider that scrubs the preview is just a two-way bound `double` in
[0,1]. An auto-play that sweeps that value is a timer — and a timer that animates
a value is a **view concern**, so put it in code-behind, not the view model. The
view nudges the bound value each tick; the funnel re-renders. The view model
stays free of UI timers and remains unit-testable.

```csharp
// code-behind: DispatcherTimer ticks -> vm.PreviewValue += step (bounce at 0 and 1)
```

## Anti-patterns

- Rendering on the UI thread for a heavy render — the window freezes mid-drag.
- Threading and debouncing a trivial render — the machinery costs more than it
  saves; render cheap previews synchronously.
- Refreshing on every property change including outputs — infinite loops.
- No stale-result guard — a slow old render overwrites a newer one and the
  preview jumps backwards.
- Re-rendering on every drag tick with no debounce for an expensive render.
- Previewing at full export quality — laggy; cap preview quality, export full.
- Putting the play/scrub timer in the view model — couples it to a UI timer and
  breaks testability.
- Scattering refresh calls through every setter instead of one funnel — there is
  then no single place to add debouncing or threading.
