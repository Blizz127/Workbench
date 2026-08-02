using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Progress derives ONLY from explicit milestone/checklist state: no
/// milestones means "not configured" (null), never 0%; dismissed milestones
/// leave the denominator; blocked/active/not-started never count as done.
/// </summary>
public sealed class ProgressCalculatorTests
{
    private static Milestone M(MilestoneStatus status, int? weight = null) =>
        new() { Title = status.ToString(), Status = status, Weight = weight };

    [Fact]
    public void No_milestones_means_progress_not_configured()
    {
        Assert.Null(ProgressCalculator.Calculate([]));
    }

    [Fact]
    public void Only_dismissed_milestones_means_progress_not_configured()
    {
        Assert.Null(ProgressCalculator.Calculate([M(MilestoneStatus.Dismissed), M(MilestoneStatus.Dismissed)]));
    }

    [Fact]
    public void Equal_weights_give_a_plain_fraction()
    {
        var progress = ProgressCalculator.Calculate(
            [M(MilestoneStatus.Done), M(MilestoneStatus.NotStarted), M(MilestoneStatus.Active), M(MilestoneStatus.Blocked)]);

        Assert.NotNull(progress);
        Assert.Equal(1, progress.CompletedCount);
        Assert.Equal(4, progress.TotalCount);
        Assert.Equal(25.0, progress.Percent, precision: 6);
    }

    [Fact]
    public void Explicit_weights_scale_each_milestones_share()
    {
        var progress = ProgressCalculator.Calculate(
            [M(MilestoneStatus.Done, weight: 3), M(MilestoneStatus.NotStarted, weight: 1)]);

        Assert.NotNull(progress);
        Assert.Equal(75.0, progress.Percent, precision: 6);
    }

    [Fact]
    public void Null_zero_and_negative_weights_count_as_one()
    {
        var progress = ProgressCalculator.Calculate(
            [M(MilestoneStatus.Done), M(MilestoneStatus.NotStarted, weight: 0), M(MilestoneStatus.Active, weight: -5)]);

        Assert.NotNull(progress);
        Assert.Equal(3, progress.TotalCount);
        Assert.Equal(100.0 / 3.0, progress.Percent, precision: 6);
    }

    [Fact]
    public void Blocked_active_and_not_started_never_count_as_complete()
    {
        var progress = ProgressCalculator.Calculate(
            [M(MilestoneStatus.Blocked), M(MilestoneStatus.Active), M(MilestoneStatus.NotStarted)]);

        Assert.NotNull(progress);
        Assert.Equal(0, progress.CompletedCount);
        Assert.Equal(0.0, progress.Percent);
        Assert.Equal(1, progress.BlockedCount);
        Assert.Equal(1, progress.ActiveCount);
        Assert.Equal(1, progress.NotStartedCount);
        Assert.Equal(0, progress.DoneCount);
    }

    [Fact]
    public void Dismissed_milestones_are_excluded_from_counts_and_weights()
    {
        var progress = ProgressCalculator.Calculate(
            [M(MilestoneStatus.Done), M(MilestoneStatus.NotStarted), M(MilestoneStatus.Dismissed, weight: 100)]);

        Assert.NotNull(progress);
        Assert.Equal(2, progress.TotalCount);
        Assert.Equal(1, progress.CompletedCount);
        Assert.Equal(50.0, progress.Percent, precision: 6); // the 100-weight dismissed row must not dilute
        Assert.Equal(1, progress.DismissedCount);
    }

    [Fact]
    public void Per_status_counts_feed_the_progress_page_groups()
    {
        var progress = ProgressCalculator.Calculate(
        [
            M(MilestoneStatus.Done), M(MilestoneStatus.Done),
            M(MilestoneStatus.Active), M(MilestoneStatus.Blocked),
            M(MilestoneStatus.NotStarted), M(MilestoneStatus.Dismissed),
        ]);

        Assert.NotNull(progress);
        Assert.Equal(2, progress.DoneCount);
        Assert.Equal(1, progress.ActiveCount);
        Assert.Equal(1, progress.BlockedCount);
        Assert.Equal(1, progress.NotStartedCount);
        Assert.Equal(1, progress.DismissedCount);
        Assert.Equal(5, progress.TotalCount);
    }

    [Fact]
    public void Frontier_progress_counts_completed_and_finds_the_current_edge()
    {
        var progress = ProgressCalculator.CalculateFrontiers(
        [
            new Frontier { Title = "boot", Status = FrontierStatus.Completed },
            new Frontier { Title = "lobby protocol", Status = FrontierStatus.Current },
            new Frontier { Title = "matchmaking", Status = FrontierStatus.Next },
        ]);

        Assert.Equal(1, progress.CompletedCount);
        Assert.Equal(3, progress.TotalCount);
        Assert.Equal("lobby protocol", progress.CurrentFrontierTitle);
    }

    [Fact]
    public void Current_frontier_wins_over_next_and_next_is_the_fallback()
    {
        var withCurrent = ProgressCalculator.CalculateFrontiers(
        [
            new Frontier { Title = "queued", Status = FrontierStatus.Next },
            new Frontier { Title = "pushing", Status = FrontierStatus.Current },
        ]);
        Assert.Equal("pushing", withCurrent.CurrentFrontierTitle);

        var nextOnly = ProgressCalculator.CalculateFrontiers(
            [new Frontier { Title = "queued", Status = FrontierStatus.Next }]);
        Assert.Equal("queued", nextOnly.CurrentFrontierTitle);

        var none = ProgressCalculator.CalculateFrontiers(
            [new Frontier { Title = "shipped", Status = FrontierStatus.Completed }]);
        Assert.Null(none.CurrentFrontierTitle);
    }
}
