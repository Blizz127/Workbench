namespace Workbench.Core.Models;

/// <summary>
/// Append-only record of one execution of a named command (matching-build or
/// a custom <see cref="ProjectCommand"/>). Build/Run/Test keep using
/// <see cref="BuildRecord"/>/<see cref="TestRecord"/>; this table exists for
/// the open-ended set of additional commands a project can define.
/// </summary>
public sealed class CommandRun
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>e.g. "matching-build", or a custom command's name.</summary>
    public string CommandName { get; set; } = string.Empty;

    public string CommandText { get; set; } = string.Empty;

    /// <summary>Directory the command executed in (the project's repository path).</summary>
    public string? WorkingDirectory { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null when the process could not start, timed out, or was cancelled.</summary>
    public int? ExitCode { get; set; }

    public TimeSpan Duration { get; set; }

    public string OutputLog { get; set; } = string.Empty;

    public bool OutputTruncated { get; set; }

    /// <summary>HEAD commit at the time the command ran, when available.</summary>
    public string? GitHeadSha { get; set; }

    /// <summary>Whether the working tree had uncommitted changes when the command ran.</summary>
    public bool WasDirty { get; set; }

    /// <summary>True if the previous run of this same command name had succeeded and this one did not.</summary>
    public bool RegressionDetected { get; set; }

    public bool Cancelled { get; set; }

    public bool Succeeded => ExitCode == 0;
}
