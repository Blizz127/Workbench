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
    public void Missing_or_corrupt_config_returns_null()
    {
        var service = new ProjectConfigService();
        Assert.Null(service.Load(_directory));

        File.WriteAllText(Path.Combine(_directory, "workbench.json"), "{ not json ]");
        Assert.Null(service.Load(_directory));
    }
}
