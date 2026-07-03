using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class PromptGeneratorServiceTests
{
    private readonly PromptGeneratorService _generator = new();

    private static PromptContext MinimalContext(Project? project = null) => new(
        Project: project ?? new Project { Name = "Sample Project" },
        CurrentFrontier: null,
        LastFailedCommand: null,
        LogFindings: [],
        Repository: null);

    [Fact]
    public void Generated_prompt_contains_all_required_sections()
    {
        var text = _generator.Generate(MinimalContext());

        Assert.Contains("## Rules", text);
        Assert.Contains("## Current frontier", text);
        Assert.Contains("## Last failed command", text);
        Assert.Contains("## Git status", text);
    }

    [Fact]
    public void Missing_prompt_rules_produces_an_explicit_placeholder_not_silence()
    {
        var text = _generator.Generate(MinimalContext());
        Assert.Contains("No rules recorded", text);
    }

    [Fact]
    public void Project_prompt_rules_are_rendered_as_a_bullet_list()
    {
        var project = new Project { Name = "P", PromptRules = "Never touch asm/\nConfirm via matching-build" };
        var text = _generator.Generate(MinimalContext(project));

        Assert.Contains("- Never touch asm/", text);
        Assert.Contains("- Confirm via matching-build", text);
    }

    [Fact]
    public void Current_frontier_fields_are_all_included_when_present()
    {
        var ctx = MinimalContext() with
        {
            CurrentFrontier = new Frontier
            {
                Title = "func_8001FBE4",
                Symbol = "func_8001FBE4",
                Evidence = "55% matched",
                BlockerClass = "Unknown ABI",
                NextSafestStep = "Add a diag breakpoint",
            },
        };

        var text = _generator.Generate(ctx);

        Assert.Contains("func_8001FBE4", text);
        Assert.Contains("55% matched", text);
        Assert.Contains("Unknown ABI", text);
        Assert.Contains("Add a diag breakpoint", text);
    }

    [Fact]
    public void Last_failed_command_includes_a_bounded_log_excerpt()
    {
        var ctx = MinimalContext() with
        {
            LastFailedCommand = new FailedCommandInfo(
                "matching-build", "make -B build", DateTimeOffset.Now, 2, "undefined reference to `foo'"),
        };

        var text = _generator.Generate(ctx);

        Assert.Contains("matching-build", text);
        Assert.Contains("make -B build", text);
        Assert.Contains("undefined reference to `foo'", text);
    }

    [Fact]
    public void Log_findings_are_listed_with_line_numbers_and_category()
    {
        var ctx = MinimalContext() with
        {
            LogFindings = [new LogPatternMatch("SIGSEGV", "Crash", 42, "  SIGSEGV")],
        };

        var text = _generator.Generate(ctx);

        Assert.Contains("## Log findings", text);
        Assert.Contains("line 42", text);
        Assert.Contains("Crash/SIGSEGV", text);
    }

    [Fact]
    public void No_log_findings_section_when_there_are_none()
    {
        var text = _generator.Generate(MinimalContext());
        Assert.DoesNotContain("## Log findings", text);
    }

    [Fact]
    public void Git_status_reports_branch_ahead_behind_and_files_changed()
    {
        var repo = new RepositorySnapshot(
            IsValid: true,
            BranchName: "main",
            LastCommit: new CommitInfo("abc1234567", "abc1234", "fix thing", "dev", DateTimeOffset.Now),
            RecentCommits: [],
            ModifiedFiles: [new FileChange("src/foo.c", "Modified")],
            IsDirty: true,
            Error: null,
            AheadBy: 2,
            BehindBy: 0,
            DiffStat: "1 file(s) changed, +5/-1");

        var text = _generator.Generate(MinimalContext() with { Repository = repo });

        Assert.Contains("main", text);
        Assert.Contains("2 ahead", text);
        Assert.Contains("src/foo.c", text);
        Assert.Contains("+5/-1", text);
    }

    [Fact]
    public void Invalid_repository_snapshot_is_reported_as_unavailable()
    {
        var text = _generator.Generate(MinimalContext() with { Repository = RepositorySnapshot.Invalid("no repo") });
        Assert.Contains("Repository state unavailable", text);
    }
}
