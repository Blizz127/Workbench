using Avalonia;

namespace Workbench.App;

internal static class Program
{
    // Avalonia configuration lives in BuildAvaloniaApp; the previewer also uses it.
    [STAThread]
    public static void Main(string[] args)
    {
        CrashDiagnostics.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
