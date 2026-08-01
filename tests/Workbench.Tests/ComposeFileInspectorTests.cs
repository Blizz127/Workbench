using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class ComposeFileInspectorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "compose-" + Guid.NewGuid().ToString("N"));

    public ComposeFileInspectorTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Service_names_are_scraped_from_the_services_block()
    {
        var path = Path.Combine(_directory, "docker-compose.yml");
        File.WriteAllText(path, """
            name: banshee-realm
            services:
              database:
                image: mariadb:11
                environment:
                  MARIADB_ROOT_PASSWORD: dev
              connect:
                build: ./login
                depends_on:
                  - database
              search:
                build: ./search
              world:
                build: ./world
              map:
                build: ./map
            volumes:
              db-data:
            """);

        Assert.Equal(["database", "connect", "search", "world", "map"], ComposeFileInspector.GetServiceNames(path));
    }

    [Fact]
    public void File_without_a_services_block_yields_no_services()
    {
        var path = Path.Combine(_directory, "docker-compose.yml");
        File.WriteAllText(path, """
            volumes:
              db-data:
            networks:
              internal:
            """);

        Assert.Empty(ComposeFileInspector.GetServiceNames(path));
    }

    [Fact]
    public void HasComposeFile_detects_yml_and_yaml()
    {
        Assert.False(ComposeFileInspector.HasComposeFile(_directory));

        var path = Path.Combine(_directory, "docker-compose.yaml");
        File.WriteAllText(path, "services:\n  web:\n    image: nginx\n");
        Assert.True(ComposeFileInspector.HasComposeFile(_directory));
        Assert.Equal(path, ComposeFileInspector.FindComposeFile(_directory));
    }

    [Theory]
    [InlineData("docker-compose.docker.yml")]
    [InlineData("compose.yml")]
    [InlineData("compose.yaml")]
    public void Alternative_compose_filenames_are_detected(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, "services:\n  web:\n    image: nginx\n");

        Assert.True(ComposeFileInspector.HasComposeFile(_directory));
        Assert.Equal(path, ComposeFileInspector.FindComposeFile(_directory));
    }

    [Fact]
    public void Conventional_names_win_in_preference_order()
    {
        File.WriteAllText(Path.Combine(_directory, "compose.yaml"), "services: {}\n");
        File.WriteAllText(Path.Combine(_directory, "docker-compose.docker.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(_directory, "docker-compose.yml"), "services: {}\n");

        Assert.Equal(Path.Combine(_directory, "docker-compose.yml"), ComposeFileInspector.FindComposeFile(_directory));
    }
}
