namespace Workbench.Core.Models;

/// <summary>
/// Post-launch health of a project's runtime, assessed by the configured
/// status command — never inferred from the launch command's exit code.
/// </summary>
public enum RuntimeHealth
{
    /// <summary>No assessment possible (no status command, or it could not run).</summary>
    Unknown,

    /// <summary>Launch command succeeded; waiting out the startup grace period.</summary>
    Starting,

    /// <summary>All required services are up and no failed-state pattern matched.</summary>
    Healthy,

    /// <summary>All required services are up, but at least one optional service is down.</summary>
    Degraded,

    /// <summary>A required service is down or a failed-state pattern matched.</summary>
    Failed,

    /// <summary>Stopped by the user (or never started).</summary>
    Stopped,
}

/// <summary>
/// How to validate a project's runtime after the run command reports success.
/// Stored on the project (DB columns) and mirrored in <c>workbench.json</c>.
/// </summary>
public sealed class LaunchProfile
{
    /// <summary>Seconds to wait after a successful start before the first health check.</summary>
    public int StartupGraceSeconds { get; set; } = 5;

    /// <summary>
    /// Shell command whose output is parsed for service state. Convention: one
    /// line per service containing the service name and a state token
    /// (e.g. <c>map up</c>); a service counts as up when its name shares a
    /// line with a healthy token.
    /// </summary>
    public string? StatusCommand { get; set; }

    /// <summary>Services that must be up for the runtime to be Healthy.</summary>
    public List<string> RequiredServices { get; set; } = [];

    /// <summary>Services whose absence only drops the runtime to Degraded.</summary>
    public List<string> OptionalServices { get; set; } = [];

    /// <summary>
    /// Tokens marking a service line as "up" (case-insensitive substring
    /// match). Defaults to up/running/healthy/listening/ok when empty.
    /// </summary>
    public List<string> HealthyPatterns { get; set; } = [];

    /// <summary>Tokens that mark the whole runtime Failed when seen anywhere in the output.</summary>
    public List<string> FailedPatterns { get; set; } = [];

    /// <summary>Command fetching one service's logs; <c>{service}</c> is replaced with the service name.</summary>
    public string? ServiceLogCommand { get; set; }

    /// <summary>Status-command attempts before giving up (minimum 1).</summary>
    public int HealthCheckRetries { get; set; } = 3;

    /// <summary>Seconds between status-command attempts.</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 5;
}

/// <summary>One persisted health assessment; the newest row per project is the current state.</summary>
public sealed class RuntimeHealthRecord
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    public RuntimeHealth Health { get; set; } = RuntimeHealth.Unknown;

    /// <summary>The required service found down, when known.</summary>
    public string? FailedComponent { get; set; }

    /// <summary>Raw status-command output the assessment was parsed from.</summary>
    public string Output { get; set; } = string.Empty;
}
