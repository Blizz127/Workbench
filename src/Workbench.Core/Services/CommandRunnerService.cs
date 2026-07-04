using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Runs a named command (matching-build or a custom <see cref="ProjectCommand"/>),
/// appends the result to the append-only <see cref="ICommandRunRepository"/>,
/// and journals timeline entries — mirroring <see cref="ExecutionService"/>'s
/// regression-detection behavior for the open-ended set of additional
/// commands a project can define.
/// </summary>
public sealed class CommandRunnerService(
    ICommandRunRepository commandRuns,
    ITimelineRepository timeline,
    IGitService git,
    IProcessRunner runner)
{
    public async Task<CommandRun> RunAsync(
        Project project,
        string commandName,
        string commandText,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var previous = commandRuns.GetLatest(project.Id, commandName);
        var result = await runner.RunAsync(commandText, project.RepositoryPath, timeout, cancellationToken)
            .ConfigureAwait(false);

        var snapshot = git.GetSnapshot(project.RepositoryPath, recentCommitCount: 0);

        var record = new CommandRun
        {
            ProjectId = project.Id,
            CommandName = commandName,
            CommandText = result.Command,
            WorkingDirectory = project.RepositoryPath,
            StartedAt = result.StartedAt,
            ExitCode = result.ExitCode,
            Duration = result.Duration,
            OutputLog = result.Output,
            OutputTruncated = result.OutputTruncated,
            GitHeadSha = snapshot.LastCommit?.Sha,
            WasDirty = snapshot.IsDirty,
            Cancelled = result.Cancelled,
        };

        // A user-initiated cancel is not a regression; a timeout is.
        record.RegressionDetected = previous is { } prev && prev.Succeeded && !record.Succeeded && !result.Cancelled;

        commandRuns.Add(record);

        var message = result switch
        {
            { Cancelled: true } => $"{commandName} cancelled after {result.Duration.TotalSeconds:0.#}s",
            { TimedOut: true } => $"{commandName} timed out after {result.Duration.TotalSeconds:0} seconds",
            _ when record.Succeeded => $"{commandName} succeeded in {result.Duration.TotalSeconds:0.#}s",
            _ => $"{commandName} failed (exit {record.ExitCode?.ToString() ?? "n/a"})",
        };

        timeline.Add(new TimelineEntry
        {
            ProjectId = project.Id,
            Timestamp = record.StartedAt,
            Category = TimelineCategory.Build,
            Message = message,
            Detail = record.Succeeded ? null : Tail(result.Output),
        });

        if (record.RegressionDetected)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Category = TimelineCategory.Regression,
                Message = $"{commandName} regression: previously green command now fails ({commandText})",
            });
        }

        return record;
    }

    private static string Tail(string output, int maxChars = 2000) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];
}
