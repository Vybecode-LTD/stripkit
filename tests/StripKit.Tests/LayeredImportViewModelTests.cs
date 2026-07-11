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

    const string ButtonSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="off"><circle cx="50" cy="50" r="40" fill="#333333"/></g>
          <g id="on"><circle cx="50" cy="50" r="40" fill="#00ff00"/></g>
        </svg>
        """;

    const string CapSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="80" height="80" viewBox="0 0 80 80">
          <g id="body"><rect x="20" y="20" width="40" height="40" rx="8" fill="#888888"/></g>
        </svg>
        """;

    const string MeterSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="48" height="160" viewBox="0 0 48 160">
          <g id="off"><rect x="8" y="0" width="32" height="160" fill="#222222"/></g>
          <g id="on"><rect x="8" y="0" width="32" height="160" fill="#00ff00"/></g>
        </svg>
        """;

    const string WideMeterSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="160" height="48" viewBox="0 0 160 48">
          <g id="off"><rect x="0" y="8" width="160" height="32" fill="#222222"/></g>
          <g id="on"><rect x="0" y="8" width="160" height="32" fill="#00ff00"/></g>
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

        var tutorial = new TutorialViewModel(new SettingsService(
            Path.Combine(Path.GetTempPath(), $"stripkit_test_settings_{Guid.NewGuid():N}.json")));
        var vm = new MainWindowViewModel(load, renderer, dialogs, export, Substitute.For<IManifestService>(),
                                         new CodeSnippetService(), new RenderRecipeService(), new LayeredImportService(), Substitute.For<IAssetService>(),
                                         TestFakes.TempSettings(),
                                         importer, batch, skin, tutorial, TestFakes.GenerateVm(),
                                         TestFakes.AssembleVm());
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
    public async Task Importing_as_a_button_honors_the_type_and_builds_state_frames()
    {
        // Regression for the Generate→Create handoff bug: a generated button must arrive as a Button
        // (off/on Frame layers), not be silently forced to RotaryKnob (which stacked both states).
        var (vm, _, _, _) = Build();
        var path = WriteTempSvg(ButtonSvg);
        try
        {
            await vm.ImportLayeredFromPathAsync(path, ComponentType.Button);

            vm.ComponentType.Should().Be(ComponentType.Button, "the handoff honors the generated type");
            vm.HasImportedLayers.Should().BeTrue();
            vm.ImportedLayers.Should().HaveCount(2);
            vm.ImportedLayers.Should().OnlyContain(r => r.Behavior == LayerBehavior.Frame, "off/on are discrete state frames");
            vm.FrameCount.Should().Be(2, "one frame per state layer");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Importing_as_a_toggle_honors_the_type_and_builds_state_frames()
    {
        // A generated toggle arrives as off/on groups and must land as a Toggle (its own type) with
        // discrete state frames, exactly like a 2-state button.
        var (vm, _, _, _) = Build();
        var path = WriteTempSvg(ButtonSvg);
        try
        {
            await vm.ImportLayeredFromPathAsync(path, ComponentType.Toggle);

            vm.ComponentType.Should().Be(ComponentType.Toggle, "the handoff honors the generated type");
            vm.IsToggle.Should().BeTrue();
            vm.IsStateFrames.Should().BeTrue("a toggle uses the state-frame path");
            vm.ImportedLayers.Should().HaveCount(2);
            vm.ImportedLayers.Should().OnlyContain(r => r.Behavior == LayerBehavior.Frame, "off/on are discrete state frames");
            vm.FrameCount.Should().Be(2, "one frame per state layer");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Importing_an_off_on_file_via_the_picker_auto_detects_a_toggle()
    {
        // From the file picker (no explicit type) an off/on SVG should be recognised as a toggle
        // rather than mis-loaded as a knob.
        var (vm, dialogs, _, _) = Build();
        var path = WriteTempSvg(ButtonSvg);
        dialogs.OpenLayeredFileAsync().Returns(path);
        try
        {
            await vm.ImportLayeredFileCommand.ExecuteAsync(null);

            vm.ComponentType.Should().Be(ComponentType.Toggle, "off/on layers auto-detect as a toggle");
            vm.ImportedLayers.Should().HaveCount(2);
            vm.HasImportedLayers.Should().BeTrue();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Importing_as_a_meter_routes_off_to_background_and_on_to_source()
    {
        // A generated meter arrives as off/on groups; the handoff must adopt them as the meter's
        // background (off, drawn full) + source (on, revealed up to the value) — the meter render path
        // consumes a source + background, NOT the layer stack.
        var (vm, _, _, _) = Build();
        var path = WriteTempSvg(MeterSvg);
        try
        {
            await vm.ImportLayeredFromPathAsync(path, ComponentType.Meter);

            vm.ComponentType.Should().Be(ComponentType.Meter, "the handoff honors the generated type");
            vm.IsMeter.Should().BeTrue();
            vm.HasSource.Should().BeTrue("the on-state is adopted as the revealed source");
            vm.HasBackground.Should().BeTrue("the off-state is adopted as the background");
            vm.ContinuousFill.Should().BeTrue("generated meter art reveals smoothly, not in steps");
            vm.FillDirection.Should().Be(MeterFillDirection.Up, "a tall meter fills bottom→top");
            vm.HasImportedLayers.Should().BeFalse("a meter is a source+background pair, not a layer stack");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Importing_a_wide_meter_infers_a_left_to_right_fill()
    {
        // The handoff reads orientation from the generated art's aspect: a landscape off/on pair
        // becomes a left→right meter without any extra flag threaded through.
        var (vm, _, _, _) = Build();
        var path = WriteTempSvg(WideMeterSvg);
        try
        {
            await vm.ImportLayeredFromPathAsync(path, ComponentType.Meter);

            vm.ComponentType.Should().Be(ComponentType.Meter);
            vm.FillDirection.Should().Be(MeterFillDirection.LeftToRight, "a wide meter fills left→right");
            vm.FrameWidth.Should().BeGreaterThan(vm.FrameHeight, "the frame matches the landscape canvas");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(ComponentType.VerticalFader)]
    [InlineData(ComponentType.HorizontalSlider)]
    public async Task Importing_as_a_linear_cap_flattens_to_a_single_source(ComponentType type)
    {
        // Regression: a generated fader/slider cap must load as the single source (the linear renderer
        // translates a source) rather than as a knob/layer stack that would rotate or render blank.
        var (vm, _, _, _) = Build();
        var path = WriteTempSvg(CapSvg);
        try
        {
            await vm.ImportLayeredFromPathAsync(path, type);

            vm.ComponentType.Should().Be(type, "the handoff honors the generated type");
            vm.IsLinear.Should().BeTrue();
            vm.HasSource.Should().BeTrue("the cap is adopted as the single source");
            vm.HasImportedLayers.Should().BeFalse("a linear cap is not a layer stack");
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

    [Theory]
    [InlineData(ComponentType.RotaryKnob, true)]
    [InlineData(ComponentType.Button, true)]
    [InlineData(ComponentType.Toggle, true)]
    [InlineData(ComponentType.VerticalFader, false)]
    [InlineData(ComponentType.HorizontalSlider, false)]
    [InlineData(ComponentType.Meter, false)]
    public void IsLayerImportRelevant_matches_the_types_that_actually_support_layered_import(
        ComponentType type, bool expected)
    {
        // Locks in the Create tab's single shared "Import layered file" section (previously
        // duplicated once for Rotary and once for Button/Toggle — audit finding, consolidated into
        // one IsLayerImportRelevant-gated block). Linear/Meter have their own separate art-adoption
        // paths (a flattened cap, an on/off pair) and must NOT show this section.
        var (vm, _, _, _) = Build();
        vm.ComponentType = type;
        vm.IsLayerImportRelevant.Should().Be(expected);
    }
}
