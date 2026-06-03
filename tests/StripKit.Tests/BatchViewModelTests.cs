using FluentAssertions;
using NSubstitute;
using SkiaSharp;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>Command-gating and folder-selection behaviour for the Batch tab view model.</summary>
public class BatchViewModelTests : IDisposable
{
    readonly string _base;
    readonly string _inDir;
    readonly string _outDir;

    public BatchViewModelTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "stripkit-batchvm", Guid.NewGuid().ToString("N"));
        _inDir = Path.Combine(_base, "in");
        _outDir = Path.Combine(_base, "out");
        Directory.CreateDirectory(_inDir);
        Directory.CreateDirectory(_outDir);

        var path = Path.Combine(_inDir, "knob.png");
        using var bmp = TestImages.Knob(64);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Run_and_cancel_are_disabled_initially()
    {
        var vm = new BatchViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IBatchProcessor>());

        vm.RunCommand.CanExecute(null).Should().BeFalse();
        vm.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Choosing_input_and_output_folders_enables_run()
    {
        var dialogs = Substitute.For<IFileDialogService>();
        dialogs.OpenFolderAsync(Arg.Any<string>())
            .Returns(Task.FromResult<string?>(_inDir), Task.FromResult<string?>(_outDir));
        var vm = new BatchViewModel(dialogs, Substitute.For<IBatchProcessor>());

        await vm.ChooseInputFolderCommand.ExecuteAsync(null);
        await vm.ChooseOutputFolderCommand.ExecuteAsync(null);

        vm.HasInputFiles.Should().BeTrue();
        vm.InputSummary.Should().Contain("1 image");
        vm.HasOutput.Should().BeTrue();
        vm.RunCommand.CanExecute(null).Should().BeTrue();
    }
}
