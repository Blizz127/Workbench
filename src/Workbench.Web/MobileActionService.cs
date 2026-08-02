using System.Collections.Concurrent;
using System.Diagnostics;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Web;

/// <summary>The mobile action layer: allowlist resolution, previews, guarded execution.</summary>
public interface IMobileActionService
{
    /// <summary>The effective allowlist for one project (empty for unknown projects).</summary>
    IReadOnlyList<MobileActionDescriptor> GetActions(long projectId);

    /// <summary>
    /// The confirmation-page model. Null only for an unknown project; unknown,
    /// unlisted, or disallowed actions come back with
    /// <see cref="MobileActionPreview.BlockReason"/> set — never throws.
    /// </summary>
    MobileActionPreview? BuildPreview(long projectId, string actionId);

    /// <summary>
    /// Runs one allowlisted action. Every attempt is journaled; blocked or
    /// unlisted actions are refused with a typed result instead of throwing.
    /// One action per project at a time — a concurrent second execution is
    /// refused ("already running"), never queued. Null only for an unknown
    /// project.
    /// </summary>
    Task<MobileActionExecution?> ExecuteAsync(
        long projectId, string actionId, bool confirmed, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes the mobile allowlist strictly through the existing typed Core
/// services — <see cref="IDockerComposeService"/>, <see cref="ITmuxService"/>,
/// <see cref="IPredefinedCommandRunner"/> — so no command parsing and no raw
/// shell text exists in this layer. Every command shown in a preview is the
/// exact command the typed builder produces. All output is funneled through
/// <see cref="SecretRedactor"/> again before it can reach a phone, and every
/// attempt (attempted/blocked/rejected/succeeded/failed) lands in the
/// <see cref="ActionJournal"/>.
/// </summary>
public sealed class MobileActionService(
    IProjectRepository projects,
    IProjectConfigService configs,
    IDockerComposeService docker,
    ITmuxService tmux,
    IPredefinedCommandRunner predefined,
    DashboardSnapshotService snapshots,
    ActionJournal journal) : IMobileActionService
{
    public const string RefreshStatusId = "refreshStatus";
    public const string TmuxAttachInfoId = "tmuxAttachInfo";

    public const string TypeRefreshStatus = "refreshStatus";
    public const string TypeTmuxAttachInfo = "tmuxAttachInfo";
    public const string TypeDockerLogsAll = "dockerLogsAll";
    public const string TypeDockerLogsService = "dockerLogsService";
    public const string TypeDockerRestartService = "dockerRestartService";
    public const string TypeDockerRestartAllowed = "dockerRestartAllowed";
    public const string TypeTmuxCreateSession = "tmuxCreateSession";
    public const string TypePredefinedCommand = "predefinedCommand";

    /// <summary>Service names that hold data — restarting them needs allowDatabase: true.</summary>
    private static readonly string[] DatabaseLikeNames =
        ["database", "db", "mysql", "mariadb", "postgres", "postgresql", "mongo", "mongodb", "redis"];

    private readonly ConcurrentDictionary<long, SemaphoreSlim> _projectLocks = new();

    private sealed record ResolvedAction(MobileActionDescriptor Descriptor, MobileActionConfig? Config);

    // ---------- allowlist ----------

    public IReadOnlyList<MobileActionDescriptor> GetActions(long projectId)
    {
        if (projects.GetById(projectId) is not { } project)
            return [];

        var actions = new List<MobileActionDescriptor>
        {
            new(RefreshStatusId, "Refresh status", TypeRefreshStatus,
                MobileActionSafety.ReadOnly, null, null),
        };

        if (tmux.ProjectSessionName(project) is { Length: > 0 })
        {
            actions.Add(new MobileActionDescriptor(TmuxAttachInfoId, "How to attach (terminal)",
                TypeTmuxAttachInfo, MobileActionSafety.ReadOnly, null, null));
        }

        foreach (var entry in LoadConfig(project)?.MobileActions ?? [])
            actions.Add(DescriptorFor(entry));

        return actions;
    }

    // ---------- preview ----------

    public MobileActionPreview? BuildPreview(long projectId, string actionId)
    {
        if (projects.GetById(projectId) is not { } project)
            return null;

        var resolved = Resolve(project, actionId);
        if (resolved is null)
        {
            return new MobileActionPreview(projectId, project.Name,
                new MobileActionDescriptor(actionId, actionId, "unknown",
                    MobileActionSafety.ReadOnly, null, null),
                CommandPreview: string.Empty, MobileActionSafety.ReadOnly,
                BlockReason: "This action is not on the project's mobile allowlist.");
        }

        var descriptor = resolved.Descriptor;
        if (descriptor.DisabledReason is { } disabled)
            return new MobileActionPreview(projectId, project.Name, descriptor,
                string.Empty, descriptor.Safety, disabled);

        var (commandPreview, blockReason) = PreviewFor(project, resolved);
        return new MobileActionPreview(projectId, project.Name, descriptor,
            commandPreview, descriptor.Safety, blockReason);
    }

    private (string CommandPreview, string? BlockReason) PreviewFor(Project project, ResolvedAction resolved)
    {
        var entry = resolved.Config;
        switch (resolved.Descriptor.Type)
        {
            case TypeRefreshStatus:
                return ("(no external command — clears the server-side dashboard caches)", null);

            case TypeTmuxAttachInfo:
                return (TmuxCommandBuilder.AttachCommand(tmux.ProjectSessionName(project)), null);

            case TypeDockerLogsAll:
                return TryBuildCompose(project, ComposeOperationKind.LogsAll, null, out var logsAll, out var logsAllError)
                    ? (logsAll!.Command, null)
                    : (string.Empty, logsAllError);

            case TypeDockerLogsService:
                return TryBuildCompose(project, ComposeOperationKind.LogsService, entry?.Service, out var logs, out var logsError)
                    ? (logs!.Command, null)
                    : (string.Empty, logsError);

            case TypeDockerRestartService:
            {
                if (DatabaseRestartRefused(entry, out var refuseReason))
                    return (string.Empty, refuseReason);
                return TryBuildCompose(project, ComposeOperationKind.RestartService, entry?.Service, out var restart, out var restartError)
                    ? (restart!.Command, null)
                    : (string.Empty, restartError);
            }

            case TypeDockerRestartAllowed:
            {
                var lines = new List<string>();
                foreach (var target in RestartTargets(project))
                {
                    if (DatabaseRestartRefused(target, out _))
                    {
                        lines.Add($"# skipped: {target.Service} (database-like — needs allowDatabase: true)");
                        continue;
                    }

                    if (TryBuildCompose(project, ComposeOperationKind.RestartService, target.Service, out var op, out _))
                        lines.Add(op!.Command);
                }

                return lines.Count == 0
                    ? (string.Empty, "No services in this project's allowlist have their own dockerRestartService entry.")
                    : (string.Join('\n', lines), null);
            }

            case TypeTmuxCreateSession:
            {
                var sessionName = tmux.ProjectSessionName(project);
                return (TmuxCommandBuilder.BuildCreate(sessionName, project.RepositoryPath).Command, null);
            }

            case TypePredefinedCommand:
            {
                var (command, error) = PredefinedCommandFor(project, entry);
                return error is not null ? (string.Empty, error) : (command!, null);
            }

            default:
                return (string.Empty, "Unknown action type — refused.");
        }
    }

    // ---------- execution ----------

    public async Task<MobileActionExecution?> ExecuteAsync(
        long projectId, string actionId, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (projects.GetById(projectId) is not { } project)
            return null;

        Record(project, actionId, ActionOutcome.Attempted, confirmed ? "confirmed" : "unconfirmed");

        var preview = BuildPreview(projectId, actionId)!;
        var descriptor = preview.Action;
        var startedAt = DateTimeOffset.Now;

        MobileActionExecution Refused(ActionOutcome outcome, string reason)
        {
            Record(project, actionId, outcome, reason);
            return new MobileActionExecution(NewExecutionId(), descriptor, Succeeded: false,
                Blocked: true, ExitCode: null, Duration: TimeSpan.Zero, Output: reason, startedAt);
        }

        if (preview.BlockReason is { } blockReason)
        {
            return Refused(
                descriptor.Type == "unknown" ? ActionOutcome.Rejected : ActionOutcome.Blocked,
                blockReason);
        }

        if (descriptor.IsMutating && !confirmed)
            return Refused(ActionOutcome.Rejected, "This action mutates state and requires confirmation.");

        var projectLock = _projectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        if (!await projectLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Refused(ActionOutcome.Blocked,
                "Another action is already running for this project — wait for it to finish.");
        }

        try
        {
            var execution = await RunAsync(project, preview, startedAt, cancellationToken).ConfigureAwait(false);
            Record(project, actionId, execution.Succeeded ? ActionOutcome.Succeeded : ActionOutcome.Failed,
                execution.Succeeded
                    ? $"exit {execution.ExitCode?.ToString() ?? "n/a"} in {execution.Duration.TotalSeconds:0.#}s"
                    : SecretRedactor.Redact(Tail(execution.Output)));
            return execution;
        }
        finally
        {
            projectLock.Release();
        }
    }

    private async Task<MobileActionExecution> RunAsync(
        Project project, MobileActionPreview preview, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var descriptor = preview.Action;
        var stopwatch = Stopwatch.StartNew();

        MobileActionExecution Done(bool succeeded, int? exitCode, TimeSpan duration, string output) =>
            new(NewExecutionId(), descriptor, succeeded, Blocked: false, exitCode, duration,
                SecretRedactor.Redact(output), startedAt);

        switch (descriptor.Type)
        {
            case TypeRefreshStatus:
                snapshots.Invalidate();
                return Done(true, 0, stopwatch.Elapsed,
                    "Dashboard caches cleared — the next poll refreshes from live data.");

            case TypeTmuxAttachInfo:
                return Done(true, 0, stopwatch.Elapsed,
                    $"Nothing runs on the server for this. Open a terminal and run:\n\n{preview.CommandPreview}");

            case TypeDockerLogsAll:
            case TypeDockerLogsService:
            case TypeDockerRestartService:
            {
                var kind = descriptor.Type switch
                {
                    TypeDockerLogsAll => ComposeOperationKind.LogsAll,
                    TypeDockerLogsService => ComposeOperationKind.LogsService,
                    _ => ComposeOperationKind.RestartService,
                };
                TryBuildCompose(project, kind, descriptor.Service, out var operation, out _);
                var result = await docker.ExecuteAsync(project, operation!, confirmed: true, cancellationToken)
                    .ConfigureAwait(false);
                return Done(result.Succeeded, result.ExitCode, result.Duration, result.Output);
            }

            case TypeDockerRestartAllowed:
            {
                var output = new List<string>();
                var allSucceeded = true;
                int? firstFailureExit = null;
                foreach (var target in RestartTargets(project))
                {
                    if (DatabaseRestartRefused(target, out _))
                    {
                        output.Add($"== {target.Service} == skipped (database-like — needs allowDatabase: true)");
                        continue;
                    }

                    TryBuildCompose(project, ComposeOperationKind.RestartService, target.Service, out var operation, out _);
                    var result = await docker.ExecuteAsync(project, operation!, confirmed: true, cancellationToken)
                        .ConfigureAwait(false);
                    output.Add($"== {target.Service} == {(result.Succeeded ? "restarted" : $"failed (exit {result.ExitCode?.ToString() ?? "n/a"})")}");
                    if (result.Output.Length > 0)
                        output.Add(result.Output.TrimEnd());
                    if (!result.Succeeded)
                    {
                        allSucceeded = false;
                        firstFailureExit ??= result.ExitCode;
                    }
                }

                return Done(allSucceeded, allSucceeded ? 0 : firstFailureExit, stopwatch.Elapsed,
                    string.Join('\n', output));
            }

            case TypeTmuxCreateSession:
            {
                var sessionName = tmux.ProjectSessionName(project);
                if (await tmux.SessionExistsAsync(sessionName, cancellationToken).ConfigureAwait(false))
                {
                    return Done(true, 0, stopwatch.Elapsed,
                        $"Session '{sessionName}' already exists — nothing to do.");
                }

                var operation = TmuxCommandBuilder.BuildCreate(sessionName, project.RepositoryPath);
                var result = await tmux.ExecuteAsync(operation, confirmed: true, cancellationToken)
                    .ConfigureAwait(false);
                return Done(result.Succeeded, result.ExitCode, stopwatch.Elapsed, result.Output);
            }

            case TypePredefinedCommand:
            {
                var kind = PredefinedKind(descriptor.CommandId)!.Value; // preview already validated
                var (command, _) = PredefinedCommandFor(project, FindEntry(project, descriptor.Id)!);
                var result = await predefined.RunAsync(project, kind, command!, cancellationToken)
                    .ConfigureAwait(false);
                return result is null
                    ? Done(false, null, stopwatch.Elapsed, "Command is not configured.")
                    : Done(result.Succeeded, result.ExitCode, result.Duration, result.Output);
            }

            default:
                return Done(false, null, stopwatch.Elapsed, "Unknown action type — refused.");
        }
    }

    // ---------- helpers ----------

    private ResolvedAction? Resolve(Project project, string actionId)
    {
        if (actionId == RefreshStatusId)
        {
            return new ResolvedAction(new MobileActionDescriptor(RefreshStatusId, "Refresh status",
                TypeRefreshStatus, MobileActionSafety.ReadOnly, null, null), null);
        }

        if (actionId == TmuxAttachInfoId && tmux.ProjectSessionName(project) is { Length: > 0 })
        {
            return new ResolvedAction(new MobileActionDescriptor(TmuxAttachInfoId, "How to attach (terminal)",
                TypeTmuxAttachInfo, MobileActionSafety.ReadOnly, null, null), null);
        }

        return FindEntry(project, actionId) is { } entry
            ? new ResolvedAction(DescriptorFor(entry), entry)
            : null;
    }

    private MobileActionConfig? FindEntry(Project project, string actionId) =>
        LoadConfig(project)?.MobileActions.FirstOrDefault(e => e.Id == actionId);

    private ProjectConfig? LoadConfig(Project project) =>
        project.RepositoryPath is { Length: > 0 } path ? configs.Load(path) : null;

    private static MobileActionDescriptor DescriptorFor(MobileActionConfig entry)
    {
        var defaultSafety = DefaultSafety(entry);
        if (defaultSafety is null)
        {
            // Fail closed: unknown types are listed but can never run.
            return new MobileActionDescriptor(entry.Id, entry.Label, "unknown",
                MobileActionSafety.ReadOnly, entry.Service, entry.CommandId,
                DisabledReason: $"Unknown action type '{entry.Type}' — not on the mobile allowlist.");
        }

        var safety = (MobileActionSafety)Math.Max((int)defaultSafety.Value, (int)ParseSafety(entry.Safety));
        return new MobileActionDescriptor(entry.Id, entry.Label, entry.Type,
            safety, entry.Service, entry.CommandId);
    }

    /// <summary>The safety floor for each type; null means the type is unknown.</summary>
    private static MobileActionSafety? DefaultSafety(MobileActionConfig entry) => entry.Type switch
    {
        TypeDockerLogsAll or TypeDockerLogsService => MobileActionSafety.ReadOnly,
        TypeDockerRestartService or TypeDockerRestartAllowed => MobileActionSafety.RequiresDoubleConfirmation,
        TypeTmuxCreateSession => MobileActionSafety.RequiresConfirmation,
        TypePredefinedCommand => entry.CommandId == "launch"
            ? MobileActionSafety.RequiresDoubleConfirmation
            : MobileActionSafety.RequiresConfirmation,
        _ => null,
    };

    /// <summary>Unknown/invalid safety strings parse as readOnly, so the type default always wins.</summary>
    private static MobileActionSafety ParseSafety(string? safety) => safety?.Trim().ToLowerInvariant() switch
    {
        "readonly" => MobileActionSafety.ReadOnly,
        "requiresconfirmation" => MobileActionSafety.RequiresConfirmation,
        "requiresdoubleconfirmation" => MobileActionSafety.RequiresDoubleConfirmation,
        _ => MobileActionSafety.ReadOnly,
    };

    private bool TryBuildCompose(
        Project project, ComposeOperationKind kind, string? serviceName,
        out ComposeOperation? operation, out string? error)
    {
        operation = null;
        error = null;

        if (docker.ResolveComposeFile(project) is not { } composeFile)
        {
            error = "This project has no compose file — docker actions are not configured.";
            return false;
        }

        try
        {
            operation = ComposeCommandBuilder.Build(kind, composeFile, serviceName);
            return true;
        }
        catch (ArgumentException)
        {
            error = $"Invalid or missing compose service name '{serviceName ?? "(none)"}' — not configured.";
            return false;
        }
    }

    private IEnumerable<MobileActionConfig> RestartTargets(Project project) =>
        LoadConfig(project)?.MobileActions
            .Where(e => e.Type == TypeDockerRestartService && e.Service is { Length: > 0 })
        ?? [];

    /// <summary>The database hard guard: exact or prefix match against the database-like set.</summary>
    private static bool DatabaseRestartRefused(MobileActionConfig? entry, out string? reason)
    {
        reason = null;
        if (entry?.Service is not { Length: > 0 } service || entry.AllowDatabase)
            return false;

        var databaseLike = DatabaseLikeNames.Any(name =>
            service.Equals(name, StringComparison.OrdinalIgnoreCase)
            || service.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (!databaseLike)
            return false;

        reason = $"Refusing to restart database-like service '{service}' from a phone — " +
            "set allowDatabase: true on the entry in workbench.json to permit it.";
        return true;
    }

    private (string? Command, string? Error) PredefinedCommandFor(Project project, MobileActionConfig? entry)
    {
        if (PredefinedKind(entry?.CommandId) is not { } kind)
        {
            return (null,
                $"Predefined command '{entry?.CommandId ?? "(none)"}' is not configured — known command ids: build, test, launch.");
        }

        var config = LoadConfig(project);
        var command = kind switch
        {
            PredefinedCommandKind.Build => config?.BuildCommand,
            PredefinedCommandKind.Test => config?.TestCommand,
            PredefinedCommandKind.Launch => config?.RunCommand,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(command)
            ? (null, $"The project's workbench.json has no {kind.ToString().ToLowerInvariant()} command — not configured.")
            : (command, null);
    }

    private static PredefinedCommandKind? PredefinedKind(string? commandId) => commandId switch
    {
        "build" => PredefinedCommandKind.Build,
        "test" => PredefinedCommandKind.Test,
        "launch" => PredefinedCommandKind.Launch,
        _ => null,
    };

    private void Record(Project project, string actionId, ActionOutcome outcome, string detail) =>
        journal.Record(new ActionJournalEntry(
            DateTimeOffset.Now, project.Id, project.Name, actionId, outcome, detail));

    private static string NewExecutionId() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    private static string Tail(string output, int maxChars = 300) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];
}
