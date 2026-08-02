using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class ProjectConfigServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));

    public ProjectConfigServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Config_roundtrips_through_workbench_json()
    {
        var service = new ProjectConfigService();
        var project = new Project
        {
            Name = "TEN Revival",
            RepositoryPath = _directory,
            BuildCommand = "cargo build",
            TestCommand = "cargo test",
            DocumentationPaths = ["docs/protocol.md"],
            AiContextPaths = ["src/net/"],
            Tags = ["Networking", "Game Preservation"],
        };

        service.Save(project);
        Assert.True(File.Exists(Path.Combine(_directory, "workbench.json")));

        var loaded = service.Load(_directory);
        Assert.NotNull(loaded);
        Assert.Equal("TEN Revival", loaded.Name);
        Assert.Equal("cargo build", loaded.BuildCommand);
        Assert.Equal(["docs/protocol.md"], loaded.DocumentationPaths);
        Assert.Equal(2, loaded.Tags.Count);

        var target = new Project { Name = "placeholder" };
        service.ApplyTo(loaded, target);
        Assert.Equal("TEN Revival", target.Name);
        Assert.Equal("cargo test", target.TestCommand);
        Assert.Contains("Networking", target.Tags);
    }

    [Fact]
    public void Compose_file_selection_roundtrips_through_workbench_json()
    {
        var service = new ProjectConfigService();
        var project = new Project
        {
            Name = "Banshee",
            RepositoryPath = _directory,
            ComposeFile = "docker-compose.docker.yml",
        };

        service.Save(project);

        var loaded = service.Load(_directory);
        Assert.NotNull(loaded);
        Assert.Equal("docker-compose.docker.yml", loaded.ComposeFile);

        var target = new Project { Name = "placeholder" };
        service.ApplyTo(loaded, target);
        Assert.Equal("docker-compose.docker.yml", target.ComposeFile);
    }

    [Fact]
    public void Tmux_session_selection_roundtrips_through_workbench_json()
    {
        var service = new ProjectConfigService();
        var project = new Project
        {
            Name = "Banshee",
            RepositoryPath = _directory,
            TmuxSession = "banshee-dev",
        };

        service.Save(project);

        var loaded = service.Load(_directory);
        Assert.NotNull(loaded);
        Assert.Equal("banshee-dev", loaded.TmuxSession);

        var target = new Project { Name = "placeholder" };
        service.ApplyTo(loaded, target);
        Assert.Equal("banshee-dev", target.TmuxSession);
    }

    [Fact]
    public void Missing_or_corrupt_config_returns_null()
    {
        var service = new ProjectConfigService();
        Assert.Null(service.Load(_directory));

        File.WriteAllText(Path.Combine(_directory, "workbench.json"), "{ not json ]");
        Assert.Null(service.Load(_directory));
    }

    [Fact]
    public void Launch_profile_roundtrips_through_workbench_json()
    {
        var service = new ProjectConfigService();
        var project = new Project
        {
            Name = "Banshee",
            RepositoryPath = _directory,
            RunCommand = "podman compose up -d",
            Launch = new LaunchProfile
            {
                StartupGraceSeconds = 20,
                StatusCommand = "podman ps --format '{{.Names}} {{.Status}}'",
                RequiredServices = ["database", "map"],
                OptionalServices = ["metrics"],
                FailedPatterns = ["Segmentation fault"],
                ServiceLogCommand = "podman logs --tail=200 banshee_{service}_1",
                HealthCheckRetries = 4,
                HealthCheckIntervalSeconds = 10,
                StopCommand = "podman compose down",
                LogsCommand = "podman compose logs --tail=100",
                UpdateCommand = "git pull --ff-only && podman compose up -d",
                MetricsCommand = "echo 'Players: 0'",
            },
        };

        service.Save(project);
        var json = File.ReadAllText(Path.Combine(_directory, "workbench.json"));
        Assert.Contains("\"$schema\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"launchProfile\"", json);

        var loaded = service.Load(_directory);
        Assert.NotNull(loaded?.LaunchProfile);
        Assert.Equal(20, loaded.LaunchProfile.StartupGraceSeconds);
        Assert.Equal(["database", "map"], loaded.LaunchProfile.RequiredServices);

        var target = new Project();
        service.ApplyTo(loaded, target);
        Assert.Equal("podman compose up -d", target.RunCommand);
        Assert.Equal(["database", "map"], target.Launch.RequiredServices);
        Assert.Equal(["metrics"], target.Launch.OptionalServices);
        Assert.Equal(["Segmentation fault"], target.Launch.FailedPatterns);
        Assert.Equal("podman logs --tail=200 banshee_{service}_1", target.Launch.ServiceLogCommand);
        Assert.Equal(4, target.Launch.HealthCheckRetries);
        Assert.Equal(10, target.Launch.HealthCheckIntervalSeconds);
        Assert.Equal("podman compose down", target.Launch.StopCommand);
        Assert.Equal("podman compose logs --tail=100", target.Launch.LogsCommand);
        Assert.Equal("git pull --ff-only && podman compose up -d", target.Launch.UpdateCommand);
        Assert.Equal("echo 'Players: 0'", target.Launch.MetricsCommand);
    }

    [Fact]
    public void Project_without_a_status_command_omits_the_launch_profile()
    {
        var service = new ProjectConfigService();
        var project = new Project { Name = "plain", RepositoryPath = _directory };

        service.Save(project);
        var json = File.ReadAllText(Path.Combine(_directory, "workbench.json"));

        Assert.DoesNotContain("launchProfile", json);
    }

    [Fact]
    public void Save_preserves_the_existing_mobile_actions_allowlist()
    {
        var service = new ProjectConfigService();
        File.WriteAllText(Path.Combine(_directory, "workbench.json"), """
            {
              "name": "Banshee",
              "mobileActions": [
                { "id": "logs-all", "label": "View all logs", "type": "dockerLogsAll" },
                { "id": "restart-map", "label": "Restart map", "type": "dockerRestartService", "service": "map" }
              ]
            }
            """);

        // A desktop settings save rewrites the file from the Project model —
        // which knows nothing about the mobile allowlist.
        service.Save(new Project
        {
            Name = "Banshee",
            RepositoryPath = _directory,
            BuildCommand = "make",
        });

        var loaded = service.Load(_directory);
        Assert.NotNull(loaded);
        Assert.Equal("make", loaded.BuildCommand);
        Assert.Equal(2, loaded.MobileActions.Count);
        Assert.Equal("logs-all", loaded.MobileActions[0].Id);
        Assert.Equal("map", loaded.MobileActions[1].Service);

        var json = File.ReadAllText(Path.Combine(_directory, "workbench.json"));
        Assert.Contains("\"mobileActions\"", json);
    }
}
