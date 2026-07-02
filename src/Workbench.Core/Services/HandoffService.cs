using System.Text;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>Everything the handoff generator needs, gathered by the caller.</summary>
public sealed record HandoffContext(
    Project Project,
    Frontier? CurrentFrontier,
    Frontier? NextFrontier,
    IReadOnlyList<Blocker> OpenBlockers,
    Milestone? CurrentMilestone,
    IReadOnlyList<Milestone> Milestones,
    IReadOnlyList<TimelineEntry> RecentTimeline,
    BuildRecord? LastBuild,
    TestRecord? LastTest,
    RepositorySnapshot? Repository);

public interface IHandoffService
{
    /// <summary>Generates a markdown handoff document for the given project state.</summary>
    string Generate(HandoffContext context);
}

/// <summary>
/// Summarizes a project's state into a markdown document: current state, last
/// completed work, blockers, and a recommended next step. Pure function of its
/// input so it is trivially testable; in later phases the same document seeds
/// AI prompts.
/// </summary>
public sealed class HandoffService : IHandoffService
{
    public string Generate(HandoffContext context)
    {
        var p = context.Project;
        var sb = new StringBuilder();

        sb.AppendLine($"# Handoff — {p.Name}");
        sb.AppendLine();
        sb.AppendLine($"*Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm} · Status: **{p.Status}***");
        sb.AppendLine();

        AppendCurrentState(sb, context);
        AppendLastCompletedWork(sb, context);
        AppendBlockers(sb, context);
        AppendRecommendedNextStep(sb, context);

        return sb.ToString();
    }

    private static void AppendCurrentState(StringBuilder sb, HandoffContext ctx)
    {
        var p = ctx.Project;
        sb.AppendLine("## Current State");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(p.Description))
        {
            sb.AppendLine(p.Description);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(p.CurrentObjective))
            sb.AppendLine($"- **Objective:** {p.CurrentObjective}");
        if (!string.IsNullOrWhiteSpace(p.ActiveTask))
            sb.AppendLine($"- **Active task:** {p.ActiveTask}");
        if (ctx.CurrentFrontier is { } cf)
            sb.AppendLine($"- **Current frontier:** {cf.Title}");
        if (ctx.NextFrontier is { } nf)
            sb.AppendLine($"- **Next frontier:** {nf.Title}");
        if (ctx.CurrentMilestone is { } cm)
            sb.AppendLine($"- **Current milestone:** {cm.Title}");

        var done = ctx.Milestones.Count(m => m.IsCompleted);
        if (ctx.Milestones.Count > 0)
            sb.AppendLine($"- **Milestones:** {done}/{ctx.Milestones.Count} complete");

        if (ctx.Repository is { IsValid: true } repo)
        {
            sb.AppendLine($"- **Branch:** `{repo.BranchName}`" +
                          (repo.IsDirty ? $" (dirty, {repo.ModifiedFiles.Count} modified files)" : " (clean)"));
            if (repo.LastCommit is { } c)
                sb.AppendLine($"- **HEAD:** `{c.ShortSha}` — {c.MessageSummary}");
        }

        if (ctx.LastBuild is { } b)
            sb.AppendLine($"- **Last build:** {(b.Succeeded ? "succeeded" : $"failed (exit {b.ExitCode?.ToString() ?? "n/a"})")} at {b.StartedAt:yyyy-MM-dd HH:mm}");
        if (ctx.LastTest is { } t)
            sb.AppendLine($"- **Last test run:** {t.Result} at {t.StartedAt:yyyy-MM-dd HH:mm}");

        sb.AppendLine();
    }

    private static void AppendLastCompletedWork(StringBuilder sb, HandoffContext ctx)
    {
        sb.AppendLine("## Last Completed Work");
        sb.AppendLine();

        var recent = ctx.RecentTimeline.Take(8).ToList();
        if (recent.Count == 0)
        {
            sb.AppendLine("_No timeline entries yet._");
        }
        else
        {
            foreach (var e in recent)
                sb.AppendLine($"- `{e.Timestamp:MM-dd HH:mm}` **[{e.Category}]** {e.Message}");
        }

        sb.AppendLine();
    }

    private static void AppendBlockers(StringBuilder sb, HandoffContext ctx)
    {
        sb.AppendLine("## Current Blockers");
        sb.AppendLine();

        if (ctx.OpenBlockers.Count == 0)
        {
            sb.AppendLine("_None._");
        }
        else
        {
            foreach (var b in ctx.OpenBlockers)
                sb.AppendLine($"- {b.Description} _(since {b.CreatedAt:yyyy-MM-dd})_");
        }

        sb.AppendLine();
    }

    private static void AppendRecommendedNextStep(StringBuilder sb, HandoffContext ctx)
    {
        sb.AppendLine("## Recommended Next Step");
        sb.AppendLine();
        sb.AppendLine(RecommendNextStep(ctx));
        sb.AppendLine();
    }

    /// <summary>
    /// Simple rule-based recommendation, in priority order: fix a failing build,
    /// clear a blocker, resume the active task, push the current frontier, pick
    /// up the next frontier or milestone.
    /// </summary>
    public static string RecommendNextStep(HandoffContext ctx)
    {
        if (ctx.LastBuild is { } b && !b.Succeeded)
            return $"The last build failed (exit {b.ExitCode?.ToString() ?? "n/a"}). Fix the build before anything else. Command: `{b.Command}`";

        if (ctx.OpenBlockers.Count > 0)
            return $"Resolve the open blocker: {ctx.OpenBlockers[0].Description}";

        if (ctx.LastTest is { } t && t.Result != TestResult.Passed)
            return $"The last test run resulted in **{t.Result}**. Investigate before moving the frontier. Command: `{t.Command}`";

        if (!string.IsNullOrWhiteSpace(ctx.Project.ActiveTask))
            return $"Continue the active task: {ctx.Project.ActiveTask}";

        if (ctx.CurrentFrontier is { } cf)
            return $"Push the current frontier: {cf.Title}";

        if (ctx.NextFrontier is { } nf)
            return $"Start the next frontier: {nf.Title}";

        var nextMilestone = ctx.Milestones.FirstOrDefault(m => !m.IsCompleted);
        if (nextMilestone is not null)
            return $"Work toward the next milestone: {nextMilestone.Title}";

        return "No open work is recorded. Define the next frontier or milestone for this project.";
    }
}
