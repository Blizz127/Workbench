using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// The desktop dashboard's filter chips and sort menu operate on the shared
/// projection via <see cref="DashboardFilters"/> — every chip selects the
/// right cards and every sort orders by the documented rule.
/// </summary>
public sealed class DashboardFiltersTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static long _nextId;

    private static ProjectDashboardCard Card(
        string name,
        ProjectHealthState health = ProjectHealthState.Healthy,
        bool repoAvailable = true,
        bool dirty = false,
        int activeBlockers = 0,
        string? severity = null,
        RuntimeHealth? runtime = null,
        DateTimeOffset? lastActivity = null,
        double? progressPercent = null) =>
        new(
            ProjectId: ++_nextId,
            Name: name,
            AccentOrIcon: name[..1],
            RepositoryAvailable: repoAvailable,
            BranchName: "main",
            IsDirty: dirty,
            AheadBy: 0,
            BehindBy: 0,
            Status: ProjectStatus.Active,
            CurrentFrontierTitle: null,
            NextSafestStep: null,
            MilestoneProgress: progressPercent is { } percent
                ? new MilestoneProgress(1, 2, percent, 1, 0, 0, 1, 0)
                : null,
            FrontierProgress: new FrontierProgress(0, 0, null),
            ActiveBlockerCount: activeBlockers,
            MonitoringBlockerCount: 0,
            HighestActiveSeverity: severity,
            LastBuildResult: null,
            LastTestResult: null,
            LastLaunchResult: null,
            RuntimeHealth: runtime,
            LastActivityAt: lastActivity,
            HealthAssessment: new ProjectHealthAssessment(health, []),
            PrimaryAction: DashboardPrimaryAction.OpenProject);

    private static IReadOnlyList<ProjectDashboardCard> Apply(
        DashboardFilter filter, DashboardSort sort = DashboardSort.Alphabetical, params ProjectDashboardCard[] cards) =>
        DashboardFilters.Apply(cards, filter, sort, Now);

    // ---------- filters ----------

    [Fact]
    public void filter_all_keeps_everything()
    {
        var cards = new[] { Card("a"), Card("b", ProjectHealthState.Unavailable, repoAvailable: false) };

        Assert.Equal(2, Apply(DashboardFilter.All, cards: cards).Count);
    }

    [Fact]
    public void filter_needs_attention_keeps_the_four_attention_states_only()
    {
        var attention = Card("attention", ProjectHealthState.Attention);
        var blocked = Card("blocked", ProjectHealthState.Blocked, activeBlockers: 1, severity: "critical");
        var failing = Card("failing", ProjectHealthState.Failing);
        var unavailable = Card("unavailable", ProjectHealthState.Unavailable, repoAvailable: false);
        var healthy = Card("healthy", ProjectHealthState.Healthy);
        var unknown = Card("unknown", ProjectHealthState.Unknown);

        var result = Apply(DashboardFilter.NeedsAttention,
            cards: [attention, blocked, failing, unavailable, healthy, unknown]);

        Assert.Equal(
            ["attention", "blocked", "failing", "unavailable"],
            result.Select(c => c.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void filter_blocked_means_any_active_blocker_not_just_the_blocked_verdict()
    {
        var severe = Card("severe", ProjectHealthState.Blocked, activeBlockers: 2, severity: "high");
        var minor = Card("minor", ProjectHealthState.Attention, activeBlockers: 1, severity: "low");
        var clear = Card("clear", ProjectHealthState.Healthy);

        var result = Apply(DashboardFilter.Blocked, cards: [severe, minor, clear]);

        Assert.Equal(["minor", "severe"], result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void filter_running_matches_the_summary_running_services_rule()
    {
        var healthy = Card("healthy", runtime: RuntimeHealth.Healthy);
        var degraded = Card("degraded", runtime: RuntimeHealth.Degraded);
        var starting = Card("starting", runtime: RuntimeHealth.Starting);
        var failed = Card("failed", runtime: RuntimeHealth.Failed);
        var unknown = Card("unknown");

        var result = Apply(DashboardFilter.Running, cards: [healthy, degraded, starting, failed, unknown]);

        Assert.Equal(["degraded", "healthy"], result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void filter_unavailable_and_dirty_select_on_git_state()
    {
        var missing = Card("missing", ProjectHealthState.Unavailable, repoAvailable: false);
        var dirty = Card("dirty", dirty: true);
        var clean = Card("clean");

        Assert.Equal(["missing"], Apply(DashboardFilter.Unavailable, cards: [missing, dirty, clean]).Select(c => c.Name));
        Assert.Equal(["dirty"], Apply(DashboardFilter.Dirty, cards: [missing, dirty, clean]).Select(c => c.Name));
    }

    [Fact]
    public void filter_recently_active_uses_the_seven_day_window()
    {
        var fresh = Card("fresh", lastActivity: Now.AddDays(-2));
        var stale = Card("stale", lastActivity: Now.AddDays(-30));
        var never = Card("never");

        var result = Apply(DashboardFilter.RecentlyActive, cards: [fresh, stale, never]);

        Assert.Equal(["fresh"], result.Select(c => c.Name).ToArray());
    }

    // ---------- sorts ----------

    [Fact]
    public void sort_needs_attention_first_follows_the_health_precedence()
    {
        var result = DashboardFilters.Apply(
            [
                Card("healthy", ProjectHealthState.Healthy),
                Card("unknown", ProjectHealthState.Unknown),
                Card("attention", ProjectHealthState.Attention),
                Card("blocked", ProjectHealthState.Blocked),
                Card("unavailable", ProjectHealthState.Unavailable, repoAvailable: false),
                Card("failing", ProjectHealthState.Failing),
            ],
            DashboardFilter.All, DashboardSort.NeedsAttentionFirst, Now);

        // The same precedence the health calculator evaluates:
        // Unavailable > Failing > Blocked > Attention > Healthy > Unknown.
        Assert.Equal(
            ["unavailable", "failing", "blocked", "attention", "healthy", "unknown"],
            result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void sort_needs_attention_first_breaks_ties_by_severity_then_recent_activity()
    {
        var low = Card("low", ProjectHealthState.Attention, severity: "low", lastActivity: Now.AddDays(-1));
        var critical = Card("critical", ProjectHealthState.Attention, severity: "critical", lastActivity: Now.AddDays(-5));
        var none = Card("none", ProjectHealthState.Attention, lastActivity: Now);

        var result = DashboardFilters.Apply(
            [low, none, critical], DashboardFilter.All, DashboardSort.NeedsAttentionFirst, Now);

        Assert.Equal(["critical", "low", "none"], result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void sort_recently_active_orders_newest_first_nulls_last()
    {
        var older = Card("older", lastActivity: Now.AddDays(-3));
        var newer = Card("newer", lastActivity: Now.AddHours(-1));
        var never = Card("never");

        var result = DashboardFilters.Apply(
            [older, never, newer], DashboardFilter.All, DashboardSort.RecentlyActive, Now);

        Assert.Equal(["newer", "older", "never"], result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void sort_alphabetical_is_case_insensitive()
    {
        var result = DashboardFilters.Apply(
            [Card("beta"), Card("Alpha"), Card("gamma")],
            DashboardFilter.All, DashboardSort.Alphabetical, Now);

        Assert.Equal(["Alpha", "beta", "gamma"], result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void sort_progress_orders_by_weighted_percent_unconfigured_last()
    {
        var result = DashboardFilters.Apply(
            [Card("half", progressPercent: 50), Card("none"), Card("full", progressPercent: 100)],
            DashboardFilter.All, DashboardSort.Progress, Now);

        Assert.Equal(["full", "half", "none"], result.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void sort_blocker_severity_orders_critical_first_then_count()
    {
        var twoLow = Card("two-low", severity: "low", activeBlockers: 2);
        var oneHigh = Card("one-high", severity: "high", activeBlockers: 1);
        var none = Card("none");

        var result = DashboardFilters.Apply(
            [twoLow, none, oneHigh], DashboardFilter.All, DashboardSort.BlockerSeverity, Now);

        Assert.Equal(["one-high", "two-low", "none"], result.Select(c => c.Name).ToArray());
    }
}
