using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>Outcome of one export attempt. A warning means nothing was written.</summary>
public sealed record SharedExportResult(bool Success, string FilePath, string? Warning);

/// <summary>One field-level difference between the local project and the shared file.</summary>
public sealed record SharedFieldChange(string Field, string LocalValue, string SharedValue);

/// <summary>A parsed shared state file and where it came from.</summary>
public sealed record SharedStateFileInfo(string FilePath, SharedProjectState State);

/// <summary>
/// What an import would do, computed before anything is applied. The UI shows
/// this and the user explicitly confirms — there is no silent overwrite.
/// </summary>
public sealed class SharedImportPreview
{
    public required SharedProjectState State { get; init; }

    /// <summary>Fields that would change, local → shared. Empty means up to date.</summary>
    public required IReadOnlyList<SharedFieldChange> Changes { get; init; }

    /// <summary>The shared file changed since this machine last synced it.</summary>
    public required bool SharedChangedSinceSync { get; init; }

    /// <summary>The local project changed since this machine last synced.</summary>
    public required bool LocalChangedSinceSync { get; init; }

    /// <summary>Both sides changed — applying overwrites local edits; the user must choose.</summary>
    public bool HasConflict => SharedChangedSinceSync && LocalChangedSinceSync;

    /// <summary>Machine profile matching this machine, if the shared file has one.</summary>
    public MachineProfile? MatchedProfile { get; init; }

    /// <summary>Whether the repository would exist on this machine after applying.</summary>
    public required bool RepositoryAvailable { get; init; }
}

public sealed record SharedImportResult(Project Project, bool Created, bool RepositoryAvailable);

/// <summary>Summary of an Import All / Export All pass over the shared folder.</summary>
public sealed class SharedSyncSummary
{
    public List<string> Applied { get; } = [];
    public List<string> Skipped { get; } = [];
    public List<string> Warnings { get; } = [];
}

public interface ISharedStateService
{
    string GetProjectsFolder(string sharedFolder);

    /// <summary>Existing file matched by stable id, or the default slug-named path for new exports.</summary>
    string GetSharedFilePath(string sharedFolder, Project project);

    /// <summary>All parseable shared project files. Unreadable files are skipped.</summary>
    IReadOnlyList<SharedStateFileInfo> LoadAll(string sharedFolder);

    SharedProjectState? LoadFile(string filePath);

    /// <summary>The registered project matching a shared state's stable id, if any.</summary>
    Project? MatchProject(IReadOnlyList<Project> projects, SharedProjectState state);

    /// <summary>
    /// Writes the project's shared file, preserving other machines' profiles and
    /// upserting this machine's. Warns (and writes nothing) if the shared file
    /// changed since this machine last synced it, unless <paramref name="force"/>.
    /// </summary>
    SharedExportResult Export(string sharedFolder, Project project, string machineName, bool force = false);

    /// <summary>Computes what applying a shared state to a project would change.</summary>
    SharedImportPreview PreviewImport(SharedProjectState state, Project project, string machineName);

    /// <summary>Applies a shared state to an existing project (fields, milestones, frontier checklist, repo path).</summary>
    SharedImportResult ApplyImport(SharedProjectState state, Project project, string machineName);

    /// <summary>Creates a new local project from a shared state (used when no stable-id match exists).</summary>
    SharedImportResult CreateFromShared(SharedProjectState state, string machineName);

    /// <summary>
    /// Imports every shared file: creates projects that don't exist locally;
    /// existing projects are never overwritten here — they are reported for
    /// per-project preview instead.
    /// </summary>
    SharedSyncSummary ImportAll(string sharedFolder, string machineName);

    /// <summary>Exports every local project, skipping (with a warning) those whose shared file is newer.</summary>
    SharedSyncSummary ExportAll(string sharedFolder, string machineName);

    /// <summary>
    /// Probes the shared folder: existence, writability, project file count, and
    /// warnings (Dropbox conflicted copies, unreadable files). Creates
    /// <c>projects/</c> when the folder is valid and writable. Touches nothing
    /// outside the folder and only ever reads <c>*.workbench.json</c> files.
    /// </summary>
    SharedFolderStatus CheckSharedFolder(string sharedFolder);
}

/// <summary>
/// File-based cross-machine project state. The shared folder is the user's
/// Dropbox folder (<c>~/Dropbox/workbench</c> by default) — Dropbox syncs it
/// between machines externally; Workbench never runs sync commands. Local
/// SQLite history (runs, logs, timeline) is never written here.
/// </summary>
public sealed class SharedStateService(
    IProjectRepository projects,
    IMilestoneRepository milestones,
    IFrontierRepository frontiers) : ISharedStateService
{
    public const string ProjectsSubfolder = "projects";
    public const string FileSuffix = ".workbench.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string GetProjectsFolder(string sharedFolder) => Path.Combine(sharedFolder, ProjectsSubfolder);

    public string GetSharedFilePath(string sharedFolder, Project project)
    {
        if (project.SharedId is { Length: > 0 } id)
        {
            var existing = LoadAll(sharedFolder).FirstOrDefault(f => f.State.Id == id);
            if (existing is not null)
                return existing.FilePath;
        }

        return Path.Combine(GetProjectsFolder(sharedFolder), Slugify(project.Name) + FileSuffix);
    }

    public IReadOnlyList<SharedStateFileInfo> LoadAll(string sharedFolder)
    {
        var folder = GetProjectsFolder(sharedFolder);
        if (!Directory.Exists(folder))
            return [];

        var result = new List<SharedStateFileInfo>();
        foreach (var path in Directory.EnumerateFiles(folder, "*" + FileSuffix).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (LoadFile(path) is { } state)
                result.Add(new SharedStateFileInfo(path, state));
        }

        return result;
    }

    public SharedProjectState? LoadFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<SharedProjectState>(File.ReadAllText(filePath), JsonOptions);
            return state is { Id.Length: > 0 } ? state : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Project? MatchProject(IReadOnlyList<Project> allProjects, SharedProjectState state) =>
        allProjects.FirstOrDefault(p => p.SharedId is { Length: > 0 } id && id == state.Id);

    public SharedExportResult Export(string sharedFolder, Project project, string machineName, bool force = false)
    {
        var filePath = GetSharedFilePath(sharedFolder, project);
        var existing = LoadFile(filePath);

        if (!force && existing is not null)
        {
            if (project.SharedLastSyncedStamp is not { } stamp)
            {
                return new SharedExportResult(false, filePath,
                    "A shared file already exists for this project and this machine has never synced it. " +
                    "Import it first, or export again with overwrite confirmed.");
            }

            if (existing.UpdatedAt > stamp)
            {
                return new SharedExportResult(false, filePath,
                    $"The shared file is newer than this machine's last sync " +
                    $"(shared {existing.UpdatedAt:yyyy-MM-dd HH:mm} by {existing.UpdatedBy ?? "unknown"}). " +
                    "Import/review it first, or export again with overwrite confirmed.");
            }
        }

        var state = BuildState(project, machineName, existing);

        Directory.CreateDirectory(GetProjectsFolder(sharedFolder));
        File.WriteAllText(filePath, JsonSerializer.Serialize(state, JsonOptions), Encoding.UTF8);

        // Bookkeeping only — exporting is not a user edit, so updated_at stays put.
        project.SharedId = state.Id;
        project.SharedLastExportedAt = DateTimeOffset.Now;
        project.SharedLastSyncedStamp = state.UpdatedAt;
        projects.UpdateSharedSync(project);

        return new SharedExportResult(true, filePath, null);
    }

    public SharedImportPreview PreviewImport(SharedProjectState state, Project project, string machineName)
    {
        var changes = new List<SharedFieldChange>();

        AddChange(changes, "Name", project.Name, state.Name);
        AddChange(changes, "Status", project.Status.ToString(), state.Status);
        AddChange(changes, "Description", project.Description, state.Description);
        AddChange(changes, "Objective", project.CurrentObjective, state.Objective);
        AddChange(changes, "Active task", project.ActiveTask, state.ActiveTask);
        AddChange(changes, "Blocker class", project.BlockerClass, state.BlockerClass);
        AddChange(changes, "Evidence", project.Evidence, state.Evidence);
        AddChange(changes, "Next safest step", project.NextSafestStep, state.NextSafestStep);
        AddChange(changes, "Assigned model", project.PreferredAiProvider, state.AssignedModel);
        AddChange(changes, "Build command", project.BuildCommand, state.CommandTemplates.Build);
        AddChange(changes, "Run command", project.RunCommand, state.CommandTemplates.Run);
        AddChange(changes, "Test command", project.TestCommand, state.CommandTemplates.Test);
        AddChange(changes, "Prompt rules", Join(project.PromptRules), Join(state.PromptRules));
        AddChange(changes, "AI context paths", Join(project.AiContextPaths), Join(state.AiContextPaths));
        AddChange(changes, "Documentation paths", Join(project.DocumentationPaths), Join(state.DocumentationPaths));
        AddChange(changes, "Log patterns", Join(project.CustomLogPatterns), Join(state.CustomLogPatterns));
        AddChange(changes, "Tags",
            Join(project.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)),
            Join(state.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)));
        AddChange(changes, "GitHub repo", project.GitHubRepoUrl, state.GitHubRepoUrl);
        AddChange(changes, "Default branch", project.DefaultBranch, state.DefaultBranch);

        var localMilestones = project.Id > 0 ? milestones.GetForProject(project.Id) : [];
        AddChange(changes, "Milestones", DescribeMilestones(localMilestones), DescribeMilestones(state.Milestones));

        var localFrontiers = project.Id > 0 ? frontiers.GetForProject(project.Id) : [];
        AddChange(changes, "Frontier checklist", DescribeFrontiers(localFrontiers), DescribeFrontiers(state.FrontierChecklist));

        var profile = FindProfile(state, machineName);
        var effectivePath = profile?.RepositoryPath is { Length: > 0 } p ? p : project.RepositoryPath;
        AddChange(changes, "Repository path (this machine)", project.RepositoryPath, effectivePath);

        var lastSync = Max(project.SharedLastImportedAt, project.SharedLastExportedAt);
        return new SharedImportPreview
        {
            State = state,
            Changes = changes,
            SharedChangedSinceSync = project.SharedLastSyncedStamp is not { } stamp || state.UpdatedAt != stamp,
            LocalChangedSinceSync = lastSync is { } sync && project.UpdatedAt > sync,
            MatchedProfile = profile,
            RepositoryAvailable = effectivePath is { Length: > 0 } path && Directory.Exists(path),
        };
    }

    public SharedImportResult ApplyImport(SharedProjectState state, Project project, string machineName)
    {
        var available = ApplyFields(state, project, machineName);
        projects.Update(project);

        ReplaceMilestones(project.Id, state.Milestones);
        ReplaceFrontiers(project.Id, state.FrontierChecklist);

        MarkSynced(project, state, imported: true);
        return new SharedImportResult(project, Created: false, available);
    }

    public SharedImportResult CreateFromShared(SharedProjectState state, string machineName)
    {
        var project = new Project();
        var available = ApplyFields(state, project, machineName);
        projects.Add(project);

        ReplaceMilestones(project.Id, state.Milestones);
        ReplaceFrontiers(project.Id, state.FrontierChecklist);

        MarkSynced(project, state, imported: true);
        return new SharedImportResult(project, Created: true, available);
    }

    /// <summary>
    /// Records the sync AFTER the field writes so the import's own
    /// <c>updated_at</c> bump lands before the sync mark — otherwise every
    /// import would immediately read as "local changed since sync".
    /// </summary>
    private void MarkSynced(Project project, SharedProjectState state, bool imported)
    {
        project.SharedId = state.Id;
        if (imported)
            project.SharedLastImportedAt = DateTimeOffset.Now;
        project.SharedLastSyncedStamp = state.UpdatedAt;
        projects.UpdateSharedSync(project);
    }

    public SharedSyncSummary ImportAll(string sharedFolder, string machineName)
    {
        var summary = new SharedSyncSummary();
        var all = projects.GetAll();

        foreach (var file in LoadAll(sharedFolder))
        {
            var name = Path.GetFileName(file.FilePath);
            if (MatchProject(all, file.State) is { } existing)
            {
                if (existing.SharedLastSyncedStamp == file.State.UpdatedAt)
                    summary.Skipped.Add($"{name}: '{existing.Name}' is up to date.");
                else
                    summary.Warnings.Add($"{name}: '{existing.Name}' already exists — review it from the project's Settings tab (import preview).");
                continue;
            }

            var result = CreateFromShared(file.State, machineName);
            summary.Applied.Add(result.RepositoryAvailable
                ? $"{name}: created '{result.Project.Name}'."
                : $"{name}: created '{result.Project.Name}' (not available on this machine).");
        }

        return summary;
    }

    public SharedSyncSummary ExportAll(string sharedFolder, string machineName)
    {
        var summary = new SharedSyncSummary();

        foreach (var project in projects.GetAll())
        {
            var result = Export(sharedFolder, project, machineName);
            if (result.Success)
                summary.Applied.Add($"'{project.Name}' → {Path.GetFileName(result.FilePath)}");
            else
                summary.Warnings.Add($"'{project.Name}': {result.Warning}");
        }

        return summary;
    }

    public SharedFolderStatus CheckSharedFolder(string sharedFolder)
    {
        var status = new SharedFolderStatus { Folder = sharedFolder };

        if (string.IsNullOrWhiteSpace(sharedFolder) || !Directory.Exists(sharedFolder))
        {
            status.Warnings.Add($"Shared folder is missing or unavailable (Dropbox may not have synced it): {sharedFolder}");
            return status;
        }

        status.Exists = true;
        status.Writable = ProbeWritable(sharedFolder);
        if (!status.Writable)
        {
            status.Warnings.Add($"Shared folder is not writable: {sharedFolder}");
            return status;
        }

        var projectsFolder = GetProjectsFolder(sharedFolder);
        Directory.CreateDirectory(projectsFolder);
        status.ProjectsFolderExists = true;

        foreach (var path in Directory.EnumerateFiles(projectsFolder, "*" + FileSuffix, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            status.ProjectFileCount++;
            var name = Path.GetFileName(path);
            if (name.Contains("conflicted copy", StringComparison.OrdinalIgnoreCase))
                status.Warnings.Add($"{name}: Dropbox conflicted copy — resolve it against the original file.");
            else if (LoadFile(path) is null)
                status.Warnings.Add($"{name}: unreadable or corrupt — the file could not be loaded.");
        }

        return status;
    }

    /// <summary>Writes and deletes a temp probe file to prove the folder is writable.</summary>
    private static bool ProbeWritable(string folder)
    {
        var probe = Path.Combine(folder, ".workbench-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The shared state for a project as it would be exported right now.</summary>
    public SharedProjectState BuildState(Project project, string machineName, SharedProjectState? existing)
    {
        var projectFrontiers = project.Id > 0 ? frontiers.GetForProject(project.Id) : [];
        var state = new SharedProjectState
        {
            Id = project.SharedId is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"),
            Name = project.Name,
            Description = project.Description,
            Objective = project.CurrentObjective,
            Status = project.Status.ToString(),
            ActiveTask = project.ActiveTask,
            CurrentFrontier = projectFrontiers.FirstOrDefault(f => f.Status == FrontierStatus.Current)?.Title,
            BlockerClass = project.BlockerClass,
            Evidence = project.Evidence,
            NextSafestStep = project.NextSafestStep,
            AssignedModel = project.PreferredAiProvider,
            PromptRules = [.. project.PromptRules],
            AiContextPaths = [.. project.AiContextPaths],
            DocumentationPaths = [.. project.DocumentationPaths],
            CommandTemplates = new SharedCommandTemplates
            {
                Build = project.BuildCommand,
                Run = project.RunCommand,
                Test = project.TestCommand,
            },
            CustomLogPatterns = [.. project.CustomLogPatterns],
            Milestones = project.Id > 0
                ? milestones.GetForProject(project.Id).Select(m => new SharedMilestone
                {
                    Title = m.Title,
                    IsCompleted = m.IsCompleted,
                    CompletedAt = m.CompletedAt,
                    SortOrder = m.SortOrder,
                    Notes = m.Notes,
                }).ToList()
                : [],
            FrontierChecklist = projectFrontiers.Select(f => new SharedFrontier
            {
                Title = f.Title,
                Status = f.Status.ToString(),
                Notes = f.Notes,
                CompletedAt = f.CompletedAt,
            }).ToList(),
            // Tags are a set (the local store keeps them alphabetical) — export
            // them canonically sorted so files diff cleanly across machines.
            Tags = [.. project.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)],
            GitHubRepoUrl = project.GitHubRepoUrl,
            DefaultBranch = project.DefaultBranch,
            MachineProfiles = existing?.MachineProfiles ?? [],
            UpdatedAt = DateTimeOffset.Now,
            UpdatedBy = machineName,
        };

        UpsertProfile(state, machineName, project.RepositoryPath);
        return state;
    }

    public static MachineProfile? FindProfile(SharedProjectState state, string machineName) =>
        state.MachineProfiles.FirstOrDefault(p =>
            string.Equals(p.MachineName, machineName, StringComparison.OrdinalIgnoreCase));

    private static void UpsertProfile(SharedProjectState state, string machineName, string? repositoryPath)
    {
        var profile = FindProfile(state, machineName);
        if (profile is null)
        {
            profile = new MachineProfile { MachineName = machineName };
            state.MachineProfiles.Add(profile);
        }

        profile.RepositoryPath = repositoryPath;
        profile.LastKnownAvailable = repositoryPath is { Length: > 0 } path && Directory.Exists(path);
        profile.LastSeenAt = DateTimeOffset.Now;
    }

    /// <summary>Applies shared fields to the project; returns repository availability on this machine.</summary>
    private static bool ApplyFields(SharedProjectState state, Project project, string machineName)
    {
        if (!string.IsNullOrWhiteSpace(state.Name))
            project.Name = state.Name;
        if (Enum.TryParse<ProjectStatus>(state.Status, ignoreCase: true, out var status))
            project.Status = status;

        project.Description = state.Description;
        project.CurrentObjective = state.Objective;
        project.ActiveTask = state.ActiveTask;
        project.BlockerClass = state.BlockerClass;
        project.Evidence = state.Evidence;
        project.NextSafestStep = state.NextSafestStep;
        project.PreferredAiProvider = state.AssignedModel;
        project.BuildCommand = state.CommandTemplates.Build;
        project.RunCommand = state.CommandTemplates.Run;
        project.TestCommand = state.CommandTemplates.Test;
        project.PromptRules = [.. state.PromptRules];
        project.AiContextPaths = [.. state.AiContextPaths];
        project.DocumentationPaths = [.. state.DocumentationPaths];
        project.CustomLogPatterns = [.. state.CustomLogPatterns];
        project.Tags = [.. state.Tags];
        project.GitHubRepoUrl = state.GitHubRepoUrl;
        project.DefaultBranch = state.DefaultBranch;

        // The repository path is per-machine knowledge. A matching profile wins;
        // without one, an already-known local path is kept. A missing repository
        // is a normal state ("not available on this machine"), never an error.
        if (FindProfile(state, machineName)?.RepositoryPath is { Length: > 0 } profilePath)
            project.RepositoryPath = profilePath;

        return project.RepositoryPath is { Length: > 0 } finalPath && Directory.Exists(finalPath);
    }

    private void ReplaceMilestones(long projectId, List<SharedMilestone> shared)
    {
        foreach (var existing in milestones.GetForProject(projectId))
            milestones.Delete(existing.Id);

        foreach (var m in shared)
        {
            milestones.Add(new Milestone
            {
                ProjectId = projectId,
                Title = m.Title,
                IsCompleted = m.IsCompleted,
                CompletedAt = m.CompletedAt,
                SortOrder = m.SortOrder,
                Notes = m.Notes,
            });
        }
    }

    private void ReplaceFrontiers(long projectId, List<SharedFrontier> shared)
    {
        foreach (var existing in frontiers.GetForProject(projectId))
            frontiers.Delete(existing.Id);

        foreach (var f in shared)
        {
            frontiers.Add(new Frontier
            {
                ProjectId = projectId,
                Title = f.Title,
                Status = Enum.TryParse<FrontierStatus>(f.Status, ignoreCase: true, out var s) ? s : FrontierStatus.Next,
                Notes = f.Notes,
                CreatedAt = DateTimeOffset.Now,
                CompletedAt = f.CompletedAt,
            });
        }
    }

    private static void AddChange(List<SharedFieldChange> changes, string field, string? local, string? shared)
    {
        var localValue = local ?? string.Empty;
        var sharedValue = shared ?? string.Empty;
        if (!string.Equals(localValue, sharedValue, StringComparison.Ordinal))
            changes.Add(new SharedFieldChange(field, Display(localValue), Display(sharedValue)));
    }

    private static string Display(string value) => value.Length == 0 ? "(empty)" : value;

    private static string Join(IEnumerable<string> values) => string.Join("; ", values);

    private static string DescribeMilestones(IReadOnlyList<Milestone> items) =>
        Join(items.OrderBy(m => m.SortOrder).Select(m => (m.IsCompleted ? "[x] " : "[ ] ") + m.Title));

    private static string DescribeMilestones(List<SharedMilestone> items) =>
        Join(items.OrderBy(m => m.SortOrder).Select(m => (m.IsCompleted ? "[x] " : "[ ] ") + m.Title));

    private static string DescribeFrontiers(IReadOnlyList<Frontier> items) =>
        Join(items.Select(f => $"{f.Title} ({f.Status})"));

    private static string DescribeFrontiers(List<SharedFrontier> items) =>
        Join(items.Select(f => $"{f.Title} ({f.Status})"));

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;

    /// <summary>"Xenogears Decompilation" → "xenogears-decompilation".</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastWasDash = true;
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().TrimEnd('-');
        return slug.Length > 0 ? slug : "project";
    }
}
