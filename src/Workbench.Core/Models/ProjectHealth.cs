namespace Workbench.Core.Models;

/// <summary>
/// Overall project health verdict. Decided by runtime/build/test/blocker/git
/// signals only — milestone progress never feeds into it.
/// </summary>
public enum ProjectHealthState
{
    Healthy,
    Attention,
    Blocked,
    Failing,
    Unavailable,
    Unknown,
}

/// <summary>
/// Everything the health calculator looks at, already flattened. Null fields
/// mean "no signal" (never ran, not configured) and simply do not vote.
/// </summary>
public sealed record ProjectHealthInput(
    bool RepositoryAvailable,
    RuntimeHealth? LatestRuntimeHealth,
    bool? LastBuildSucceeded,
    bool? LastTestPassed,
    /// <summary>Severity strings of ACTIVE blockers only (monitoring/dismissed are filtered upstream).</summary>
    IReadOnlyList<string?> ActiveBlockerSeverities,
    bool IsDirty,
    int AheadBy,
    int BehindBy);

/// <summary>
/// The verdict plus every deciding factor that fired, human-readable, for the
/// tooltip/details panel. <see cref="State"/> is the first matching rule;
/// <see cref="Reasons"/> lists all applicable ones for transparency.
/// </summary>
public sealed record ProjectHealthAssessment(
    ProjectHealthState State,
    IReadOnlyList<string> Reasons);
