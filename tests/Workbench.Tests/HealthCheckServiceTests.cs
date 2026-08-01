using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class HealthCheckServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public HealthCheckServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    /// <summary>Returns queued status-command results; never touches real processes.</summary>
    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new();

        public List<string> Commands { get; } = [];

        public void Enqueue(string output, int exitCode = 0) =>
            _results.Enqueue(new ProcessResult("status", DateTimeOffset.Now, exitCode, TimeSpan.FromMilliseconds(5), output, false));

        public Task<ProcessResult> RunAsync(
            string command, string? workingDirectory, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : new ProcessResult(command, DateTimeOffset.Now, 0, TimeSpan.Zero, string.Empty, false));
        }
    }

    private (HealthCheckService Service, StubProcessRunner Runner, Project Project, TimelineRepository Timeline)
        CreateHarness(LaunchProfile profile)
    {
        var projects = new ProjectRepository(_fixture.Database);
        var timeline = new TimelineRepository(_fixture.Database);
        var runner = new StubProcessRunner();
        var service = new HealthCheckService(new RuntimeHealthRepository(_fixture.Database), timeline, runner);
        var id = projects.Add(new Project { Name = $"health-{Guid.NewGuid():N}" });
        var project = new Project { Id = id, Name = "health-test", Launch = profile };
        return (service, runner, project, timeline);
    }

    private static LaunchProfile Profile(params string[] required) => new()
    {
        StatusCommand = "status.sh",
        RequiredServices = [.. required],
        StartupGraceSeconds = 0,
        HealthCheckRetries = 1,
        HealthCheckIntervalSeconds = 1,
    };

    [Fact]
    public async Task Exit_zero_start_with_a_required_service_down_is_never_healthy()
    {
        // The launcher exits 0 (recorded by ExecutionService); the status
        // command then shows a required service crashed — health must be Failed.
        var (service, runner, project, _) = CreateHarness(Profile("database", "map"));
        runner.Enqueue("banshee_database_1 Up 2 hours (healthy)\nbanshee_map_1 Exited (139) 3 seconds ago");

        var report = await service.CheckAsync(project);

        Assert.Equal(RuntimeHealth.Failed, report.Health);
        Assert.Equal("map", report.FailedComponent);
    }

    [Fact]
    public async Task All_required_services_up_reports_healthy()
    {
        var (service, runner, project, _) = CreateHarness(Profile("database", "map"));
        runner.Enqueue("database Up 2 hours (healthy)\nmap Up 2 hours");

        var report = await service.CheckAsync(project);

        Assert.Equal(RuntimeHealth.Healthy, report.Health);
        Assert.Null(report.FailedComponent);
    }

    [Fact]
    public async Task Optional_service_down_reports_degraded_not_failed()
    {
        var profile = Profile("database");
        profile.OptionalServices.Add("metrics");
        var (service, runner, project, _) = CreateHarness(profile);
        runner.Enqueue("database up\nmetrics down");

        var report = await service.CheckAsync(project);

        Assert.Equal(RuntimeHealth.Degraded, report.Health);
    }

    [Fact]
    public async Task Status_command_failure_reports_unknown_never_healthy()
    {
        var (service, runner, project, _) = CreateHarness(Profile("database"));
        runner.Enqueue("podman: command not found", exitCode: 127);

        var report = await service.CheckAsync(project);

        Assert.Equal(RuntimeHealth.Unknown, report.Health);
    }

    [Fact]
    public async Task Failed_state_pattern_is_immediately_definitive()
    {
        var profile = Profile("database");
        profile.FailedPatterns.Add("Segmentation fault");
        profile.HealthCheckRetries = 3;
        var (service, runner, project, _) = CreateHarness(profile);
        runner.Enqueue("database up\nxi_map: Segmentation fault (core dumped)");
        runner.Enqueue("database up"); // must not be reached

        var report = await service.CheckAsync(project);

        Assert.Equal(RuntimeHealth.Failed, report.Health);
        Assert.Single(runner.Commands);
    }

    [Fact]
    public async Task Service_still_starting_is_retried_until_it_comes_up()
    {
        var profile = Profile("map");
        profile.HealthCheckRetries = 2;
        profile.HealthCheckIntervalSeconds = 1;
        var (service, runner, project, _) = CreateHarness(profile);
        runner.Enqueue("map starting");      // no healthy token → not yet up
        runner.Enqueue("map up");

        var report = await service.CheckAsync(project);

        Assert.Equal(RuntimeHealth.Healthy, report.Health);
        Assert.Equal(2, runner.Commands.Count);
    }

    [Fact]
    public async Task Detached_launcher_stays_trackable_after_the_launcher_exits()
    {
        // setsid / compose -d: the start command returns immediately, health
        // is assessed later and re-assessed on demand via the status command.
        var (service, runner, project, _) = CreateHarness(Profile("map"));

        service.MarkStarting(project.Id);
        runner.Enqueue("map up");
        var atLaunch = await service.CheckAfterLaunchAsync(project);
        Assert.Equal(RuntimeHealth.Healthy, atLaunch.Health);

        runner.Enqueue("map Exited (139)");
        var later = await service.CheckAsync(project);
        Assert.Equal(RuntimeHealth.Failed, later.Health);
        Assert.Equal("map", later.FailedComponent);
    }

    [Fact]
    public async Task Duplicate_start_is_blocked_while_starting_or_healthy_and_allowed_after_stop()
    {
        var (service, runner, project, _) = CreateHarness(Profile("map"));

        Assert.Null(service.GetStartBlockReason(project.Id));

        service.MarkStarting(project.Id);
        Assert.NotNull(service.GetStartBlockReason(project.Id));

        service.MarkStopped(project.Id);
        Assert.Null(service.GetStartBlockReason(project.Id));

        runner.Enqueue("map up");
        await service.CheckAsync(project);
        Assert.NotNull(service.GetStartBlockReason(project.Id));

        service.MarkStopped(project.Id);
        Assert.Null(service.GetStartBlockReason(project.Id));
    }

    [Fact]
    public async Task Health_transitions_are_journaled_to_the_timeline_once()
    {
        var (service, runner, project, timeline) = CreateHarness(Profile("map"));
        runner.Enqueue("map up");
        runner.Enqueue("map up");

        await service.CheckAsync(project);
        await service.CheckAsync(project); // same state: no second entry

        var entries = timeline.GetForProject(project.Id)
            .Where(e => e.Category == TimelineCategory.Status)
            .ToList();
        Assert.Single(entries);
        Assert.Contains("→ Healthy", entries[0].Message);
    }

    [Fact]
    public async Task Failed_transition_journals_the_failed_component()
    {
        var (service, runner, project, timeline) = CreateHarness(Profile("database", "map"));
        runner.Enqueue("database up\nmap Exited (139)");

        await service.CheckAsync(project);

        Assert.Contains(timeline.GetForProject(project.Id),
            e => e.Category == TimelineCategory.Status && e.Message.Contains("failed component: map"));
    }

    [Fact]
    public void Service_states_parse_per_service_with_health_matching_rules()
    {
        var profile = new LaunchProfile
        {
            RequiredServices = ["database", "map", "world"],
            OptionalServices = ["metrics"],
        };

        var states = HealthCheckService.GetServiceStates(
            profile, "database running healthy\nmap running\nworld Exited (1)");

        Assert.Equal(
            [
                new ServiceState("database", IsUp: true, IsRequired: true),
                new ServiceState("map", IsUp: true, IsRequired: true),
                new ServiceState("world", IsUp: false, IsRequired: true),
                new ServiceState("metrics", IsUp: false, IsRequired: false),
            ],
            states);
    }

    [Fact]
    public void Failed_service_log_command_expands_the_service_placeholder()
    {
        var profile = new LaunchProfile { ServiceLogCommand = "podman logs --tail=200 banshee_{service}_1" };

        Assert.Equal(
            "podman logs --tail=200 banshee_map_1",
            HealthCheckService.BuildServiceLogCommand(profile, "map"));
        Assert.Null(HealthCheckService.BuildServiceLogCommand(profile, null));
        Assert.Null(HealthCheckService.BuildServiceLogCommand(new LaunchProfile(), "map"));
    }

    [Fact]
    public async Task Missing_status_command_reports_unknown_without_running_anything()
    {
        var (service, runner, project, _) = CreateHarness(new LaunchProfile());

        var report = await service.CheckAsync(project);

        Assert.Equal(RuntimeHealth.Unknown, report.Health);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Cancelled_health_check_stops_promptly_instead_of_running_all_retries()
    {
        // Shutdown path: a health check cancelled mid-retry must unwind fast
        // (the closing window awaits it) instead of sleeping out every retry.
        var profile = Profile("map");
        profile.HealthCheckRetries = 10;
        profile.HealthCheckIntervalSeconds = 30;
        var (service, runner, project, _) = CreateHarness(profile);
        runner.Enqueue("map starting"); // non-definitive: would retry after 30s
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAfterLaunchAsync(project, cts.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        Assert.Single(runner.Commands);
    }
}
