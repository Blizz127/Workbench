using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workbench.Core.Services;

/// <summary>Application-level settings, stored as <c>settings.json</c> next to the database.</summary>
public sealed class AppSettings
{
    /// <summary>The user-selected Shared State Folder (a synced or plain directory), if configured.</summary>
    public string? SharedStateFolder { get; set; }

    /// <summary>Overrides the detected machine name used for machine profiles.</summary>
    public string? MachineName { get; set; }
}

public interface IAppSettingsService
{
    AppSettings Settings { get; }

    string SettingsPath { get; }

    /// <summary>Configured machine name, falling back to the detected one.</summary>
    string MachineName { get; }

    /// <summary>The shared folder if configured and existing, else null.</summary>
    string? SharedStateFolder { get; }

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
        string.IsNullOrWhiteSpace(Settings.SharedStateFolder) ? null : Settings.SharedStateFolder.Trim();

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
