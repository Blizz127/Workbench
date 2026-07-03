namespace Workbench.Core.Models;

/// <summary>
/// A named pattern to scan command output for — a linker error, a crash
/// signal, a project-specific marker like a test harness's status line.
/// </summary>
public sealed record LogPatternDefinition(
    string Name,
    string Pattern,
    bool IsRegex = true,
    string Category = "General");

/// <summary>One line in a log that matched a <see cref="LogPatternDefinition"/>.</summary>
public sealed record LogPatternMatch(
    string PatternName,
    string Category,
    int LineNumber,
    string LineText);
