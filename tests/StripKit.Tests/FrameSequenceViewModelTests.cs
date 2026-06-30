using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// View-model tests for the Assemble tab: the frame list, reorder/remove, the export gate, and that
/// non-image drops are ignored. Services are mocked (NSubstitute); the assembler's probe echoes the
/// input set so the list logic can be exercised without touching disk.
/// </summary>
public class FrameSequenceViewModelTests
{
    static FrameSequenceViewModel Build()
    {
        var imageLoad = Substitute.For<IImageLoadService>();
        var assembler = Substitute.For<IFrameSequenceAssembler>();
        assembler.Probe(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IImageLoadService>())
            .Returns(ci =>
            {
                var paths = ci.Arg<IReadOnlyList<string>>().ToList();
                return new SequenceProbe(paths, 64, 64, 64, 64, true, Array.Empty<string>());
            });

        return new FrameSequenceViewModel(imageLoad, assembler,
            Substitute.For<IFileDialogService>(), Substitute.For<IExportService>(),
            Substitute.For<IManifestService>(), Substitute.For<ICodeSnippetService>(),
            new RenderRecipeService());
    }

    [Fact]
    public void Export_is_gated_until_at_least_two_frames_are_present()
    {
        var vm = Build();
        vm.ExportCommand.CanExecute(null).Should().BeFalse();

        vm.AddDroppedPaths(new[] { "a.png", "b.png" });

        vm.HasFrames.Should().BeTrue();
        vm.Frames.Should().HaveCount(2);
        vm.ExportCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void A_single_dropped_frame_is_not_enough_to_export()
    {
        var vm = Build();
        vm.AddDroppedPaths(new[] { "only.png" });

        vm.Frames.Should().HaveCount(1);
        vm.HasFrames.Should().BeFalse();
        vm.ExportCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Dropped_frames_are_numbered_in_order()
    {
        var vm = Build();
        vm.AddDroppedPaths(new[] { "a.png", "b.png", "c.png" });

        vm.Frames.Select(f => f.Position).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Removing_a_frame_renumbers_the_rest()
    {
        var vm = Build();
        vm.AddDroppedPaths(new[] { "a.png", "b.png", "c.png" });

        vm.RemoveFrameCommand.Execute(vm.Frames[1]);

        vm.Frames.Should().HaveCount(2);
        vm.Frames.Select(f => f.Position).Should().Equal(1, 2);
    }

    [Fact]
    public void Moving_a_frame_up_changes_the_order()
    {
        var vm = Build();
        vm.AddDroppedPaths(new[] { "a.png", "b.png", "c.png" });
        var third = vm.Frames[2];

        vm.MoveFrameUpCommand.Execute(third);

        vm.Frames[1].Should().BeSameAs(third);
        vm.Frames.Select(f => f.Position).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Clearing_removes_all_frames_and_disables_export()
    {
        var vm = Build();
        vm.AddDroppedPaths(new[] { "a.png", "b.png" });

        vm.ClearFramesCommand.Execute(null);

        vm.Frames.Should().BeEmpty();
        vm.HasFrames.Should().BeFalse();
        vm.ExportCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Non_image_dropped_paths_are_ignored()
    {
        var vm = Build();
        vm.AddDroppedPaths(new[] { "notes.txt", "readme.md" });
        vm.Frames.Should().BeEmpty();
    }

    [Fact]
    public void Target_presets_set_the_resample_count()
    {
        var vm = Build();
        vm.SetTarget128Command.Execute(null);
        vm.TargetFrameCount.Should().Be(128);
    }
}
