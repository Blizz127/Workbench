namespace Workbench.Core.Models;

/// <summary>
/// The cross-machine, file-based representation of one project, stored as
/// <c>&lt;slug&gt;.workbench.json</c> inside the shared state folder's
/// <c>projects/</c> subfolder. The shared folder is the user's Dropbox folder
/// (<c>~/Dropbox/workbench</c> by default), synced externally by Dropbox —
/// Workbench never talks to a sync service directly.
///
/// Shared state carries configuration and progress shape only. Local history
/// (command runs, logs, timeline, dirty state) never leaves the local SQLite
/// database.
/// </summary>
public sealed class SharedProjectState
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Stable identity across machines; prevents duplicates on repeated imports.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Maps to <see cref="Project.CurrentObjective"/>.</summary>
    public string? Objective { get; set; }

    /// <summary>String form of <see cref="ProjectStatus"/> so the file stays readable/diffable.</summary>
    public string Status { get; set; } = nameof(ProjectStatus.Active);

    public string? ActiveTask { get; set; }

    /// <summary>Title of the current frontier at export time (display convenience; the checklist is authoritative).</summary>
    public string? CurrentFrontier { get; set; }

    public string? BlockerClass { get; set; }

    public string? Evidence { get; set; }

    public string? NextSafestStep { get; set; }

    /// <summary>Maps to <see cref="Project.PreferredAiProvider"/> (assigned/preferred model).</summary>
    public string? AssignedModel { get; set; }

    public List<string> PromptRules { get; set; } = [];

    public List<string> AiContextPaths { get; set; } = [];

    public List<string> DocumentationPaths { get; set; } = [];

    public SharedCommandTemplates CommandTemplates { get; set; } = new();

    public List<string> CustomLogPatterns { get; set; } = [];

    public List<SharedMilestone> Milestones { get; set; } = [];

    public List<SharedFrontier> FrontierChecklist { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public string? GitHubRepoUrl { get; set; }

    public string? DefaultBranch { get; set; }

    /// <summary>Per-machine local facts; the repository path differs on every PC.</summary>
    public List<MachineProfile> MachineProfiles { get; set; } = [];

    /// <summary>Set on every export; the conflict-detection baseline.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Machine name that last exported this file.</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>Build/run/test command templates shared across machines.</summary>
public sealed class SharedCommandTemplates
{
    public string? Build { get; set; }
    public string? Run { get; set; }
    public string? Test { get; set; }
}

public sealed class SharedMilestone
{
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
}

public sealed class SharedFrontier
{
    public string Title { get; set; } = string.Empty;

    /// <summary>String form of <see cref="FrontierStatus"/> (Next/Current/Completed).</summary>
    public string Status { get; set; } = nameof(FrontierStatus.Next);

    public string? Notes { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Where (and whether) a shared project lives on one specific machine.</summary>
public sealed class MachineProfile
{
    public string MachineName { get; set; } = string.Empty;

    /// <summary>Absolute path of the repository on that machine, if checked out there.</summary>
    public string? RepositoryPath { get; set; }

    public string? Notes { get; set; }

    /// <summary>Whether the repository path existed the last time that machine looked.</summary>
    public bool? LastKnownAvailable { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }
}
