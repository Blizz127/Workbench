using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>Dashboard filter chips.</summary>
public enum DashboardFilter
{
    All,

    /// <summary>Health verdict is Attention, Blocked, Failing, or Unavailable.</summary>
    NeedsAttention,

    /// <summary>At least one ACTIVE blocker (any severity).</summary>
    Blocked,

    /// <summary>Latest runtime verdict is Healthy or Degraded (same rule as the summary's RunningServices).</summary>
    Running,

    /// <summary>Repository missing on this machine.</summary>
    Unavailable,

    /// <summary>Working tree has uncommitted changes.</summary>
    Dirty,

    /// <summary>Any timeline/audit activity within <see cref="DashboardFilters.RecentlyActiveWindow"/>.</summary>
    RecentlyActive,
}

/// <summary>Dashboard sort orders.</summary>
public enum DashboardSort
{
    /// <summary>
    /// Health precedence first (Unavailable &gt; Failing &gt; Blocked &gt; Attention &gt;
    /// Healthy &gt; Unknown — the same order <see cref="ProjectHealthCalculator"/>
    /// evaluates), then highest active blocker severity, then most recent activity.
    /// </summary>
    NeedsAttentionFirst,

    RecentlyActive,
    Alphabetical,

    /// <summary>Weighted milestone percent, descending; unconfigured progress sorts last.</summary>
    Progress,

    /// <summary>Highest active blocker severity (critical &gt; high &gt; medium &gt; low &gt; none), then blocker count.</summary>
    BlockerSeverity,
}

/// <summary>
/// Pure filter/sort over the shared dashboard projection — the one place the
/// chip and sort-menu semantics live, so every host orders cards identically.
/// No I/O, no UI types: the same inputs always produce the same ordering.
/// </summary>
public static class DashboardFilters
{
    /// <summary>"Recently active" means activity within the last 7 days.</summary>
    public static readonly TimeSpan RecentlyActiveWindow = TimeSpan.FromDays(7);

    public static IReadOnlyList<ProjectDashboardCard> Apply(
        IReadOnlyList<ProjectDashboardCard> cards,
        DashboardFilter filter,
        DashboardSort sort,
        DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.Now;
        return Sort(cards.Where(card => Matches(card, filter, reference)), sort).ToList();
    }

    public static bool Matches(ProjectDashboardCard card, DashboardFilter filter, DateTimeOffset now) =>
        filter switch
        {
            DashboardFilter.All => true,
            DashboardFilter.NeedsAttention => card.HealthAssessment.State
                is ProjectHealthState.Attention or ProjectHealthState.Blocked
                or ProjectHealthState.Failing or ProjectHealthState.Unavailable,
            DashboardFilter.Blocked => card.ActiveBlockerCount > 0,
            DashboardFilter.Running => card.RuntimeHealth is RuntimeHealth.Healthy or RuntimeHealth.Degraded,
            DashboardFilter.Unavailable => !card.RepositoryAvailable,
            DashboardFilter.Dirty => card.IsDirty,
            DashboardFilter.RecentlyActive => card.LastActivityAt is { } at && now - at <= RecentlyActiveWindow,
            _ => true,
        };

    public static IEnumerable<ProjectDashboardCard> Sort(
        IEnumerable<ProjectDashboardCard> cards, DashboardSort sort) =>
        sort switch
        {
            DashboardSort.NeedsAttentionFirst => cards
                .OrderBy(c => HealthRank(c.HealthAssessment.State))
                .ThenByDescending(c => SeverityRank(c.HighestActiveSeverity))
                .ThenByDescending(c => c.LastActivityAt ?? DateTimeOffset.MinValue)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
            DashboardSort.RecentlyActive => cards
                .OrderByDescending(c => c.LastActivityAt ?? DateTimeOffset.MinValue)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
            DashboardSort.Alphabetical => cards
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
            DashboardSort.Progress => cards
                .OrderByDescending(c => c.MilestoneProgress is not null)
                .ThenByDescending(c => c.MilestoneProgress?.Percent ?? 0)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
            DashboardSort.BlockerSeverity => cards
                .OrderByDescending(c => SeverityRank(c.HighestActiveSeverity))
                .ThenByDescending(c => c.ActiveBlockerCount)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
            _ => cards,
        };

    /// <summary>Lower sorts first; mirrors the health calculator's rule precedence.</summary>
    private static int HealthRank(ProjectHealthState state) => state switch
    {
        ProjectHealthState.Unavailable => 0,
        ProjectHealthState.Failing => 1,
        ProjectHealthState.Blocked => 2,
        ProjectHealthState.Attention => 3,
        ProjectHealthState.Healthy => 4,
        _ => 5, // Unknown last — nothing is known to be wrong
    };

    private static int SeverityRank(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0,
    };
}
