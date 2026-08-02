using Workbench.Core.Models;
using Workbench.Core.Services;
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
        WebStubs.Blockers? blockers = null,
        WebStubs.Milestones? milestones = null,
        WebStubs.Frontiers? frontiers = null,
        WebStubs.RuntimeHealthRecords? runtimeHealth = null,
        WebStubs.BuildHistory? builds = null,
        WebStubs.TestHistory? tests = null,
        WebStubs.ProjectConfigs? configs = null)
    {
        var projectRepo = new WebStubs.Projects(projects);
        var timelineRepo = new WebStubs.Timeline(timeline ?? []);
        var blockerRepo = blockers ?? new WebStubs.Blockers();
        var milestoneRepo = milestones ?? new WebStubs.Milestones();
        var frontierRepo = frontiers ?? new WebStubs.Frontiers();
        var runtimeRepo = runtimeHealth ?? new WebStubs.RuntimeHealthRecords();
        var buildRepo = builds ?? new WebStubs.BuildHistory();
        var testRepo = tests ?? new WebStubs.TestHistory();
        var gitService = git ?? new WebStubs.Git(WebFixtures.BansheeGit());

        var projection = new DashboardProjectionBuilder(
            projectRepo, milestoneRepo, blockerRepo, frontierRepo, runtimeRepo,
            buildRepo, testRepo, timelineRepo, new WebStubs.MobileActionAudits(), gitService);

        return new DashboardSnapshotService(
            new WebStubs.SystemHealth(),
            projectRepo,
            gitService,
            docker ?? new WebStubs.Docker(
                p => p.ComposeFile is null ? null : "/repo/banshee/docker-compose.docker.yml",
                _ => WebFixtures.BansheeCompose()),
            tmux ?? new WebStubs.Tmux(WebFixtures.TmuxRunning()),
            timelineRepo,
            blockerRepo,
            projection,
            configs ?? new WebStubs.ProjectConfigs(_ => null));
    }

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

    [Fact]
    public async Task Cards_carry_the_shared_projection_health_progress_and_blockers()
    {
        var snapshot = await Service(
            [WebFixtures.Banshee()],
            milestones: new WebStubs.Milestones(
                new Milestone { ProjectId = 7, Title = "boot", Status = MilestoneStatus.Done, Weight = 3 },
                new Milestone { ProjectId = 7, Title = "renderer", Status = MilestoneStatus.Active, Weight = 1 }),
            frontiers: new WebStubs.Frontiers(
                new Frontier { ProjectId = 7, Title = "boot", Status = FrontierStatus.Completed },
                new Frontier { ProjectId = 7, Title = "lobby protocol", Status = FrontierStatus.Current }),
            blockers: new WebStubs.Blockers(
                new Blocker { ProjectId = 7, Description = "NAT drops", Severity = "Critical", Status = BlockerStatus.Active }),
            runtimeHealth: new WebStubs.RuntimeHealthRecords(
                new RuntimeHealthRecord { ProjectId = 7, CheckedAt = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero), Health = RuntimeHealth.Healthy }),
            builds: new WebStubs.BuildHistory(
                new BuildRecord { ProjectId = 7, Kind = ExecutionKind.Build, StartedAt = new DateTimeOffset(2026, 8, 1, 11, 0, 0, TimeSpan.Zero), ExitCode = 0 }),
            tests: new WebStubs.TestHistory(
                new TestRecord { ProjectId = 7, Result = TestResult.Failed, StartedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero) }))
            .BuildAsync();

        var card = Assert.Single(snapshot.Projects);

        // Health: the test failure wins over the healthy runtime; the badge is text, never color alone.
        Assert.Equal("Failing", card.HealthText);
        Assert.Equal("bad", card.HealthBadgeClass);
        Assert.Contains("Last test run failed", card.HealthReasonsText);

        // Weighted progress: 3 of 4 weight done = 75%.
        Assert.Equal("1/2 milestones · 75%", card.ProgressText);
        Assert.Equal(75.0, card.ProgressPercent, precision: 6);

        Assert.Equal("1/2 frontiers · lobby protocol", card.FrontierText);
        Assert.Equal("1 active · highest critical", card.BlockerText);
        Assert.Equal("build OK · test FAIL", card.ResultsText);
        Assert.Equal("OpenProject", card.PrimaryAction); // the fixture project has no run command
        Assert.False(card.HasMobileActions);

        // Summary totals mirror the desktop bar.
        Assert.Equal(1, snapshot.Summary.TotalProjects);
        Assert.Equal(1, snapshot.Summary.FailingCount);
        Assert.Equal(1, snapshot.Summary.ActiveBlockerCount);
        Assert.Equal(1, snapshot.Summary.RunningServices);
        Assert.Equal(1, snapshot.Summary.DirtyProjects);
    }

    [Fact]
    public async Task No_milestones_means_progress_not_configured_never_zero()
    {
        var snapshot = await Service([WebFixtures.Banshee()]).BuildAsync();

        var card = Assert.Single(snapshot.Projects);
        Assert.Null(card.ProgressText);
        Assert.Equal(0, card.ProgressPercent);
        Assert.Null(card.FrontierText);
        Assert.Null(card.BlockerText);
        Assert.Null(card.ResultsText);
        Assert.Null(card.LastActivityText);
    }

    [Fact]
    public async Task Card_fields_redact_secret_shaped_text()
    {
        var snapshot = await Service(
            [WebFixtures.Banshee()],
            frontiers: new WebStubs.Frontiers(
                new Frontier { ProjectId = 7, Title = "deploy with MARIADB_PASSWORD=sup3rsecret", Status = FrontierStatus.Current }),
            blockers: new WebStubs.Blockers(
                new Blocker { ProjectId = 7, Description = "x", Severity = "high password=sekrit", Status = BlockerStatus.Active }))
            .BuildAsync();

        var card = Assert.Single(snapshot.Projects);
        Assert.DoesNotContain("sup3rsecret", card.FrontierText);
        Assert.Contains("MARIADB_PASSWORD=***", card.FrontierText);
        Assert.DoesNotContain("sekrit", card.BlockerText);
    }

    [Fact]
    public async Task Actions_link_is_gated_on_the_mobile_allowlist()
    {
        var snapshot = await Service(
            [WebFixtures.Banshee()],
            configs: new WebStubs.ProjectConfigs(path =>
                path == WebFixtures.BansheeRepoDir ? WebFixtures.BansheeConfig() : null))
            .BuildAsync();

        var card = Assert.Single(snapshot.Projects);
        Assert.True(card.HasMobileActions);
    }
}
