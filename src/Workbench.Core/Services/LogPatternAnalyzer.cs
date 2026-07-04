using System.Text.RegularExpressions;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>Scans command output for known failure signatures.</summary>
public interface ILogPatternAnalyzer
{
    /// <summary>
    /// Matches <paramref name="log"/> against the built-in default patterns
    /// plus any <paramref name="customPatterns"/> a project defines. Returns
    /// matches in the order they appear in the log.
    /// </summary>
    IReadOnlyList<LogPatternMatch> Analyze(string log, IEnumerable<LogPatternDefinition>? customPatterns = null);
}

/// <summary>
/// Built-in patterns cover generic C/C++ toolchain and crash signatures that
/// apply across most native decompilation, emulation, and server-revival
/// projects. Project-specific markers (a custom test harness's status line,
/// a project's internal function names) are supplied per-project via
/// <see cref="Project.CustomLogPatterns"/>.
/// </summary>
public sealed class LogPatternAnalyzer : ILogPatternAnalyzer
{
    public static readonly IReadOnlyList<LogPatternDefinition> DefaultPatterns =
    [
        new("undefined reference", @"undefined reference to", Category: "Linker"),
        new("multiple definition", @"multiple definition of", Category: "Linker"),
        new("discarded section", @"discards section", Category: "Linker"),
        new("relocation truncated", @"relocation truncated to fit", Category: "Linker"),
        new("small-data section too large", @"small-data section.*too large|SDA.*overflow", Category: "Linker"),
        new("fatal error", @"fatal error:", Category: "Compiler"),
        new("SIGSEGV", @"\bSIGSEGV\b", Category: "Crash"),
        new("Aborted", @"\bAborted\b", Category: "Crash"),
        new("assert", @"\bassert(ion)?\s+.*failed\b|\bAssertion\b.*failed", Category: "Crash"),
    ];

    public IReadOnlyList<LogPatternMatch> Analyze(string log, IEnumerable<LogPatternDefinition>? customPatterns = null)
    {
        if (string.IsNullOrEmpty(log))
            return [];

        var patterns = customPatterns is null
            ? DefaultPatterns
            : DefaultPatterns.Concat(customPatterns).ToList();

        var compiled = patterns
            .Select(p => (p.Name, p.Category, Regex: BuildRegex(p)))
            .ToList();

        var matches = new List<LogPatternMatch>();
        var lines = log.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var (name, category, regex) in compiled)
            {
                if (regex.IsMatch(line))
                    matches.Add(new LogPatternMatch(name, category, i + 1, line.TrimEnd('\r')));
            }
        }

        return matches;
    }

    private static Regex BuildRegex(LogPatternDefinition definition)
    {
        var pattern = definition.IsRegex ? definition.Pattern : Regex.Escape(definition.Pattern);
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
