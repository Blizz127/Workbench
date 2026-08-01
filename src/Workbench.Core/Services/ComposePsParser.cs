using System.Text.Json;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Parses <c>docker compose ps --all --format json</c> output (one JSON object
/// per line) into typed <see cref="ComposeServiceStatus"/> rows. Containers are
/// rolled up per service: the worst state wins, so a failed replica is never
/// hidden by a healthy sibling. A one-shot service that exited with code 0 is
/// <see cref="ComposeServiceState.Completed"/> — a success, not a failure.
/// </summary>
public static class ComposePsParser
{
    public static ComposeProjectStatus Parse(string output, string composeFilePath)
    {
        var containers = new List<ComposeServiceStatus>();
        string? projectName = null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || !trimmed.StartsWith('{'))
                continue;

            ComposeContainer? container;
            try
            {
                container = JsonSerializer.Deserialize<ComposeContainer>(trimmed);
            }
            catch (JsonException)
            {
                // A non-JSON line (docker warning, progress noise) never
                // invalidates the rest of the snapshot.
                continue;
            }

            if (container is null || string.IsNullOrWhiteSpace(container.Service))
                continue;

            projectName ??= string.IsNullOrWhiteSpace(container.Project) ? null : container.Project;
            containers.Add(new ComposeServiceStatus(
                Name: container.Service,
                ContainerName: container.Name ?? string.Empty,
                State: MapState(container.State, container.ExitCode),
                Health: MapHealth(container.Health),
                ExitCode: container.ExitCode,
                StatusText: container.Status ?? string.Empty));
        }

        var services = containers
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(RollUp)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ComposeProjectStatus(projectName, composeFilePath, services);
    }

    /// <summary>State from the <c>State</c>/<c>ExitCode</c> fields: exited 0 is Completed, not Failed.</summary>
    internal static ComposeServiceState MapState(string? state, int? exitCode) =>
        state?.Trim().ToLowerInvariant() switch
        {
            "running" => ComposeServiceState.Running,
            "paused" => ComposeServiceState.Paused,
            "restarting" => ComposeServiceState.Restarting,
            "created" => ComposeServiceState.Created,
            "exited" => exitCode == 0 ? ComposeServiceState.Completed : ComposeServiceState.Failed,
            "dead" => ComposeServiceState.Failed,
            _ => ComposeServiceState.Unknown,
        };

    internal static ComposeHealth MapHealth(string? health) =>
        health?.Trim().ToLowerInvariant() switch
        {
            "healthy" => ComposeHealth.Healthy,
            "unhealthy" => ComposeHealth.Unhealthy,
            "starting" => ComposeHealth.Starting,
            _ => ComposeHealth.None,
        };

    /// <summary>One row per service; the container with the worst state represents it.</summary>
    private static ComposeServiceStatus RollUp(IGrouping<string, ComposeServiceStatus> group)
    {
        var containers = group.ToList();
        var representative = containers
            .OrderByDescending(c => Severity(c.State))
            .ThenByDescending(c => HealthSeverity(c.Health))
            .First();

        var worstHealth = containers
            .Select(c => c.Health)
            .OrderByDescending(HealthSeverity)
            .First();

        return representative with { Health = worstHealth };
    }

    private static int Severity(ComposeServiceState state) => state switch
    {
        ComposeServiceState.Failed => 6,
        ComposeServiceState.Restarting => 5,
        ComposeServiceState.Unknown => 4,
        ComposeServiceState.Created => 3,
        ComposeServiceState.Running => 2,
        ComposeServiceState.Paused => 1,
        _ => 0, // Completed — the quietest end state
    };

    private static int HealthSeverity(ComposeHealth health) => health switch
    {
        ComposeHealth.Unhealthy => 3,
        ComposeHealth.Starting => 2,
        ComposeHealth.Healthy => 1,
        _ => 0,
    };

    /// <summary>Subset of the <c>docker compose ps --format json</c> schema this app reads.</summary>
    private sealed class ComposeContainer
    {
        public string? Name { get; set; }
        public string? Service { get; set; }
        public string? State { get; set; }
        public string? Status { get; set; }
        public int? ExitCode { get; set; }
        public string? Health { get; set; }
        public string? Project { get; set; }
    }
}
