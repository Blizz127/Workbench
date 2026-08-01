using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>Safe, explicit Docker Compose operations for one project.</summary>
public interface IDockerComposeService
{
    /// <summary>
    /// The compose file for this project: the explicit <see cref="Project.ComposeFile"/>
    /// selection when set, otherwise the first conventional compose file at the
    /// repository root. Null when the project has neither.
    /// </summary>
    string? ResolveComposeFile(Project project);

    /// <summary>Read-only service inspection; never changes any container state.</summary>
    Task<ComposeProjectStatus?> InspectAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one user-initiated operation. Operations flagged
    /// <see cref="ComposeOperation.RequiresConfirmation"/> are refused unless
    /// <paramref name="confirmed"/> is true — the confirmation gate lives here,
    /// not only in the UI. Output is secret-redacted before it is returned.
    /// </summary>
    Task<ComposeOperationResult> ExecuteAsync(
        Project project,
        ComposeOperation operation,
        bool confirmed = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Docker Compose operations behind <see cref="IProcessRunner"/>. Every
/// command comes from <see cref="ComposeCommandBuilder"/> (no destructive
/// primitives), nothing ever runs automatically at startup, and nothing
/// mutating runs without an explicit user action carrying it here.
/// </summary>
public sealed class DockerComposeService(IProcessRunner runner) : IDockerComposeService
{
    private static readonly TimeSpan InspectTimeout = TimeSpan.FromSeconds(30);

    public string? ResolveComposeFile(Project project)
    {
        if (project.ComposeFile is { Length: > 0 } selection)
            return Path.IsPathRooted(selection)
                ? selection
                : Path.GetFullPath(Path.Combine(project.RepositoryPath ?? string.Empty, selection));

        return project.RepositoryPath is { Length: > 0 } path
            ? ComposeFileInspector.FindComposeFile(path)
            : null;
    }

    public async Task<ComposeProjectStatus?> InspectAsync(Project project, CancellationToken cancellationToken = default)
    {
        var composeFile = ResolveComposeFile(project);
        if (composeFile is null)
            return null;

        var result = await runner.RunAsync(
            ComposeCommandBuilder.InspectCommand(composeFile),
            project.RepositoryPath,
            InspectTimeout,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new ComposeProjectStatus(
                ProjectName: null,
                ComposeFilePath: composeFile,
                Services: [],
                Error: SecretRedactor.Redact(result.Output));
        }

        return ComposePsParser.Parse(result.Output, composeFile);
    }

    public async Task<ComposeOperationResult> ExecuteAsync(
        Project project,
        ComposeOperation operation,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        if (operation.RequiresConfirmation && !confirmed)
        {
            return ComposeOperationResult.Blocked(operation,
                "This operation stops or restarts containers and requires explicit confirmation.");
        }

        var result = await runner.RunAsync(
            operation.Command,
            project.RepositoryPath,
            TimeoutFor(operation.Kind),
            cancellationToken).ConfigureAwait(false);

        return new ComposeOperationResult(
            Operation: operation,
            Succeeded: result.Result == Models.CommandResult.Succeeded,
            WasBlocked: false,
            ExitCode: result.ExitCode,
            Duration: result.Duration,
            Output: SecretRedactor.Redact(result.Output));
    }

    private static TimeSpan TimeoutFor(ComposeOperationKind kind) => kind switch
    {
        // up -d may pull images on first run.
        ComposeOperationKind.StartAll or ComposeOperationKind.StartService => TimeSpan.FromMinutes(10),
        ComposeOperationKind.LogsAll or ComposeOperationKind.LogsService => TimeSpan.FromMinutes(1),
        _ => TimeSpan.FromMinutes(5),
    };
}
