using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Explicit, user-initiated git synchronization: fetch, fast-forward-only
/// pull, and push. Every write operation is preceded by a safety check that
/// runs before any git command executes; if a precondition fails, the git
/// command is never invoked and the result is <see cref="GitSyncStatus.Blocked"/>
/// with a plain-English reason. Commands run through <see cref="IProcessRunner"/>
/// against the user's own <c>git</c> installation — reusing whatever
/// credential helper, SSH agent, or cached token they already have configured
/// — rather than reimplementing authentication.
///
/// Never auto-commits, auto-stashes, auto-merges, auto-rebases, force-pushes,
/// or creates remotes/branches. Every method here corresponds to exactly one
/// explicit button in the UI; nothing here is called automatically.
/// </summary>
public interface IGitSyncService
{
    Task<GitSyncResult> FetchAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>git pull --ff-only</c> only if the working tree is clean and
    /// the branch is behind without having diverged. Otherwise returns a
    /// <see cref="GitSyncStatus.Blocked"/> result and never touches the repo.
    /// </summary>
    Task<GitSyncResult> PullFastForwardAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>git push</c> only if an upstream is configured and the branch
    /// is ahead. A dirty working tree does not block the push — only
    /// committed changes are ever pushed — but is surfaced as a
    /// <see cref="GitSyncResult.Warning"/> so the user isn't surprised by
    /// what didn't go along for the ride.
    /// </summary>
    Task<GitSyncResult> PushAsync(Project project, CancellationToken cancellationToken = default);
}

public sealed class GitSyncService(IGitService git, ITimelineRepository timeline, IProcessRunner runner)
    : IGitSyncService
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);

    public async Task<GitSyncResult> FetchAsync(Project project, CancellationToken cancellationToken = default)
    {
        const string command = "git fetch --all --prune";
        var before = Snapshot(project);

        if (!before.IsValid)
            return Journal(project, Blocked(command, before, "Repository is not valid."));

        var process = await runner.RunAsync(command, project.RepositoryPath, NetworkTimeout, cancellationToken)
            .ConfigureAwait(false);
        var after = Snapshot(project);

        return Journal(project, FromProcessResult(command, process, before, after, warning: null));
    }

    public async Task<GitSyncResult> PullFastForwardAsync(Project project, CancellationToken cancellationToken = default)
    {
        const string command = "git pull --ff-only";
        var before = Snapshot(project);

        if (!before.IsValid)
            return Journal(project, Blocked(command, before, "Repository is not valid."));

        if (before.AheadBy is null || before.BehindBy is null)
        {
            return Journal(project, Blocked(command, before,
                "No upstream tracking branch is configured for this branch."));
        }

        if (before.IsDirty)
        {
            return Journal(project, Blocked(command, before,
                "Working tree has uncommitted changes. Commit or discard them before pulling."));
        }

        if (before.AheadBy > 0 && before.BehindBy > 0)
        {
            return Journal(project, Blocked(command, before,
                "Branch has diverged from upstream. Resolve manually (merge or rebase) — " +
                "Workbench will not do this automatically."));
        }

        if (before.BehindBy == 0)
            return Journal(project, Blocked(command, before, "Already up to date — nothing to pull."));

        var process = await runner.RunAsync("git pull --ff-only", project.RepositoryPath, NetworkTimeout, cancellationToken)
            .ConfigureAwait(false);
        var after = Snapshot(project);

        return Journal(project, FromProcessResult(command, process, before, after, warning: null));
    }

    public async Task<GitSyncResult> PushAsync(Project project, CancellationToken cancellationToken = default)
    {
        const string command = "git push";
        var before = Snapshot(project);

        if (!before.IsValid)
            return Journal(project, Blocked(command, before, "Repository is not valid."));

        if (before.AheadBy is null)
        {
            return Journal(project, Blocked(command, before,
                "No upstream is configured for this branch. Set one manually once, " +
                "e.g. `git push -u origin <branch>` — Workbench will not create it for you."));
        }

        if (before.AheadBy == 0)
            return Journal(project, Blocked(command, before, "Nothing to push — branch is not ahead of upstream."));

        var warning = before.IsDirty
            ? "Working tree has uncommitted changes. Only committed changes will be pushed."
            : null;

        var process = await runner.RunAsync("git push", project.RepositoryPath, NetworkTimeout, cancellationToken)
            .ConfigureAwait(false);
        var after = Snapshot(project);

        return Journal(project, FromProcessResult(command, process, before, after, warning));
    }

    private RepositorySnapshot Snapshot(Project project) => git.GetSnapshot(project.RepositoryPath, recentCommitCount: 0);

    private static GitSyncResult Blocked(string command, RepositorySnapshot state, string reason) => new(
        command, GitSyncStatus.Blocked, reason, Warning: null, ErrorMessage: null, Output: string.Empty,
        Branch: state.BranchName, HeadShaBefore: state.LastCommit?.Sha, HeadShaAfter: state.LastCommit?.Sha,
        AheadByBefore: state.AheadBy, BehindByBefore: state.BehindBy,
        AheadByAfter: state.AheadBy, BehindByAfter: state.BehindBy,
        StartedAt: DateTimeOffset.Now, Duration: TimeSpan.Zero);

    private static GitSyncResult FromProcessResult(
        string command, ProcessResult process, RepositorySnapshot before, RepositorySnapshot after, string? warning)
    {
        var status = process.ExitCode == 0 ? GitSyncStatus.Succeeded : GitSyncStatus.Failed;
        return new GitSyncResult(
            command, status,
            BlockReason: null,
            Warning: warning,
            ErrorMessage: status == GitSyncStatus.Failed ? Tail(process.Output) : null,
            Output: process.Output,
            Branch: before.BranchName,
            HeadShaBefore: before.LastCommit?.Sha,
            HeadShaAfter: after.LastCommit?.Sha,
            AheadByBefore: before.AheadBy,
            BehindByBefore: before.BehindBy,
            AheadByAfter: after.AheadBy,
            BehindByAfter: after.BehindBy,
            StartedAt: process.StartedAt,
            Duration: process.Duration);
    }

    private GitSyncResult Journal(Project project, GitSyncResult result)
    {
        timeline.Add(new TimelineEntry
        {
            ProjectId = project.Id,
            Timestamp = result.StartedAt,
            Category = TimelineCategory.GitSync,
            Message = BuildMessage(result),
            Detail = result.Status == GitSyncStatus.Failed ? result.ErrorMessage : null,
        });

        return result;
    }

    private static string BuildMessage(GitSyncResult result)
    {
        var trackingSuffix = result.AheadByAfter is { } ahead && result.BehindByAfter is { } behind
            ? $" ({ahead} ahead / {behind} behind)"
            : string.Empty;

        return result.Status switch
        {
            GitSyncStatus.Blocked => $"{result.Command} blocked: {result.BlockReason}",
            GitSyncStatus.Failed => $"{result.Command} failed (exit code non-zero)",
            _ when result.Warning is { } warning => $"{result.Command} succeeded{trackingSuffix} — {warning}",
            _ => $"{result.Command} succeeded{trackingSuffix}",
        };
    }

    private static string Tail(string output, int maxChars = 2000) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];
}
