using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class HandoffServiceTests
{
    private static HandoffContext EmptyContext(Project? project = null) => new(
        Project: project ?? new Project { Name = "P" },
        CurrentFrontier: null,
        NextFrontier: null,
        OpenBlockers: [],
        CurrentMilestone: null,
        Milestones: [],
        RecentTimeline: [],
        LastBuild: null,
        LastTest: null,
        Repository: null);

    [Fact]
    public void Generated_document_contains_all_required_sections()
    {
        var markdown = new HandoffService().Generate(EmptyContext());

        Assert.Contains("## Current State", markdown);
        Assert.Contains("## Last Completed Work", markdown);
        Assert.Contains("## Current Blockers", markdown);
        Assert.Contains("## Recommended Next Step", markdown);
    }

    [Fact]
    public void Failing_build_outranks_everything_in_the_recommendation()
    {
        var ctx = EmptyContext() with
        {
            LastBuild = new BuildRecord { Command = "make", ExitCode = 2 },
            OpenBlockers = [new Blocker { Description = "some blocker" }],
            CurrentFrontier = new Frontier { Title = "frontier" },
        };

        var step = HandoffService.RecommendNextStep(ctx);
        Assert.Contains("build failed", step, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_blocker_outranks_frontier()
    {
        var ctx = EmptyContext() with
        {
            OpenBlockers = [new Blocker { Description = "opcode 0xB7 unknown" }],
            CurrentFrontier = new Frontier { Title = "frontier" },
        };

        var step = HandoffService.RecommendNextStep(ctx);
        Assert.Contains("opcode 0xB7 unknown", step);
    }

    [Fact]
    public void Current_frontier_recommended_when_nothing_is_broken()
    {
        var ctx = EmptyContext() with
        {
            LastBuild = new BuildRecord { Command = "make", ExitCode = 0 },
            CurrentFrontier = new Frontier { Title = "func_8001FBE4" },
        };

        var step = HandoffService.RecommendNextStep(ctx);
        Assert.Contains("func_8001FBE4", step);
    }

    [Fact]
    public void Next_milestone_is_the_fallback()
    {
        var ctx = EmptyContext() with
        {
            Milestones =
            [
                new Milestone { Title = "done already", IsCompleted = true },
                new Milestone { Title = "the next goal" },
            ],
        };

        var step = HandoffService.RecommendNextStep(ctx);
        Assert.Contains("the next goal", step);
    }

    [Fact]
    public void Empty_project_still_yields_a_recommendation()
    {
        var step = HandoffService.RecommendNextStep(EmptyContext());
        Assert.False(string.IsNullOrWhiteSpace(step));
    }
}
