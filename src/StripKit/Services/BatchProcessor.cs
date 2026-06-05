using StripKit.Models;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class BatchProcessor : IBatchProcessor
{
    private readonly IImageLoadService _imageLoad;
    private readonly IFilmstripRenderer _renderer;
    private readonly IExportService _export;
    private readonly IManifestService _manifest;

    public BatchProcessor(IImageLoadService imageLoad, IFilmstripRenderer renderer,
                          IExportService export, IManifestService manifest)
    {
        _imageLoad = imageLoad;
        _renderer = renderer;
        _export = export;
        _manifest = manifest;
    }

    // The whole loop runs on a thread-pool thread (Task.Run), so every decode/render/
    // encode is off the UI thread. The cancellation token is NOT passed to Task.Run
    // (so a cancel never surfaces as an exception to the caller) — instead the loop
    // checks it between items and returns a result with Cancelled = true.
    public Task<BatchResult> ProcessAsync(BatchOptions options, IProgress<BatchProgress>? progress,
                                          CancellationToken cancellationToken = default)
        => Task.Run(async () =>
        {
            var results = new List<BatchItemResult>();
            Directory.CreateDirectory(options.OutputDirectory);
            int total = options.InputFiles.Count;
            int completed = 0;
            bool cancelled = false;

            foreach (var input in options.InputFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var baseName = Path.GetFileNameWithoutExtension(input);
                try
                {
                    using var source = _imageLoad.Load(input)
                        ?? throw new InvalidOperationException("could not decode the image");

                    var settings = options.Settings.Clone();
                    if (options.MatchKnobFrameToSource && settings.ComponentType == ComponentType.RotaryKnob)
                    {
                        settings.FrameWidth = Math.Max(source.Width, source.Height);
                        settings.FrameHeight = settings.FrameWidth;
                    }

                    // A meter file is either the lit on-state art (layered) or a housing the
                    // procedural LEDs are drawn over (backdrop) — see options.MeterSourceIsBackdrop.
                    bool meterBackdrop = settings.ComponentType == ComponentType.Meter && options.MeterSourceIsBackdrop;
                    var renderSource = meterBackdrop ? null : source;
                    var renderBackground = meterBackdrop ? source : null;

                    var assetName = $"{baseName}_{settings.FrameCount}frames.png";
                    var outPath = Path.Combine(options.OutputDirectory, assetName);
                    using (var strip = _renderer.RenderStrip(settings, renderSource, renderBackground, 1.0))
                        await _export.SavePngAsync(strip, outPath);

                    string? asset2xName = null;
                    if (options.ExportAt2x)
                    {
                        asset2xName = $"{baseName}_{settings.FrameCount}frames@2x.png";
                        using var strip2x = _renderer.RenderStrip(settings, renderSource, renderBackground, 2.0);
                        await _export.SavePngAsync(strip2x, Path.Combine(options.OutputDirectory, asset2xName));
                    }

                    if (options.ExportManifest)
                    {
                        var manifest = _manifest.BuildSingleControl(settings, assetName, asset2xName, baseName, baseName);
                        await _manifest.SaveAsync(manifest, Path.Combine(options.OutputDirectory, baseName + ".skin.json"));
                    }

                    results.Add(new BatchItemResult(input, true, outPath, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BatchItemResult(input, false, null, ex.Message));
                }

                completed++;
                progress?.Report(new BatchProgress(completed, total, Path.GetFileName(input)));
            }

            return new BatchResult(results, cancelled);
        });
}
