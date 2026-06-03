using Avalonia;
using Avalonia.Headless;
using StripKit;
using StripKit.Tests;

// Registers the headless Avalonia application used by [AvaloniaFact] tests.
// Default options use headless (non-Skia) drawing — enough to build the visual
// tree and read attached properties like DragDrop.AllowDrop without rendering.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace StripKit.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
