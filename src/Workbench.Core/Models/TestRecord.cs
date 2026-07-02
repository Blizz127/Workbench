namespace Workbench.Core.Models;

public enum TestResult
{
    Passed,
    Failed,
    Error,
    TimedOut,
}

/// <summary>One test run. History is append-only and never overwritten.</summary>
public sealed class TestRecord
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Command { get; set; } = string.Empty;

    public TestResult Result { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public TimeSpan Duration { get; set; }

    public string? Notes { get; set; }

    /// <summary>Commit the tests ran against, when available.</summary>
    public string? CommitSha { get; set; }

    public string OutputLog { get; set; } = string.Empty;
}
