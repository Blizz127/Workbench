using System.Text.RegularExpressions;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Builds the exact <c>docker compose</c> command lines for every supported
/// operation. This is the only place compose commands are constructed, and it
/// deliberately offers no destructive primitives: no <c>down</c>, no
/// <c>-v</c>/<c>--volumes</c>, no <c>prune</c>, no <c>rm</c>/<c>rmi</c> —
/// stopping keeps containers, images, and database volumes intact.
/// </summary>
public static partial class ComposeCommandBuilder
{
    public const int LogTail = 200;

    /// <summary>Read-only service inspection (includes stopped/one-shot containers).</summary>
    public static string InspectCommand(string composeFilePath) =>
        $"{ComposePrefix(composeFilePath)} ps --all --format json";

    /// <summary>
    /// Builds one operation. Service-targeting kinds require
    /// <paramref name="serviceName"/>; invalid names are rejected here so no
    /// caller can smuggle shell fragments into a command line.
    /// </summary>
    public static ComposeOperation Build(ComposeOperationKind kind, string composeFilePath, string? serviceName = null)
    {
        var needsService = kind is ComposeOperationKind.StartService
            or ComposeOperationKind.StopService
            or ComposeOperationKind.RestartService
            or ComposeOperationKind.LogsService;

        if (needsService)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException($"Operation {kind} requires a service name.", nameof(serviceName));
            if (!ServiceNamePattern().IsMatch(serviceName))
                throw new ArgumentException($"Invalid compose service name: {serviceName}", nameof(serviceName));
        }

        var compose = ComposePrefix(composeFilePath);
        return kind switch
        {
            ComposeOperationKind.StartAll => new(kind, null, $"{compose} up -d", RequiresConfirmation: false,
                "Start all services. Creates containers that don't exist yet; never deletes anything — volumes are preserved."),
            ComposeOperationKind.StopAll => new(kind, null, $"{compose} stop", RequiresConfirmation: true,
                "Stop all containers. Containers, images, and volumes (including database data) are preserved."),
            ComposeOperationKind.RestartAll => new(kind, null, $"{compose} restart", RequiresConfirmation: true,
                "Restart all containers. Brief service interruption; volumes are preserved."),
            ComposeOperationKind.StartService => new(kind, serviceName, $"{compose} start {serviceName}", RequiresConfirmation: false,
                $"Start the existing '{serviceName}' container. (Use Start all if it has never been created.)"),
            ComposeOperationKind.StopService => new(kind, serviceName, $"{compose} stop {serviceName}", RequiresConfirmation: true,
                $"Stop the '{serviceName}' container. The container and its volumes are preserved."),
            ComposeOperationKind.RestartService => new(kind, serviceName, $"{compose} restart {serviceName}", RequiresConfirmation: true,
                $"Restart the '{serviceName}' container. Brief interruption of that service; volumes are preserved."),
            ComposeOperationKind.LogsAll => new(kind, null, $"{compose} logs --no-color --tail={LogTail}", RequiresConfirmation: false,
                $"Show the last {LogTail} log lines across all services (read-only)."),
            ComposeOperationKind.LogsService => new(kind, serviceName, $"{compose} logs --no-color --tail={LogTail} {serviceName}", RequiresConfirmation: false,
                $"Show the last {LogTail} log lines of '{serviceName}' (read-only)."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    // The compose file is always named explicitly so the exact command shown to
    // the user is the exact command that runs, regardless of docker's default
    // filename search.
    private static string ComposePrefix(string composeFilePath) =>
        $"docker compose -f \"{composeFilePath.Replace("\"", "")}\"";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled)]
    private static partial Regex ServiceNamePattern();
}
