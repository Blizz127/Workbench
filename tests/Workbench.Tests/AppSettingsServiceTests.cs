using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class AppSettingsServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));

    public AppSettingsServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Settings_round_trip_through_settings_json()
    {
        var path = Path.Combine(_directory, "settings.json");

        var service = new AppSettingsService(path);
        service.Settings.SharedStateFolder = "/mnt/gdrive/WorkbenchShared";
        service.Settings.MachineName = "desktop";
        service.Save();

        var reloaded = new AppSettingsService(path);
        Assert.Equal("/mnt/gdrive/WorkbenchShared", reloaded.SharedStateFolder);
        Assert.Equal("desktop", reloaded.MachineName);
    }

    [Fact]
    public void Machine_name_falls_back_to_detected_name()
    {
        var service = new AppSettingsService(Path.Combine(_directory, "settings.json"));
        Assert.Equal(Environment.MachineName, service.MachineName);
    }

    [Fact]
    public void Shared_state_folder_defaults_to_dropbox_workbench()
    {
        var service = new AppSettingsService(Path.Combine(_directory, "settings.json"));

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox", "workbench");
        Assert.Equal(expected, service.SharedStateFolder);
        Assert.Equal(expected, AppSettingsService.GetDefaultSharedStateFolder());
    }

    [Fact]
    public void Configured_shared_state_folder_overrides_the_default()
    {
        var path = Path.Combine(_directory, "settings.json");

        var service = new AppSettingsService(path);
        service.Settings.SharedStateFolder = "/mnt/other/WorkbenchShared";
        service.Save();

        Assert.Equal("/mnt/other/WorkbenchShared", new AppSettingsService(path).SharedStateFolder);
    }

    [Fact]
    public void Missing_or_corrupt_settings_yield_defaults()
    {
        var path = Path.Combine(_directory, "settings.json");
        Assert.Equal(AppSettingsService.GetDefaultSharedStateFolder(), new AppSettingsService(path).SharedStateFolder);

        File.WriteAllText(path, "{ not json ]");
        var service = new AppSettingsService(path);
        Assert.Equal(AppSettingsService.GetDefaultSharedStateFolder(), service.SharedStateFolder);
        Assert.Equal(Environment.MachineName, service.MachineName);
    }

    [Fact]
    public void Ui_state_fields_round_trip_through_settings_json()
    {
        var path = Path.Combine(_directory, "settings.json");

        var service = new AppSettingsService(path);
        service.Settings.WindowWidth = 1440;
        service.Settings.WindowHeight = 900;
        service.Settings.WindowX = 120;
        service.Settings.WindowY = 80;
        service.Settings.LastSelectedProjectId = 42;
        service.Settings.LastPage = "project";
        service.Settings.RightPanelVisible = true;
        service.Settings.DashboardDensity = "compact";
        service.Settings.ReducedMotion = true;
        service.Save();

        var reloaded = new AppSettingsService(path).Settings;
        Assert.Equal(1440, reloaded.WindowWidth);
        Assert.Equal(900, reloaded.WindowHeight);
        Assert.Equal(120, reloaded.WindowX);
        Assert.Equal(80, reloaded.WindowY);
        Assert.Equal(42, reloaded.LastSelectedProjectId);
        Assert.Equal("project", reloaded.LastPage);
        Assert.True(reloaded.RightPanelVisible);
        Assert.Equal("compact", reloaded.DashboardDensity);
        Assert.True(reloaded.ReducedMotion);
    }

    [Fact]
    public void Legacy_settings_json_without_ui_state_fields_loads_with_defaults()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """
        {
          "sharedStateFolder": "/mnt/gdrive/WorkbenchShared",
          "machineName": "desktop"
        }
        """);

        var settings = new AppSettingsService(path).Settings;
        Assert.Equal("/mnt/gdrive/WorkbenchShared", settings.SharedStateFolder);
        Assert.Equal("desktop", settings.MachineName);
        Assert.Equal(1280, settings.WindowWidth);
        Assert.Equal(800, settings.WindowHeight);
        Assert.Null(settings.WindowX);
        Assert.Null(settings.WindowY);
        Assert.Null(settings.LastSelectedProjectId);
        Assert.Equal("dashboard", settings.LastPage);
        Assert.False(settings.RightPanelVisible);
        Assert.Equal("comfortable", settings.DashboardDensity);
        Assert.Equal("dark", settings.Theme);
        Assert.False(settings.ReducedMotion);
    }

    [Fact]
    public void Theme_round_trips_and_legacy_files_default_to_dark()
    {
        var path = Path.Combine(_directory, "settings.json");

        var service = new AppSettingsService(path);
        Assert.Equal("dark", service.Settings.Theme);
        service.Settings.Theme = "light";
        service.Save();

        Assert.Equal("light", new AppSettingsService(path).Settings.Theme);

        File.WriteAllText(path, """
        {
          "sharedStateFolder": "/mnt/gdrive/WorkbenchShared",
          "machineName": "desktop"
        }
        """);
        Assert.Equal("dark", new AppSettingsService(path).Settings.Theme);
    }
}
