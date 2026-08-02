using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Builds the shared dashboard projection from the repositories and the git
/// service — no SQL, no Avalonia, no ASP.NET. Both UIs call this one builder.
/// Milestone/frontier math comes from <see cref="ProgressCalculator"/>, health
/// from <see cref="ProjectHealthCalculator"/>; every message string passes
/// through <see cref="SecretRedactor"/> as defense-in-depth, and no raw
/// command output ever lands in the projection.
/// </summary>
public sealed class DashboardProjectionBuilder(
    IProjectRepository projects,
    IMilestoneRepository milestones,
    IBlockerRepository blockers,
    IFrontierRepository frontiers,
    IRuntimeHealthRepository runtimeHealth,
    IBuildHistoryRepository builds,
    ITestHistoryRepository tests,
    ITimelineRepository timeline,
    IMobileActionAuditRepository audit,
    IGitService git)
{
    public const int DefaultRecentActivityLimit = 50;

    public DashboardProjection Build(int recentActivityLimit = DefaultRecentActivityLimit)
    {
        var allProjects = projects.GetAll();
        var cards = allProjects.Select(BuildCard).ToList();
        return new DashboardProjection(cards, BuildSummary(cards), BuildRecentActivity(allProjects, recentActivityLimit));
    }

    public ProjectDashboardCard BuildCard(Project project) =>
        BuildCard(project, git.GetSnapshot(project.RepositoryPath));

    /// <summary>
    /// Card build with a caller-supplied git snapshot — the web host routes
    /// this through its TTL cache so phone polling never doubles the
    /// repository walks the builder would otherwise trigger.
    /// </summary>
    public ProjectDashboardCard BuildCard(Project project, RepositorySnapshot snapshot)
    {
        var milestoneProgress = ProgressCalculator.Calculate(milestones.GetForProject(project.Id));
        var frontierProgress = ProgressCalculator.CalculateFrontiers(frontiers.GetForProject(project.Id));
        var projectBlockers = blockers.GetForProject(project.Id);
        var activeBlockers = projectBlockers.Where(b => b.Status == BlockerStatus.Active).ToList();
        var latestRuntime = runtimeHealth.GetLatest(project.Id);
        var lastBuild = builds.GetLatest(project.Id, ExecutionKind.Build);
        var lastLaunch = builds.GetLatest(project.Id, ExecutionKind.Run);
        var lastTest = tests.GetLatest(project.Id);

        var topBlocker = activeBlockers
            .OrderByDescending(b => SeverityRank(b.Severity))
            .ThenByDescending(b => b.Id)
            .FirstOrDefault();

        var assessment = ProjectHealthCalculator.Assess(new ProjectHealthInput(
            RepositoryAvailable: snapshot.IsValid,
            LatestRuntimeHealth: latestRuntime?.Health,
            LastBuildSucceeded: lastBuild?.ExitCode is { } exitCode ? exitCode == 0 : null,
            LastTestPassed: lastTest is null ? null : lastTest.Result == TestResult.Passed,
            ActiveBlockerSeverities: [.. activeBlockers.Select(b => b.Severity)],
            IsDirty: snapshot.IsDirty,
            AheadBy: snapshot.AheadBy ?? 0,
            BehindBy: snapshot.BehindBy ?? 0));

        return new ProjectDashboardCard(
            ProjectId: project.Id,
            Name: project.Name,
            AccentOrIcon: project.Tags.Count > 0 ? project.Tags[0] : project.Name[..1].ToUpperInvariant(),
            RepositoryAvailable: snapshot.IsValid,
            BranchName: snapshot.BranchName,
            IsDirty: snapshot.IsDirty,
            AheadBy: snapshot.AheadBy,
            BehindBy: snapshot.BehindBy,
            Status: project.Status,
            CurrentFrontierTitle: frontierProgress.CurrentFrontierTitle,
            NextSafestStep: topBlocker?.NextSafestStep is { Length: > 0 } step ? step : project.NextSafestStep,
            MilestoneProgress: milestoneProgress,
            FrontierProgress: frontierProgress,
            ActiveBlockerCount: activeBlockers.Count,
            MonitoringBlockerCount: projectBlockers.Count(b => b.Status == BlockerStatus.Monitoring),
            HighestActiveSeverity: topBlocker?.Severity?.Trim().ToLowerInvariant(),
            LastBuildResult: lastBuild is null ? null : ToResult(lastBuild, "Build"),
            LastTestResult: lastTest is null ? null : ToResult(lastTest),
            LastLaunchResult: lastLaunch is null ? null : ToResult(lastLaunch, "Run"),
            RuntimeHealth: latestRuntime?.Health,
            LastActivityAt: LastActivity(project.Id),
            HealthAssessment: assessment,
            PrimaryAction: project.RunCommand is { Length: > 0 }
                ? DashboardPrimaryAction.RunOrStart
                : DashboardPrimaryAction.OpenProject);
    }

    public static ProjectDashboardSummary BuildSummary(IReadOnlyList<ProjectDashboardCard> cards) =>
        new(
            TotalProjects: cards.Count,
            HealthyCount: cards.Count(c => c.HealthAssessment.State == ProjectHealthState.Healthy),
            AttentionCount: cards.Count(c => c.HealthAssessment.State == ProjectHealthState.Attention),
            BlockedCount: cards.Count(c => c.HealthAssessment.State == ProjectHealthState.Blocked),
            FailingCount: cards.Count(c => c.HealthAssessment.State == ProjectHealthState.Failing),
            UnavailableCount: cards.Count(c => c.HealthAssessment.State == ProjectHealthState.Unavailable),
            UnknownCount: cards.Count(c => c.HealthAssessment.State == ProjectHealthState.Unknown),
            ActiveBlockerCount: cards.Sum(c => c.ActiveBlockerCount),
            RunningServices: cards.Count(c => c.RuntimeHealth is RuntimeHealth.Healthy or RuntimeHealth.Degraded),
            FailingServices: cards.Count(c => c.RuntimeHealth == RuntimeHealth.Failed),
            DirtyProjects: cards.Count(c => c.IsDirty));

    /// <summary>
    /// Timeline entries and mobile audit records merged newest-first and
    /// capped. Audit items are "Label — outcome" only; everything passes
    /// through <see cref="SecretRedactor"/>.
    /// </summary>
    public IReadOnlyList<DashboardActivityItem> BuildRecentActivity(IReadOnlyList<Project> allProjects, int limit)
    {
        var items = new List<DashboardActivityItem>();
        foreach (var project in allProjects)
        {
            foreach (var entry in timeline.GetForProject(project.Id, limit))
            {
                items.Add(new DashboardActivityItem(
                    entry.Timestamp, project.Id, project.Name,
                    entry.Category.ToString(), SecretRedactor.Redact(entry.Message)));
            }
        }

        var names = allProjects.ToDictionary(p => p.Id, p => p.Name);
        foreach (var record in audit.Query(new MobileActionAuditFilter { Limit = limit }))
        {
            items.Add(new DashboardActivityItem(
                record.TimestampUtc, record.ProjectId,
                names.TryGetValue(record.ProjectId, out var name) ? name : record.ProjectName,
                "Mobile action",
                SecretRedactor.Redact($"{record.ActionLabel} — {DescribeOutcome(record.State)}")));
        }

        return items.OrderByDescending(i => i.When).Take(limit).ToList();
    }

    private DateTimeOffset? LastActivity(long projectId)
    {
        DateTimeOffset? latest = timeline.GetForProject(projectId, limit: 1).FirstOrDefault()?.Timestamp;
        var auditLatest = audit.Query(new MobileActionAuditFilter { ProjectId = projectId, Limit = 1 })
            .FirstOrDefault()?.TimestampUtc;
        if (latest is null || auditLatest > latest)
            latest = auditLatest;
        return latest;
    }

    // Verdict and timestamp only — never the command or its output.
    private static DashboardExecutionResult ToResult(BuildRecord record, string kind) =>
        new(kind, record.ExitCode == 0, TimedOut: false, record.StartedAt);

    private static DashboardExecutionResult ToResult(TestRecord record) =>
        new("Test", record.Result == TestResult.Passed, record.Result == TestResult.TimedOut, record.StartedAt);

    private static string DescribeOutcome(MobileActionAuditState state) => state switch
    {
        MobileActionAuditState.Succeeded => "succeeded",
        MobileActionAuditState.Failed => "failed",
        MobileActionAuditState.TimedOut => "timed out",
        MobileActionAuditState.Cancelled => "cancelled",
        MobileActionAuditState.Blocked => "blocked",
        MobileActionAuditState.Rejected => "rejected",
        _ => "attempted",
    };

    private static int SeverityRank(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0,
    };
}
