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

    /// <summary>How to validate runtime health after the run command succeeds.</summary>
    public LaunchProfile Launch { get; set; } = new();

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

    /// <summary>
    /// Stable cross-machine identity used by shared state files
    /// (<c>projects/*.workbench.json</c> in the shared folder). Assigned on
    /// first export or import; local-only projects keep <c>null</c>.
    /// </summary>
    public string? SharedId { get; set; }

    /// <summary>Category of the current blocker (e.g. "camera math", "protocol unknown").</summary>
    public string? BlockerClass { get; set; }

    /// <summary>Evidence backing the current state (logs, control matrices, readbacks).</summary>
    public string? Evidence { get; set; }

    /// <summary>The next safest step to take on the project.</summary>
    public string? NextSafestStep { get; set; }

    /// <summary>Standing rules to include in AI prompts for this project.</summary>
    public List<string> PromptRules { get; set; } = [];

    /// <summary>Log-line patterns of interest (guards, markers) for this project.</summary>
    public List<string> CustomLogPatterns { get; set; } = [];

    public string? GitHubRepoUrl { get; set; }

    public string? DefaultBranch { get; set; }

    /// <summary>When this project last applied a shared state file. Local-only; not exported.</summary>
    public DateTimeOffset? SharedLastImportedAt { get; set; }

    /// <summary>When this project last wrote its shared state file. Local-only; not exported.</summary>
    public DateTimeOffset? SharedLastExportedAt { get; set; }

    /// <summary>
    /// The shared file's <c>updatedAt</c> as of the last import/export — the
    /// baseline for detecting that the shared file changed under us.
    /// </summary>
    public DateTimeOffset? SharedLastSyncedStamp { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
