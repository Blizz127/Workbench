namespace Workbench.Core.Models;

/// <summary>
/// A registered long-running project (decompilation, revival, research, ...).
/// Configuration fields (repository path, commands, documentation paths) can be
/// exported to / imported from a per-project <c>workbench.json</c> file.
/// </summary>
public sealed class Project
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public string? Description { get; set; }

    /// <summary>Absolute path to the local git repository, if any.</summary>
    public string? RepositoryPath { get; set; }

    public string? BuildCommand { get; set; }

    public string? RunCommand { get; set; }

    public string? TestCommand { get; set; }

    /// <summary>A build invoked specifically to check match/regression status (e.g. a decomp's "make -B build").</summary>
    public string? MatchingBuildCommand { get; set; }

    /// <summary>Paths (relative to the repository or absolute) to documentation.</summary>
    public List<string> DocumentationPaths { get; set; } = [];

    /// <summary>Paths that future AI providers should read for context. Unused in Phase 1.</summary>
    public List<string> AiContextPaths { get; set; } = [];

    /// <summary>Reserved for later phases; providers plug in as modules.</summary>
    public string? PreferredAiProvider { get; set; }

    /// <summary>Free-text constraints included verbatim in generated model prompts (e.g. "never touch original decomp source").</summary>
    public string? PromptRules { get; set; }

    /// <summary>Project-specific log patterns (e.g. a custom test harness's markers), merged with the built-in defaults.</summary>
    public List<LogPatternDefinition> CustomLogPatterns { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    /// <summary>The one task currently being worked on, shown on the dashboard card.</summary>
    public string? ActiveTask { get; set; }

    /// <summary>The current objective shown in the detail view.</summary>
    public string? CurrentObjective { get; set; }

    // Populated exclusively by the explicit "Refresh Project" action (Overview
    // tab), not by the dashboard's passive per-card git lookup — so a change
    // here always means the user asked Workbench to check.
    public string? LastKnownBranch { get; set; }
    public string? LastKnownCommitSha { get; set; }
    public bool? LastKnownIsDirty { get; set; }
    public int? LastKnownAheadBy { get; set; }
    public int? LastKnownBehindBy { get; set; }
    public DateTimeOffset? LastRefreshedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
