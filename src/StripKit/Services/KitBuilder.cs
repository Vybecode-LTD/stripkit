using SkiaSharp;
using StripKit.Models;

namespace StripKit.Services;

/// <inheritdoc cref="IKitBuilder"/>
public sealed class KitBuilder : IKitBuilder
{
    private readonly ILayeredImportService _layeredImport;
    private readonly IFilmstripRenderer _renderer;
    private readonly IExportService _export;
    private readonly IManifestService _manifest;

    // The generated skin.json lays controls out in a single left-to-right row with padding, so an
    // author opens it to see the family side by side rather than stacked at the origin. Base-res px.
    private const int LayoutPad = 24;   // margin around the row
    private const int LayoutGap = 24;   // gap between controls

    public KitBuilder(ILayeredImportService layeredImport, IFilmstripRenderer renderer,
                      IExportService export, IManifestService manifest)
    {
        _layeredImport = layeredImport;
        _renderer = renderer;
        _export = export;
        _manifest = manifest;
    }

    public async Task<KitBuildResult> BuildAsync(IReadOnlyList<KitControlSource> sources,
                                                 KitBuildOptions options, CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        string prefix = Slug(options.FilePrefix, "kit");

        var results = new List<KitControlResult>(sources.Count);
        var controls = new List<ManifestControl>();

        double x = LayoutPad;
        double rowHeight = 0;

        // Disambiguate duplicate types (e.g. two knobs in one kit) so filenames + control ids can't collide.
        var typeSeen = new Dictionary<ComponentType, int>();

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();

            int ordinal = typeSeen.TryGetValue(source.Type, out var seen) ? seen + 1 : 0;
            typeSeen[source.Type] = ordinal;
            string typeSlug = TypeSlug(source.Type);
            string controlId = ordinal == 0 ? $"{prefix}-{typeSlug}" : $"{prefix}-{typeSlug}-{ordinal + 1}";

            PreparedRender? prep;
            try
            {
                prep = await Task.Run(() => Prepare(source, options), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                results.Add(Fail(source.Type, ex.Message));
                continue;
            }

            if (prep is null)
            {
                results.Add(Fail(source.Type, "Couldn't read any layers from the generated art."));
                continue;
            }

            using (prep)
            {
                try
                {
                    string assetName = $"{controlId}.png";
                    string assetPath = Path.Combine(options.OutputDirectory, assetName);

                    using (var strip = await Task.Run(
                        () => _renderer.RenderStrip(prep.Settings, prep.Source, prep.Background, 1.0, prep.LayerArt), ct))
                        await _export.SavePngAsync(strip, assetPath);

                    string? asset2xName = null;
                    if (options.ExportAt2x && options.HiDpiScale > 1)
                    {
                        asset2xName = $"{controlId}@{options.HiDpiScale}x.png";
                        using var strip2x = await Task.Run(
                            () => _renderer.RenderStrip(prep.Settings, prep.Source, prep.Background, options.HiDpiScale, prep.LayerArt), ct);
                        await _export.SavePngAsync(strip2x, Path.Combine(options.OutputDirectory, asset2xName));
                    }

                    // Reuse the tested single-control builder for the type mapping + grid clamping, then
                    // place it in the row (records are immutable — override bounds via `with`).
                    var control = _manifest
                        .BuildSingleControl(prep.Settings, assetName, asset2xName, controlId, controlId)
                        .Controls[0] with
                        {
                            Bounds = new ManifestBounds(x, LayoutPad, prep.Settings.FrameWidth, prep.Settings.FrameHeight),
                        };
                    controls.Add(control);

                    x += prep.Settings.FrameWidth + LayoutGap;
                    rowHeight = Math.Max(rowHeight, prep.Settings.FrameHeight);

                    results.Add(new KitControlResult
                    {
                        Type = source.Type, Success = true, AssetPath = assetPath, Warning = prep.Warning,
                    });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    results.Add(Fail(source.Type, ex.Message));
                }
            }
        }

        string? skinPath = null;
        if (controls.Count > 0)
        {
            // Trailing gap trimmed, a symmetric right/bottom margin added back.
            int baseWidth = (int)Math.Ceiling(x - LayoutGap + LayoutPad);
            int baseHeight = (int)Math.Ceiling(rowHeight + LayoutPad * 2);
            var manifest = _manifest.BuildManifest(controls, options.KitName, options.Author, baseWidth, baseHeight, null);
            skinPath = Path.Combine(options.OutputDirectory, $"{prefix}.skin.json");
            await _manifest.SaveAsync(manifest, skinPath);
        }

        return new KitBuildResult(results, skinPath, options.OutputDirectory);
    }

    // ---- per-type preparation (mirrors MainWindowViewModel's import paths) ----

    /// <summary>Imports one control's layered art and builds the render inputs for its type. Returns
    /// null when the art yields no readable layers. Pure CPU work — call inside <see cref="Task.Run(Action)"/>.</summary>
    private PreparedRender? Prepare(KitControlSource source, KitBuildOptions options)
    {
        var import = _layeredImport.Import(source.SourcePath);
        if (import is null || import.Layers.Count == 0) return null;

        try
        {
            return source.Type switch
            {
                ComponentType.Meter => PrepareMeter(import, options),
                ComponentType.VerticalFader or ComponentType.HorizontalSlider => PrepareLinear(import, source.Type, options),
                ComponentType.Button or ComponentType.Toggle => PrepareStateFrames(import, source.Type, options),
                _ => PrepareKnob(import, options),
            };
        }
        catch
        {
            // A helper threw after Import() but before ownership transferred to a PreparedRender (e.g. an
            // OOM allocating a copy on a huge canvas). Dispose every imported layer bitmap so the native
            // art can't leak — SKBitmap.Dispose is idempotent, so a helper that already disposed them on
            // its own success path is harmless to re-dispose (a success path never reaches here anyway).
            foreach (var layer in import.Layers)
                try { layer.Art.Dispose(); } catch { /* best-effort */ }
            throw;
        }
    }

    /// <summary>Layered knob: the whole layer stack drives the render (a Static body + a Rotate pointer),
    /// index-matched to <see cref="FilmstripSettings.Layers"/>; the rotation pivot is the merged art's
    /// detected content centre (the knob axis). Square frame to the document canvas.</summary>
    private static PreparedRender PrepareKnob(LayeredImportResult import, KitBuildOptions options)
    {
        int edge = Math.Max(import.CanvasWidth, import.CanvasHeight);
        var (cx, cy) = DetectCenter(import);

        var settings = new FilmstripSettings
        {
            ComponentType = ComponentType.RotaryKnob,
            FrameCount = Math.Max(2, options.FrameCount),
            FrameWidth = edge,
            FrameHeight = edge,
            Supersample = ClampSs(options.Supersample),
            SourceCenterX = cx,
            SourceCenterY = cy,
        };

        var layerArt = new List<SKBitmap>(import.Layers.Count);
        foreach (var layer in import.Layers)
        {
            settings.Layers.Add(new RenderLayer { Behavior = layer.SuggestedBehavior, PivotX = cx, PivotY = cy });
            layerArt.Add(layer.Art);
        }

        string? warn = import.Layers.All(l => l.SuggestedBehavior != LayerBehavior.Rotate)
            ? "No rotating layer detected — the knob won't animate."
            : null;
        return new PreparedRender(layerArt) { Settings = settings, LayerArt = layerArt, Warning = warn };
    }

    /// <summary>Button / toggle: each layer is Static (all frames) or Frame-indexed (off / on / …); one
    /// frame per Frame-behavior layer, keeping the canvas size.</summary>
    private static PreparedRender PrepareStateFrames(LayeredImportResult import, ComponentType type, KitBuildOptions options)
    {
        int frameLayers = import.Layers.Count(l => l.SuggestedBehavior == LayerBehavior.Frame);
        var settings = new FilmstripSettings
        {
            ComponentType = type,
            FrameCount = Math.Max(2, frameLayers),
            FrameWidth = import.CanvasWidth,
            FrameHeight = import.CanvasHeight,
            Supersample = ClampSs(options.Supersample),
        };

        var layerArt = new List<SKBitmap>(import.Layers.Count);
        foreach (var layer in import.Layers)
        {
            settings.Layers.Add(new RenderLayer { Behavior = layer.SuggestedBehavior, PivotX = 0.5, PivotY = 0.5 });
            layerArt.Add(layer.Art);
        }

        string? warn = frameLayers < 2 ? "Fewer than two on/off state layers — the states may not switch." : null;
        return new PreparedRender(layerArt) { Settings = settings, LayerArt = layerArt, Warning = warn };
    }

    /// <summary>Fader / slider: a single static cap the linear renderer translates — flatten the layer
    /// stack into one source bitmap (the layer stack is knob/button-only) and use linear frame defaults.</summary>
    private static PreparedRender PrepareLinear(LayeredImportResult import, ComponentType type, KitBuildOptions options)
    {
        var flat = Flatten(import);
        foreach (var layer in import.Layers) layer.Art.Dispose();   // the flattened copy is all we keep

        var settings = new FilmstripSettings
        {
            ComponentType = type,
            FrameCount = Math.Max(2, options.FrameCount),
            FrameWidth = type == ComponentType.VerticalFader ? 40 : 128,
            FrameHeight = type == ComponentType.VerticalFader ? 128 : 32,
            Supersample = ClampSs(options.Supersample),
            SourceCenterX = 0.5,
            SourceCenterY = 0.5,
        };
        return new PreparedRender(new[] { flat }) { Settings = settings, Source = flat };
    }

    /// <summary>Meter: the layer named "on" (else the top layer) is the lit source revealed up to the
    /// value; "off" (else the bottom layer) is the full off-state background. Fills along the art's long
    /// axis. Smooth reveal (continuous fill), matching the Create tab's generated-meter adoption.</summary>
    private static PreparedRender PrepareMeter(LayeredImportResult import, KitBuildOptions options)
    {
        int w = import.CanvasWidth, h = import.CanvasHeight;

        var on = import.Layers.FirstOrDefault(l => l.Name.Trim().Equals("on", StringComparison.OrdinalIgnoreCase))
                 ?? import.Layers[^1];                                    // top = lit, by convention
        var off = import.Layers.FirstOrDefault(l => l.Name.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
                  ?? (import.Layers.Count > 1 ? import.Layers[0] : null); // bottom = unlit

        var onArt = CopyCanvas(on.Art, w, h);
        SKBitmap? offArt = null;
        try
        {
            offArt = off is not null && !ReferenceEquals(off, on) ? CopyCanvas(off.Art, w, h) : null;
        }
        catch
        {
            onArt.Dispose();   // the first copy is orphaned if the second throws (import arts freed by Prepare's guard)
            throw;
        }
        foreach (var layer in import.Layers) layer.Art.Dispose();        // the copies are all we keep

        var settings = new FilmstripSettings
        {
            ComponentType = ComponentType.Meter,
            FrameCount = Math.Max(2, options.FrameCount),
            FrameWidth = w,
            FrameHeight = h,
            Supersample = ClampSs(options.Supersample),
            ContinuousFill = true,                                       // generated art reveals smoothly
            FillDirection = w > h ? MeterFillDirection.LeftToRight : MeterFillDirection.Up,
        };

        var owned = offArt is not null ? new[] { onArt, offArt } : new[] { onArt };
        return new PreparedRender(owned) { Settings = settings, Source = onArt, Background = offArt };
    }

    // ---- helpers ----

    private static SKBitmap Flatten(LayeredImportResult import)
    {
        var flat = new SKBitmap(import.CanvasWidth, import.CanvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        try
        {
            using var c = new SKCanvas(flat);
            c.Clear(SKColors.Transparent);
            foreach (var layer in import.Layers) c.DrawBitmap(layer.Art, 0, 0);
            return flat;
        }
        catch
        {
            flat.Dispose();   // don't leak the interim bitmap if a draw/alloc fails mid-flatten
            throw;
        }
    }

    private static (double cx, double cy) DetectCenter(LayeredImportResult import)
    {
        if (import.CanvasWidth <= 0 || import.CanvasHeight <= 0) return (0.5, 0.5);
        using var merged = Flatten(import);
        return ContentAnalysis.DetectContentCenter(merged);
    }

    private static SKBitmap CopyCanvas(SKBitmap src, int w, int h)
    {
        var copy = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(copy);
        c.Clear(SKColors.Transparent);
        c.DrawBitmap(src, 0, 0);
        return copy;
    }

    private static int ClampSs(int ss) => Math.Clamp(ss, 1, 8);

    private static KitControlResult Fail(ComponentType type, string error) =>
        new() { Type = type, Success = false, Error = error };

    private static string TypeSlug(ComponentType type) => type switch
    {
        ComponentType.RotaryKnob => "knob",
        ComponentType.VerticalFader => "fader",
        ComponentType.HorizontalSlider => "slider",
        ComponentType.Meter => "meter",
        ComponentType.Button => "button",
        ComponentType.Toggle => "toggle",
        _ => "control",
    };

    /// <summary>Lowercases and hyphenates a prefix into a filename-safe slug, falling back when empty.</summary>
    private static string Slug(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var chars = value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? fallback : slug;
    }

    /// <summary>Owns the transient bitmaps for one control's render (the flattened source, meter copies,
    /// or the imported layer stack) and disposes them once both the 1x and @Nx strips are rendered.</summary>
    private sealed class PreparedRender : IDisposable
    {
        private readonly List<SKBitmap> _owned;

        public PreparedRender(IEnumerable<SKBitmap> owned) => _owned = owned.ToList();

        public required FilmstripSettings Settings { get; init; }
        public SKBitmap? Source { get; init; }
        public SKBitmap? Background { get; init; }
        public IReadOnlyList<SKBitmap>? LayerArt { get; init; }
        public string? Warning { get; init; }

        public void Dispose()
        {
            foreach (var b in _owned) b.Dispose();
        }
    }
}
