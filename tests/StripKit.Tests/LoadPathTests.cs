using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Unit tests for the shared source-load path. Both the "Load source image…"
/// button and the drag-and-drop handler funnel through
/// <see cref="MainWindowViewModel.LoadSourceFromPath"/>, so testing it here is
/// what covers the drag-and-drop logic.
/// </summary>
public class LoadPathTests
{
    static SKBitmap Bmp(int w, int h) => new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

    static (MainWindowViewModel vm, IImageLoadService load, IFileDialogService dialogs) Build()
    {
        var load = Substitute.For<IImageLoadService>();
        var renderer = Substitute.For<IFilmstripRenderer>();
        var dialogs = Substitute.For<IFileDialogService>();
        var export = Substitute.For<IExportService>();

        // The preview path converts a rendered frame into an Avalonia Bitmap, which
        // needs a UI platform. These are plain unit tests, so force the render to
        // throw: RefreshPreview swallows it, and the load STATE asserted below is
        // set before the preview ever runs.
        renderer
            .RenderFrame(Arg.Any<FilmstripSettings>(), Arg.Any<SKBitmap>(), Arg.Any<SKBitmap?>(), Arg.Any<int>(), Arg.Any<double>())
            .Throws(new InvalidOperationException("preview not exercised in unit tests"));

        var importer = new ImporterViewModel(
            Substitute.For<IImageLoadService>(),
            Substitute.For<IFilmstripImporter>(),
            Substitute.For<IFileDialogService>(),
            Substitute.For<IExportService>());
        var batch = new BatchViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IBatchProcessor>());
        var skin = new SkinViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IImageLoadService>(),
                                     Substitute.For<IFilmstripImporter>(), Substitute.For<IManifestService>());

        return (new MainWindowViewModel(load, renderer, dialogs, export, Substitute.For<IManifestService>(),
                                        new CodeSnippetService(), importer, batch, skin), load, dialogs);
    }

    [Fact]
    public void LoadSourceFromPath_sets_source_state_and_squares_the_frame_for_a_knob()
    {
        var (vm, load, _) = Build();
        load.Load("knob.png").Returns(Bmp(120, 80));
        vm.ComponentType = ComponentType.RotaryKnob;

        vm.LoadSourceFromPath("knob.png");

        vm.HasSource.Should().BeTrue();
        vm.SourceInfo.Should().Contain("knob.png").And.Contain("120×80");
        vm.FrameWidth.Should().Be(120);   // max(120, 80) — the square knob frame
        vm.FrameHeight.Should().Be(120);
    }

    [Fact]
    public void LoadSourceFromPath_reports_an_error_when_the_image_cannot_be_decoded()
    {
        var (vm, load, _) = Build();
        load.Load("bad.png").Returns((SKBitmap?)null);

        vm.LoadSourceFromPath("bad.png");

        vm.HasSource.Should().BeFalse();
        vm.StatusMessage.Should().Contain("could not load");
    }

    [Fact]
    public async Task OpenSource_button_uses_the_same_load_path_as_a_drop()
    {
        var (vm, load, dialogs) = Build();
        dialogs.OpenImageAsync().Returns(Task.FromResult<string?>("picked.png"));
        load.Load("picked.png").Returns(Bmp(64, 64));

        await vm.OpenSourceCommand.ExecuteAsync(null);

        vm.HasSource.Should().BeTrue();
        vm.SourceInfo.Should().Contain("picked.png");
        load.Received(1).Load("picked.png"); // single shared load path, no duplication
    }

    [Fact]
    public void Export_is_disabled_until_a_source_is_loaded()
    {
        var (vm, load, _) = Build();
        vm.ExportCommand.CanExecute(null).Should().BeFalse();

        load.Load("k.png").Returns(Bmp(50, 50));
        vm.LoadSourceFromPath("k.png");

        vm.ExportCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Export_is_enabled_for_a_procedural_meter_even_without_a_source()
    {
        var (vm, _, _) = Build();
        vm.ExportCommand.CanExecute(null).Should().BeFalse();   // knob, no source yet

        vm.ComponentType = ComponentType.Meter;
        vm.ExportCommand.CanExecute(null).Should().BeTrue();    // a procedural meter needs none
    }

    [Fact]
    public void Loading_an_offcenter_knob_auto_centers_on_its_content()
    {
        var (vm, load, _) = Build();
        load.Load("k.png").Returns(OffCenter(100, 100));

        vm.LoadSourceFromPath("k.png"); // default component type is a rotary knob

        // Opaque box at x/y 10..40 → content centre ≈ (0.25, 0.25), not the (0.5, 0.5) default.
        vm.SourceCenterX.Should().BeLessThan(0.4);
        vm.SourceCenterY.Should().BeLessThan(0.4);
    }

    [Fact]
    public void Source_center_persists_when_the_guide_is_toggled_off()
    {
        var (vm, load, _) = Build();
        load.Load("k.png").Returns(OffCenter(100, 100));
        vm.LoadSourceFromPath("k.png");

        vm.SourceCenterX = 0.72;   // a crosshair drag commits a chosen centre
        vm.SourceCenterY = 0.18;

        vm.ShowCenterGuide = true;   // enter align mode
        vm.ShowCenterGuide = false;  // "remove the crosshairs"

        vm.SourceCenterX.Should().Be(0.72);  // must NOT revert
        vm.SourceCenterY.Should().Be(0.18);
    }

    [Fact]
    public void LoadBaseLayerFromPath_sets_state_squares_the_frame_and_seeds_the_pointer_pivot()
    {
        var (vm, load, _) = Build();
        vm.ComponentType = ComponentType.RotaryKnob;
        load.Load("body.png").Returns(OffCenter(100, 100)); // opaque box top-left → centre ≈ (0.25, 0.25)

        vm.LoadBaseLayerFromPath("body.png");

        vm.HasBaseLayer.Should().BeTrue();
        vm.BaseLayerInfo.Should().Contain("body.png").And.Contain("100×100");
        vm.FrameWidth.Should().Be(100);   // the body squares the knob frame
        vm.FrameHeight.Should().Be(100);
        vm.PointerPivotX.Should().BeLessThan(0.4, "the pointer pivot seeds from the body's content centre");
        vm.PointerPivotY.Should().BeLessThan(0.4);
        vm.ExportCommand.CanExecute(null).Should().BeTrue("a base layer is exportable without a single source");
    }

    [Fact]
    public void LoadPointerFromPath_sets_pointer_state()
    {
        var (vm, load, _) = Build();
        load.Load("ptr.png").Returns(Bmp(100, 100));

        vm.LoadPointerFromPath("ptr.png");

        vm.HasPointer.Should().BeTrue();
        vm.PointerInfo.Should().Contain("ptr.png").And.Contain("100×100");
    }

    [Fact]
    public void Clearing_the_base_layer_disables_export_again()
    {
        var (vm, load, _) = Build();
        load.Load("body.png").Returns(Bmp(64, 64));
        vm.LoadBaseLayerFromPath("body.png");
        vm.ExportCommand.CanExecute(null).Should().BeTrue();

        vm.ClearBaseLayerCommand.Execute(null);

        vm.HasBaseLayer.Should().BeFalse();
        vm.ExportCommand.CanExecute(null).Should().BeFalse("nothing is loaded to export");
    }

    [Fact]
    public async Task AutoExtractPointer_splits_a_flat_knob_into_the_base_and_pointer_slots()
    {
        var (vm, load, dialogs) = Build();
        dialogs.OpenImageAsync().Returns(Task.FromResult<string?>("flatknob.png"));
        load.Load("flatknob.png").Returns(TestImages.Knob(100));   // a flat knob: body + ring + indicator

        await vm.AutoExtractPointerCommand.ExecuteAsync(null);

        vm.HasBaseLayer.Should().BeTrue();
        vm.HasPointer.Should().BeTrue();
        vm.PointerInfo.Should().Contain("Auto-extracted");
        vm.ExportCommand.CanExecute(null).Should().BeTrue("a layered knob is exportable");
    }

    // An image whose opaque content sits in the top-left, not the centre.
    static SKBitmap OffCenter(int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        c.DrawRect(new SKRect(10, 10, 40, 40), p);
        return bmp;
    }
}
