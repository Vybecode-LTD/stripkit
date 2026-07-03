using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using NSubstitute;
using StripKit.Services;
using StripKit.ViewModels;
using StripKit.Views;

namespace StripKit.Tests;

/// <summary>
/// The Create / Import / Assemble tabs each end in a "tile" transport bar (transport buttons +
/// timeline + frame readout, a dimensions line, a status/action row) that must render at an
/// identical height, or switching tabs visibly shifts the preview area (a reported regression).
/// Measures the real Avalonia-headless layout of each tab's <c>TransportTile</c> Border rather than
/// inspecting a screenshot, so it's exact and doesn't depend on anything being on screen.
/// </summary>
public class TransportTileAlignmentTests
{
    // The app's own default window size, so the shared "370,*" left/right split (and the
    // StatusMessage TextBlock's TextWrapping) lays out identically to the real app.
    private const double WindowWidth = 1060;
    private const double WindowHeight = 740;

    private static MainWindowViewModel BuildMainVm()
    {
        var importer = new ImporterViewModel(Substitute.For<IImageLoadService>(), Substitute.For<IFilmstripImporter>(),
            Substitute.For<IFileDialogService>(), Substitute.For<IExportService>());
        var batch = new BatchViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IBatchProcessor>());
        var skin = new SkinViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IImageLoadService>(),
            Substitute.For<IFilmstripImporter>(), Substitute.For<IManifestService>());
        var tutorial = new TutorialViewModel(TestFakes.TempSettings());

        return new MainWindowViewModel(Substitute.For<IImageLoadService>(), Substitute.For<IFilmstripRenderer>(),
            Substitute.For<IFileDialogService>(), Substitute.For<IExportService>(), Substitute.For<IManifestService>(),
            new CodeSnippetService(), new RenderRecipeService(), new LayeredImportService(), Substitute.For<IAssetService>(),
            TestFakes.TempSettings(),
            importer, batch, skin, tutorial, TestFakes.GenerateVm(), TestFakes.AssembleVm());
    }

    private static double MeasureTileHeight(Control root)
    {
        var window = new Window { Width = WindowWidth, Height = WindowHeight, Content = root };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tile = root.FindControl<Border>("TransportTile")
                   ?? throw new InvalidOperationException("TransportTile not found — check the view's x:Name.");
        return tile.Bounds.Height;
    }

    [AvaloniaFact]
    public void The_three_tabs_transport_tiles_render_at_the_same_height()
    {
        var mainVm = BuildMainVm();
        var mainWindow = new MainWindow { Width = WindowWidth, Height = WindowHeight, DataContext = mainVm };
        mainWindow.Show();
        Dispatcher.UIThread.RunJobs();
        double createHeight = mainWindow.FindControl<Border>("TransportTile")!.Bounds.Height;

        double importHeight = MeasureTileHeight(new ImporterView { DataContext = mainVm.Importer });
        double assembleHeight = MeasureTileHeight(new AssembleView { DataContext = mainVm.Assemble });

        createHeight.Should().BeGreaterThan(0, "the Create tile must actually have laid out");
        importHeight.Should().Be(createHeight, "the Import transport tile must match Create's height exactly");
        assembleHeight.Should().Be(createHeight, "the Assemble transport tile must match Create's height exactly");
    }
}
