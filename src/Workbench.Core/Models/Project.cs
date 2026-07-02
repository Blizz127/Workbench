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

    /// <summary>Paths (relative to the repository or absolute) to documentation.</summary>
    public List<string> DocumentationPaths { get; set; } = [];

    /// <summary>Paths that future AI providers should read for context. Unused in Phase 1.</summary>
    public List<string> AiContextPaths { get; set; } = [];

    /// <summary>Reserved for later phases; providers plug in as modules.</summary>
    public string? PreferredAiProvider { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>The one task currently being worked on, shown on the dashboard card.</summary>
    public string? ActiveTask { get; set; }

    /// <summary>The current objective shown in the detail view.</summary>
    public string? CurrentObjective { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
