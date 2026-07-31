using System.Diagnostics;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class ExecutionServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ExecutionServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    private sealed class NullGitService : IGitService
    {
        public RepositorySnapshot GetSnapshot(string? repositoryPath, int recentCommitCount = 10) =>
            RepositorySnapshot.Invalid("test");

        public string? GetHeadSha(string? repositoryPath) => null;
    }

    private (ExecutionService Service, long ProjectId, BuildHistoryRepository Builds,
        TestHistoryRepository Tests, TimelineRepository Timeline) CreateHarness()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var builds = new BuildHistoryRepository(_fixture.Database);
        var tests = new TestHistoryRepository(_fixture.Database);
        var timeline = new TimelineRepository(_fixture.Database);
        var id = projects.Add(new Project { Name = $"exec-{Guid.NewGuid():N}" });
        var service = new ExecutionService(builds, tests, timeline, new NullGitService(), new ProcessRunner());
        return (service, id, builds, tests, timeline);
    }

    [Fact]
    public async Task Successful_build_is_recorded_with_output_and_timeline_entry()
    {
        var (service, id, builds, _, timeline) = CreateHarness();
        var project = new Project { Id = id, BuildCommand = "echo compiled-fine" };

        var record = await service.RunBuildAsync(project, ExecutionKind.Build);

        Assert.NotNull(record);
        Assert.True(record.Succeeded);
        Assert.Contains("compiled-fine", record.OutputLog);
        Assert.Single(builds.GetForProject(id));
        Assert.Contains(timeline.GetForProject(id), e => e.Category == TimelineCategory.Build);
    }

    [Fact]
    public async Task Build_going_red_after_green_journals_a_regression()
    {
        var (service, id, _, _, timeline) = CreateHarness();

        await service.RunBuildAsync(new Project { Id = id, BuildCommand = "true" }, ExecutionKind.Build);
        await service.RunBuildAsync(new Project { Id = id, BuildCommand = "false" }, ExecutionKind.Build);

        Assert.Contains(timeline.GetForProject(id), e => e.Category == TimelineCategory.Regression);
    }

    [Fact]
    public async Task Failing_test_command_maps_to_failed_result()
    {
        var (service, id, _, tests, _) = CreateHarness();
        var project = new Project { Id = id, TestCommand = "exit 3" };

        var record = await service.RunTestsAsync(project);

        Assert.NotNull(record);
        Assert.Equal(TestResult.Failed, record.Result);
        Assert.Equal(TestResult.Failed, tests.GetLatest(id)!.Result);
    }

    [Fact]
    public async Task Missing_command_returns_null_and_records_nothing()
    {
        var (service, id, builds, _, _) = CreateHarness();

        var record = await service.RunBuildAsync(new Project { Id = id }, ExecutionKind.Build);

        Assert.Null(record);
        Assert.Empty(builds.GetForProject(id));
    }

    [Fact]
    public async Task Timed_out_command_is_marked_timed_out()
    {
        var (service, id, _, _, _) = CreateHarness();
        var project = new Project { Id = id, TestCommand = "sleep 30" };

        var record = await service.RunTestsAsync(project, timeout: TimeSpan.FromMilliseconds(300));

        Assert.NotNull(record);
        Assert.Equal(TestResult.TimedOut, record.Result);
    }

    [Fact]
    public async Task Cancelled_command_completes_quickly_without_throwing()
    {
        var (service, id, _, _, _) = CreateHarness();
        var project = new Project { Id = id, TestCommand = "sleep 30" };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var stopwatch = Stopwatch.StartNew();
        var record = await service.RunTestsAsync(project, cancellationToken: cts.Token);
        stopwatch.Stop();

        Assert.NotNull(record);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
        // ProcessRunner reports cancellation as a null exit code, which maps to Error.
        Assert.Equal(TestResult.Error, record.Result);
        Assert.Contains("cancelled", record.OutputLog);
    }

    [Fact]
    public async Task Cancelled_build_after_green_journals_no_regression()
    {
        var (service, id, _, _, timeline) = CreateHarness();

        await service.RunBuildAsync(new Project { Id = id, BuildCommand = "true" }, ExecutionKind.Build);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await service.RunBuildAsync(
            new Project { Id = id, BuildCommand = "sleep 30" }, ExecutionKind.Build, cancellationToken: cts.Token);

        Assert.DoesNotContain(timeline.GetForProject(id), e => e.Category == TimelineCategory.Regression);
    }
}
