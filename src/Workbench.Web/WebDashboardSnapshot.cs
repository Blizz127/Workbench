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
    string? TmuxText);

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
