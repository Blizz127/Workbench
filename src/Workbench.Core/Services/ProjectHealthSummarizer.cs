using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// A project's health at a glance: last build/run/matching-build outcome,
/// working-tree dirty status, and open blocker count. Every field is either
/// a plain fact (dirty, blocker count) or a straight pass-through of the most
/// recent recorded outcome — never an estimate, score, or trend.
/// </summary>
public sealed record ProjectHealthSummary(
    bool? LastBuildSucceeded,
    bool? LastRunSucceeded,
    bool? LastMatchingBuildSucceeded,
    bool IsDirty,
    int OpenBlockerCount)
{
    public bool HasOpenBlockers => OpenBlockerCount > 0;
}

public static class ProjectHealthSummarizer
{
    public static ProjectHealthSummary Summarize(
        BuildRecord? lastBuild,
        BuildRecord? lastRun,
        CommandRun? lastMatchingBuild,
        bool isDirty,
        int openBlockerCount) =>
        new(
            lastBuild?.Succeeded,
            lastRun?.Succeeded,
            lastMatchingBuild?.Succeeded,
            isDirty,
            openBlockerCount);
}
