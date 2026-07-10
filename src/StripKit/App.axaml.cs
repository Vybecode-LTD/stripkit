using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using StripKit.Services;
using StripKit.ViewModels;
using StripKit.Views;
using Microsoft.Extensions.DependencyInjection;

namespace StripKit;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // CommunityToolkit.Mvvm surfaces validation via INotifyDataErrorInfo, so Avalonia's default
        // DataAnnotations validator would double-report. Remove it (the standard toolkit+Avalonia fix).
        if (BindingPlugins.DataValidators.Count > 0)
            BindingPlugins.DataValidators.RemoveAt(0);

        var services = new ServiceCollection();

        services.AddSingleton<IImageLoadService, ImageLoadService>();
        services.AddSingleton<IFilmstripRenderer, SkiaFilmstripRenderer>();
        services.AddSingleton<IFilmstripImporter, FilmstripImporter>();
        services.AddSingleton<ILayeredImportService, LayeredImportService>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IKitBuilder, KitBuilder>();
        services.AddSingleton<ICodeSnippetService, CodeSnippetService>();
        services.AddSingleton<IRenderRecipeService, RenderRecipeService>();
        services.AddSingleton<IBatchProcessor, BatchProcessor>();
        services.AddSingleton<IFrameSequenceAssembler, FrameSequenceAssembler>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAssetService, AssetService>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();

        // Generate tab (AI SVG art): one shared HttpClient + three providers behind one service.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(120) });
        services.AddSingleton<IAssetGenerationProvider, ClaudeProvider>();
        services.AddSingleton<IAssetGenerationProvider, OpenAiProvider>();
        services.AddSingleton<IAssetGenerationProvider, GeminiProvider>();
        services.AddSingleton<IAssetGenerationProvider, CustomOpenAiProvider>();
        services.AddSingleton<IAssetGenerationService, AssetGenerationService>();

        // FileDialogService needs a concrete reference so we can set its Owner
        // after the window exists; expose it through the interface too.
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());

        services.AddTransient<ImporterViewModel>();
        services.AddTransient<BatchViewModel>();
        services.AddTransient<FrameSequenceViewModel>();
        services.AddTransient<SkinViewModel>();
        services.AddTransient<GenerateViewModel>();
        services.AddTransient<TutorialViewModel>();
        services.AddTransient<MainWindowViewModel>();

        var provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = provider.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = mainVm };

            // Restore the last window size + open tab, then persist them on close (best-effort).
            var settings = provider.GetRequiredService<ISettingsService>();
            var saved = settings.Settings;
            if (saved.WindowWidth is > 200 and < 20000) window.Width = saved.WindowWidth.Value;
            if (saved.WindowHeight is > 200 and < 20000) window.Height = saved.WindowHeight.Value;
            if (saved.LastTabIndex >= 0) mainVm.SelectedTabIndex = saved.LastTabIndex;

            window.Closing += (_, _) =>
            {
                saved.WindowWidth = window.Bounds.Width;
                saved.WindowHeight = window.Bounds.Height;
                saved.LastTabIndex = mainVm.SelectedTabIndex;
                settings.Save();
            };

            // Give the dialog service a top-level to host its file pickers.
            provider.GetRequiredService<FileDialogService>().Owner = window;

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
