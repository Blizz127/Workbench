using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class ProjectDiscoveryServiceTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "discovery-" + Guid.NewGuid().ToString("N"));

    private readonly ProjectRepository _projects;
    private readonly TimelineRepository _timeline;
    private readonly AppSettingsService _appSettings;
    private readonly ProjectDiscoveryService _service;

    public ProjectDiscoveryServiceTests()
    {
        Directory.CreateDirectory(_root);
        _projects = new ProjectRepository(_fixture.Database);
        _timeline = new TimelineRepository(_fixture.Database);
        _appSettings = new AppSettingsService(Path.Combine(_root, "settings.json"));
        _appSettings.Settings.DiscoveryFolder = _root;
        _appSettings.Settings.DiscoveryEnabled = true;
        _service = new ProjectDiscoveryService(_projects, new ProjectConfigService(), _timeline, _appSettings);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Repo_with_workbench_json_is_adopted()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(Path.Combine(repo.FullName, "workbench.json"), """
            {
              "name": "Alpha Project",
              "buildCommand": "make -j8",
              "testCommand": "make check",
              "tags": ["Preservation"]
            }
            """);

        Assert.Equal(1, _service.Scan());

        var project = Assert.Single(_projects.GetAll());
        Assert.Equal("Alpha Project", project.Name);
        Assert.Equal("make -j8", project.BuildCommand);
        Assert.Equal("make check", project.TestCommand);
        Assert.Contains("Preservation", project.Tags);
        Assert.Equal(ProjectStatus.Active, project.Status);

        var entries = _timeline.GetForProject(project.Id);
        Assert.Contains(entries, e => e.Category == TimelineCategory.Status && e.Message.Contains("discovered"));
    }

    [Fact]
    public void Nested_project_is_found_at_depth_2_but_its_container_is_not()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "ffxi", "Banshee-Realm-11"));
        Directory.CreateDirectory(Path.Combine(nested.FullName, ".git"));
        File.WriteAllText(Path.Combine(nested.FullName, "docker-compose.yml"), """
            services:
              database:
                image: mariadb
              world:
                image: banshee
            """);

        Assert.Equal(1, _service.Scan());

        var project = Assert.Single(_projects.GetAll());
        Assert.Equal("Banshee-Realm-11", project.Name);
        Assert.Equal("docker compose up -d", project.RunCommand);
        Assert.Equal("docker compose down", project.Launch.StopCommand);
        Assert.Equal("docker compose logs --no-color --tail=120", project.Launch.LogsCommand);
        Assert.NotNull(project.Launch.StatusCommand);
        Assert.Equal(["database", "world"], project.Launch.RequiredServices);
    }

    [Fact]
    public void Bare_git_directory_is_registered_with_synthesized_name_and_dotnet_defaults()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_root, "bare-repo"));
        Directory.CreateDirectory(Path.Combine(repo.FullName, ".git"));
        File.WriteAllText(Path.Combine(repo.FullName, "Bare.csproj"), "<Project />");

        Assert.Equal(1, _service.Scan());

        var project = Assert.Single(_projects.GetAll());
        Assert.Equal("bare-repo", project.Name);
        Assert.Equal("dotnet build", project.BuildCommand);
        Assert.Equal("dotnet test", project.TestCommand);
        Assert.Null(project.Launch.StatusCommand);
    }

    [Fact]
    public void Empty_and_hidden_directories_are_skipped()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        Directory.CreateDirectory(Path.Combine(_root, ".hidden", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "pkg", "node_modules", ".git"));

        Assert.Equal(0, _service.Scan());
        Assert.Empty(_projects.GetAll());
    }

    [Fact]
    public void Second_scan_registers_nothing()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(repo.FullName, ".git"));

        Assert.Equal(1, _service.Scan());
        Assert.Equal(0, _service.Scan());
        Assert.Single(_projects.GetAll());
    }

    [Fact]
    public void Disabled_discovery_is_a_no_op()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(repo.FullName, ".git"));
        _appSettings.Settings.DiscoveryEnabled = false;

        Assert.Equal(0, _service.Scan());
        Assert.Empty(_projects.GetAll());
    }

    [Fact]
    public void Missing_discovery_folder_is_a_no_op()
    {
        _appSettings.Settings.DiscoveryFolder = Path.Combine(_root, "does-not-exist");

        Assert.Equal(0, _service.Scan());
        Assert.Empty(_projects.GetAll());
    }
}
