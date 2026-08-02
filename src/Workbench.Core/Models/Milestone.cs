namespace Workbench.Core.Models;

/// <summary>
/// Lifecycle of a milestone. Only <see cref="Done"/> counts as complete;
/// <see cref="Dismissed"/> milestones are excluded from progress entirely.
/// </summary>
public enum MilestoneStatus
{
    NotStarted,
    Active,
    Blocked,
    Done,
    Dismissed,
}

/// <summary>A user-defined checkpoint; the milestone list renders as a visual checklist.</summary>
public sealed class Milestone
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Legacy flag kept in sync with <see cref="Status"/> by the repository
    /// (Done ↔ completed) so older queries that only know the flag stay correct.
    /// </summary>
    public bool IsCompleted { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Display order within the project.</summary>
    public int SortOrder { get; set; }

    public string? Notes { get; set; }

    public MilestoneStatus Status { get; set; } = MilestoneStatus.NotStarted;

    /// <summary>
    /// Progress weight relative to the other milestones; null (or any
    /// non-positive value) means the default weight of 1.
    /// </summary>
    public int? Weight { get; set; }
}
