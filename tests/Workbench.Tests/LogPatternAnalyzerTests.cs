using Workbench.Core.Models;
using Workbench.Core.Services;
using Xunit;

namespace Workbench.Tests;

public sealed class LogPatternAnalyzerTests
{
    private readonly LogPatternAnalyzer _analyzer = new();

    [Fact]
    public void Detects_builtin_linker_and_crash_patterns_with_correct_line_numbers()
    {
        var log = "compiling foo.c\nundefined reference to `bar_func'\nmake: *** Error 1\nSIGSEGV\n";

        var matches = _analyzer.Analyze(log);

        Assert.Contains(matches, m => m.PatternName == "undefined reference" && m.LineNumber == 2);
        Assert.Contains(matches, m => m.PatternName == "SIGSEGV" && m.LineNumber == 4);
    }

    [Fact]
    public void Clean_log_produces_no_matches()
    {
        var matches = _analyzer.Analyze("Build complete.\nAll tests passed.\n");
        Assert.Empty(matches);
    }

    [Fact]
    public void Empty_or_null_log_produces_no_matches()
    {
        Assert.Empty(_analyzer.Analyze(""));
        Assert.Empty(_analyzer.Analyze(null!));
    }

    [Fact]
    public void Custom_project_patterns_are_merged_with_builtin_defaults()
    {
        var custom = new List<LogPatternDefinition>
        {
            new("RUN_RC=", @"RUN_RC=", Category: "TestHarness"),
            new("field-diag", @"field-diag", Category: "FieldEngine"),
        };
        var log = "field-diag: entering ParsePrimitive\nundefined reference to `xyz'\nRUN_RC=134\n";

        var matches = _analyzer.Analyze(log, custom);

        Assert.Contains(matches, m => m.PatternName == "field-diag");
        Assert.Contains(matches, m => m.PatternName == "RUN_RC=");
        Assert.Contains(matches, m => m.PatternName == "undefined reference"); // built-in still applies
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var matches = _analyzer.Analyze("FATAL ERROR: something broke\n");
        Assert.Contains(matches, m => m.PatternName == "fatal error");
    }

    [Fact]
    public void Non_regex_custom_pattern_is_matched_literally()
    {
        var custom = new List<LogPatternDefinition> { new("literal-brackets", "[stub]", IsRegex: false) };
        var matches = _analyzer.Analyze("call site hits [stub] handler\n", custom);
        Assert.Contains(matches, m => m.PatternName == "literal-brackets");
    }

    [Fact]
    public void Multiple_matches_on_the_same_line_are_all_reported()
    {
        var matches = _analyzer.Analyze("SIGSEGV then Aborted\n");
        Assert.Contains(matches, m => m.PatternName == "SIGSEGV");
        Assert.Contains(matches, m => m.PatternName == "Aborted");
    }
}
