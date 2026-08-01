namespace Workbench.Web;

/// <summary>
/// Everything the mobile dashboard renders, assembled by
/// <see cref="DashboardSnapshotService"/> from the shared typed services.
/// Display strings are precomputed here — the renderer and JSON endpoint stay
/// free of parsing or formatting logic.
/// </summary>
public sealed record WebDashboardSnapshot(
    DateTimeOffset GeneratedAt,
    SystemHealthBlock System,
    TmuxBlock Tmux,
    IReadOnlyList<WebProjectCard> Projects);

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

/// <summary>One project card: git, docker, and recent journal — all read-only.</summary>
public sealed record WebProjectCard(
    string Name,
    string Status,
    string GitText,
    bool GitDirty,
    string? GitError,
    string? DockerSummary,
    IReadOnlyList<WebComposeService> Services,
    string? DockerError,
    IReadOnlyList<WebActivityEntry> RecentActivity);

public sealed record WebComposeService(
    string Name,
    string StateText,
    string? HealthText);

/// <summary>One recent journal line. Message and Detail are secret-redacted.</summary>
public sealed record WebActivityEntry(
    DateTimeOffset Timestamp,
    string Category,
    string Message,
    string? Detail);
