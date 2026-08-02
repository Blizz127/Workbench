namespace Workbench.Web;

/// <summary>
/// Typed snapshot for one project's detail page — the same typed services as
/// the main dashboard, deeper views: recent commits, full service rows, the
/// project's tmux session, and its recent journal.
/// </summary>
public sealed record ProjectDetailSnapshot(
    long Id,
    string Name,
    string Status,
    string? RepositoryPath,
    DateTimeOffset GeneratedAt,
    GitDetailBlock Git,
    DockerDetailBlock Docker,
    ProjectSessionBlock Session,
    BlockerSummaryBlock Blockers,
    IReadOnlyList<WebActivityEntry> RecentActivity);

/// <summary>
/// Read-only blocker summary for the detail page: counts by lifecycle status
/// plus the open (active/monitoring) records with their evidence and next
/// safest step. Resolved/dismissed blockers are counts only.
/// </summary>
public sealed record BlockerSummaryBlock(
    int Active,
    int Monitoring,
    int Resolved,
    int Dismissed,
    IReadOnlyList<WebBlockerEntry> Open);

public sealed record WebBlockerEntry(
    string Title,
    string? Severity,
    string Status,
    string? Evidence,
    string? NextSafestStep,
    string? Owner);

public sealed record GitDetailBlock(
    bool Available,
    string? Error,
    string? Branch,
    bool Dirty,
    int DirtyCount,
    int? AheadBy,
    int? BehindBy,

    /// <summary>"short-sha · subject (MM-dd HH:mm)" of HEAD, or null with no commits.</summary>
    string? LastCommitText,
    IReadOnlyList<WebCommit> RecentCommits);

public sealed record WebCommit(
    string ShortSha,
    string Subject,
    string When);

public sealed record DockerDetailBlock(
    bool HasComposeFile,
    string? Summary,
    string? Error,
    IReadOnlyList<WebComposeService> Services);

/// <summary>The project's own tmux session (explicit name or synthesized slug) and its live state.</summary>
public sealed record ProjectSessionBlock(
    string SessionName,
    bool TmuxAvailable,
    bool ServerRunning,
    bool Exists,
    bool IsAttached,
    int WindowCount,
    string? ActiveWindowName);
