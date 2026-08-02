namespace Workbench.Web;

/// <summary>
/// Everything the main dashboard renders, assembled by
/// <see cref="DashboardSnapshotService"/> from the shared typed services.
/// Display strings are precomputed here — the renderer, JSON endpoint, and
/// client-side refresher stay free of parsing or formatting logic.
/// </summary>
public sealed record WebDashboardSnapshot(
    DateTimeOffset GeneratedAt,
    SystemHealthBlock System,
    TmuxBlock Tmux,

    /// <summary>Fleet-wide totals from the shared projection (same semantics as the desktop summary bar).</summary>
    WebSummaryBlock Summary,
    IReadOnlyList<WebProjectCard> Projects);

/// <summary>
/// The shared <see cref="Workbench.Core.Models.ProjectDashboardSummary"/>
/// flattened for the page — one compact text+count bar, no cards.
/// </summary>
public sealed record WebSummaryBlock(
    int TotalProjects,
    int HealthyCount,
    int AttentionCount,
    int BlockedCount,
    int FailingCount,
    int UnavailableCount,
    int UnknownCount,
    int ActiveBlockerCount,
    int RunningServices,
    int FailingServices,
    int DirtyProjects);

public sealed record SystemHealthBlock(
    string CpuText,
    double CpuPercent,
    string MemoryText,
    double MemoryPercent,
    string DiskText,
    double DiskPercent,
    string UptimeText);

public sealed record TmuxBlock(
    bool TmuxAvailable,
    bool ServerRunning,
    string? Error,
    IReadOnlyList<WebTmuxSession> Sessions);

public sealed record WebTmuxSession(
    string Name,
    bool IsAttached,
    int WindowCount,
    string? ActiveWindowName);

/// <summary>
/// One compact project card on the main dashboard — summaries only; the full
/// service/commit/activity lists live on the detail page it links to.
/// </summary>
public sealed record WebProjectCard(
    long Id,
    string Name,
    string Status,
    string GitText,
    bool GitDirty,

    /// <summary>The repository path does not exist on this machine — the card
    /// is shown as unavailable, never silently dropped or deleted.</summary>
    bool GitUnavailable,
    string? DockerSummary,

    /// <summary>The project's tmux session summary ("attached", "detached",
    /// "no session"), or null when tmux can't say.</summary>
    string? TmuxText,

    // --- Shared projection fields (DashboardProjectionBuilder semantics) ---

    /// <summary>Health verdict as TEXT — the badge never relies on color alone.</summary>
    string HealthText = "Unknown",

    /// <summary>Badge CSS class for the health verdict: ok / warn / bad / "" (neutral).</summary>
    string HealthBadgeClass = "",

    /// <summary>Every health reason that fired, joined for the badge's tooltip.</summary>
    string? HealthReasonsText = null,

    /// <summary>"3/5 milestones · 60%"; null means "Progress not configured" — never an implied 0%.</summary>
    string? ProgressText = null,

    /// <summary>Weighted milestone percent (0 when unconfigured; the bar hides then anyway).</summary>
    double ProgressPercent = 0,

    /// <summary>"1/2 frontiers · current title"; null means no frontier set.</summary>
    string? FrontierText = null,

    /// <summary>"2 active · highest critical"; null means no active blockers.</summary>
    string? BlockerText = null,

    /// <summary>"build OK · test FAIL · run OK" (verdicts only, no output); null when nothing ever ran.</summary>
    string? ResultsText = null,

    /// <summary>Last timeline/audit activity, formatted; null when none.</summary>
    string? LastActivityText = null,

    /// <summary>True only when the project's mobile allowlist has entries — gates the Actions link.</summary>
    bool HasMobileActions = false,

    /// <summary>The shared primary-action verdict ("RunOrStart" when a launch profile exists).</summary>
    string PrimaryAction = "OpenProject");

public sealed record WebComposeService(
    string Name,
    string StateText,
    string? HealthText);

/// <summary>One recent journal line. Message and Detail are secret-redacted.</summary>
public sealed record WebActivityEntry(
    string When,
    string Category,
    string Message,
    string? Detail);
