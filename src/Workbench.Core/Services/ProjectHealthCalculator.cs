using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Pure, deterministic project health verdict. Rules are evaluated in a fixed
/// precedence order; the state is the FIRST matching rule, while the reasons
/// list collects every rule that fired so the UI can explain the verdict.
/// Health never reads milestone state — progress and health are independent.
/// </summary>
public static class ProjectHealthCalculator
{
    public static ProjectHealthAssessment Assess(ProjectHealthInput input)
    {
        List<(ProjectHealthState State, string Reason)> matches = [];

        // 1. The repository is the prerequisite for everything else.
        if (!input.RepositoryAvailable)
            matches.Add((ProjectHealthState.Unavailable, "Repository is missing on this machine"));

        // 2/3. Hard failures: a failing runtime, build, or test run.
        if (input.LatestRuntimeHealth == RuntimeHealth.Failed)
            matches.Add((ProjectHealthState.Failing, "Latest runtime health check failed"));
        if (input.LastBuildSucceeded == false)
            matches.Add((ProjectHealthState.Failing, "Last build failed"));
        if (input.LastTestPassed == false)
            matches.Add((ProjectHealthState.Failing, "Last test run failed"));

        // 4. Severe active blockers (severity match is case-insensitive).
        foreach (var severity in SevereBlockerSeverities(input.ActiveBlockerSeverities))
            matches.Add((ProjectHealthState.Blocked, $"Active blocker with {severity} severity"));

        // 5. Warning signs: lower-severity/unknown-severity blockers or git drift.
        var lowerBlockers = input.ActiveBlockerSeverities.Count(s => !IsSevere(s));
        if (lowerBlockers > 0)
            matches.Add((ProjectHealthState.Attention, $"{lowerBlockers} active blocker(s)"));
        if (input.IsDirty)
            matches.Add((ProjectHealthState.Attention, "Working tree has uncommitted changes"));
        if (input.AheadBy > 0)
            matches.Add((ProjectHealthState.Attention, $"Repository is {input.AheadBy} commit(s) ahead of upstream"));
        if (input.BehindBy > 0)
            matches.Add((ProjectHealthState.Attention, $"Repository is {input.BehindBy} commit(s) behind upstream"));

        // 6. Explicit healthy evidence.
        if (input.LatestRuntimeHealth == RuntimeHealth.Healthy)
            matches.Add((ProjectHealthState.Healthy, "Runtime is healthy"));
        if (input.LastBuildSucceeded == true)
            matches.Add((ProjectHealthState.Healthy, "Last build succeeded"));
        if (input.LastTestPassed == true)
            matches.Add((ProjectHealthState.Healthy, "Last tests passed"));

        // 7. Nothing to go on.
        if (matches.Count == 0)
            return new ProjectHealthAssessment(ProjectHealthState.Unknown, ["No health signals available"]);

        return new ProjectHealthAssessment(matches[0].State, [.. matches.Select(m => m.Reason)]);
    }

    /// <summary>Distinct severe blocker severities present, normalized to lowercase, critical first.</summary>
    private static IEnumerable<string> SevereBlockerSeverities(IReadOnlyList<string?> severities) =>
        severities.Where(IsSevere)
            .Select(s => s!.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(s => s == "critical" ? 0 : 1);

    /// <summary>Only "critical"/"high" (case-insensitive); null or unknown severities count as lower.</summary>
    private static bool IsSevere(string? severity) =>
        string.Equals(severity?.Trim(), "critical", StringComparison.OrdinalIgnoreCase)
        || string.Equals(severity?.Trim(), "high", StringComparison.OrdinalIgnoreCase);
}
