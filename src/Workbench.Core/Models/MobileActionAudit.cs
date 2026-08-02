namespace Workbench.Core.Models;

/// <summary>
/// Lifecycle states of one mobile action, as persisted in the audit trail.
/// Stored lowercase (<see cref="TimedOut"/> as "timedOut") to match the
/// mobile-facing vocabulary.
/// </summary>
public enum MobileActionAuditState
{
    /// <summary>The phone asked for the action; execution is starting.</summary>
    Attempted,

    /// <summary>A known action that could not run right now (guard, lock, unconfigured).</summary>
    Blocked,

    /// <summary>Not on the project's allowlist (or confirmation missing) — refused outright.</summary>
    Rejected,

    /// <summary>Execution is underway (reserved; transitions are recorded at start and end).</summary>
    Running,

    /// <summary>The action ran and reported success.</summary>
    Succeeded,

    /// <summary>The action ran and reported failure.</summary>
    Failed,

    /// <summary>The caller's cancellation token aborted the run.</summary>
    Cancelled,

    /// <summary>The underlying command hit its timeout.</summary>
    TimedOut,
}

/// <summary>
/// One persisted audit row for a mobile action state transition. Append-only:
/// one row per transition, transitions of one execution share a correlation
/// id. Command previews and output summaries are secret-redacted BEFORE they
/// land here; the session is stored as a non-reversible hash, never the raw
/// session id. There is intentionally no project foreign key — audit records
/// must survive project deletion, so the project name is snapshotted.
/// </summary>
public sealed class MobileActionAuditRecord
{
    public long Id { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public long ProjectId { get; set; }

    /// <summary>Project name at the time of the action (snapshot).</summary>
    public string ProjectName { get; set; } = string.Empty;

    public string ActionId { get; set; } = string.Empty;

    /// <summary>Action label at the time of the action (snapshot).</summary>
    public string ActionLabel { get; set; } = string.Empty;

    public string ActionType { get; set; } = string.Empty;

    /// <summary>The effective safety level, e.g. "requiresDoubleConfirmation".</summary>
    public string Safety { get; set; } = string.Empty;

    public MobileActionAuditState State { get; set; }

    /// <summary>SHA-256 hex of the session id — non-reversible, never the raw id.</summary>
    public string? SessionHash { get; set; }

    /// <summary>The exact command shown on the confirm page, secret-redacted.</summary>
    public string? CommandPreview { get; set; }

    /// <summary>Secret-redacted output, truncated to the service's limit.</summary>
    public string? OutputSummary { get; set; }

    public bool OutputTruncated { get; set; }

    public int? ExitCode { get; set; }

    public long? DurationMs { get; set; }

    /// <summary>Why a blocked/rejected/failed action did not succeed.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Names of the services the action touched.</summary>
    public IReadOnlyList<string> Services { get; set; } = [];

    /// <summary>Where the action came from; "web" for the mobile dashboard.</summary>
    public string Source { get; set; } = "web";

    /// <summary>Links the attempted/terminal rows of one execution.</summary>
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Optional, combinable filters for audit queries. Null/empty fields match
/// everything; results are always newest first.
/// </summary>
public sealed class MobileActionAuditFilter
{
    public long? ProjectId { get; set; }

    public string? ActionId { get; set; }

    public MobileActionAuditState? State { get; set; }

    /// <summary>Inclusive lower bound on the UTC timestamp.</summary>
    public DateTimeOffset? FromUtc { get; set; }

    /// <summary>Inclusive upper bound on the UTC timestamp.</summary>
    public DateTimeOffset? ToUtc { get; set; }

    public int Limit { get; set; } = 100;
}
