namespace Workbench.Core.Models;

/// <summary>
/// Milestone completion derived ONLY from explicit milestone state — never an
/// AI estimate. Dismissed milestones are excluded from both the counts and the
/// weights. <see cref="Percent"/> is the share of total weight that is Done,
/// 0–100. A null <see cref="MilestoneProgress"/> (from the calculator) means
/// "progress not configured", never 0%.
/// </summary>
public sealed record MilestoneProgress(
    int CompletedCount,
    int TotalCount,
    double Percent,
    int NotStartedCount,
    int ActiveCount,
    int BlockedCount,
    int DoneCount,
    int DismissedCount);

/// <summary>
/// Frontier checklist progress. <see cref="CurrentFrontierTitle"/> is the
/// frontier being pushed right now (Status Current, else the first Next).
/// </summary>
public sealed record FrontierProgress(
    int CompletedCount,
    int TotalCount,
    string? CurrentFrontierTitle);
