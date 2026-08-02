namespace Workbench.Core.Models;

/// <summary>What the card's main button does.</summary>
public enum DashboardPrimaryAction
{
    /// <summary>The project has a launch profile (run command) — offer to run/start it.</summary>
    RunOrStart,

    /// <summary>No launch profile — the button just opens the project.</summary>
    OpenProject,
}

/// <summary>
/// Outcome of the latest build/test/launch, safe for any UI: kind, verdict,
/// and when — NEVER output text or commands.
/// </summary>
public sealed record DashboardExecutionResult(
    /// <summary>"Build", "Test", or "Run".</summary>
    string Kind,
    bool Succeeded,
    bool TimedOut,
    DateTimeOffset When);

/// <summary>
/// One line of the merged recent-activity stream (timeline entries + mobile
/// action audit records), newest first. Messages are secret-redacted and
/// carry no raw command output.
/// </summary>
public sealed record DashboardActivityItem(
    DateTimeOffset When,
    long ProjectId,
    string ProjectName,
    string Category,
    string Message);

/// <summary>
/// The shared dashboard projection both the Avalonia app and the web
/// dashboard consume. Everything a card needs, pre-computed; no secrets and
/// no raw command output anywhere.
/// </summary>
public sealed record ProjectDashboardCard(
    long ProjectId,
    string Name,
    /// <summary>Display hint from the project's tags, or its initial when untagged.</summary>
    string AccentOrIcon,
    bool RepositoryAvailable,
    string? BranchName,
    bool IsDirty,
    int? AheadBy,
    int? BehindBy,
    ProjectStatus Status,
    string? CurrentFrontierTitle,
    /// <summary>Top active blocker's next step, else the project's own.</summary>
    string? NextSafestStep,
    /// <summary>Null means "progress not configured" — never an implied 0%.</summary>
    MilestoneProgress? MilestoneProgress,
    FrontierProgress FrontierProgress,
    int ActiveBlockerCount,
    int MonitoringBlockerCount,
    /// <summary>Highest severity among ACTIVE blockers, normalized lowercase; null when none.</summary>
    string? HighestActiveSeverity,
    DashboardExecutionResult? LastBuildResult,
    DashboardExecutionResult? LastTestResult,
    DashboardExecutionResult? LastLaunchResult,
    RuntimeHealth? RuntimeHealth,
    DateTimeOffset? LastActivityAt,
    ProjectHealthAssessment HealthAssessment,
    DashboardPrimaryAction PrimaryAction);

/// <summary>Fleet-wide totals for the dashboard header.</summary>
public sealed record ProjectDashboardSummary(
    int TotalProjects,
    int HealthyCount,
    int AttentionCount,
    int BlockedCount,
    int FailingCount,
    int UnavailableCount,
    int UnknownCount,
    int ActiveBlockerCount,
    /// <summary>Projects whose latest runtime verdict is Healthy or Degraded.</summary>
    int RunningServices,
    /// <summary>Projects whose latest runtime verdict is Failed.</summary>
    int FailingServices,
    int DirtyProjects);

/// <summary>Everything a dashboard (desktop or web) renders in one shot.</summary>
public sealed record DashboardProjection(
    IReadOnlyList<ProjectDashboardCard> Cards,
    ProjectDashboardSummary Summary,
    IReadOnlyList<DashboardActivityItem> RecentActivity);
