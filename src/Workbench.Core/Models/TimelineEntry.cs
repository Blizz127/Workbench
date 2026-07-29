namespace Workbench.Core.Models;

public enum TimelineCategory
{
    General,
    Build,
    Run,
    Test,
    Code,
    Milestone,
    Frontier,
    Blocker,
    Note,
    Regression,
    Status,
    GitSync,
}

/// <summary>
/// One line of the project's engineering journal. Entries are append-only;
/// the timeline is never rewritten.
/// </summary>
public sealed class TimelineEntry
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public TimelineCategory Category { get; set; } = TimelineCategory.General;

    public string Message { get; set; } = string.Empty;

    /// <summary>Optional longer detail (e.g. an error excerpt).</summary>
    public string? Detail { get; set; }
}
