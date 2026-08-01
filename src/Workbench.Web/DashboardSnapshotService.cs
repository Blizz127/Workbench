using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Web;

/// <summary>
/// Assembles the read-only dashboard snapshot from the same typed services
/// the desktop app uses — no command construction, no parsing, no process
/// execution of its own. Every journal line is funneled through
/// <see cref="SecretRedactor"/> before it can reach a phone.
/// </summary>
public sealed class DashboardSnapshotService(
    ISystemHealthService systemHealth,
    IProjectRepository projects,
    IGitService git,
    IDockerComposeService docker,
    ITmuxService tmux,
    ITimelineRepository timeline)
{
    /// <summary>Journal lines shown per project card — "recent logs where already available".</summary>
    private const int RecentActivityLimit = 3;

    public async Task<WebDashboardSnapshot> BuildAsync(CancellationToken cancellationToken = default)
    {
        var health = await systemHealth.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var tmuxStatus = await tmux.InspectAsync(cancellationToken).ConfigureAwait(false);

        var cards = new List<WebProjectCard>();
        foreach (var project in projects.GetAll())
            cards.Add(await BuildCardAsync(project, cancellationToken).ConfigureAwait(false));

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
            Tmux: new TmuxBlock(
                tmuxStatus.TmuxAvailable,
                tmuxStatus.ServerRunning,
                tmuxStatus.Error,
                tmuxStatus.Sessions
                    .Select(s => new WebTmuxSession(s.Name, s.IsAttached, s.WindowCount, s.ActiveWindowName))
                    .ToList()),
            Projects: cards);
    }

    private async Task<WebProjectCard> BuildCardAsync(Project project, CancellationToken cancellationToken)
    {
        var snapshot = git.GetSnapshot(project.RepositoryPath, recentCommitCount: 1);
        var gitText = !snapshot.IsValid
            ? "no repository"
            : (snapshot.BranchName ?? "?")
              + (snapshot.IsDirty ? " *" : "")
              + (snapshot.AheadBy is int ahead and > 0 ? $" ↑{ahead}" : "")
              + (snapshot.BehindBy is int behind and > 0 ? $" ↓{behind}" : "")
              + (snapshot.LastCommit is { } commit ? $" · {commit.ShortSha} {commit.MessageSummary}" : "");

        ComposeProjectStatus? compose = null;
        if (docker.ResolveComposeFile(project) is not null)
            compose = await docker.InspectAsync(project, cancellationToken).ConfigureAwait(false);

        return new WebProjectCard(
            Name: project.Name,
            Status: project.Status.ToString(),
            GitText: gitText,
            GitDirty: snapshot.IsValid && snapshot.IsDirty,
            GitError: snapshot.IsValid ? null : snapshot.Error,
            DockerSummary: compose?.Error is null ? compose?.Summary : null,
            Services: compose?.Services
                .Select(s => new WebComposeService(
                    s.Name,
                    s.State.ToString().ToLowerInvariant(),
                    s.Health is ComposeHealth.None ? null : s.Health.ToString().ToLowerInvariant()))
                .ToList() ?? (IReadOnlyList<WebComposeService>)[],
            DockerError: compose?.Error,
            RecentActivity: timeline.GetForProject(project.Id, RecentActivityLimit)
                .Select(e => new WebActivityEntry(
                    e.Timestamp,
                    e.Category.ToString(),
                    SecretRedactor.Redact(e.Message),
                    e.Detail is null ? null : SecretRedactor.Redact(e.Detail)))
                .ToList());
    }

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
