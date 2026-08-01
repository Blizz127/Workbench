namespace Workbench.Core.Models;

/// <summary>Lifecycle state of one compose service, mapped from <c>docker compose ps</c>.</summary>
public enum ComposeServiceState
{
    Unknown,
    Created,
    Running,
    Paused,
    Restarting,

    /// <summary>Exited with code 0 — a one-shot service (e.g. db-update) that did its job.</summary>
    Completed,

    /// <summary>Exited with a non-zero code, or reported dead.</summary>
    Failed,
}

/// <summary>Healthcheck verdict of one compose service (<c>None</c> when no healthcheck is defined).</summary>
public enum ComposeHealth
{
    None,
    Starting,
    Healthy,
    Unhealthy,
}

/// <summary>Typed status of one compose service — view models never parse raw output.</summary>
public sealed record ComposeServiceStatus(
    string Name,
    string ContainerName,
    ComposeServiceState State,
    ComposeHealth Health,
    int? ExitCode,
    string StatusText)
{
    public bool IsRunning => State is ComposeServiceState.Running or ComposeServiceState.Paused;
}

/// <summary>
/// Read-only snapshot of a compose project's services. <see cref="Error"/>
/// carries the (redacted) failure output when the inspection itself failed —
/// e.g. the Docker daemon is down — in which case <see cref="Services"/> is empty.
/// </summary>
public sealed record ComposeProjectStatus(
    string? ProjectName,
    string ComposeFilePath,
    IReadOnlyList<ComposeServiceStatus> Services,
    string? Error = null)
{
    public int TotalCount => Services.Count;
    public int RunningCount => Services.Count(s => s.IsRunning);
    public int CompletedCount => Services.Count(s => s.State == ComposeServiceState.Completed);

    /// <summary>Short container summary, e.g. "5/6 running · 1 completed".</summary>
    public string Summary => Services.Count == 0
        ? "no containers"
        : CompletedCount > 0
            ? $"{RunningCount}/{TotalCount} running · {CompletedCount} completed"
            : $"{RunningCount}/{TotalCount} running";
}

/// <summary>The safe, user-triggered compose operations Phase 1B supports.</summary>
public enum ComposeOperationKind
{
    StartAll,
    StopAll,
    RestartAll,
    StartService,
    StopService,
    RestartService,
    LogsAll,
    LogsService,
}

/// <summary>
/// A fully-built compose operation: the exact command to run, whether it must
/// be confirmed first, and a plain-English description for the preview panel.
/// </summary>
public sealed record ComposeOperation(
    ComposeOperationKind Kind,
    string? ServiceName,
    string Command,
    bool RequiresConfirmation,
    string Description)
{
    public bool IsReadOnly => Kind is ComposeOperationKind.LogsAll or ComposeOperationKind.LogsService;
}

/// <summary>Outcome of executing a compose operation. <see cref="Output"/> is secret-redacted.</summary>
public sealed record ComposeOperationResult(
    ComposeOperation Operation,
    bool Succeeded,
    bool WasBlocked,
    int? ExitCode,
    TimeSpan Duration,
    string Output)
{
    public static ComposeOperationResult Blocked(ComposeOperation operation, string reason) =>
        new(operation, Succeeded: false, WasBlocked: true, ExitCode: null, Duration: TimeSpan.Zero, Output: reason);
}
