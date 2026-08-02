using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Web;

/// <summary>
/// Assembles the read-only dashboard snapshots from the same typed services
/// the desktop app uses — no command construction, no parsing, no process
/// execution of its own. Every journal line is funneled through
/// <see cref="SecretRedactor"/> before it can reach a phone.
///
/// Phones poll the JSON endpoints frequently, so each subsystem is cached
/// with its own TTL: system health 10s, Docker and tmux 15s, git 30s. Within
/// a TTL the previous value is served, which caps process-spawning and
/// repository-walking load no matter how many browsers are open. Builds are
/// serialized through one gate so concurrent polls never duplicate work.
/// </summary>
public sealed class DashboardSnapshotService(
    ISystemHealthService systemHealth,
    IProjectRepository projects,
    IGitService git,
    IDockerComposeService docker,
    ITmuxService tmux,
    ITimelineRepository timeline,
    IBlockerRepository blockers)
{
    internal static readonly TimeSpan HealthTtl = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan DockerTtl = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan TmuxTtl = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan GitTtl = TimeSpan.FromSeconds(30);

    /// <summary>Journal lines on the detail page — "recent logs where already available".</summary>
    private const int RecentActivityLimit = 10;
    private const int RecentCommitLimit = 8;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private (DateTimeOffset At, SystemHealthSnapshot Value)? _health;
    private (DateTimeOffset At, TmuxServerStatus Value)? _tmux;
    private readonly Dictionary<long, (DateTimeOffset At, RepositorySnapshot Value)> _gitCache = new();
    private readonly Dictionary<long, (DateTimeOffset At, ComposeProjectStatus? Value)> _dockerCache = new();

    public async Task<WebDashboardSnapshot> BuildAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
            var tmuxStatus = await GetTmuxAsync(cancellationToken).ConfigureAwait(false);

            var cards = new List<WebProjectCard>();
            foreach (var project in projects.GetAll())
                cards.Add(await BuildCardAsync(project, tmuxStatus, cancellationToken).ConfigureAwait(false));

            return new WebDashboardSnapshot(
                GeneratedAt: DateTimeOffset.Now,
                System: new SystemHealthBlock(
                    CpuText: $"{health.CpuUsagePercent:F0}%",
                    CpuPercent: health.CpuUsagePercent,
                    MemoryText: $"{FormatBytes(health.UsedMemoryBytes)} / {FormatBytes(health.TotalMemoryBytes)}",
                    MemoryPercent: health.MemoryUsagePercent,
                    DiskText: $"{FormatBytes(health.UsedDiskBytes)} / {FormatBytes(health.TotalDiskBytes)}",
                    DiskPercent: health.DiskUsagePercent,
                    UptimeText: FormatUptime(health.Uptime)),
                Tmux: MapTmux(tmuxStatus),
                Projects: cards);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>One project's detail snapshot, or null when the id is unknown (→ 404).</summary>
    public async Task<ProjectDetailSnapshot?> BuildProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var project = projects.GetById(projectId);
            if (project is null)
                return null;

            var snapshot = await GetGitAsync(project, cancellationToken).ConfigureAwait(false);
            var compose = await GetDockerAsync(project, cancellationToken).ConfigureAwait(false);
            var tmuxStatus = await GetTmuxAsync(cancellationToken).ConfigureAwait(false);
            var sessionName = tmux.ProjectSessionName(project);
            var session = tmuxStatus.Sessions.FirstOrDefault(
                s => string.Equals(s.Name, sessionName, StringComparison.Ordinal));

            return new ProjectDetailSnapshot(
                Id: project.Id,
                Name: project.Name,
                Status: project.Status.ToString(),
                RepositoryPath: project.RepositoryPath,
                GeneratedAt: DateTimeOffset.Now,
                Git: new GitDetailBlock(
                    Available: snapshot.IsValid,
                    Error: snapshot.IsValid ? null : snapshot.Error,
                    Branch: snapshot.BranchName,
                    Dirty: snapshot.IsValid && snapshot.IsDirty,
                    DirtyCount: snapshot.IsValid ? snapshot.ModifiedFiles.Count : 0,
                    AheadBy: snapshot.AheadBy,
                    BehindBy: snapshot.BehindBy,
                    LastCommitText: snapshot.LastCommit is { } commit
                        ? $"{commit.ShortSha} · {commit.MessageSummary} ({commit.When:MM-dd HH:mm})"
                        : null,
                    RecentCommits: snapshot.RecentCommits.Take(RecentCommitLimit)
                        .Select(c => new WebCommit(c.ShortSha, c.MessageSummary, c.When.ToString("MM-dd HH:mm")))
                        .ToList()),
                Docker: new DockerDetailBlock(
                    HasComposeFile: docker.ResolveComposeFile(project) is not null,
                    Summary: compose?.Error is null ? compose?.Summary : null,
                    Error: compose?.Error,
                    Services: MapServices(compose)),
                Session: new ProjectSessionBlock(
                    SessionName: sessionName,
                    TmuxAvailable: tmuxStatus.TmuxAvailable,
                    ServerRunning: tmuxStatus.ServerRunning,
                    Exists: session is not null,
                    IsAttached: session?.IsAttached ?? false,
                    WindowCount: session?.WindowCount ?? 0,
                    ActiveWindowName: session?.ActiveWindowName),
                Blockers: MapBlockers(project.Id),
                RecentActivity: timeline.GetForProject(project.Id, RecentActivityLimit)
                    .Select(e => new WebActivityEntry(
                        e.Timestamp.ToString("MM-dd HH:mm"),
                        e.Category.ToString(),
                        SecretRedactor.Redact(e.Message),
                        e.Detail is null ? null : SecretRedactor.Redact(e.Detail)))
                    .ToList());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops every cached subsystem value so the next poll rebuilds from the
    /// source. Used by the mobile refreshStatus action; never throws.
    /// </summary>
    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _health = null;
            _tmux = null;
            _gitCache.Clear();
            _dockerCache.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WebProjectCard> BuildCardAsync(
        Project project, TmuxServerStatus tmuxStatus, CancellationToken cancellationToken)
    {
        var snapshot = await GetGitAsync(project, cancellationToken).ConfigureAwait(false);
        var compose = await GetDockerAsync(project, cancellationToken).ConfigureAwait(false);

        var repoMissing = project.RepositoryPath is not { Length: > 0 } path || !Directory.Exists(path);
        var gitText = repoMissing
            ? "repo missing"
            : !snapshot.IsValid
                ? "no repository"
                : (snapshot.BranchName ?? "?")
                  + (snapshot.IsDirty ? " *" : "")
                  + (snapshot.AheadBy is int ahead and > 0 ? $" ↑{ahead}" : "")
                  + (snapshot.BehindBy is int behind and > 0 ? $" ↓{behind}" : "")
                  + (snapshot.LastCommit is { } commit ? $" · {commit.ShortSha} {commit.MessageSummary}" : "");

        var sessionName = tmux.ProjectSessionName(project);
        var session = tmuxStatus.Sessions.FirstOrDefault(
            s => string.Equals(s.Name, sessionName, StringComparison.Ordinal));
        var tmuxText = !tmuxStatus.TmuxAvailable || !tmuxStatus.ServerRunning
            ? null
            : session is null
                ? "no session"
                : session.IsAttached ? "attached" : "detached";

        return new WebProjectCard(
            Id: project.Id,
            Name: project.Name,
            Status: project.Status.ToString(),
            GitText: gitText,
            GitDirty: !repoMissing && snapshot.IsValid && snapshot.IsDirty,
            GitUnavailable: repoMissing,
            DockerSummary: compose?.Error is null ? compose?.Summary : null,
            TmuxText: tmuxText);
    }

    private async Task<SystemHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken)
    {
        if (_health is { } cached && IsFresh(cached.At, HealthTtl))
            return cached.Value;

        var value = await systemHealth.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        _health = (DateTimeOffset.UtcNow, value);
        return value;
    }

    private async Task<TmuxServerStatus> GetTmuxAsync(CancellationToken cancellationToken)
    {
        if (_tmux is { } cached && IsFresh(cached.At, TmuxTtl))
            return cached.Value;

        var value = await tmux.InspectAsync(cancellationToken).ConfigureAwait(false);
        _tmux = (DateTimeOffset.UtcNow, value);
        return value;
    }

    private Task<RepositorySnapshot> GetGitAsync(Project project, CancellationToken cancellationToken)
    {
        if (_gitCache.TryGetValue(project.Id, out var cached) && IsFresh(cached.At, GitTtl))
            return Task.FromResult(cached.Value);

        // One snapshot serves both the card (HEAD line) and the detail page (recent commits).
        var value = git.GetSnapshot(project.RepositoryPath, RecentCommitLimit);
        _gitCache[project.Id] = (DateTimeOffset.UtcNow, value);
        return Task.FromResult(value);
    }

    private async Task<ComposeProjectStatus?> GetDockerAsync(Project project, CancellationToken cancellationToken)
    {
        if (_dockerCache.TryGetValue(project.Id, out var cached) && IsFresh(cached.At, DockerTtl))
            return cached.Value;

        var value = docker.ResolveComposeFile(project) is null
            ? null
            : await docker.InspectAsync(project, cancellationToken).ConfigureAwait(false);
        _dockerCache[project.Id] = (DateTimeOffset.UtcNow, value);
        return value;
    }

    /// <summary>
    /// Counts by lifecycle status plus the open records. Free-form fields go
    /// through the same redactor as the journal before they can reach a phone.
    /// </summary>
    private BlockerSummaryBlock MapBlockers(long projectId)
    {
        var all = blockers.GetForProject(projectId);
        return new BlockerSummaryBlock(
            Active: all.Count(b => b.Status == BlockerStatus.Active),
            Monitoring: all.Count(b => b.Status == BlockerStatus.Monitoring),
            Resolved: all.Count(b => b.Status == BlockerStatus.Resolved),
            Dismissed: all.Count(b => b.Status == BlockerStatus.Dismissed),
            Open: all
                .Where(b => b.Status is BlockerStatus.Active or BlockerStatus.Monitoring)
                .Select(b => new WebBlockerEntry(
                    b.TitleOrDescription,
                    b.Severity,
                    b.Status.ToString(),
                    b.Evidence is null ? null : SecretRedactor.Redact(b.Evidence),
                    b.NextSafestStep is null ? null : SecretRedactor.Redact(b.NextSafestStep),
                    b.Owner))
                .ToList());
    }

    private static bool IsFresh(DateTimeOffset at, TimeSpan ttl) =>
        DateTimeOffset.UtcNow - at < ttl;

    private static TmuxBlock MapTmux(TmuxServerStatus status) =>
        new(status.TmuxAvailable, status.ServerRunning, status.Error,
            status.Sessions
                .Select(s => new WebTmuxSession(s.Name, s.IsAttached, s.WindowCount, s.ActiveWindowName))
                .ToList());

    private static IReadOnlyList<WebComposeService> MapServices(ComposeProjectStatus? compose) =>
        compose?.Services
            .Select(s => new WebComposeService(
                s.Name,
                s.State.ToString().ToLowerInvariant(),
                s.Health is ComposeHealth.None ? null : s.Health.ToString().ToLowerInvariant()))
            .ToList() ?? (IReadOnlyList<WebComposeService>)[];

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
            : uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                : $"{uptime.Minutes}m";
}
