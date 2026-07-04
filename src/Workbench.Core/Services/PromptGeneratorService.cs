using System.Text;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// A command failure from whichever history table it came from
/// (build/run, test, or a named <see cref="CommandRun"/>) — unified just
/// enough to describe it in a prompt.
/// </summary>
public sealed record FailedCommandInfo(
    string CommandName,
    string CommandText,
    DateTimeOffset StartedAt,
    int? ExitCode,
    string OutputLog);

/// <summary>Everything <see cref="IPromptGeneratorService"/> needs, gathered by the caller.</summary>
public sealed record PromptContext(
    Project Project,
    Frontier? CurrentFrontier,
    FailedCommandInfo? LastFailedCommand,
    IReadOnlyList<LogPatternMatch> LogFindings,
    RepositorySnapshot? Repository);

public interface IPromptGeneratorService
{
    /// <summary>
    /// Assembles a plain-text prompt from recorded project state. This is
    /// text generation only — nothing is sent anywhere; the caller decides
    /// what to do with the returned string (copy it, paste it, save it).
    /// </summary>
    string Generate(PromptContext context);
}

public sealed class PromptGeneratorService : IPromptGeneratorService
{
    private const int MaxLogExcerptChars = 4000;
    private const int MaxFindings = 20;

    public string Generate(PromptContext context)
    {
        var sb = new StringBuilder();
        var p = context.Project;

        sb.AppendLine($"# Task handoff — {p.Name}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(p.Description))
        {
            sb.AppendLine(p.Description);
            sb.AppendLine();
        }

        AppendRules(sb, context);
        AppendFrontier(sb, context);
        AppendFailedCommand(sb, context);
        AppendLogFindings(sb, context);
        AppendGitStatus(sb, context);

        return sb.ToString();
    }

    private static void AppendRules(StringBuilder sb, PromptContext ctx)
    {
        sb.AppendLine("## Rules");
        sb.AppendLine();
        if (string.IsNullOrWhiteSpace(ctx.Project.PromptRules))
        {
            sb.AppendLine("- No rules recorded for this project — ask before assuming scope.");
        }
        else
        {
            foreach (var line in ctx.Project.PromptRules.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    sb.AppendLine(trimmed.StartsWith('-') ? trimmed : $"- {trimmed}");
            }
        }
        sb.AppendLine();
    }

    private static void AppendFrontier(StringBuilder sb, PromptContext ctx)
    {
        sb.AppendLine("## Current frontier");
        sb.AppendLine();

        if (ctx.CurrentFrontier is not { } f)
        {
            sb.AppendLine("_No current frontier recorded._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"- **Title:** {f.Title}");
        if (!string.IsNullOrWhiteSpace(f.Symbol))
            sb.AppendLine($"- **Symbol:** `{f.Symbol}`");
        if (!string.IsNullOrWhiteSpace(f.Evidence))
            sb.AppendLine($"- **Evidence:** {f.Evidence}");
        if (!string.IsNullOrWhiteSpace(f.BlockerClass))
            sb.AppendLine($"- **Blocker class:** {f.BlockerClass}");
        if (!string.IsNullOrWhiteSpace(f.NextSafestStep))
            sb.AppendLine($"- **Suggested next safest step:** {f.NextSafestStep}");
        if (!string.IsNullOrWhiteSpace(f.Notes))
            sb.AppendLine($"- **Notes:** {f.Notes}");
        sb.AppendLine();
    }

    private static void AppendFailedCommand(StringBuilder sb, PromptContext ctx)
    {
        sb.AppendLine("## Last failed command");
        sb.AppendLine();

        if (ctx.LastFailedCommand is not { } cmd)
        {
            sb.AppendLine("_No failing command on record._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"- **Command:** `{cmd.CommandName}` — `{cmd.CommandText}`");
        sb.AppendLine($"- **When:** {cmd.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"- **Exit code:** {cmd.ExitCode?.ToString() ?? "n/a"}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(cmd.OutputLog))
        {
            sb.AppendLine("```");
            sb.AppendLine(Tail(cmd.OutputLog, MaxLogExcerptChars));
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    private static void AppendLogFindings(StringBuilder sb, PromptContext ctx)
    {
        if (ctx.LogFindings.Count == 0)
            return;

        sb.AppendLine("## Log findings");
        sb.AppendLine();
        foreach (var finding in ctx.LogFindings.Take(MaxFindings))
            sb.AppendLine($"- line {finding.LineNumber} [{finding.Category}/{finding.PatternName}]: `{finding.LineText.Trim()}`");
        if (ctx.LogFindings.Count > MaxFindings)
            sb.AppendLine($"- … {ctx.LogFindings.Count - MaxFindings} more finding(s) omitted");
        sb.AppendLine();
    }

    private static void AppendGitStatus(StringBuilder sb, PromptContext ctx)
    {
        sb.AppendLine("## Git status");
        sb.AppendLine();

        if (ctx.Repository is not { IsValid: true } repo)
        {
            sb.AppendLine("_Repository state unavailable._");
            return;
        }

        sb.AppendLine($"- **Branch:** `{repo.BranchName}`" +
                       (repo.AheadBy is { } ahead && repo.BehindBy is { } behind ? $" ({ahead} ahead / {behind} behind)" : ""));
        if (repo.LastCommit is { } c)
            sb.AppendLine($"- **HEAD:** `{c.ShortSha}` — {c.MessageSummary}");
        sb.AppendLine($"- **Working tree:** {(repo.IsDirty ? "dirty" : "clean")}");

        if (repo.ModifiedFiles.Count > 0)
        {
            sb.AppendLine("- **Files changed:**");
            foreach (var change in repo.ModifiedFiles.Take(30))
                sb.AppendLine($"  - {change.State}: `{change.Path}`");
            if (repo.ModifiedFiles.Count > 30)
                sb.AppendLine($"  - … {repo.ModifiedFiles.Count - 30} more file(s) omitted");
        }

        if (!string.IsNullOrWhiteSpace(repo.DiffStat))
            sb.AppendLine($"- **Diff stat:** {repo.DiffStat}");
    }

    private static string Tail(string output, int maxChars) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];
}
