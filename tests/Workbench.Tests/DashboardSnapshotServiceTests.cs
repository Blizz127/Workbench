using Workbench.Core.Models;
using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

public sealed class DashboardSnapshotServiceTests
{
    private static Project Banshee() => new()
    {
        Id = 7,
        Name = "Banshee Realm 11",
        Status = ProjectStatus.Active,
        RepositoryPath = "/repo/banshee",
        ComposeFile = "docker-compose.docker.yml",
    };

    private static RepositorySnapshot GitSnapshot() => new(
        IsValid: true,
        BranchName: "main",
        LastCommit: new CommitInfo("abcdef123456", "abcdef1", "Fix login packet", "blizz",
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)),
        RecentCommits: [],
        ModifiedFiles: [new FileChange("login.cs", "modified")],
        IsDirty: true,
        Error: null,
        AheadBy: 2,
        BehindBy: 1);

    private static ComposeProjectStatus ComposeStatus() => new(
        ProjectName: "banshee",
        ComposeFilePath: "/repo/banshee/docker-compose.docker.yml",
        Services:
        [
            new ComposeServiceStatus("database", "banshee-db-1", ComposeServiceState.Running, ComposeHealth.Healthy, 0, "Up"),
            new ComposeServiceStatus("db-update", "banshee-db-update-1", ComposeServiceState.Completed, ComposeHealth.None, 0, "Exited (0)"),
        ]);

    private static DashboardSnapshotService Service(
        Project[] projects,
        TimelineEntry[]? timeline = null,
        WebStubs.Docker? docker = null) =>
        new(
            new WebStubs.SystemHealth(),
            new WebStubs.Projects(projects),
            new WebStubs.Git(GitSnapshot()),
            docker ?? new WebStubs.Docker("/repo/banshee/docker-compose.docker.yml", ComposeStatus()),
            new WebStubs.Tmux(new TmuxServerStatus(true, true,
                [new TmuxSession("banshee", null, false, 1, "bash")], null)),
            new WebStubs.Timeline(timeline ?? []));

    [Fact]
    public async Task Snapshot_composes_health_tmux_and_project_cards()
    {
        var snapshot = await Service([Banshee()]).BuildAsync();

        Assert.Equal("25%", snapshot.System.CpuText);
        Assert.Equal("4 GB / 16 GB", snapshot.System.MemoryText);
        Assert.Equal("1d 6h", snapshot.System.UptimeText);

        Assert.True(snapshot.Tmux.TmuxAvailable);
        var session = Assert.Single(snapshot.Tmux.Sessions);
        Assert.Equal("banshee", session.Name);
        Assert.False(session.IsAttached);

        var card = Assert.Single(snapshot.Projects);
        Assert.Equal("Banshee Realm 11", card.Name);
        Assert.Equal("Active", card.Status);
        Assert.True(card.GitDirty);
        Assert.Contains("main", card.GitText);
        Assert.Contains("↑2", card.GitText);
        Assert.Contains("↓1", card.GitText);
        Assert.Contains("abcdef1", card.GitText);
        Assert.Equal("1/2 running · 1 completed", card.DockerSummary);
        Assert.Equal(2, card.Services.Count);
        Assert.Equal("completed", card.Services[1].StateText);
        Assert.Null(card.Services[1].HealthText);
    }

    [Fact]
    public async Task Journal_entries_are_secret_redacted_at_the_source()
    {
        var timeline = new[]
        {
            new TimelineEntry
            {
                ProjectId = 7,
                Timestamp = DateTimeOffset.Now,
                Category = TimelineCategory.Run,
                Message = "run failed",
                Detail = "connecting with MARIADB_PASSWORD=sup3rsecret refused",
            },
        };

        var snapshot = await Service([Banshee()], timeline).BuildAsync();
        var entry = Assert.Single(snapshot.Projects[0].RecentActivity);

        Assert.DoesNotContain("sup3rsecret", entry.Detail);
        Assert.Contains("MARIADB_PASSWORD=***", entry.Detail);
    }

    [Fact]
    public async Task Project_without_a_compose_file_is_never_inspected()
    {
        var docker = new WebStubs.Docker(composeFile: null, status: null);

        var snapshot = await Service([Banshee()], docker: docker).BuildAsync();
        var card = Assert.Single(snapshot.Projects);

        Assert.Equal(0, docker.InspectCount);
        Assert.Null(card.DockerSummary);
        Assert.Empty(card.Services);
    }

    [Fact]
    public async Task Invalid_git_repository_surfaces_its_error_without_commit_details()
    {
        var service = new DashboardSnapshotService(
            new WebStubs.SystemHealth(),
            new WebStubs.Projects(Banshee()),
            new WebStubs.Git(RepositorySnapshot.Invalid("Not a git repository.")),
            new WebStubs.Docker(null, null),
            new WebStubs.Tmux(TmuxServerStatus.NoServer()),
            new WebStubs.Timeline());

        var card = Assert.Single((await service.BuildAsync()).Projects);

        Assert.Equal("no repository", card.GitText);
        Assert.Equal("Not a git repository.", card.GitError);
        Assert.False(card.GitDirty);
    }
}
