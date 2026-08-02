using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// The projection builder assembles cards, fleet summary, and the merged
/// activity stream from a seeded database — with no secrets or raw command
/// output anywhere in the projection. Seeding lives in the fixture so it
/// runs exactly once per test class (the test class itself is re-created
/// per test method).
/// </summary>
public sealed class DashboardProjectionBuilderTests : IClassFixture<DashboardProjectionBuilderTests.Fixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 8, 1, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T4 = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One seeded database shared by every test in this class.</summary>
    public sealed class Fixture : IDisposable
    {
        private readonly DatabaseFixture _inner = new();

        public WorkbenchDatabase Database => _inner.Database;

        public long AlphaId { get; }
        public long BetaId { get; }
        public long GammaId { get; }

        public void Dispose() => _inner.Dispose();

        public Fixture()
        {
            var projects = new ProjectRepository(Database);
            var milestones = new MilestoneRepository(Database);
            var blockers = new BlockerRepository(Database);
            var frontiers = new FrontierRepository(Database);
            var builds = new BuildHistoryRepository(Database);
            var tests = new TestHistoryRepository(Database);
            var runtime = new RuntimeHealthRepository(Database);
            var timeline = new TimelineRepository(Database);
            var audit = new MobileActionAuditRepository(Database);

            // Alpha: fully wired project — progress, blockers, history, activity.
            AlphaId = projects.Add(new Project
            {
                Name = "Alpha",
                Tags = ["PSX"],
                RepositoryPath = "/repo/alpha",
                RunCommand = "make run",
                NextSafestStep = "project fallback step",
            });
            milestones.Add(new Milestone { ProjectId = AlphaId, Title = "boot", Status = MilestoneStatus.Done, Weight = 2, SortOrder = 0 });
            milestones.Add(new Milestone { ProjectId = AlphaId, Title = "renderer", Status = MilestoneStatus.Active, Weight = 1, SortOrder = 1 });
            milestones.Add(new Milestone { ProjectId = AlphaId, Title = "netplay", Status = MilestoneStatus.Blocked, SortOrder = 2 });
            milestones.Add(new Milestone { ProjectId = AlphaId, Title = "sound", Status = MilestoneStatus.Dismissed, SortOrder = 3 });
            blockers.Add(new Blocker { ProjectId = AlphaId, Description = "camera guard", Severity = "CRITICAL", NextSafestStep = "Fix camera guard" });
            blockers.Add(new Blocker { ProjectId = AlphaId, Description = "cosmetic", Severity = "low" });
            blockers.Add(new Blocker { ProjectId = AlphaId, Description = "watching upstream", Severity = "medium", Status = BlockerStatus.Monitoring });
            blockers.Add(new Blocker { ProjectId = AlphaId, Description = "won't fix", Severity = "high", Status = BlockerStatus.Dismissed });
            frontiers.Add(new Frontier { ProjectId = AlphaId, Title = "boot", Status = FrontierStatus.Completed });
            frontiers.Add(new Frontier { ProjectId = AlphaId, Title = "lobby protocol", Status = FrontierStatus.Current });
            builds.Add(new BuildRecord { ProjectId = AlphaId, Kind = ExecutionKind.Run, Command = "make run", StartedAt = T0, ExitCode = 1, OutputLog = "launch output with password=sekrit" });
            builds.Add(new BuildRecord { ProjectId = AlphaId, Kind = ExecutionKind.Build, Command = "make", StartedAt = T1, ExitCode = 0, OutputLog = "build output with password=sekrit" });
            tests.Add(new TestRecord { ProjectId = AlphaId, Command = "make test", Result = TestResult.Passed, StartedAt = T1, OutputLog = "test output with password=sekrit" });
            runtime.Add(new RuntimeHealthRecord { ProjectId = AlphaId, CheckedAt = T0, Health = RuntimeHealth.Failed, Output = "map down" });
            runtime.Add(new RuntimeHealthRecord { ProjectId = AlphaId, CheckedAt = T1, Health = RuntimeHealth.Healthy, Output = "map up" });
            timeline.Add(new TimelineEntry { ProjectId = AlphaId, Timestamp = T2, Category = TimelineCategory.Milestone, Message = "Milestone completed: boot" });
            timeline.Add(new TimelineEntry { ProjectId = AlphaId, Timestamp = T3, Category = TimelineCategory.Run, Message = "deploy finished with password=hunter2" });
            audit.Add(new MobileActionAuditRecord
            {
                TimestampUtc = T4,
                ProjectId = AlphaId,
                ProjectName = "Alpha",
                ActionId = "restart-map",
                ActionLabel = "Restart map",
                ActionType = "restart",
                Safety = "safe",
                State = MobileActionAuditState.Succeeded,
                OutputSummary = "raw secret output that must not leak",
                CorrelationId = "corr-1",
            });

            // Beta: repository missing on this machine.
            BetaId = projects.Add(new Project { Name = "Beta", RepositoryPath = "/repo/beta" });

            // Gamma: only non-active blockers — they must not count.
            GammaId = projects.Add(new Project { Name = "Gamma", RepositoryPath = "/repo/gamma" });
            blockers.Add(new Blocker { ProjectId = GammaId, Description = "watching", Severity = "critical", Status = BlockerStatus.Monitoring });
            blockers.Add(new Blocker { ProjectId = GammaId, Description = "dismissed", Severity = "high", Status = BlockerStatus.Dismissed });
        }
    }

    private readonly Fixture _fixture;

    public DashboardProjectionBuilderTests(Fixture fixture) => _fixture = fixture;

    private DashboardProjectionBuilder CreateBuilder()
    {
        var git = new WebStubs.Git(path => path == "/repo/beta"
            ? RepositorySnapshot.Invalid("repository not cloned on this machine")
            : new RepositorySnapshot(
                IsValid: true, BranchName: "main", LastCommit: null,
                RecentCommits: [], ModifiedFiles: [],
                IsDirty: path == "/repo/alpha", Error: null,
                AheadBy: path == "/repo/alpha" ? 2 : 0, BehindBy: 0));

        return new DashboardProjectionBuilder(
            new ProjectRepository(_fixture.Database),
            new MilestoneRepository(_fixture.Database),
            new BlockerRepository(_fixture.Database),
            new FrontierRepository(_fixture.Database),
            new RuntimeHealthRepository(_fixture.Database),
            new BuildHistoryRepository(_fixture.Database),
            new TestHistoryRepository(_fixture.Database),
            new TimelineRepository(_fixture.Database),
            new MobileActionAuditRepository(_fixture.Database),
            git);
    }

    private ProjectDashboardCard Card(DashboardProjection projection, long projectId) =>
        projection.Cards.Single(c => c.ProjectId == projectId);

    [Fact]
    public void Card_carries_weighted_milestone_progress_from_explicit_state()
    {
        var alpha = Card(CreateBuilder().Build(), _fixture.AlphaId);

        var progress = alpha.MilestoneProgress;
        Assert.NotNull(progress);
        Assert.Equal(1, progress.CompletedCount);
        Assert.Equal(3, progress.TotalCount); // dismissed excluded
        Assert.Equal(50.0, progress.Percent, precision: 6); // 2 of 4 weight
        Assert.Equal(1, progress.DismissedCount);
        Assert.Equal(1, progress.ActiveCount);
        Assert.Equal(1, progress.BlockedCount);
    }

    [Fact]
    public void Card_carries_blocker_counts_and_the_highest_active_severity()
    {
        var alpha = Card(CreateBuilder().Build(), _fixture.AlphaId);

        Assert.Equal(2, alpha.ActiveBlockerCount);
        Assert.Equal(1, alpha.MonitoringBlockerCount);
        Assert.Equal("critical", alpha.HighestActiveSeverity);
        Assert.Equal("Fix camera guard", alpha.NextSafestStep);
        Assert.Equal("lobby protocol", alpha.CurrentFrontierTitle);
        Assert.Equal(1, alpha.FrontierProgress.CompletedCount);
        Assert.Equal(2, alpha.FrontierProgress.TotalCount);
    }

    [Fact]
    public void Card_carries_execution_verdicts_without_any_output_text()
    {
        var alpha = Card(CreateBuilder().Build(), _fixture.AlphaId);

        Assert.NotNull(alpha.LastBuildResult);
        Assert.Equal("Build", alpha.LastBuildResult.Kind);
        Assert.True(alpha.LastBuildResult.Succeeded);
        Assert.Equal(T1, alpha.LastBuildResult.When);

        Assert.NotNull(alpha.LastLaunchResult);
        Assert.Equal("Run", alpha.LastLaunchResult.Kind);
        Assert.False(alpha.LastLaunchResult.Succeeded);

        Assert.NotNull(alpha.LastTestResult);
        Assert.Equal("Test", alpha.LastTestResult.Kind);
        Assert.True(alpha.LastTestResult.Succeeded);
        Assert.False(alpha.LastTestResult.TimedOut);

        // Structural check: the card exposes no command or output property at all.
        Assert.DoesNotContain(typeof(ProjectDashboardCard).GetProperties().Select(p => p.Name),
            name => name.Contains("Output", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Command", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Card_carries_runtime_health_git_state_and_last_activity()
    {
        var alpha = Card(CreateBuilder().Build(), _fixture.AlphaId);

        Assert.Equal(RuntimeHealth.Healthy, alpha.RuntimeHealth); // newest record wins
        Assert.True(alpha.RepositoryAvailable);
        Assert.Equal("main", alpha.BranchName);
        Assert.True(alpha.IsDirty);
        Assert.Equal(2, alpha.AheadBy);
        Assert.Equal(T4, alpha.LastActivityAt); // the audit record is the newest signal
        Assert.Equal(DashboardPrimaryAction.RunOrStart, alpha.PrimaryAction);
        Assert.Equal("PSX", alpha.AccentOrIcon);
    }

    [Fact]
    public void Missing_repository_makes_the_card_unavailable()
    {
        var beta = Card(CreateBuilder().Build(), _fixture.BetaId);

        Assert.False(beta.RepositoryAvailable);
        Assert.Equal(ProjectHealthState.Unavailable, beta.HealthAssessment.State);
        Assert.Null(beta.MilestoneProgress); // no milestones → not configured, never 0%
        Assert.Equal(DashboardPrimaryAction.OpenProject, beta.PrimaryAction);
        Assert.Equal("B", beta.AccentOrIcon);
        Assert.Null(beta.LastActivityAt);
    }

    [Fact]
    public void Monitoring_and_dismissed_blockers_neither_count_nor_block()
    {
        var gamma = Card(CreateBuilder().Build(), _fixture.GammaId);

        Assert.Equal(0, gamma.ActiveBlockerCount);
        Assert.Equal(1, gamma.MonitoringBlockerCount);
        Assert.Null(gamma.HighestActiveSeverity);
        // No active blocker, no failures, no healthy evidence → Unknown.
        Assert.Equal(ProjectHealthState.Unknown, gamma.HealthAssessment.State);
    }

    [Fact]
    public void Summary_totals_and_health_distribution_match_the_cards()
    {
        var projection = CreateBuilder().Build();
        var summary = projection.Summary;

        Assert.Equal(3, summary.TotalProjects);
        Assert.Equal(1, summary.BlockedCount);      // Alpha: critical active blocker
        Assert.Equal(1, summary.UnavailableCount);  // Beta: missing repository
        Assert.Equal(1, summary.UnknownCount);      // Gamma: no signals
        Assert.Equal(0, summary.HealthyCount);
        Assert.Equal(0, summary.FailingCount);
        Assert.Equal(2, summary.ActiveBlockerCount);
        Assert.Equal(1, summary.RunningServices);   // Alpha runtime is Healthy
        Assert.Equal(0, summary.FailingServices);
        Assert.Equal(1, summary.DirtyProjects);
        Assert.Equal(summary.TotalProjects,
            summary.HealthyCount + summary.AttentionCount + summary.BlockedCount
            + summary.FailingCount + summary.UnavailableCount + summary.UnknownCount);
    }

    [Fact]
    public void Recent_activity_merges_timeline_and_audit_newest_first()
    {
        var projection = CreateBuilder().Build();

        Assert.Equal(3, projection.RecentActivity.Count);
        Assert.Equal(T4, projection.RecentActivity[0].When);
        Assert.Equal(T3, projection.RecentActivity[1].When);
        Assert.Equal(T2, projection.RecentActivity[2].When);

        var auditItem = projection.RecentActivity[0];
        Assert.Equal("Mobile action", auditItem.Category);
        Assert.Equal("Alpha", auditItem.ProjectName);
        Assert.Equal("Restart map — succeeded", auditItem.Message);
    }

    [Fact]
    public void Recent_activity_respects_the_cap()
    {
        var projection = CreateBuilder().Build(recentActivityLimit: 2);

        Assert.Equal(2, projection.RecentActivity.Count);
        Assert.Equal(T4, projection.RecentActivity[0].When);
        Assert.Equal(T3, projection.RecentActivity[1].When);
    }

    [Fact]
    public void Recent_activity_redacts_secrets_and_never_carries_output_text()
    {
        var projection = CreateBuilder().Build();

        var timelineItem = projection.RecentActivity.Single(i => i.When == T3);
        Assert.DoesNotContain("hunter2", timelineItem.Message);
        Assert.Contains(SecretRedactor.Replacement, timelineItem.Message);

        var auditItem = projection.RecentActivity.Single(i => i.Category == "Mobile action");
        Assert.DoesNotContain("raw secret output", auditItem.Message);
    }
}
