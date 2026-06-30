using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using NSubstitute;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using StripKit.Views;

namespace StripKit.Tests;

/// <summary>
/// Runtime smoke test for the Assemble tab's markup: loading the view with a populated frame list
/// exercises its compiled bindings, the design-token resources, and the reorder-list DataTemplate
/// (which uses classic bindings to reach the parent VM's commands) — catching XAML load errors a
/// headless build can't.
/// </summary>
public class AssembleViewTests
{
    static FrameSequenceViewModel VmWithFrames()
    {
        var assembler = Substitute.For<IFrameSequenceAssembler>();
        assembler.Probe(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IImageLoadService>())
            .Returns(ci => new SequenceProbe(ci.Arg<IReadOnlyList<string>>().ToList(),
                                             64, 64, 64, 64, true, Array.Empty<string>()));

        var vm = new FrameSequenceViewModel(Substitute.For<IImageLoadService>(), assembler,
            Substitute.For<IFileDialogService>(), Substitute.For<IExportService>(),
            Substitute.For<IManifestService>(), Substitute.For<ICodeSnippetService>());
        vm.AddDroppedPaths(new[] { "a.png", "b.png", "c.png" });
        return vm;
    }

    [AvaloniaFact]
    public void Assemble_view_loads_and_realizes_with_a_populated_frame_list()
    {
        var window = new Window { Content = new AssembleView { DataContext = VmWithFrames() } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Content.Should().BeOfType<AssembleView>("the tab content loaded without a XAML error");
    }
}
