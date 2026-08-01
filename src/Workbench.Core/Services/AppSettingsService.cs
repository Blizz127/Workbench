using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workbench.Core.Services;

/// <summary>Application-level settings, stored as <c>settings.json</c> next to the database.</summary>
public sealed class AppSettings
{
    /// <summary>The user-selected Shared State Folder (a synced or plain directory), if configured.</summary>
    public string? SharedStateFolder { get; set; }

    /// <summary>When true, the discovery folder is scanned for new projects at startup.</summary>
    public bool DiscoveryEnabled { get; set; } = true;

    /// <summary>The folder scanned for projects (null resolves to <c>~/dev</c>).</summary>
    public string? DiscoveryFolder { get; set; }

    /// <summary>Overrides the detected machine name used for machine profiles.</summary>
    public string? MachineName { get; set; }

    /// <summary>Last window size, restored on startup.</summary>
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;

    /// <summary>Last window position; null means "center on screen" on startup.</summary>
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    /// <summary>Project selected when the app was last closed, if any.</summary>
    public long? LastSelectedProjectId { get; set; }

    /// <summary>Page shown when the app was last closed ("dashboard", "project", …).</summary>
    public string LastPage { get; set; } = "dashboard";

    /// <summary>Whether the right-hand details panel is visible.</summary>
    public bool RightPanelVisible { get; set; }

    /// <summary>Dashboard row density ("comfortable" or "compact").</summary>
    public string DashboardDensity { get; set; } = "comfortable";

    /// <summary>UI theme: "dark" (default) or "light".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>When true, page/section fade transitions are skipped (reduced motion).</summary>
    public bool ReducedMotion { get; set; }
}

public interface IAppSettingsService
{
    AppSettings Settings { get; }

    string SettingsPath { get; }

    /// <summary>Configured machine name, falling back to the detected one.</summary>
    string MachineName { get; }

    /// <summary>The shared folder: the configured path, else <c>~/Dropbox/workbench</c>.</summary>
    string? SharedStateFolder { get; }

    /// <summary>The discovery folder: the configured path, else <c>~/dev</c>.</summary>
    string DiscoveryFolder { get; }

    void Save();
}

/// <summary>
/// Loads and persists <see cref="AppSettings"/>. Missing or corrupt files
/// yield defaults — settings must never block startup.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AppSettings Settings { get; }

    public string SettingsPath { get; }

    public AppSettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(GetDefaultDataDirectory(), FileName);
        Settings = Load(SettingsPath);
    }

    /// <summary>Same resolution as the database: WORKBENCH_DATA_DIR, else the per-user application data folder.</summary>
    public static string GetDefaultDataDirectory()
    {
        var dir = Environment.GetEnvironmentVariable("WORKBENCH_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
                "Workbench");
        }

        return dir;
    }

    public string MachineName =>
        string.IsNullOrWhiteSpace(Settings.MachineName) ? Environment.MachineName : Settings.MachineName.Trim();

    public string? SharedStateFolder =>
        string.IsNullOrWhiteSpace(Settings.SharedStateFolder)
            ? GetDefaultSharedStateFolder()
            : Settings.SharedStateFolder.Trim();

    /// <summary>The Dropbox-synced shared folder used when none is configured.</summary>
    public static string GetDefaultSharedStateFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox", "workbench");

    public string DiscoveryFolder =>
        ExpandHome(string.IsNullOrWhiteSpace(Settings.DiscoveryFolder)
            ? GetDefaultDiscoveryFolder()
            : Settings.DiscoveryFolder.Trim());

    /// <summary>The folder scanned for projects when none is configured.</summary>
    public static string GetDefaultDiscoveryFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev");

    /// <summary>Expands a leading <c>~</c> to the user's home directory; other paths pass through.</summary>
    private static string ExpandHome(string path) =>
        path == "~"
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
                : path;

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, JsonOptions));
    }

    private static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }
}
