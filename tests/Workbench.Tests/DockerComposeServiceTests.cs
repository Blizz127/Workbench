using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class DockerComposeServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "compose-svc-" + Guid.NewGuid().ToString("N"));

    public DockerComposeServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Returns queued results; never touches real processes or Docker.</summary>
    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new();

        public List<string> Commands { get; } = [];

        public void Enqueue(string output, int exitCode = 0) =>
            _results.Enqueue(new ProcessResult("stub", DateTimeOffset.Now, exitCode, TimeSpan.FromMilliseconds(5), output, false));

        public Task<ProcessResult> RunAsync(
            string command, string? workingDirectory, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : new ProcessResult(command, DateTimeOffset.Now, 0, TimeSpan.Zero, string.Empty, false));
        }
    }

    private Project ProjectInDirectory()
    {
        File.WriteAllText(Path.Combine(_directory, "docker-compose.yml"), "services:\n  map:\n    image: x\n");
        return new Project { Name = "compose-test", RepositoryPath = _directory };
    }

    [Fact]
    public async Task Inspect_runs_the_read_only_ps_command_and_parses_typed_status()
    {
        var project = ProjectInDirectory();
        var runner = new StubProcessRunner();
        runner.Enqueue(string.Join('\n',
            """{"Name":"p-map-1","Service":"map","State":"running","Status":"Up 1 hour","ExitCode":0,"Health":"","Project":"p"}""",
            """{"Name":"p-db-update-1","Service":"db-update","State":"exited","Status":"Exited (0) 1 hour ago","ExitCode":0,"Health":"","Project":"p"}"""));

        var service = new DockerComposeService(runner);
        var status = await service.InspectAsync(project);

        Assert.NotNull(status);
        Assert.Equal(Path.Combine(_directory, "docker-compose.yml"), status.ComposeFilePath);
        Assert.Equal("p", status.ProjectName);
        Assert.Equal(2, status.TotalCount);
        Assert.Equal(1, status.RunningCount);
        Assert.Equal(1, status.CompletedCount);
        Assert.Equal(ComposeServiceState.Completed, Assert.Single(status.Services, s => s.Name == "db-update").State);

        var command = Assert.Single(runner.Commands);
        Assert.Equal(
            $"docker compose -f \"{Path.Combine(_directory, "docker-compose.yml")}\" ps --all --format json",
            command);
    }

    [Fact]
    public async Task Inspect_failure_surfaces_a_redacted_error_not_an_exception()
    {
        var project = ProjectInDirectory();
        var runner = new StubProcessRunner();
        runner.Enqueue("Cannot connect to the Docker daemon (DB_PASSWORD=oops)", exitCode: 1);

        var status = await new DockerComposeService(runner).InspectAsync(project);

        Assert.NotNull(status);
        Assert.Empty(status.Services);
        Assert.NotNull(status.Error);
        Assert.DoesNotContain("oops", status.Error);
    }

    [Fact]
    public async Task Stop_and_restart_are_blocked_without_explicit_confirmation()
    {
        var project = ProjectInDirectory();
        var runner = new StubProcessRunner();
        var service = new DockerComposeService(runner);
        var composeFile = service.ResolveComposeFile(project)!;

        foreach (var kind in new[]
        {
            ComposeOperationKind.StopAll, ComposeOperationKind.RestartAll,
            ComposeOperationKind.StopService, ComposeOperationKind.RestartService,
        })
        {
            var operation = ComposeCommandBuilder.Build(kind, composeFile,
                kind is ComposeOperationKind.StopService or ComposeOperationKind.RestartService ? "map" : null);

            var result = await service.ExecuteAsync(project, operation, confirmed: false);

            Assert.True(result.WasBlocked);
            Assert.False(result.Succeeded);
        }

        // The confirmation gate held: not a single command reached the runner.
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Confirmed_stop_runs_exactly_the_previewed_command()
    {
        var project = ProjectInDirectory();
        var runner = new StubProcessRunner();
        runner.Enqueue("Stopped");
        var service = new DockerComposeService(runner);

        var operation = ComposeCommandBuilder.Build(
            ComposeOperationKind.StopService, service.ResolveComposeFile(project)!, "map");
        var result = await service.ExecuteAsync(project, operation, confirmed: true);

        Assert.True(result.Succeeded);
        Assert.Equal(operation.Command, Assert.Single(runner.Commands));
    }

    [Fact]
    public async Task Start_and_logs_run_without_confirmation_but_still_only_on_request()
    {
        var project = ProjectInDirectory();
        var runner = new StubProcessRunner();
        var service = new DockerComposeService(runner);
        var composeFile = service.ResolveComposeFile(project)!;

        var start = await service.ExecuteAsync(project,
            ComposeCommandBuilder.Build(ComposeOperationKind.StartAll, composeFile));
        var logs = await service.ExecuteAsync(project,
            ComposeCommandBuilder.Build(ComposeOperationKind.LogsService, composeFile, "map"));

        Assert.True(start.Succeeded);
        Assert.True(logs.Succeeded);
        Assert.Equal(2, runner.Commands.Count);
    }

    [Fact]
    public async Task Operation_output_is_secret_redacted_before_it_is_returned()
    {
        var project = ProjectInDirectory();
        var runner = new StubProcessRunner();
        runner.Enqueue("map  | connecting with MARIADB_PASSWORD=sup3rsecret ok", exitCode: 0);
        var service = new DockerComposeService(runner);

        var result = await service.ExecuteAsync(project,
            ComposeCommandBuilder.Build(ComposeOperationKind.LogsService, service.ResolveComposeFile(project)!, "map"));

        Assert.DoesNotContain("sup3rsecret", result.Output);
        Assert.Contains("MARIADB_PASSWORD=***", result.Output);
    }

    [Fact]
    public async Task Failed_operation_reports_failure_with_exit_code()
    {
        var project = ProjectInDirectory();
        var runner = new StubProcessRunner();
        runner.Enqueue("no such service", exitCode: 1);
        var service = new DockerComposeService(runner);

        var result = await service.ExecuteAsync(project,
            ComposeCommandBuilder.Build(ComposeOperationKind.StartService, service.ResolveComposeFile(project)!, "map"),
            confirmed: true);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void Resolve_prefers_the_explicit_workbench_json_selection()
    {
        File.WriteAllText(Path.Combine(_directory, "docker-compose.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(_directory, "docker-compose.docker.yml"), "services: {}\n");
        var project = new Project
        {
            Name = "explicit",
            RepositoryPath = _directory,
            ComposeFile = "docker-compose.docker.yml",
        };

        var resolved = new DockerComposeService(new StubProcessRunner()).ResolveComposeFile(project);

        Assert.Equal(Path.Combine(_directory, "docker-compose.docker.yml"), resolved);
    }

    [Fact]
    public void Resolve_falls_back_to_auto_detection_and_null_without_any_compose_file()
    {
        var service = new DockerComposeService(new StubProcessRunner());

        File.WriteAllText(Path.Combine(_directory, "compose.yaml"), "services: {}\n");
        var auto = service.ResolveComposeFile(new Project { Name = "auto", RepositoryPath = _directory });
        Assert.Equal(Path.Combine(_directory, "compose.yaml"), auto);

        var empty = Directory.CreateDirectory(Path.Combine(_directory, "empty"));
        Assert.Null(service.ResolveComposeFile(new Project { Name = "none", RepositoryPath = empty.FullName }));
    }
}
