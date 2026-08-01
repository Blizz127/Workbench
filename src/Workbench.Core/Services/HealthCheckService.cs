using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Assesses post-launch runtime health using the project's launch profile:
/// after a successful Run start, waits out the startup grace period, runs the
/// status command (with retries), and parses its output against the required /
/// optional services and the healthy / failed patterns. The launch command's
/// exit code never implies health — a detached launcher exits 0 even when a
/// service it started crashes seconds later.
///
/// Assessments are appended to the runtime_health table (newest row = current
/// state) and health transitions are journaled to the timeline.
/// </summary>
public sealed class HealthCheckService(
    IRuntimeHealthRepository runtimeHealth,
    ITimelineRepository timeline,
    IProcessRunner runner)
{
    private static readonly string[] DefaultHealthyTokens = ["up", "running", "healthy", "listening", "ok"];
    private static readonly TimeSpan StatusCommandTimeout = TimeSpan.FromMinutes(2);

    public RuntimeHealthRecord? GetCurrent(long projectId) => runtimeHealth.GetLatest(projectId);

    /// <summary>
    /// Duplicate-start prevention: a runtime already tracked as Starting or
    /// Healthy (with no intervening Stop) must not be started again.
    /// Returns the user-facing reason, or null when a start is allowed.
    /// </summary>
    public string? GetStartBlockReason(long projectId)
    {
        var latest = runtimeHealth.GetLatest(projectId);
        return latest?.Health is RuntimeHealth.Starting or RuntimeHealth.Healthy
            ? $"Runtime is already tracked as {latest.Health} — stop it or recheck health before starting again."
            : null;
    }

    public RuntimeHealthRecord MarkStarting(long projectId) =>
        Record(projectId, RuntimeHealth.Starting, null, string.Empty);

    public RuntimeHealthRecord MarkStopped(long projectId) =>
        Record(projectId, RuntimeHealth.Stopped, null, string.Empty);

    /// <summary>Wait out the startup grace period, then assess with retries.</summary>
    public async Task<RuntimeHealthRecord> CheckAfterLaunchAsync(Project project, CancellationToken cancellationToken = default)
    {
        var grace = TimeSpan.FromSeconds(Math.Max(0, project.Launch.StartupGraceSeconds));
        if (grace > TimeSpan.Zero)
            await Task.Delay(grace, cancellationToken).ConfigureAwait(false);

        return await CheckAsync(project, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the status command and records the assessment. A required service
    /// still being down (or the status command itself failing) is retried —
    /// the runtime may still be coming up; failed-state patterns are
    /// immediately definitive.
    /// </summary>
    public async Task<RuntimeHealthRecord> CheckAsync(Project project, CancellationToken cancellationToken = default)
    {
        var profile = project.Launch;
        if (string.IsNullOrWhiteSpace(profile.StatusCommand))
            return Record(project.Id, RuntimeHealth.Unknown, null, "no status command configured");

        var attempts = Math.Max(1, profile.HealthCheckRetries);
        var interval = TimeSpan.FromSeconds(Math.Max(1, profile.HealthCheckIntervalSeconds));

        RuntimeHealthRecord? record = null;
        for (var attempt = 1; ; attempt++)
        {
            var result = await runner.RunAsync(
                profile.StatusCommand, project.RepositoryPath, StatusCommandTimeout, cancellationToken)
                .ConfigureAwait(false);

            var (health, failedComponent, definitive) = Parse(result, profile);
            record = Record(project.Id, health, failedComponent, result.Output);

            if (definitive || attempt >= attempts || cancellationToken.IsCancellationRequested)
                break;

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        return record;
    }

    /// <summary>Expands the <c>{service}</c> placeholder of the per-service log command.</summary>
    public static string? BuildServiceLogCommand(LaunchProfile profile, string? service) =>
        service is null || string.IsNullOrWhiteSpace(profile.ServiceLogCommand)
            ? null
            : profile.ServiceLogCommand.Replace("{service}", service);

    /// <summary>
    /// Per-service up/down states parsed from status-command output with the
    /// same matching rules as the health assessment: a service is up when its
    /// name shares a line with a healthy token. Required services first, in
    /// configured order.
    /// </summary>
    public static IReadOnlyList<ServiceState> GetServiceStates(LaunchProfile profile, string statusOutput)
    {
        var lines = statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<string> healthyTokens =
            profile.HealthyPatterns.Count > 0 ? profile.HealthyPatterns : DefaultHealthyTokens;

        bool ServiceUp(string service) => lines.Any(line =>
            line.Contains(service, StringComparison.OrdinalIgnoreCase) &&
            healthyTokens.Any(token => line.Contains(token, StringComparison.OrdinalIgnoreCase)));

        return
        [
            .. profile.RequiredServices.Select(s => new ServiceState(s, ServiceUp(s), IsRequired: true)),
            .. profile.OptionalServices.Select(s => new ServiceState(s, ServiceUp(s), IsRequired: false)),
        ];
    }

    /// <summary>Pure assessment of one status-command result; internal for tests.</summary>
    internal static (RuntimeHealth Health, string? FailedComponent, bool Definitive) Parse(
        ProcessResult result, LaunchProfile profile)
    {
        // The status command itself failing says nothing about the runtime —
        // never treat it as Healthy.
        if (result.ExitCode != 0)
            return (RuntimeHealth.Unknown, null, false);

        var output = result.Output;
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<string> healthyTokens = profile.HealthyPatterns.Count > 0 ? profile.HealthyPatterns : DefaultHealthyTokens;

        bool ServiceUp(string service) => lines.Any(line =>
            line.Contains(service, StringComparison.OrdinalIgnoreCase) &&
            healthyTokens.Any(token => line.Contains(token, StringComparison.OrdinalIgnoreCase)));

        // A failed-state pattern anywhere in the output is immediately definitive.
        if (profile.FailedPatterns.Count > 0
            && profile.FailedPatterns.Any(p => output.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return (RuntimeHealth.Failed, profile.RequiredServices.FirstOrDefault(s => !ServiceUp(s)), true);
        }

        if (profile.RequiredServices.Count == 0)
        {
            // No services configured: fall back to healthy-pattern presence.
            return profile.HealthyPatterns.Count > 0
                && healthyTokens.Any(t => output.Contains(t, StringComparison.OrdinalIgnoreCase))
                    ? (RuntimeHealth.Healthy, null, true)
                    : (RuntimeHealth.Unknown, null, false);
        }

        var failedRequired = profile.RequiredServices.Where(s => !ServiceUp(s)).ToList();
        if (failedRequired.Count > 0)
            return (RuntimeHealth.Failed, failedRequired[0], false);

        return profile.OptionalServices.Any(s => !ServiceUp(s))
            ? (RuntimeHealth.Degraded, null, true)
            : (RuntimeHealth.Healthy, null, true);
    }

    private RuntimeHealthRecord Record(long projectId, RuntimeHealth health, string? failedComponent, string output)
    {
        var previous = runtimeHealth.GetLatest(projectId);
        var record = new RuntimeHealthRecord
        {
            ProjectId = projectId,
            CheckedAt = DateTimeOffset.Now,
            Health = health,
            FailedComponent = failedComponent,
            Output = output,
        };
        runtimeHealth.Add(record);

        if (previous?.Health != health)
        {
            var message = failedComponent is null
                ? $"Runtime health: {previous?.Health.ToString() ?? "Unknown"} → {health}"
                : $"Runtime health: {previous?.Health.ToString() ?? "Unknown"} → {health} (failed component: {failedComponent})";
            timeline.Add(new TimelineEntry
            {
                ProjectId = projectId,
                Timestamp = record.CheckedAt,
                Category = TimelineCategory.Status,
                Message = message,
                Detail = health is RuntimeHealth.Failed or RuntimeHealth.Degraded ? Tail(output) : null,
            });
        }

        return record;
    }

    private static string Tail(string output, int maxChars = 2000) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];
}
