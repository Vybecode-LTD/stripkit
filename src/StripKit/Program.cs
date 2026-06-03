using Avalonia;
using Velopack;

namespace StripKit;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't
    // initialized yet and stuff will break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run first: it handles the install / update / uninstall
        // lifecycle hooks (and may briefly take over the process on those events)
        // before the GUI starts. A no-op during a normal run.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
