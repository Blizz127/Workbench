namespace Workbench.Web;

/// <summary>How dangerous an action is; drives the confirmation flow.</summary>
public enum MobileActionSafety
{
    /// <summary>No side effects (logs, cache refresh, attach hint).</summary>
    ReadOnly,

    /// <summary>One explicit Confirm tap before anything runs.</summary>
    RequiresConfirmation,

    /// <summary>Two explicit taps on two separate pages before anything runs.</summary>
    RequiresDoubleConfirmation,
}

/// <summary>One allowlisted action as shown on the phone.</summary>
public sealed record MobileActionDescriptor(
    string Id,
    string Label,
    string Type,
    MobileActionSafety Safety,
    string? Service,
    string? CommandId,
    /// <summary>Set when the action is listed but cannot run (unknown type
    /// fails closed). Disabled actions render without a link.</summary>
    string? DisabledReason = null)
{
    public bool IsReadOnly => Safety is MobileActionSafety.ReadOnly;
    public bool IsMutating => Safety is not MobileActionSafety.ReadOnly;
}

/// <summary>
/// Everything the confirmation page needs: the exact command text that will
/// run, the safety level, and — when the action cannot run at all — the
/// reason it is blocked (no form is rendered then).
/// </summary>
public sealed record MobileActionPreview(
    long ProjectId,
    string ProjectName,
    MobileActionDescriptor Action,
    string CommandPreview,
    MobileActionSafety Safety,
    string? BlockReason)
{
    public bool IsExecutable => BlockReason is null;
}

/// <summary>Outcome of one execution attempt. <see cref="Output"/> is secret-redacted.</summary>
public sealed record MobileActionExecution(
    string Id,
    MobileActionDescriptor Action,
    bool Succeeded,
    bool Blocked,
    int? ExitCode,
    TimeSpan Duration,
    string Output,
    DateTimeOffset StartedAt);

/// <summary>What the journal recorded for one action attempt.</summary>
public enum ActionOutcome
{
    /// <summary>The phone asked for the action (every request is journaled first).</summary>
    Attempted,

    /// <summary>A known action that could not run right now (guard, lock, unconfigured).</summary>
    Blocked,

    /// <summary>The action ran and reported success.</summary>
    Succeeded,

    /// <summary>The action ran and reported failure.</summary>
    Failed,

    /// <summary>Not on the project's allowlist (or confirmation missing) — refused outright.</summary>
    Rejected,
}

/// <summary>One line of the in-memory action journal.</summary>
public sealed record ActionJournalEntry(
    DateTimeOffset Timestamp,
    long ProjectId,
    string ProjectName,
    string ActionId,
    ActionOutcome Outcome,
    string Detail);
