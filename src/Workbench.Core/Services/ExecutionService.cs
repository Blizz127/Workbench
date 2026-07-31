using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Runs a project's build/run/test commands, appends the results to the
/// append-only history tables, and journals timeline entries — including
/// regression entries when a previously green build or test goes red.
/// </summary>
public sealed class ExecutionService(
    IBuildHistoryRepository buildHistory,
    ITestHistoryRepository testHistory,
    ITimelineRepository timeline,
    IGitService git,
    IProcessRunner runner)
{
    public async Task<BuildRecord?> RunBuildAsync(
        Project project, ExecutionKind kind, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var command = kind == ExecutionKind.Build ? project.BuildCommand : project.RunCommand;
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var previous = buildHistory.GetLatest(project.Id, kind);
        var result = await runner.RunAsync(command, project.RepositoryPath, timeout, cancellationToken)
            .ConfigureAwait(false);

        var record = new BuildRecord
        {
            ProjectId = project.Id,
            Kind = kind,
            Command = result.Command,
            StartedAt = result.StartedAt,
            ExitCode = result.ExitCode,
            Duration = result.Duration,
            OutputLog = result.Output,
            CommitSha = git.GetHeadSha(project.RepositoryPath),
        };
        buildHistory.Add(record);

        var label = kind == ExecutionKind.Build ? "Build" : "Run";
        var message = result.TimedOut
            ? $"{label} timed out after {result.Duration.TotalSeconds:0} seconds"
            : record.Succeeded
                ? $"{label} succeeded in {result.Duration.TotalSeconds:0.#}s"
                : $"{label} failed (exit {record.ExitCode?.ToString() ?? "n/a"})";

        timeline.Add(new TimelineEntry
        {
            ProjectId = project.Id,
            Timestamp = record.StartedAt,
            Category = kind == ExecutionKind.Build ? TimelineCategory.Build : TimelineCategory.Run,
            Message = message,
            Detail = record.Succeeded ? null : Tail(result.Output),
        });

        // A user-cancelled run reports a null exit code; don't treat it as a regression.
        if (!cancellationToken.IsCancellationRequested && previous is { } prev && prev.Succeeded && !record.Succeeded)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Category = TimelineCategory.Regression,
                Message = $"{label} regression: previously green command now fails ({command})",
            });
        }

        return record;
    }

    public async Task<TestRecord?> RunTestsAsync(
        Project project, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project.TestCommand))
            return null;

        var previous = testHistory.GetLatest(project.Id);
        var result = await runner.RunAsync(project.TestCommand, project.RepositoryPath, timeout, cancellationToken)
            .ConfigureAwait(false);

        var testResult = result switch
        {
            { TimedOut: true } => TestResult.TimedOut,
            { ExitCode: 0 } => TestResult.Passed,
            { ExitCode: null } => TestResult.Error,
            _ => TestResult.Failed,
        };

        var record = new TestRecord
        {
            ProjectId = project.Id,
            Command = result.Command,
            Result = testResult,
            StartedAt = result.StartedAt,
            Duration = result.Duration,
            OutputLog = result.Output,
            CommitSha = git.GetHeadSha(project.RepositoryPath),
        };
        testHistory.Add(record);

        timeline.Add(new TimelineEntry
        {
            ProjectId = project.Id,
            Timestamp = record.StartedAt,
            Category = TimelineCategory.Test,
            Message = $"Tests {testResult} in {result.Duration.TotalSeconds:0.#}s",
            Detail = testResult == TestResult.Passed ? null : Tail(result.Output),
        });

        // A user-cancelled run maps to TestResult.Error; don't treat it as a regression.
        if (!cancellationToken.IsCancellationRequested
            && previous is { Result: TestResult.Passed } && testResult != TestResult.Passed)
        {
            timeline.Add(new TimelineEntry
            {
                ProjectId = project.Id,
                Category = TimelineCategory.Regression,
                Message = $"Test regression: previously passing suite now reports {testResult}",
            });
        }

        return record;
    }

    private static string Tail(string output, int maxChars = 2000) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];
}
