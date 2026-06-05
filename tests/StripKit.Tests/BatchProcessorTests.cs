using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Integration tests for the batch processor over real services and temp files:
/// one strip per input, failure isolation, cancellation between items, and the
/// optional @2x / manifest outputs.
/// </summary>
public class BatchProcessorTests : IDisposable
{
    readonly string _dir;
    readonly string _outDir;
    readonly BatchProcessor _processor =
        new(new ImageLoadService(), new SkiaFilmstripRenderer(), new ExportService(), new ManifestService());

    public BatchProcessorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "stripkit-batch-tests", Guid.NewGuid().ToString("N"));
        _outDir = Path.Combine(_dir, "out");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string WriteKnob(string name, int size = 100)
    {
        var path = Path.Combine(_dir, name);
        using var bmp = TestImages.Knob(size);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
        return path;
    }

    static FilmstripSettings KnobTemplate() => new()
    {
        ComponentType = ComponentType.RotaryKnob, FrameCount = 8,
        FrameWidth = 80, FrameHeight = 80, StartAngleDegrees = -135, EndAngleDegrees = 135,
        Supersample = 1, StackDirection = StackDirection.Vertical,
    };

    BatchOptions Options(IReadOnlyList<string> files, bool at2x = false, bool manifest = false) => new()
    {
        InputFiles = files,
        OutputDirectory = _outDir,
        Settings = KnobTemplate(),
        MatchKnobFrameToSource = true,
        ExportAt2x = at2x,
        ExportManifest = manifest,
    };

    static FilmstripSettings MeterTemplate() => new()
    {
        ComponentType = ComponentType.Meter, FrameCount = 8,
        FrameWidth = 48, FrameHeight = 160, SegmentCount = 12,
        FillDirection = MeterFillDirection.Up, Supersample = 1, StackDirection = StackDirection.Vertical,
    };

    static BatchOptions MeterOptions(IReadOnlyList<string> files, string outDir, bool backdrop) => new()
    {
        InputFiles = files,
        OutputDirectory = outDir,
        Settings = MeterTemplate(),
        MatchKnobFrameToSource = false,
        MeterSourceIsBackdrop = backdrop,
    };

    [Fact]
    public async Task Renders_a_strip_for_each_input()
    {
        var files = new[] { WriteKnob("a.png"), WriteKnob("b.png"), WriteKnob("c.png") };

        var result = await _processor.ProcessAsync(Options(files), null);

        result.Cancelled.Should().BeFalse();
        result.SucceededCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        foreach (var name in new[] { "a", "b", "c" })
        {
            var outPath = Path.Combine(_outDir, $"{name}_8frames.png");
            File.Exists(outPath).Should().BeTrue();
            using var strip = SKBitmap.Decode(outPath);
            strip.Width.Should().Be(100);        // match-to-source: 100×100 art → 100px square frame
            strip.Height.Should().Be(100 * 8);   // 8 frames stacked vertically
        }
    }

    [Fact]
    public async Task Records_a_failure_for_an_undecodable_file_and_keeps_going()
    {
        var good1 = WriteKnob("good1.png");
        var bad = Path.Combine(_dir, "bad.png");
        File.WriteAllText(bad, "not a png");
        var good2 = WriteKnob("good2.png");

        var result = await _processor.ProcessAsync(Options(new[] { good1, bad, good2 }), null);

        result.SucceededCount.Should().Be(2);
        result.FailedCount.Should().Be(1);
        result.Items.Single(i => !i.Success).InputFile.Should().Be(bad);
        File.Exists(Path.Combine(_outDir, "good1_8frames.png")).Should().BeTrue();
        File.Exists(Path.Combine(_outDir, "good2_8frames.png")).Should().BeTrue();
    }

    [Fact]
    public async Task Honors_cancellation_between_items()
    {
        var files = new[] { WriteKnob("a.png"), WriteKnob("b.png"), WriteKnob("c.png") };
        var cts = new CancellationTokenSource();

        // Cancels synchronously after the first item is reported done.
        var result = await _processor.ProcessAsync(Options(files), new CancelAfterFirst(cts), cts.Token);

        result.Cancelled.Should().BeTrue();
        result.SucceededCount.Should().Be(1);
    }

    [Fact]
    public async Task Also_writes_at2x_and_manifest_when_requested()
    {
        var files = new[] { WriteKnob("knob.png") };

        var result = await _processor.ProcessAsync(Options(files, at2x: true, manifest: true), null);

        result.SucceededCount.Should().Be(1);
        File.Exists(Path.Combine(_outDir, "knob_8frames.png")).Should().BeTrue();
        File.Exists(Path.Combine(_outDir, "knob_8frames@2x.png")).Should().BeTrue();
        File.Exists(Path.Combine(_outDir, "knob.skin.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Renders_meters_and_the_backdrop_toggle_changes_the_output()
    {
        var file = WriteKnob("m.png", 64);   // arbitrary art, used as the on-state OR the backdrop
        var layeredDir = Path.Combine(_dir, "layered");
        var backdropDir = Path.Combine(_dir, "backdrop");

        var rl = await _processor.ProcessAsync(MeterOptions(new[] { file }, layeredDir, backdrop: false), null);
        var rb = await _processor.ProcessAsync(MeterOptions(new[] { file }, backdropDir, backdrop: true), null);

        rl.SucceededCount.Should().Be(1);
        rb.SucceededCount.Should().Be(1);

        var layeredPath = Path.Combine(layeredDir, "m_8frames.png");
        using (var strip = SKBitmap.Decode(layeredPath))
        {
            strip.Width.Should().Be(48);
            strip.Height.Should().Be(160 * 8);
        }

        var layeredBytes = await File.ReadAllBytesAsync(layeredPath);
        var backdropBytes = await File.ReadAllBytesAsync(Path.Combine(backdropDir, "m_8frames.png"));
        layeredBytes.Should().NotEqual(backdropBytes,
            "layered reveals the source as lit art; backdrop draws procedural LEDs over it");
    }

    sealed class CancelAfterFirst(CancellationTokenSource cts) : IProgress<BatchProgress>
    {
        int _count;
        public void Report(BatchProgress value)
        {
            if (++_count == 1) cts.Cancel();
        }
    }
}
