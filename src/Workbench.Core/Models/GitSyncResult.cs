namespace Workbench.Core.Models;

public enum GitSyncStatus
{
    /// <summary>The git command ran and completed successfully.</summary>
    Succeeded,

    /// <summary>A safety precondition failed; no git command was run.</summary>
    Blocked,

    /// <summary>The git command ran but returned a non-zero exit code.</summary>
    Failed,
}

/// <summary>
/// Outcome of one fetch/pull/push attempt: what ran (or didn't), what
/// happened, and the git state before and after — everything the timeline
/// entry and the UI need. <see cref="BlockReason"/> is set only when
/// <see cref="Status"/> is <see cref="GitSyncStatus.Blocked"/>;
/// <see cref="Warning"/> can be set alongside a Blocked or Succeeded result
/// (e.g. "pushing with uncommitted changes present"); <see cref="ErrorMessage"/>
/// is set only when <see cref="Status"/> is <see cref="GitSyncStatus.Failed"/>.
/// </summary>
public sealed record GitSyncResult(
    string Command,
    GitSyncStatus Status,
    string? BlockReason,
    string? Warning,
    string? ErrorMessage,
    string Output,
    string? Branch,
    string? HeadShaBefore,
    string? HeadShaAfter,
    int? AheadByBefore,
    int? BehindByBefore,
    int? AheadByAfter,
    int? BehindByAfter,
    DateTimeOffset StartedAt,
    TimeSpan Duration);
