using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Deterministic health precedence: first matching rule decides the state,
/// every firing rule adds a human-readable reason. Health never reads
/// milestone state.
/// </summary>
public sealed class ProjectHealthCalculatorTests
{
    private static readonly ProjectHealthInput Quiet = new(
        RepositoryAvailable: true,
        LatestRuntimeHealth: null,
        LastBuildSucceeded: null,
        LastTestPassed: null,
        ActiveBlockerSeverities: [],
        IsDirty: false,
        AheadBy: 0,
        BehindBy: 0);

    private static ProjectHealthInput With(
        bool repositoryAvailable = true,
        RuntimeHealth? runtime = null,
        bool? build = null,
        bool? test = null,
        IReadOnlyList<string?>? blockers = null,
        bool dirty = false,
        int ahead = 0,
        int behind = 0) =>
        new(repositoryAvailable, runtime, build, test, blockers ?? [], dirty, ahead, behind);

    [Fact]
    public void Missing_repository_is_unavailable()
    {
        var assessment = ProjectHealthCalculator.Assess(With(repositoryAvailable: false));
        Assert.Equal(ProjectHealthState.Unavailable, assessment.State);
        Assert.Contains(assessment.Reasons, r => r.Contains("Repository", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_repository_beats_every_other_signal()
    {
        var assessment = ProjectHealthCalculator.Assess(With(
            repositoryAvailable: false, runtime: RuntimeHealth.Healthy, build: true,
            blockers: ["critical"], dirty: true));

        Assert.Equal(ProjectHealthState.Unavailable, assessment.State);
        Assert.Equal("Repository is missing on this machine", assessment.Reasons[0]);
    }

    [Fact]
    public void Failed_runtime_health_is_failing()
    {
        var assessment = ProjectHealthCalculator.Assess(With(runtime: RuntimeHealth.Failed));
        Assert.Equal(ProjectHealthState.Failing, assessment.State);
        Assert.Contains(assessment.Reasons, r => r.Contains("runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Failed_build_or_failed_tests_are_failing()
    {
        Assert.Equal(ProjectHealthState.Failing, ProjectHealthCalculator.Assess(With(build: false)).State);
        Assert.Equal(ProjectHealthState.Failing, ProjectHealthCalculator.Assess(With(test: false)).State);
    }

    [Fact]
    public void Failed_build_outranks_a_critical_blocker()
    {
        var assessment = ProjectHealthCalculator.Assess(With(build: false, blockers: ["critical"]));

        Assert.Equal(ProjectHealthState.Failing, assessment.State);
        // ...but the blocker still shows up in the reasons for transparency.
        Assert.Equal(2, assessment.Reasons.Count);
        Assert.Contains(assessment.Reasons, r => r.Contains("build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Reasons, r => r.Contains("critical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Critical_and_high_severity_blockers_are_blocking_case_insensitively()
    {
        Assert.Equal(ProjectHealthState.Blocked, ProjectHealthCalculator.Assess(With(blockers: ["CRITICAL"])).State);
        Assert.Equal(ProjectHealthState.Blocked, ProjectHealthCalculator.Assess(With(blockers: ["High"])).State);

        var lowerFirst = ProjectHealthCalculator.Assess(With(blockers: ["low", "critical"]));
        Assert.Equal(ProjectHealthState.Blocked, lowerFirst.State);
        Assert.Contains(lowerFirst.Reasons, r => r.Contains("critical"));
    }

    [Fact]
    public void Lower_or_unknown_severity_blockers_are_only_attention()
    {
        Assert.Equal(ProjectHealthState.Attention, ProjectHealthCalculator.Assess(With(blockers: ["medium"])).State);
        Assert.Equal(ProjectHealthState.Attention, ProjectHealthCalculator.Assess(With(blockers: ["trivial"])).State);
        Assert.Equal(ProjectHealthState.Attention, ProjectHealthCalculator.Assess(With(blockers: [null])).State);
    }

    [Fact]
    public void Dirty_or_diverged_repository_is_attention()
    {
        Assert.Equal(ProjectHealthState.Attention, ProjectHealthCalculator.Assess(With(dirty: true)).State);
        Assert.Equal(ProjectHealthState.Attention, ProjectHealthCalculator.Assess(With(ahead: 3)).State);
        Assert.Equal(ProjectHealthState.Attention, ProjectHealthCalculator.Assess(With(behind: 2)).State);
    }

    [Fact]
    public void Explicit_healthy_evidence_is_healthy()
    {
        Assert.Equal(ProjectHealthState.Healthy, ProjectHealthCalculator.Assess(With(runtime: RuntimeHealth.Healthy)).State);
        Assert.Equal(ProjectHealthState.Healthy, ProjectHealthCalculator.Assess(With(build: true)).State);
        Assert.Equal(ProjectHealthState.Healthy, ProjectHealthCalculator.Assess(With(test: true)).State);
    }

    [Fact]
    public void No_signals_is_unknown()
    {
        var assessment = ProjectHealthCalculator.Assess(Quiet);
        Assert.Equal(ProjectHealthState.Unknown, assessment.State);
        Assert.Single(assessment.Reasons);
    }

    [Fact]
    public void Reasons_list_the_deciding_factors_in_plain_language()
    {
        var assessment = ProjectHealthCalculator.Assess(With(build: false, blockers: ["medium"], dirty: true));

        Assert.Equal(ProjectHealthState.Failing, assessment.State);
        Assert.Equal("Last build failed", assessment.Reasons[0]);
        Assert.Contains(assessment.Reasons, r => r.Contains("blocker", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Reasons, r => r.Contains("uncommitted", StringComparison.OrdinalIgnoreCase));
        Assert.All(assessment.Reasons, r => Assert.False(string.IsNullOrWhiteSpace(r)));
    }
}
