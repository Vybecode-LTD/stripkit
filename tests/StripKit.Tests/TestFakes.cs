using NSubstitute;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;

namespace StripKit.Tests;

/// <summary>Shared fakes for view-model construction. Keeps the per-test Build() helpers small and
/// insulates them from constructor churn (e.g. a new child view model on the main window).</summary>
static class TestFakes
{
    public static SettingsService TempSettings() =>
        new(Path.Combine(Path.GetTempPath(), $"stripkit_settings_{Guid.NewGuid():N}.json"));

    public static DpapiSecretStore TempSecrets() =>
        new(Path.Combine(Path.GetTempPath(), $"stripkit_secrets_{Guid.NewGuid():N}.dat"));

    /// <summary>A <see cref="GenerateViewModel"/> wired from substitutes — enough for the main-window
    /// VM tests that just need an instance. The generation service reports all three providers so the
    /// constructor's restore-last-provider logic is satisfied.</summary>
    public static GenerateViewModel GenerateVm()
    {
        var gen = Substitute.For<IAssetGenerationService>();
        gen.AvailableProviders.Returns([AiProvider.Claude, AiProvider.OpenAI, AiProvider.Gemini]);
        gen.DefaultModelFor(Arg.Any<AiProvider>()).Returns("test-model");
        gen.ModelsFor(Arg.Any<AiProvider>()).Returns(["test-model", "test-alt"]);
        return new GenerateViewModel(gen, TempSecrets(), TempSettings(),
                                     new LayeredImportService(), Substitute.For<IFileDialogService>());
    }

    /// <summary>A <see cref="FrameSequenceViewModel"/> (Assemble tab) wired from substitutes — enough
    /// for the main-window VM tests that just need an instance.</summary>
    public static FrameSequenceViewModel AssembleVm() =>
        new(Substitute.For<IImageLoadService>(), Substitute.For<IFrameSequenceAssembler>(),
            Substitute.For<IFileDialogService>(), Substitute.For<IExportService>(),
            Substitute.For<IManifestService>(), Substitute.For<ICodeSnippetService>(),
            new RenderRecipeService());

    /// <summary>A fully-wired <see cref="MainWindowViewModel"/> from substitutes, taking the
    /// caller's <see cref="ISettingsService"/> so preset save/load can be exercised against a
    /// known settings instance (or a fresh temp one via <see cref="TempSettings"/>).</summary>
    public static MainWindowViewModel MainVm(ISettingsService settings)
    {
        var importer = new ImporterViewModel(Substitute.For<IImageLoadService>(), Substitute.For<IFilmstripImporter>(),
                                             Substitute.For<IFileDialogService>(), Substitute.For<IExportService>());
        var batch = new BatchViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IBatchProcessor>());
        var skin = new SkinViewModel(Substitute.For<IFileDialogService>(), Substitute.For<IImageLoadService>(),
                                     Substitute.For<IFilmstripImporter>(), Substitute.For<IManifestService>());
        var tutorial = new TutorialViewModel(TempSettings());

        return new MainWindowViewModel(Substitute.For<IImageLoadService>(), Substitute.For<IFilmstripRenderer>(),
            Substitute.For<IFileDialogService>(), Substitute.For<IExportService>(), Substitute.For<IManifestService>(),
            new CodeSnippetService(), new RenderRecipeService(), new LayeredImportService(), Substitute.For<IAssetService>(),
            settings,
            importer, batch, skin, tutorial, GenerateVm(), AssembleVm());
    }
}
