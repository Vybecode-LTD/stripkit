using FluentAssertions;
using NSubstitute;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The Skin tab's multi-control manifest builder: adding controls (auto-detected from a strip
/// or blank), export gating, and that an export passes every control plus the skin-level
/// metadata to the manifest service.
/// </summary>
public class SkinViewModelTests
{
    static SKBitmap Bmp(int w, int h) => new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

    static (SkinViewModel vm, IFileDialogService dialogs, IImageLoadService load,
            IFilmstripImporter importer, IManifestService manifest) Build()
    {
        var dialogs = Substitute.For<IFileDialogService>();
        var load = Substitute.For<IImageLoadService>();
        var importer = Substitute.For<IFilmstripImporter>();
        var manifest = Substitute.For<IManifestService>();
        return (new SkinViewModel(dialogs, load, importer, manifest), dialogs, load, importer, manifest);
    }

    [Fact]
    public void Export_is_disabled_until_a_control_is_added()
    {
        var (vm, _, _, _, _) = Build();
        vm.ExportSkinCommand.CanExecute(null).Should().BeFalse();

        vm.AddBlankCommand.Execute(null);

        vm.HasControls.Should().BeTrue();
        vm.ExportSkinCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Add_from_strip_detects_the_layout_and_creates_a_control()
    {
        var (vm, dialogs, load, importer, _) = Build();
        dialogs.OpenImageAsync().Returns(Task.FromResult<string?>("cutoff_64.png"));
        load.Load("cutoff_64.png").Returns(Bmp(80, 80 * 64));
        importer.Detect(Arg.Any<SKBitmap>())
            .Returns(new StripDetection(true, 64, 80, 80, ComponentType.RotaryKnob, false, System.Array.Empty<int>()));

        await vm.AddFromStripCommand.ExecuteAsync(null);

        vm.Controls.Should().HaveCount(1);
        var c = vm.Controls[0];
        c.Id.Should().Be("cutoff_64");
        c.ParameterId.Should().Be("cutoff_64");
        c.Type.Should().Be(ComponentType.RotaryKnob);
        c.Asset.Should().Be("cutoff_64.png");
        c.Frames.Should().Be(64);
        c.FrameWidth.Should().Be(80);
        c.FrameHeight.Should().Be(80);
        vm.SelectedControl.Should().Be(c);
        vm.BaseWidth.Should().Be(80);   // the first control seeds the skin's design resolution
    }

    [Fact]
    public void Remove_selected_drops_the_control_and_re_gates_export()
    {
        var (vm, _, _, _, _) = Build();
        vm.AddBlankCommand.Execute(null);
        vm.ExportSkinCommand.CanExecute(null).Should().BeTrue();

        vm.RemoveSelectedCommand.Execute(null);

        vm.Controls.Should().BeEmpty();
        vm.SelectedControl.Should().BeNull();
        vm.ExportSkinCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Export_builds_a_manifest_with_every_control_and_the_globals()
    {
        var (vm, dialogs, _, _, manifest) = Build();
        dialogs.OpenFolderAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>("/out"));

        IReadOnlyList<ManifestControl>? captured = null;
        manifest.BuildManifest(Arg.Any<IReadOnlyList<ManifestControl>>(), Arg.Any<string>(), Arg.Any<string?>(),
                               Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(ci =>
            {
                captured = ci.Arg<IReadOnlyList<ManifestControl>>();
                return new SkinManifest { Name = "x", BaseWidth = 1, BaseHeight = 1, Controls = captured! };
            });

        vm.SkinName = "Synth";
        vm.AddBlankCommand.Execute(null);
        vm.Controls[0].Id = "cutoff";
        vm.Controls[0].ParameterId = "filterCutoff";
        vm.Controls[0].Type = ComponentType.RotaryKnob;
        vm.Controls[0].Asset = "cutoff_64.png";
        vm.AddBlankCommand.Execute(null);
        vm.Controls[1].Id = "gain";

        await vm.ExportSkinCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        var controls = captured!;
        controls.Should().HaveCount(2);
        controls[0].Id.Should().Be("cutoff");
        controls[0].Type.Should().Be("knob");                 // ComponentType → schema string
        controls[0].ParameterId.Should().Be("filterCutoff");
        controls[1].Id.Should().Be("gain");
        await manifest.Received(1).SaveAsync(
            Arg.Any<SkinManifest>(),
            Arg.Is<string>(p => p.Contains("Synth") && p.EndsWith(".skin.json")));
    }
}
