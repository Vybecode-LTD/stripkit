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
/// Unit tests for the Import-tab view model's shared load path (used by both the
/// "Load filmstrip…" button and the drop handler) and command gating.
/// </summary>
public class ImporterViewModelTests
{
    static SKBitmap Bmp(int w, int h) => new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

    static ImporterViewModel Build(out IImageLoadService load, out IFilmstripImporter importer) =>
        Build(out load, out importer, out _);

    static ImporterViewModel Build(out IImageLoadService load, out IFilmstripImporter importer,
                                   out IFileDialogService dialogs)
    {
        load = Substitute.For<IImageLoadService>();
        importer = Substitute.For<IFilmstripImporter>();
        dialogs = Substitute.For<IFileDialogService>();

        // Avoid the Avalonia Bitmap conversion in the preview (no UI platform here):
        // the load STATE asserted below is set before the preview runs.
        importer
            .ExtractFrame(Arg.Any<SKBitmap>(), Arg.Any<StripDetection>(), Arg.Any<int>())
            .Throws(new InvalidOperationException("preview not exercised in unit tests"));

        return new ImporterViewModel(load, importer, dialogs, Substitute.For<IExportService>());
    }

    [Fact]
    public void LoadStripFromPath_runs_detection_and_publishes_the_layout()
    {
        var vm = Build(out var load, out var importer);
        load.Load("strip.png").Returns(Bmp(64, 6400));
        importer.Detect(Arg.Any<SKBitmap>())
            .Returns(new StripDetection(true, 100, 64, 64, ComponentType.RotaryKnob, false, new[] { 100 }));

        vm.LoadStripFromPath("strip.png");

        vm.HasStrip.Should().BeTrue();
        vm.FrameCount.Should().Be(100);
        vm.TargetFrameCount.Should().Be(100);   // resample target defaults to the detected count
        vm.StripInfo.Should().Contain("strip.png");
        vm.DetectedInfo.Should().Contain("100 frames");
        vm.ExtractCurrentFrameCommand.CanExecute(null).Should().BeTrue();
        vm.ExportRestackedCommand.CanExecute(null).Should().BeTrue();
        vm.ExportResampledCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Extract_restack_and_resample_are_disabled_until_a_strip_is_loaded()
    {
        var vm = Build(out _, out _);

        vm.ExtractCurrentFrameCommand.CanExecute(null).Should().BeFalse();
        vm.ExportRestackedCommand.CanExecute(null).Should().BeFalse();
        vm.ExportResampledCommand.CanExecute(null).Should().BeFalse();
    }

    // "Show in folder" parity with the Create/Assemble tabs — the Import tab's transport tile
    // was missing this button, which looked inconsistent switching tabs (the fix below).
    [Fact]
    public void RevealExportCommand_is_disabled_until_something_has_been_exported()
    {
        var vm = Build(out _, out _);
        vm.RevealExportCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Exporting_sets_LastExportPath_and_enables_the_reveal_command()
    {
        var vm = Build(out var load, out var importer, out var dialogs);
        load.Load("strip.png").Returns(Bmp(64, 6400));
        importer.Detect(Arg.Any<SKBitmap>())
            .Returns(new StripDetection(true, 100, 64, 64, ComponentType.RotaryKnob, false, new[] { 100 }));
        vm.LoadStripFromPath("strip.png");

        var path = Path.Combine(Path.GetTempPath(), $"stripkit_importer_test_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, Array.Empty<byte>());
        dialogs.SavePngAsync(Arg.Any<string>()).Returns(path);

        try
        {
            await vm.ExportRestackedCommand.ExecuteAsync(null);

            vm.LastExportPath.Should().Be(path);
            vm.RevealExportCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
