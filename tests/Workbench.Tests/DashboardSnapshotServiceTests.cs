using Workbench.Core.Models;
using Workbench.Web;
using Xunit;

namespace Workbench.Tests;

public sealed class DashboardSnapshotServiceTests
{
    private static DashboardSnapshotService Service(
        Project[] projects,
        TimelineEntry[]? timeline = null,
        WebStubs.Docker? docker = null,
        WebStubs.Git? git = null,
        WebStubs.Tmux? tmux = null,
        WebStubs.Blockers? blockers = null) =>
        new(
            new WebStubs.SystemHealth(),
            new WebStubs.Projects(projects),
            git ?? new WebStubs.Git(WebFixtures.BansheeGit()),
            docker ?? new WebStubs.Docker(
                p => p.ComposeFile is null ? null : "/repo/banshee/docker-compose.docker.yml",
                _ => WebFixtures.BansheeCompose()),
            tmux ?? new WebStubs.Tmux(WebFixtures.TmuxRunning()),
            new WebStubs.Timeline(timeline ?? []),
            blockers ?? new WebStubs.Blockers());

    [Fact]
    public async Task Dashboard_composes_health_tmux_and_compact_project_cards()
    {
        var snapshot = await Service([WebFixtures.Banshee(), WebFixtures.Xenogears()]).BuildAsync();

        Assert.Equal("25%", snapshot.System.CpuText);
        Assert.Equal("4 GB / 16 GB", snapshot.System.MemoryText);
        Assert.Equal("1d 6h", snapshot.System.UptimeText);

        var session = Assert.Single(snapshot.Tmux.Sessions);
        Assert.Equal("banshee", session.Name);

        var banshee = Assert.Single(snapshot.Projects, p => p.Name == "Banshee Realm 11");
        Assert.Equal(7, banshee.Id);
        Assert.True(banshee.GitDirty);
        Assert.False(banshee.GitUnavailable);
        Assert.Contains("main", banshee.GitText);
        Assert.Contains("↑2", banshee.GitText);
        Assert.Contains("ea407af", banshee.GitText);
        Assert.Equal("5/6 running · 1 completed", banshee.DockerSummary);
        Assert.Equal("detached", banshee.TmuxText);

        var xenogears = Assert.Single(snapshot.Projects, p => p.Name == "Xenogears Decompilation");
        Assert.True(xenogears.GitUnavailable);
        Assert.Equal("repo missing", xenogears.GitText);
        Assert.Null(xenogears.DockerSummary);
    }

    [Fact]
    public async Task Detail_page_carries_git_docker_session_and_activity()
    {
        var detail = await Service([WebFixtures.Banshee()], [WebFixtures.SecretJournalEntry()])
            .BuildProjectAsync(7);

        Assert.NotNull(detail);
        Assert.Equal("Banshee Realm 11", detail.Name);
        Assert.Equal(WebFixtures.BansheeRepoDir, detail.RepositoryPath);

        Assert.True(detail.Git.Available);
        Assert.Equal("main", detail.Git.Branch);
        Assert.True(detail.Git.Dirty);
        Assert.Equal(2, detail.Git.DirtyCount);
        Assert.Equal(2, detail.Git.AheadBy);
        Assert.Equal(1, detail.Git.BehindBy);
        Assert.Contains("ea407af", detail.Git.LastCommitText);
        Assert.Equal(2, detail.Git.RecentCommits.Count);
        Assert.Equal("b2c3d4e", detail.Git.RecentCommits[1].ShortSha);

        Assert.True(detail.Docker.HasComposeFile);
        Assert.Equal("5/6 running · 1 completed", detail.Docker.Summary);
        Assert.Equal(6, detail.Docker.Services.Count);
        var dbUpdate = Assert.Single(detail.Docker.Services, s => s.Name == "db-update");
        Assert.Equal("completed", dbUpdate.StateText);
        Assert.Null(dbUpdate.HealthText);
        var database = Assert.Single(detail.Docker.Services, s => s.Name == "database");
        Assert.Equal("running", database.StateText);
        Assert.Equal("healthy", database.HealthText);

        Assert.Equal("banshee", detail.Session.SessionName);
        Assert.True(detail.Session.Exists);
        Assert.False(detail.Session.IsAttached);

        var entry = Assert.Single(detail.RecentActivity);
        Assert.DoesNotContain("sup3rsecret", entry.Detail);
        Assert.Contains("MARIADB_PASSWORD=***", entry.Detail);
    }

    [Fact]
    public async Task Unknown_project_id_means_no_detail()
    {
        Assert.Null(await Service([WebFixtures.Banshee()]).BuildProjectAsync(999));
    }

    [Fact]
    public async Task Project_without_a_compose_file_is_never_inspected()
    {
        var docker = new WebStubs.Docker(_ => null, _ => null);

        var detail = await Service([WebFixtures.Banshee()], docker: docker).BuildProjectAsync(7);

        Assert.NotNull(detail);
        Assert.Equal(0, docker.InspectCount);
        Assert.False(detail.Docker.HasComposeFile);
        Assert.Empty(detail.Docker.Services);
    }

    [Fact]
    public async Task No_git_repository_and_no_tmux_session_are_honest_states()
    {
        var service = Service(
            [WebFixtures.Xenogears()],
            git: new WebStubs.Git(_ => RepositorySnapshot.Invalid("Path does not exist: /repo/xenogears-does-not-exist")),
            tmux: new WebStubs.Tmux(TmuxServerStatus.NoServer()));

        var detail = await service.BuildProjectAsync(9);

        Assert.NotNull(detail);
        Assert.False(detail.Git.Available);
        Assert.Equal("Path does not exist: /repo/xenogears-does-not-exist", detail.Git.Error);
        Assert.Empty(detail.Git.RecentCommits);
        Assert.False(detail.Session.ServerRunning);
        Assert.False(detail.Session.Exists);
    }

    [Fact]
    public async Task Repeated_polls_within_the_ttl_do_not_respawn_inspections()
    {
        var docker = new WebStubs.Docker(
            p => p.ComposeFile is null ? null : "/repo/banshee/docker-compose.docker.yml",
            _ => WebFixtures.BansheeCompose());
        var git = new WebStubs.Git(WebFixtures.BansheeGit());
        var service = Service([WebFixtures.Banshee()], docker: docker, git: git);

        await service.BuildAsync();
        await service.BuildAsync();
        await service.BuildProjectAsync(7);

        // One docker inspect and one git snapshot despite three snapshot builds —
        // the TTL caches are what keep phone polling cheap.
        Assert.Equal(1, docker.InspectCount);
        Assert.Equal(1, git.CallCount);
    }
}
