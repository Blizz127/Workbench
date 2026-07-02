namespace Workbench.Core.Models;

/// <summary>Distinguishes build invocations from run invocations in the shared history table.</summary>
public enum ExecutionKind
{
    Build,
    Run,
}

/// <summary>
/// One build (or run) invocation. History is append-only and never overwritten.
/// </summary>
public sealed class BuildRecord
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public ExecutionKind Kind { get; set; } = ExecutionKind.Build;

    public string Command { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while running or if the process could not be started.</summary>
    public int? ExitCode { get; set; }

    public TimeSpan Duration { get; set; }

    public string OutputLog { get; set; } = string.Empty;

    /// <summary>HEAD commit at the time of the build, when available.</summary>
    public string? CommitSha { get; set; }

    public bool Succeeded => ExitCode == 0;
}
