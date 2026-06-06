using FluentAssertions;
using NSubstitute;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The Create-tab "import layered file" command (★ #3 step 3): a real <see cref="LayeredImportService"/>
/// parses a temp SVG, and the view model maps the layers onto tagged rows, gates the UI, enforces the
/// base/pointer ↔ import mutual exclusivity, and feeds the imported rows to the renderer as the
/// <see cref="FilmstripSettings.Layers"/> stack + index-matched layer art.
/// </summary>
public class LayeredImportViewModelTests
{
    const string KnobSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="body"><circle cx="50" cy="50" r="40" fill="#333333"/></g>
          <g id="pointer"><line x1="50" y1="50" x2="50" y2="12" stroke="#ffffff" stroke-width="6"/></g>
        </svg>
        """;

    static SKBitmap Bmp(int w, int h) => new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

    static string WriteTempSvg(string svg)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_vm_{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, svg);
        return path;
    }

    static (MainWindowViewModel vm, IFileDialogService dialogs, IFilmstripRenderer renderer, IImageLoadService load) Build()
    {
        var load = Substitute.For<IImageLoadService>();
        var renderer = Substitute.For<IFilmstripRenderer>();
        var dialogs = Substitute.For<IFileDialogService>();
        var export = Substitute.For<IExportService>();

        var importer = new ImporterViewModel(Substitute.For<IImageLoadService>(), Substitute.For<IFilmstripImporter>(),
                                             Substitute.For<IFileDialogService>(), Substitute.For<IExportService>());
        var batch = new BatchViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IBatchProcessor>());
        var skin = new SkinViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IImageLoadService>(),
                                     Substitute.For<IFilmstripImporter>(), Substitute.For<IManifestService>());

        var vm = new MainWindowViewModel(load, renderer, dialogs, export, Substitute.For<IManifestService>(),
                                         new CodeSnippetService(), new LayeredImportService(), importer, batch, skin);
        return (vm, dialogs, renderer, load);
    }

    [Fact]
    public async Task Importing_a_layered_svg_populates_tagged_layer_rows_and_gates_the_ui()
    {
        var (vm, dialogs, _, _) = Build();
        var path = WriteTempSvg(KnobSvg);
        dialogs.OpenLayeredFileAsync().Returns(path);
        try
        {
            await vm.ImportLayeredFileCommand.ExecuteAsync(null);

            vm.HasImportedLayers.Should().BeTrue();
            vm.ImportedLayers.Should().HaveCount(2);
            vm.ImportedLayers[0].Name.Should().Be("body");
            vm.ImportedLayers[0].Behavior.Should().Be(LayerBehavior.Static);
            vm.ImportedLayers[1].Name.Should().Be("pointer");
            vm.ImportedLayers[1].Behavior.Should().Be(LayerBehavior.Rotate, "an indicator-named layer is guessed to rotate");

            vm.ComponentType.Should().Be(ComponentType.RotaryKnob, "layered import is knob-only");
            vm.FrameWidth.Should().Be(vm.FrameHeight, "the frame is squared to the document canvas");
            vm.ShowLoadHint.Should().BeFalse("an imported stack is something to preview");
            vm.ExportCommand.CanExecute(null).Should().BeTrue("a layered knob exports without a single source");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Loading_a_base_layer_clears_an_active_import()
    {
        var (vm, dialogs, _, load) = Build();
        var path = WriteTempSvg(KnobSvg);
        dialogs.OpenLayeredFileAsync().Returns(path);
        load.Load("body.png").Returns(Bmp(100, 100));
        try
        {
            await vm.ImportLayeredFileCommand.ExecuteAsync(null);
            vm.HasImportedLayers.Should().BeTrue();

            vm.LoadBaseLayerFromPath("body.png");

            vm.HasImportedLayers.Should().BeFalse("base/pointer and an imported stack are mutually exclusive");
            vm.ImportedLayers.Should().BeEmpty();
            vm.HasBaseLayer.Should().BeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Clearing_the_import_drops_the_layers_and_the_preview()
    {
        var (vm, dialogs, _, _) = Build();
        var path = WriteTempSvg(KnobSvg);
        dialogs.OpenLayeredFileAsync().Returns(path);
        try
        {
            await vm.ImportLayeredFileCommand.ExecuteAsync(null);
            vm.ImportedLayers.Should().HaveCount(2);

            vm.ClearImportedLayersCommand.Execute(null);

            vm.HasImportedLayers.Should().BeFalse();
            vm.ImportedLayers.Should().BeEmpty();
            vm.ShowLoadHint.Should().BeTrue("nothing left to preview");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Exporting_an_import_feeds_the_rows_to_the_renderer_as_the_layer_stack()
    {
        var (vm, dialogs, renderer, _) = Build();
        var svgPath = WriteTempSvg(KnobSvg);
        var savePath = Path.Combine(Path.GetTempPath(), $"stripkit_out_{Guid.NewGuid():N}.png");
        dialogs.OpenLayeredFileAsync().Returns(svgPath);
        dialogs.SavePngAsync(Arg.Any<string>()).Returns(savePath);
        renderer.RenderStrip(Arg.Any<FilmstripSettings>(), Arg.Any<SKBitmap?>(), Arg.Any<SKBitmap?>(),
                             Arg.Any<double>(), Arg.Any<IReadOnlyList<SKBitmap>?>()).Returns(_ => Bmp(2, 2));
        try
        {
            await vm.ImportLayeredFileCommand.ExecuteAsync(null);
            vm.ExportManifest = false;
            vm.ExportCode = false;
            vm.ExportAt2x = false;

            // The user re-tags the first layer; the override must flow through to the render.
            vm.ImportedLayers[0].Behavior = LayerBehavior.Rotate;

            await vm.ExportCommand.ExecuteAsync(null);

            renderer.Received().RenderStrip(
                Arg.Is<FilmstripSettings>(s => s.Layers.Count == 2
                                            && s.Layers[0].Behavior == LayerBehavior.Rotate
                                            && s.Layers[1].Behavior == LayerBehavior.Rotate),
                null, null, 1.0,
                Arg.Is<IReadOnlyList<SKBitmap>?>(a => a != null && a.Count == 2));
        }
        finally { File.Delete(svgPath); if (File.Exists(savePath)) File.Delete(savePath); }
    }
}
