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

    static ImporterViewModel Build(out IImageLoadService load, out IFilmstripImporter importer)
    {
        load = Substitute.For<IImageLoadService>();
        importer = Substitute.For<IFilmstripImporter>();

        // Avoid the Avalonia Bitmap conversion in the preview (no UI platform here):
        // the load STATE asserted below is set before the preview runs.
        importer
            .ExtractFrame(Arg.Any<SKBitmap>(), Arg.Any<StripDetection>(), Arg.Any<int>())
            .Throws(new InvalidOperationException("preview not exercised in unit tests"));

        return new ImporterViewModel(load, importer, Substitute.For<IFileDialogService>(), Substitute.For<IExportService>());
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
}
