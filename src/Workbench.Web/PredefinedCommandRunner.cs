using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Web;

/// <summary>The predefined commands a mobile action can trigger.</summary>
public enum PredefinedCommandKind
{
    Build,
    Test,
    Launch,
}

/// <summary>Uniform outcome of a build/test/launch run for the action layer.</summary>
public sealed record PredefinedCommandResult(
    bool Succeeded,
    int? ExitCode,
    TimeSpan Duration,
    string Output,
    DateTimeOffset StartedAt);

/// <summary>
/// Runs a project's configured build/test/launch command through the shared
/// <see cref="ExecutionService"/> (history + timeline journaling included).
/// A small interface so the mobile action service can be unit-tested without
/// the concrete ExecutionService and its repositories.
/// </summary>
public interface IPredefinedCommandRunner
{
    /// <summary>
    /// Runs <paramref name="command"/> as the given kind. The command text
    /// always comes from the project's workbench.json — never from request
    /// input. Null when nothing ran (no command configured).
    /// </summary>
    Task<PredefinedCommandResult?> RunAsync(
        Project project, PredefinedCommandKind kind, string command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wraps <see cref="ExecutionService"/>. A minimal copy of the project
/// carries the workbench.json command into the run, so the DB row is never
/// mutated and only Id/RepositoryPath (all ExecutionService reads) are shared.
/// Output is secret-redacted before it leaves here.
/// </summary>
public sealed class PredefinedCommandRunner(ExecutionService executions) : IPredefinedCommandRunner
{
    public async Task<PredefinedCommandResult?> RunAsync(
        Project project, PredefinedCommandKind kind, string command, CancellationToken cancellationToken = default)
    {
        var effective = new Project
        {
            Id = project.Id,
            Name = project.Name,
            RepositoryPath = project.RepositoryPath,
            BuildCommand = kind is PredefinedCommandKind.Build ? command : null,
            RunCommand = kind is PredefinedCommandKind.Launch ? command : null,
            TestCommand = kind is PredefinedCommandKind.Test ? command : null,
        };

        switch (kind)
        {
            case PredefinedCommandKind.Build:
            case PredefinedCommandKind.Launch:
            {
                var executionKind = kind is PredefinedCommandKind.Build
                    ? ExecutionKind.Build
                    : ExecutionKind.Run;
                var record = await executions.RunBuildAsync(effective, executionKind, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? null
                    : new PredefinedCommandResult(
                        record.Succeeded, record.ExitCode, record.Duration,
                        SecretRedactor.Redact(record.OutputLog), record.StartedAt);
            }

            case PredefinedCommandKind.Test:
            {
                var record = await executions.RunTestsAsync(effective, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return record is null
                    ? null
                    : new PredefinedCommandResult(
                        record.Result is TestResult.Passed, ExitCode: null, record.Duration,
                        SecretRedactor.Redact(record.OutputLog), record.StartedAt);
            }

            default:
                return null;
        }
    }
}
