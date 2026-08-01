namespace Workbench.App;

/// <summary>
/// Last-resort crash logging. At process shutdown the runtime can be too torn
/// down to even format an exception (the observed "Unhandled exception." line
/// with no stack), so the full details are captured to a file the moment the
/// handlers fire. Diagnostics only — nothing is suppressed or swallowed here.
/// </summary>
internal static class CrashDiagnostics
{
    private static string LogPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("WORKBENCH_DATA_DIR");
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
                    "Workbench");
            }

            return Path.Combine(dir, "diagnostics.log");
        }
    }

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain.UnhandledException", e.ExceptionObject, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Log("TaskScheduler.UnobservedTaskException", e.Exception, terminating: false);
    }

    private static void Log(string kind, object? exception, bool terminating)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTimeOffset.Now:O}] {kind} (terminating={terminating}):{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere left to report.
        }
    }
}
