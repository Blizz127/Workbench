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

/// <summary>A shared state file that matched the pattern but was refused, with the reason.</summary>
public sealed record SharedStateFileRejection(string FilePath, string Reason);

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

    /// <summary>
    /// Matched blockers changed on BOTH sides since the last sync. Applying
    /// skips these (keeps local) unless the caller confirms the overwrite.
    /// </summary>
    public IReadOnlyList<string> BlockerConflicts { get; init; } = [];

    /// <summary>Machine profile matching this machine, if the shared file has one.</summary>
    public MachineProfile? MatchedProfile { get; init; }

    /// <summary>Whether the repository would exist on this machine after applying.</summary>
    public required bool RepositoryAvailable { get; init; }
}

public sealed record SharedImportResult(Project Project, bool Created, bool RepositoryAvailable)
{
    /// <summary>Blockers left untouched because both sides changed them since the last sync.</summary>
    public IReadOnlyList<string> SkippedBlockerConflicts { get; init; } = [];
}

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

    /// <summary>All parseable shared project files. Unreadable and rejected files are skipped.</summary>
    IReadOnlyList<SharedStateFileInfo> LoadAll(string sharedFolder);

    /// <summary>Files that matched the shared-state pattern but were refused (unsupported schema version), with reasons.</summary>
    IReadOnlyList<SharedStateFileRejection> LoadRejections(string sharedFolder);

    SharedProjectState? LoadFile(string filePath);

    /// <summary>
    /// Like <see cref="LoadFile"/>, but explains WHY a file was refused:
    /// <paramref name="error"/> is set for an unsupported schema version; a
    /// false return with a null error means unreadable/corrupt. Newer data is
    /// never silently discarded.
    /// </summary>
    bool TryLoadFile(string filePath, out SharedProjectState? state, out string? error);

    /// <summary>The registered project matching a shared state's stable id, if any.</summary>
    Project? MatchProject(IReadOnlyList<Project> projects, SharedProjectState state);

    /// <summary>
    /// Writes the project's shared file, preserving other machines' profiles and
    /// upserting this machine's. Warns (and writes nothing) if the shared file
    /// changed since this machine last synced it, unless <paramref name="force"/>.
    /// The write is atomic: on failure the previous file stays untouched.
    /// </summary>
    SharedExportResult Export(string sharedFolder, Project project, string machineName, bool force = false);

    /// <summary>Computes what applying a shared state to a project would change.</summary>
    SharedImportPreview PreviewImport(SharedProjectState state, Project project, string machineName);

    /// <summary>
    /// Applies a shared state to an existing project (fields, milestones,
    /// frontier checklist, blockers, repo path). Blockers in conflict (changed
    /// on both sides since the last sync) are skipped and reported unless
    /// <paramref name="overwriteBlockerConflicts"/> is set.
    /// </summary>
    SharedImportResult ApplyImport(SharedProjectState state, Project project, string machineName, bool overwriteBlockerConflicts = false);

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
    IFrontierRepository frontiers,
    IBlockerRepository blockers,
    Action<string, string>? atomicWriter = null) : ISharedStateService
{
    public const string ProjectsSubfolder = "projects";
    public const string FileSuffix = ".workbench.json";

    /// <summary>Only files matching this pattern are ever cleaned up — our own abandoned temp writes.</summary>
    private const string TempFilePattern = "*" + FileSuffix + ".*.tmp";

    private readonly Action<string, string> _atomicWriter = atomicWriter ?? WriteAllTextAtomic;

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
            if (TryLoadFile(path, out var state, out _) && state is not null)
                result.Add(new SharedStateFileInfo(path, state));
        }

        return result;
    }

    public IReadOnlyList<SharedStateFileRejection> LoadRejections(string sharedFolder)
    {
        var folder = GetProjectsFolder(sharedFolder);
        if (!Directory.Exists(folder))
            return [];

        var result = new List<SharedStateFileRejection>();
        foreach (var path in Directory.EnumerateFiles(folder, "*" + FileSuffix).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!TryLoadFile(path, out _, out var error) && error is not null)
                result.Add(new SharedStateFileRejection(path, error));
        }

        return result;
    }

    public SharedProjectState? LoadFile(string filePath) =>
        TryLoadFile(filePath, out var state, out _) ? state : null;

    public bool TryLoadFile(string filePath, out SharedProjectState? state, out string? error)
    {
        state = null;
        error = null;
        if (!File.Exists(filePath))
            return false;

        try
        {
            var text = File.ReadAllText(filePath);
            var version = ReadSchemaVersion(text);
            if (version > SharedProjectState.SupportedSchemaVersion)
            {
                error = $"unsupported schema version {version} " +
                        $"(this Workbench supports up to {SharedProjectState.SupportedSchemaVersion})";
                return false;
            }

            state = JsonSerializer.Deserialize<SharedProjectState>(text, JsonOptions);
            return state is { Id.Length: > 0 };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Raw schemaVersion from the file; files without one predate the field and count as version 1.</summary>
    private static int ReadSchemaVersion(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty("schemaVersion", out var element)
               && element.TryGetInt32(out var version)
            ? version
            : 1;
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

        // Serialize first: a serialization failure must leave the previous
        // valid file untouched. The write itself is atomic — temp file in the
        // same directory, then an overwrite-move — so a crash mid-write can
        // never leave a truncated shared file for other machines to import.
        string json;
        try
        {
            json = JsonSerializer.Serialize(state, JsonOptions);
            Directory.CreateDirectory(GetProjectsFolder(sharedFolder));
            _atomicWriter(filePath, json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SharedExportResult(false, filePath,
                $"Export failed — the previous shared file (if any) is untouched: {ex.Message}");
        }

        // Bookkeeping only — exporting is not a user edit, so updated_at stays put.
        project.SharedId = state.Id;
        project.SharedLastExportedAt = DateTimeOffset.Now;
        project.SharedLastSyncedStamp = state.UpdatedAt;
        projects.UpdateSharedSync(project);

        return new SharedExportResult(true, filePath, null);
    }

    /// <summary>
    /// Atomically replaces <paramref name="targetPath"/> with
    /// <paramref name="content"/>: write a temp file next to the target (same
    /// filesystem, so the move is atomic), flush + close, then overwrite-move.
    /// On any failure the temp file is cleaned up and the target is untouched.
    /// </summary>
    public static void WriteAllTextAtomic(string targetPath, string content)
    {
        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort — the original failure is the one that matters.
            }

            throw;
        }
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

        var lastSync = Max(project.SharedLastImportedAt, project.SharedLastExportedAt);
        var localBlockers = project.Id > 0 ? blockers.GetForProject(project.Id) : [];
        var blockerConflicts = AddBlockerChanges(changes, localBlockers, state, lastSync);

        var profile = FindProfile(state, machineName);
        var effectivePath = profile?.RepositoryPath is { Length: > 0 } p ? p : project.RepositoryPath;
        AddChange(changes, "Repository path (this machine)", project.RepositoryPath, effectivePath);

        return new SharedImportPreview
        {
            State = state,
            Changes = changes,
            SharedChangedSinceSync = project.SharedLastSyncedStamp is not { } stamp || state.UpdatedAt != stamp,
            LocalChangedSinceSync = lastSync is { } sync && project.UpdatedAt > sync,
            BlockerConflicts = blockerConflicts,
            MatchedProfile = profile,
            RepositoryAvailable = effectivePath is { Length: > 0 } path && Directory.Exists(path),
        };
    }

    /// <summary>
    /// Blocker-level preview entries, matched by stable id: additions
    /// (shared-only), removals (local-only — shown BEFORE any deletion can
    /// apply), and field-level diffs on matched blockers. Returns the matched
    /// blockers changed on both sides since the last sync (conflicts).
    /// </summary>
    private static List<string> AddBlockerChanges(
        List<SharedFieldChange> changes,
        IReadOnlyList<Blocker> localBlockers,
        SharedProjectState state,
        DateTimeOffset? lastSync)
    {
        var conflicts = new List<string>();
        var localByStableId = localBlockers
            .Where(b => b.StableId.Length > 0)
            .ToDictionary(b => b.StableId, StringComparer.Ordinal);
        var sharedByStableId = state.Blockers
            .Where(b => b.StableId.Length > 0)
            .GroupBy(b => b.StableId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var shared in state.Blockers.Where(b => b.StableId.Length > 0))
        {
            if (localByStableId.ContainsKey(shared.StableId))
                continue;
            var title = shared.Title ?? shared.Description;
            AddChange(changes, $"Blocker: {title} — (new blocker)", string.Empty, Describe(shared));
        }

        foreach (var local in localBlockers.Where(b => b.StableId.Length > 0))
        {
            if (sharedByStableId.ContainsKey(local.StableId))
                continue;
            AddChange(changes, $"Blocker: {local.TitleOrDescription} — (removed)", Describe(local), string.Empty);
        }

        foreach (var shared in state.Blockers.Where(b => b.StableId.Length > 0))
        {
            if (!localByStableId.TryGetValue(shared.StableId, out var local))
                continue;

            var title = shared.Title ?? local.TitleOrDescription;
            var prefix = $"Blocker: {title} — ";
            var before = changes.Count;
            AddChange(changes, prefix + "Title", local.Title, shared.Title);
            AddChange(changes, prefix + "Description", local.Description, shared.Description);
            AddChange(changes, prefix + "Class", local.BlockerClass, shared.BlockerClass);
            AddChange(changes, prefix + "Severity", local.Severity, shared.Severity);
            AddChange(changes, prefix + "Status", local.Status.ToString(), shared.Status);
            AddChange(changes, prefix + "Evidence", local.Evidence, shared.Evidence);
            AddChange(changes, prefix + "Next safest step", local.NextSafestStep, shared.NextSafestStep);
            AddChange(changes, prefix + "Owner", local.Owner, shared.Owner);
            AddChange(changes, prefix + "Tags",
                Join(local.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)),
                Join(shared.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)));
            AddChange(changes, prefix + "Source reference", local.SourceReference, shared.SourceReference);
            AddChange(changes, prefix + "Resolved at",
                local.ResolvedAt?.ToString("yyyy-MM-dd HH:mm"), shared.ResolvedAt?.ToString("yyyy-MM-dd HH:mm"));

            // Per-blocker conflict: BOTH sides touched it since this machine's
            // last sync. Without a prior sync there is no baseline — and a
            // stable-id match without a sync cannot happen in practice.
            if (changes.Count > before
                && lastSync is { } sync
                && local.UpdatedAt > sync
                && shared.UpdatedAt > sync)
            {
                conflicts.Add($"Blocker: {title} — changed locally AND in the shared file since the last sync");
            }
        }

        return conflicts;
    }

    private static string Describe(Blocker b) =>
        $"{b.TitleOrDescription} [{b.Status}]" +
        (b.Severity is { Length: > 0 } severity ? $" · {severity}" : string.Empty);

    private static string Describe(SharedBlocker b) =>
        $"{b.Title ?? b.Description} [{b.Status}]" +
        (b.Severity is { Length: > 0 } severity ? $" · {severity}" : string.Empty);

    public SharedImportResult ApplyImport(SharedProjectState state, Project project, string machineName, bool overwriteBlockerConflicts = false)
    {
        var available = ApplyFields(state, project, machineName);
        projects.Update(project);

        ReplaceMilestones(project.Id, state.Milestones);
        ReplaceFrontiers(project.Id, state.FrontierChecklist);

        var lastSync = Max(project.SharedLastImportedAt, project.SharedLastExportedAt);
        var skipped = UpsertBlockers(project.Id, state, lastSync, overwriteBlockerConflicts);

        MarkSynced(project, state, imported: true);
        return new SharedImportResult(project, Created: false, available) { SkippedBlockerConflicts = skipped };
    }

    public SharedImportResult CreateFromShared(SharedProjectState state, string machineName)
    {
        var project = new Project();
        var available = ApplyFields(state, project, machineName);
        projects.Add(project);

        ReplaceMilestones(project.Id, state.Milestones);
        ReplaceFrontiers(project.Id, state.FrontierChecklist);
        UpsertBlockers(project.Id, state, lastSync: null, overwriteConflicts: false);

        MarkSynced(project, state, imported: true);
        return new SharedImportResult(project, Created: true, available);
    }

    /// <summary>
    /// Upserts blockers by stable id: inserts shared-only ones, updates matched
    /// ones whose fields differ, and deletes local ones the shared file no
    /// longer carries (they were shown in the import preview first). Only runs
    /// for schema ≥ 2 states — a schema-1 file has no blocker concept, so its
    /// empty list must NOT delete local blockers. Conflicting matched blockers
    /// (changed on both sides since the last sync) are skipped and returned
    /// unless <paramref name="overwriteConflicts"/> is set.
    /// </summary>
    private List<string> UpsertBlockers(
        long projectId, SharedProjectState state, DateTimeOffset? lastSync, bool overwriteConflicts)
    {
        var skipped = new List<string>();
        if (state.SchemaVersion < 2)
            return skipped;

        var local = blockers.GetForProject(projectId);
        var localByStableId = local
            .Where(b => b.StableId.Length > 0)
            .ToDictionary(b => b.StableId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var shared in state.Blockers)
        {
            if (shared.StableId is not { Length: > 0 } stableId)
                continue;
            seen.Add(stableId);

            if (!localByStableId.TryGetValue(stableId, out var existing))
            {
                blockers.Add(MapFromShared(shared, projectId));
                continue;
            }

            if (!overwriteConflicts
                && lastSync is { } sync
                && existing.UpdatedAt > sync
                && shared.UpdatedAt > sync
                && !FieldsEqual(existing, shared))
            {
                skipped.Add($"Blocker: {shared.Title ?? existing.TitleOrDescription} — kept local (changed on both sides)");
                continue;
            }

            if (!FieldsEqual(existing, shared))
            {
                ApplyShared(shared, existing);
                blockers.Update(existing);
            }
        }

        foreach (var b in local)
        {
            if (b.StableId.Length > 0 && !seen.Contains(b.StableId))
                blockers.Delete(b.Id);
        }

        return skipped;
    }

    /// <summary>The shared content fields — the same set the import preview diffs (timestamps excepted).</summary>
    private static bool FieldsEqual(Blocker local, SharedBlocker shared) =>
        string.Equals(local.Title ?? string.Empty, shared.Title ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(local.Description, shared.Description, StringComparison.Ordinal)
        && string.Equals(local.BlockerClass ?? string.Empty, shared.BlockerClass ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(local.Severity ?? string.Empty, shared.Severity ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(local.Status.ToString(), shared.Status, StringComparison.OrdinalIgnoreCase)
        && string.Equals(local.Evidence ?? string.Empty, shared.Evidence ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(local.NextSafestStep ?? string.Empty, shared.NextSafestStep ?? string.Empty, StringComparison.Ordinal)
        && string.Equals(local.Owner ?? string.Empty, shared.Owner ?? string.Empty, StringComparison.Ordinal)
        && local.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(shared.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        && string.Equals(local.SourceReference ?? string.Empty, shared.SourceReference ?? string.Empty, StringComparison.Ordinal)
        && Nullable.Equals(local.ResolvedAt, shared.ResolvedAt);

    private static void ApplyShared(SharedBlocker shared, Blocker local)
    {
        local.Title = shared.Title;
        local.Description = shared.Description;
        local.BlockerClass = shared.BlockerClass;
        local.Severity = shared.Severity;
        local.Status = Enum.TryParse<BlockerStatus>(shared.Status, ignoreCase: true, out var status)
            ? status
            : BlockerStatus.Active;
        local.Evidence = shared.Evidence;
        local.NextSafestStep = shared.NextSafestStep;
        local.Owner = shared.Owner;
        local.Tags = [.. shared.Tags];
        local.SourceReference = shared.SourceReference;
        local.ResolvedAt = shared.ResolvedAt;
        // Resolution stays local-only: the shared file deliberately does not carry it.
    }

    private static Blocker MapFromShared(SharedBlocker shared, long projectId)
    {
        var blocker = new Blocker { ProjectId = projectId };
        ApplyShared(shared, blocker);
        blocker.StableId = shared.StableId;
        blocker.CreatedAt = shared.CreatedAt;
        blocker.UpdatedAt = shared.UpdatedAt;
        return blocker;
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

        foreach (var rejection in LoadRejections(sharedFolder))
            summary.Warnings.Add($"{Path.GetFileName(rejection.FilePath)}: {rejection.Reason} — not imported; its data is untouched.");

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

        // Remove only OUR OWN abandoned temp files from interrupted atomic
        // exports — never anything else in the user's Dropbox folder.
        foreach (var temp in Directory.EnumerateFiles(projectsFolder, TempFilePattern))
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.Warnings.Add($"{Path.GetFileName(temp)}: could not remove an abandoned temp file — delete it by hand.");
            }
        }

        foreach (var path in Directory.EnumerateFiles(projectsFolder, "*" + FileSuffix, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            status.ProjectFileCount++;
            var name = Path.GetFileName(path);
            if (name.Contains("conflicted copy", StringComparison.OrdinalIgnoreCase))
                status.Warnings.Add($"{name}: Dropbox conflicted copy — resolve it against the original file.");
            else if (!TryLoadFile(path, out _, out var loadError))
                status.Warnings.Add(loadError is not null
                    ? $"{name}: {loadError} — not loaded; its data is untouched."
                    : $"{name}: unreadable or corrupt — the file could not be loaded.");
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
        var projectBlockers = project.Id > 0 ? blockers.GetForProject(project.Id) : [];

        // The legacy scalar blocker fields stay for older readers: they mirror
        // the top open blocker (most recently updated Active, else Monitoring),
        // falling back to the project's own fields when nothing is open.
        var topBlocker = projectBlockers
            .Where(b => b.Status is BlockerStatus.Active or BlockerStatus.Monitoring)
            .OrderByDescending(b => b.Status == BlockerStatus.Active)
            .ThenByDescending(b => b.UpdatedAt)
            .FirstOrDefault();

        var state = new SharedProjectState
        {
            SchemaVersion = SharedProjectState.SupportedSchemaVersion,
            Id = project.SharedId is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"),
            Name = project.Name,
            Description = project.Description,
            Objective = project.CurrentObjective,
            Status = project.Status.ToString(),
            ActiveTask = project.ActiveTask,
            CurrentFrontier = projectFrontiers.FirstOrDefault(f => f.Status == FrontierStatus.Current)?.Title,
            BlockerClass = topBlocker?.BlockerClass ?? project.BlockerClass,
            Evidence = topBlocker?.Evidence ?? project.Evidence,
            NextSafestStep = topBlocker?.NextSafestStep ?? project.NextSafestStep,
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
            // All statuses are exported — resolved/dismissed blockers are history, not clutter.
            Blockers = projectBlockers.Select(b => new SharedBlocker
            {
                StableId = b.StableId,
                Title = b.Title,
                Description = b.Description,
                BlockerClass = b.BlockerClass,
                Severity = b.Severity,
                Status = b.Status.ToString(),
                Evidence = b.Evidence,
                NextSafestStep = b.NextSafestStep,
                Owner = b.Owner,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
                ResolvedAt = b.ResolvedAt,
                Tags = [.. b.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)],
                SourceReference = b.SourceReference,
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
