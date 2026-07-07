using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Turns explicit milestone/checklist status into a plain done/total count —
/// nothing here estimates or infers a percentage; it only counts what the
/// user has explicitly marked.
/// </summary>
public static class ProgressCalculator
{
    public static (int Done, int Total) MilestoneProgress(IReadOnlyList<Milestone> milestones) =>
        (milestones.Count(m => m.Status == MilestoneStatus.Done), milestones.Count);

    public static (int Done, int Total) ChecklistProgress(IReadOnlyList<FrontierChecklistItem> items) =>
        (items.Count(i => i.Status == ChecklistItemStatus.Done), items.Count);
}
