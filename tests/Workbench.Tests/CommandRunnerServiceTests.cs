using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class CommandRunnerServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public CommandRunnerServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    private sealed class NullGitService : IGitService
    {
        public RepositorySnapshot GetSnapshot(string? repositoryPath, int recentCommitCount = 10) =>
            RepositorySnapshot.Invalid("test");

        public string? GetHeadSha(string? repositoryPath) => null;
    }

    private (CommandRunnerService Service, long ProjectId, ICommandRunRepository Runs, ITimelineRepository Timeline)
        CreateHarness()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var runs = new CommandRunRepository(_fixture.Database);
        var timeline = new TimelineRepository(_fixture.Database);
        var id = projects.Add(new Project { Name = $"cmdrunner-{Guid.NewGuid():N}" });
        var service = new CommandRunnerService(runs, timeline, new NullGitService(), new ProcessRunner());
        return (service, id, runs, timeline);
    }

    [Fact]
    public async Task Successful_run_is_recorded_and_journaled()
    {
        var (service, id, runs, timeline) = CreateHarness();
        var project = new Project { Id = id };

        var record = await service.RunAsync(project, "matching-build", "echo matched-ok");

        Assert.True(record.Succeeded);
        Assert.Equal("matching-build", record.CommandName);
        Assert.Contains("matched-ok", record.OutputLog);
        Assert.Single(runs.GetForProject(id));
        Assert.Contains(timeline.GetForProject(id), e => e.Message.Contains("matching-build succeeded"));
    }

    [Fact]
    public async Task Regression_is_flagged_when_a_previously_green_command_fails()
    {
        var (service, id, runs, timeline) = CreateHarness();
        var project = new Project { Id = id };

        await service.RunAsync(project, "field-repro", "true");
        var second = await service.RunAsync(project, "field-repro", "false");

        Assert.True(second.RegressionDetected);
        Assert.Contains(timeline.GetForProject(id), e => e.Category == TimelineCategory.Regression);
        Assert.True(runs.GetLatest(id, "field-repro")!.RegressionDetected);
    }

    [Fact]
    public async Task Different_command_names_track_regressions_independently()
    {
        var (service, id, _, _) = CreateHarness();
        var project = new Project { Id = id };

        await service.RunAsync(project, "matching-build", "true");
        await service.RunAsync(project, "field-repro", "false"); // first-ever run of this name: no regression

        var fieldRepro = await service.RunAsync(project, "field-repro", "false");
        Assert.False(fieldRepro.RegressionDetected); // still failing, not a NEW regression
    }

    [Fact]
    public async Task Cancelled_run_is_recorded_but_not_a_regression()
    {
        var (service, id, _, timeline) = CreateHarness();
        var project = new Project { Id = id };

        await service.RunAsync(project, "field-repro", "true");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var record = await service.RunAsync(project, "field-repro", "sleep 30", cancellationToken: cts.Token);

        Assert.True(record.Cancelled);
        Assert.False(record.RegressionDetected);
        Assert.DoesNotContain(timeline.GetForProject(id), e => e.Category == TimelineCategory.Regression);
    }

    [Fact]
    public async Task Output_log_is_stored_and_fetchable_by_id()
    {
        var (service, id, runs, _) = CreateHarness();
        var project = new Project { Id = id };

        var record = await service.RunAsync(project, "custom-check", "echo distinctive-marker-xyz");

        Assert.Contains("distinctive-marker-xyz", runs.GetOutputLog(record.Id));
        // List queries omit the log, matching the append-only history convention.
        Assert.Equal(string.Empty, runs.GetForProject(id).Single().OutputLog);
    }
}
