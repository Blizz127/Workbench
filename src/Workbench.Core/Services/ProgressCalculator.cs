using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Pure progress math over explicit milestone/frontier state. No estimates,
/// no I/O: the same inputs always produce the same numbers.
/// </summary>
public static class ProgressCalculator
{
    /// <summary>
    /// Weighted milestone completion. Returns null — "progress not
    /// configured" — when no NON-dismissed milestones exist; a configured but
    /// untouched checklist is 0%, an absent one is null.
    /// </summary>
    public static MilestoneProgress? Calculate(IReadOnlyList<Milestone> milestones)
    {
        var counted = milestones.Where(m => m.Status != MilestoneStatus.Dismissed).ToList();
        if (counted.Count == 0)
            return null;

        var totalWeight = counted.Sum(EffectiveWeight);
        var doneWeight = counted.Where(m => m.Status == MilestoneStatus.Done).Sum(EffectiveWeight);

        return new MilestoneProgress(
            CompletedCount: counted.Count(m => m.Status == MilestoneStatus.Done),
            TotalCount: counted.Count,
            Percent: totalWeight > 0 ? (100.0 * doneWeight / totalWeight) : 0,
            NotStartedCount: counted.Count(m => m.Status == MilestoneStatus.NotStarted),
            ActiveCount: counted.Count(m => m.Status == MilestoneStatus.Active),
            BlockedCount: counted.Count(m => m.Status == MilestoneStatus.Blocked),
            DoneCount: counted.Count(m => m.Status == MilestoneStatus.Done),
            DismissedCount: milestones.Count - counted.Count);
    }

    /// <summary>Frontier completion plus the current edge of progress (Current wins over the first Next).</summary>
    public static FrontierProgress CalculateFrontiers(IReadOnlyList<Frontier> frontiers) =>
        new(
            CompletedCount: frontiers.Count(f => f.Status == FrontierStatus.Completed),
            TotalCount: frontiers.Count,
            CurrentFrontierTitle: frontiers.FirstOrDefault(f => f.Status == FrontierStatus.Current)?.Title
                ?? frontiers.FirstOrDefault(f => f.Status == FrontierStatus.Next)?.Title);

    /// <summary>Explicit positive weight, or 1 when null/zero/negative.</summary>
    private static int EffectiveWeight(Milestone milestone) =>
        milestone.Weight is { } weight && weight > 0 ? weight : 1;
}
