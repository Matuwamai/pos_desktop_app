using Avalonia;
using System;
using Pos.Infra.Diagnostics;
using Velopack;

namespace Pos.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be the very first call in Main, ahead of everything else including
        // CrashLogger — Velopack's own documented requirement, so it can intercept its
        // install/update-relaunch command-line args before anything else starts.
        VelopackApp.Build().Run();
        CrashLogger.Initialize();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
