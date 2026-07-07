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
    public void Missing_or_corrupt_settings_yield_defaults()
    {
        var path = Path.Combine(_directory, "settings.json");
        Assert.Null(new AppSettingsService(path).SharedStateFolder);

        File.WriteAllText(path, "{ not json ]");
        var service = new AppSettingsService(path);
        Assert.Null(service.SharedStateFolder);
        Assert.Equal(Environment.MachineName, service.MachineName);
    }
}
