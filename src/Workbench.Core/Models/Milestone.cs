namespace Workbench.Core.Models;

/// <summary>A user-defined checkpoint; the milestone list renders as a visual checklist.</summary>
public sealed class Milestone
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Display order within the project.</summary>
    public int SortOrder { get; set; }

    public string? Notes { get; set; }
}
