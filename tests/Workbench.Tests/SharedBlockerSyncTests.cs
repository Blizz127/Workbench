using System.Text.Json;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Schema-2 blocker sync through the shared state folder: round-trips,
/// stable-id matching, preview diffs, per-blocker conflicts, version gating,
/// and the atomic export writer.
/// </summary>
public sealed class SharedBlockerSyncTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly string _sharedFolder =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "blocker-sync-" + Guid.NewGuid().ToString("N"));

    private readonly ProjectRepository _projects;
    private readonly BlockerRepository _blockers;
    private readonly SharedStateService _service;

    public SharedBlockerSyncTests()
    {
        Directory.CreateDirectory(_sharedFolder);
        _projects = new ProjectRepository(_fixture.Database);
        _blockers = new BlockerRepository(_fixture.Database);
        _service = new SharedStateService(
            _projects,
            new MilestoneRepository(_fixture.Database),
            new FrontierRepository(_fixture.Database),
            _blockers);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_sharedFolder))
            Directory.Delete(_sharedFolder, recursive: true);
    }

    private Project AddProject(string name = "Xenogears Decompilation")
    {
        var project = new Project { Name = name };
        _projects.Add(project);
        return project;
    }

    private Blocker AddRichBlocker(Project project) => new()
    {
        ProjectId = project.Id,
        Title = "Camera Y pitch chain",
        Description = "Eye/at Y pitch math does not match retail framing",
        BlockerClass = "camera math",
        Severity = "critical",
        Status = BlockerStatus.Active,
        Evidence = "captures/render_diag/permfix_*.log",
        NextSafestStep = "Read-only pass over func_8007CD80",
        Owner = "blizz",
        Tags = ["PSX", "camera"],
        SourceReference = "upstream issue #42",
    };

    private SharedProjectState LoadShared(Project project) =>
        _service.LoadFile(_service.GetSharedFilePath(_sharedFolder, project))!;

    [Fact]
    public void Rich_blocker_collection_round_trips_all_statuses_and_fields()
    {
        var project = AddProject();
        var active = AddRichBlocker(project);
        _blockers.Add(active);
        _blockers.Add(new Blocker
        {
            ProjectId = project.Id, Title = "Watch item", Description = "intermittent",
            Status = BlockerStatus.Monitoring, Severity = "low", Owner = "laptop",
        });
        _blockers.Add(new Blocker
        {
            ProjectId = project.Id, Title = "Old blocker", Description = "done",
            Status = BlockerStatus.Resolved, ResolvedAt = DateTimeOffset.Now.AddDays(-1),
            Resolution = "local-only resolution text",
        });
        _blockers.Add(new Blocker
        {
            ProjectId = project.Id, Title = "Not a bug", Description = "wontfix",
            Status = BlockerStatus.Dismissed,
        });

        var result = _service.Export(_sharedFolder, project, "desktop");
        Assert.True(result.Success);
        var state = _service.LoadFile(result.FilePath)!;

        Assert.Equal(2, state.SchemaVersion);
        Assert.Equal(4, state.Blockers.Count);
        var sharedActive = state.Blockers.Single(b => b.StableId == active.StableId);
        Assert.Equal("Camera Y pitch chain", sharedActive.Title);
        Assert.Equal("camera math", sharedActive.BlockerClass);
        Assert.Equal("critical", sharedActive.Severity);
        Assert.Equal("Active", sharedActive.Status);
        Assert.Equal("captures/render_diag/permfix_*.log", sharedActive.Evidence);
        Assert.Equal("Read-only pass over func_8007CD80", sharedActive.NextSafestStep);
        Assert.Equal("blizz", sharedActive.Owner);
        Assert.Equal(["camera", "PSX"], sharedActive.Tags); // canonically sorted
        Assert.Equal("upstream issue #42", sharedActive.SourceReference);
        Assert.Equal(
            ["Dismissed", "Monitoring", "Resolved"],
            state.Blockers.Where(b => b.StableId != active.StableId).Select(b => b.Status).Order());

        // Simulate a fresh machine: only the shared file remains.
        _projects.Delete(project.Id);
        var imported = _service.CreateFromShared(state, "desktop").Project;

        var importedBlockers = _blockers.GetForProject(imported.Id);
        Assert.Equal(4, importedBlockers.Count);
        var importedActive = importedBlockers.Single(b => b.StableId == active.StableId);
        Assert.Equal("Camera Y pitch chain", importedActive.Title);
        Assert.Equal("critical", importedActive.Severity);
        Assert.Equal(BlockerStatus.Active, importedActive.Status);
        Assert.Equal("captures/render_diag/permfix_*.log", importedActive.Evidence);
        Assert.Equal(["camera", "PSX"], importedActive.Tags.Order()); // culture-aware ordering
        Assert.Equal("blizz", importedActive.Owner);
        Assert.Equal("upstream issue #42", importedActive.SourceReference);

        // The resolution text is local-only: it never crosses the folder.
        var importedResolved = importedBlockers.Single(b => b.Status == BlockerStatus.Resolved);
        Assert.Null(importedResolved.Resolution);
        Assert.True(importedResolved.IsResolved);
        Assert.NotNull(importedResolved.ResolvedAt);
        Assert.DoesNotContain("local-only resolution text", File.ReadAllText(result.FilePath));
    }

    [Fact]
    public void Repeated_import_is_idempotent()
    {
        var project = AddProject();
        _blockers.Add(AddRichBlocker(project));
        _service.Export(_sharedFolder, project, "desktop");
        var state = LoadShared(project);
        _projects.Delete(project.Id);

        var imported = _service.CreateFromShared(state, "desktop").Project;
        Assert.Single(_blockers.GetForProject(imported.Id));

        // Second application of the same file: zero changes, no duplicates.
        var second = _service.ApplyImport(state, _projects.GetById(imported.Id)!, "desktop");
        Assert.Empty(second.SkippedBlockerConflicts);
        Assert.Single(_blockers.GetForProject(imported.Id));

        var preview = _service.PreviewImport(state, _projects.GetById(imported.Id)!, "desktop");
        Assert.Empty(preview.Changes);
        Assert.Empty(preview.BlockerConflicts);
    }

    [Fact]
    public void Updates_match_by_stable_id_not_by_title()
    {
        var project = AddProject();
        var blocker = AddRichBlocker(project);
        _blockers.Add(blocker);
        _service.Export(_sharedFolder, project, "desktop");

        // Another machine renames AND re-severities the same record.
        var state = LoadShared(project);
        var shared = state.Blockers.Single();
        shared.Title = "Renamed on the laptop";
        shared.Severity = "high";
        state.UpdatedAt = state.UpdatedAt.AddMinutes(5);

        var local = _projects.GetById(project.Id)!;
        var preview = _service.PreviewImport(state, local, "desktop");
        Assert.Contains(preview.Changes, c => c.Field.Contains("Severity") && c.SharedValue == "high");

        _service.ApplyImport(state, local, "desktop");

        var after = _blockers.GetForProject(project.Id);
        Assert.Single(after); // updated in place — no duplicate under the new title
        Assert.Equal(blocker.Id, after[0].Id);
        Assert.Equal(blocker.StableId, after[0].StableId);
        Assert.Equal("Renamed on the laptop", after[0].Title);
        Assert.Equal("high", after[0].Severity);
    }

    [Fact]
    public void Resolved_blocker_survives_and_reopening_syncs_back()
    {
        var project = AddProject();
        var blocker = AddRichBlocker(project);
        _blockers.Add(blocker);
        _service.Export(_sharedFolder, project, "desktop");

        // Another machine resolves it.
        var state = LoadShared(project);
        state.Blockers.Single().Status = "Resolved";
        state.Blockers.Single().ResolvedAt = DateTimeOffset.Now;
        state.UpdatedAt = state.UpdatedAt.AddMinutes(5);
        _service.ApplyImport(state, _projects.GetById(project.Id)!, "desktop");

        var resolved = _blockers.GetForProject(project.Id).Single();
        Assert.Equal(BlockerStatus.Resolved, resolved.Status);
        Assert.True(resolved.IsResolved); // legacy queries stay correct
        Assert.Empty(_blockers.GetOpen(project.Id));

        // This machine reopens it; the new state syncs back through an export.
        resolved.Status = BlockerStatus.Active;
        resolved.ResolvedAt = null;
        _blockers.Update(resolved);
        var export = _service.Export(_sharedFolder, _projects.GetById(project.Id)!, "desktop");
        Assert.True(export.Success);

        var reopened = _service.LoadFile(export.FilePath)!;
        var sharedBlocker = reopened.Blockers.Single();
        Assert.Equal("Active", sharedBlocker.Status);
        Assert.Null(sharedBlocker.ResolvedAt);

        _service.ApplyImport(reopened, _projects.GetById(project.Id)!, "desktop");
        var final = _blockers.GetForProject(project.Id).Single();
        Assert.Equal(BlockerStatus.Active, final.Status);
        Assert.False(final.IsResolved);
    }

    [Fact]
    public void Preview_shows_additions_removals_edits_and_status_changes()
    {
        var project = AddProject();
        var matched = AddRichBlocker(project);
        _blockers.Add(matched);
        _service.Export(_sharedFolder, project, "desktop");

        // Created after the export, so the shared file has never heard of it.
        _blockers.Add(new Blocker { ProjectId = project.Id, Title = "Local only", Description = "not exported" });

        // The shared file gains a blocker, and the matched one changes fields + status.
        var state = LoadShared(project);
        state.Blockers.Add(new SharedBlocker
        {
            StableId = "new-from-laptop",
            Title = "Laptop blocker",
            Description = "appeared elsewhere",
            Status = "Active",
            Severity = "medium",
        });
        var sharedMatched = state.Blockers.Single(b => b.StableId == matched.StableId);
        sharedMatched.Severity = "high";
        sharedMatched.Status = "Resolved";
        sharedMatched.ResolvedAt = DateTimeOffset.Now;

        var preview = _service.PreviewImport(state, _projects.GetById(project.Id)!, "desktop");

        Assert.Contains(preview.Changes, c => c.Field == "Blocker: Laptop blocker — (new blocker)");
        Assert.Contains(preview.Changes, c => c.Field == "Blocker: Local only — (removed)");
        Assert.Contains(preview.Changes,
            c => c.Field == "Blocker: Camera Y pitch chain — Severity" && c.LocalValue == "critical" && c.SharedValue == "high");
        Assert.Contains(preview.Changes,
            c => c.Field == "Blocker: Camera Y pitch chain — Status" && c.LocalValue == "Active" && c.SharedValue == "Resolved");
    }

    [Fact]
    public void Import_deletes_local_only_blockers_only_after_they_appeared_in_preview()
    {
        var project = AddProject();
        _blockers.Add(AddRichBlocker(project));
        _service.Export(_sharedFolder, project, "desktop");

        // Created after the export: the shared file has never heard of it.
        var localOnly = new Blocker { ProjectId = project.Id, Title = "Local only", Description = "doomed" };
        _blockers.Add(localOnly);

        var state = LoadShared(project);
        var preview = _service.PreviewImport(state, _projects.GetById(project.Id)!, "desktop");
        Assert.Contains(preview.Changes, c => c.Field == "Blocker: Local only — (removed)");

        _service.ApplyImport(state, _projects.GetById(project.Id)!, "desktop");
        Assert.Single(_blockers.GetForProject(project.Id));
        Assert.Null(_blockers.GetForProject(project.Id).FirstOrDefault(b => b.Id == localOnly.Id));
    }

    [Fact]
    public void Both_sides_changed_is_a_conflict_and_is_not_overwritten_by_default()
    {
        var project = AddProject();
        var blocker = AddRichBlocker(project);
        _blockers.Add(blocker);
        _service.Export(_sharedFolder, project, "desktop");

        // Local edit after the sync…
        var local = _blockers.GetForProject(project.Id).Single();
        local.Severity = "low";
        _blockers.Update(local);

        // …and a shared-side edit too, both newer than the last sync.
        var state = LoadShared(project);
        state.Blockers.Single().Severity = "high";
        state.Blockers.Single().UpdatedAt = DateTimeOffset.Now.AddMinutes(5);
        state.UpdatedAt = state.UpdatedAt.AddMinutes(5);

        var preview = _service.PreviewImport(state, _projects.GetById(project.Id)!, "desktop");
        var conflict = Assert.Single(preview.BlockerConflicts);
        Assert.Contains("Camera Y pitch chain", conflict);

        var applied = _service.ApplyImport(state, _projects.GetById(project.Id)!, "desktop");
        Assert.Single(applied.SkippedBlockerConflicts);
        Assert.Equal("low", _blockers.GetForProject(project.Id).Single().Severity); // local kept

        // Explicit overwrite applies the shared version. Re-bump both sides so
        // the conflict still holds after the first apply moved the sync stamp.
        var localAgain = _blockers.GetForProject(project.Id).Single();
        localAgain.Severity = "low";
        _blockers.Update(localAgain);
        state.Blockers.Single().UpdatedAt = DateTimeOffset.Now.AddMinutes(10);
        state.UpdatedAt = state.UpdatedAt.AddMinutes(10);

        var overwritePreview = _service.PreviewImport(state, _projects.GetById(project.Id)!, "desktop");
        Assert.Single(overwritePreview.BlockerConflicts);

        var overwritten = _service.ApplyImport(
            state, _projects.GetById(project.Id)!, "desktop", overwriteBlockerConflicts: true);
        Assert.Empty(overwritten.SkippedBlockerConflicts);
        Assert.Equal("high", _blockers.GetForProject(project.Id).Single().Severity);
    }

    [Fact]
    public void One_sided_blocker_change_is_not_a_conflict()
    {
        var project = AddProject();
        var blocker = AddRichBlocker(project);
        _blockers.Add(blocker);
        _service.Export(_sharedFolder, project, "desktop");

        // Only the shared side moved (local blocker untouched since the sync).
        var state = LoadShared(project);
        state.Blockers.Single().Severity = "high";
        state.Blockers.Single().UpdatedAt = DateTimeOffset.Now.AddMinutes(5);
        state.UpdatedAt = state.UpdatedAt.AddMinutes(5);

        var preview = _service.PreviewImport(state, _projects.GetById(project.Id)!, "desktop");
        Assert.Empty(preview.BlockerConflicts);

        var applied = _service.ApplyImport(state, _projects.GetById(project.Id)!, "desktop");
        Assert.Empty(applied.SkippedBlockerConflicts);
        Assert.Equal("high", _blockers.GetForProject(project.Id).Single().Severity);
    }

    [Fact]
    public void Schema_1_file_loads_and_never_touches_local_blockers()
    {
        var project = AddProject();
        _blockers.Add(AddRichBlocker(project));
        var localBlocker = _blockers.GetForProject(project.Id).Single();

        // A file written by a schema-1 Workbench: no schemaVersion, no blockers key.
        var v1Json = $$"""
            {
              "id": "legacy-shared-id",
              "name": "Xenogears Decompilation",
              "status": "Active",
              "blockerClass": "legacy scalar class",
              "evidence": "legacy scalar evidence",
              "nextSafestStep": "legacy scalar step",
              "updatedAt": "{{DateTimeOffset.Now:O}}",
              "updatedBy": "old-desktop"
            }
            """;
        var path = Path.Combine(_service.GetProjectsFolder(_sharedFolder), "legacy.workbench.json");
        Directory.CreateDirectory(_service.GetProjectsFolder(_sharedFolder));
        File.WriteAllText(path, v1Json);

        var state = _service.LoadFile(path);
        Assert.NotNull(state);
        Assert.Equal(1, state.SchemaVersion);
        Assert.Empty(state.Blockers);

        // Old-reader scalars still apply on import…
        project.SharedId = state.Id;
        _projects.UpdateSharedSync(project);
        _service.ApplyImport(state, _projects.GetById(project.Id)!, "desktop");
        var loaded = _projects.GetById(project.Id)!;
        Assert.Equal("legacy scalar class", loaded.BlockerClass);
        Assert.Equal("legacy scalar evidence", loaded.Evidence);
        Assert.Equal("legacy scalar step", loaded.NextSafestStep);

        // …but a schema-1 file has no blocker concept — local blockers survive.
        var after = _blockers.GetForProject(project.Id);
        Assert.Single(after);
        Assert.Equal(localBlocker.StableId, after[0].StableId);
    }

    [Fact]
    public void Schema_99_file_is_rejected_with_a_clear_message_and_left_untouched()
    {
        var project = AddProject();
        project.SharedId = "future-project";
        _projects.UpdateSharedSync(project);

        var v99Json = """
            {
              "schemaVersion": 99,
              "id": "future-project",
              "name": "Xenogears Decompilation",
              "blockers": [ { "stableId": "future-blocker", "title": "from the future" } ],
              "updatedAt": "2026-08-02T00:00:00.0000000+00:00"
            }
            """;
        Directory.CreateDirectory(_service.GetProjectsFolder(_sharedFolder));
        var path = Path.Combine(_service.GetProjectsFolder(_sharedFolder), "future.workbench.json");
        File.WriteAllText(path, v99Json);

        Assert.Null(_service.LoadFile(path));
        Assert.False(_service.TryLoadFile(path, out var state, out var error));
        Assert.Null(state);
        Assert.Equal("unsupported schema version 99 (this Workbench supports up to 2)", error);

        // The folder check and Import All surface the rejection instead of skipping silently.
        var status = _service.CheckSharedFolder(_sharedFolder);
        Assert.Contains(status.Warnings,
            w => w.Contains("future.workbench.json") && w.Contains("unsupported schema version 99"));

        var summary = _service.ImportAll(_sharedFolder, "desktop");
        Assert.Empty(summary.Applied);
        Assert.Contains(summary.Warnings,
            w => w.Contains("future.workbench.json") && w.Contains("unsupported schema version 99"));

        // The file's data is untouched and nothing was imported.
        Assert.Equal(v99Json, File.ReadAllText(path));
        Assert.Empty(_blockers.GetForProject(project.Id));
    }

    [Fact]
    public void Failed_export_leaves_the_previous_file_and_no_temp_files()
    {
        var project = AddProject();
        _blockers.Add(AddRichBlocker(project));
        var first = _service.Export(_sharedFolder, project, "desktop");
        Assert.True(first.Success);
        var originalContent = File.ReadAllText(first.FilePath);

        // Simulate the Dropbox folder failing mid-write.
        var failing = new SharedStateService(
            _projects,
            new MilestoneRepository(_fixture.Database),
            new FrontierRepository(_fixture.Database),
            _blockers,
            atomicWriter: (_, _) => throw new IOException("simulated sync folder failure"));

        var result = failing.Export(_sharedFolder, _projects.GetById(project.Id)!, "desktop");

        Assert.False(result.Success);
        Assert.NotNull(result.Warning);
        Assert.Contains("untouched", result.Warning);
        Assert.Equal(originalContent, File.ReadAllText(first.FilePath));
        Assert.Empty(Directory.EnumerateFiles(
            _service.GetProjectsFolder(_sharedFolder), "*.workbench.json.*.tmp"));
    }

    [Fact]
    public void Atomic_writer_replaces_content_and_cleans_up_after_serialization_failures()
    {
        var target = Path.Combine(_sharedFolder, "atomic.workbench.json");

        SharedStateService.WriteAllTextAtomic(target, "first");
        Assert.Equal("first", File.ReadAllText(target));

        SharedStateService.WriteAllTextAtomic(target, "second");
        Assert.Equal("second", File.ReadAllText(target));

        // A write failure mid-flight: previous content survives, temp is cleaned.
        Assert.ThrowsAny<IOException>(() =>
            SharedStateService.WriteAllTextAtomic(
                Path.Combine(_sharedFolder, "missing-dir", "never.workbench.json"), "boom"));
        Assert.Empty(Directory.EnumerateFiles(_sharedFolder, "*.tmp"));
    }

    [Fact]
    public void Folder_check_removes_only_our_own_temp_files()
    {
        var projectsFolder = _service.GetProjectsFolder(_sharedFolder);
        Directory.CreateDirectory(projectsFolder);
        var abandoned = Path.Combine(projectsFolder, "xeno.workbench.json.deadbeefcafe.tmp");
        File.WriteAllText(abandoned, "partial write");
        File.WriteAllText(Path.Combine(projectsFolder, "xeno.workbench.json"), "{ \"id\": \"keep-me\" }");
        File.WriteAllText(Path.Combine(projectsFolder, "dropbox-index.tmp"), "not ours");
        File.WriteAllText(Path.Combine(projectsFolder, "notes.json.bak"), "not ours");
        File.WriteAllText(Path.Combine(_sharedFolder, "xeno.workbench.json.1234.tmp"), "outside projects/");

        var status = _service.CheckSharedFolder(_sharedFolder);

        Assert.False(File.Exists(abandoned)); // ours: cleaned up
        Assert.True(File.Exists(Path.Combine(projectsFolder, "dropbox-index.tmp")));
        Assert.True(File.Exists(Path.Combine(projectsFolder, "notes.json.bak")));
        Assert.True(File.Exists(Path.Combine(projectsFolder, "xeno.workbench.json")));
        // Nothing outside the projects folder is touched.
        Assert.True(File.Exists(Path.Combine(_sharedFolder, "xeno.workbench.json.1234.tmp")));
        Assert.Equal(1, status.ProjectFileCount);
    }

    [Fact]
    public void Scalar_blocker_fields_are_derived_from_the_top_open_blocker()
    {
        var project = AddProject();
        project.BlockerClass = "project-level class";
        project.Evidence = "project-level evidence";
        project.NextSafestStep = "project-level step";
        _projects.Update(project);

        // No blockers at all: the project's own scalars are exported.
        var state = _service.BuildState(_projects.GetById(project.Id)!, "desktop", existing: null);
        Assert.Equal("project-level class", state.BlockerClass);

        // An older Monitoring blocker loses to a newer Active one.
        _blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Title = "watch",
            Description = "monitoring",
            Status = BlockerStatus.Monitoring,
            BlockerClass = "monitoring class",
            CreatedAt = DateTimeOffset.Now.AddHours(-2),
            UpdatedAt = DateTimeOffset.Now.AddHours(-2),
        });
        _blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Title = "top",
            Description = "active",
            Status = BlockerStatus.Active,
            BlockerClass = "active class",
            Evidence = "active evidence",
            NextSafestStep = "active step",
        });

        state = _service.BuildState(_projects.GetById(project.Id)!, "desktop", existing: null);
        Assert.Equal("active class", state.BlockerClass);
        Assert.Equal("active evidence", state.Evidence);
        Assert.Equal("active step", state.NextSafestStep);
    }

    [Fact]
    public void Exported_blockers_carry_no_local_only_data()
    {
        var project = AddProject();
        _blockers.Add(new Blocker
        {
            ProjectId = project.Id,
            Title = "History boundary probe",
            Description = "check the file shape",
            Status = BlockerStatus.Resolved,
            ResolvedAt = DateTimeOffset.Now,
            Resolution = "SECRET-RESOLUTION-DETAIL",
        });
        new TimelineRepository(_fixture.Database).Add(new TimelineEntry
        {
            ProjectId = project.Id, Category = TimelineCategory.Blocker, Message = "SECRET-TIMELINE-ENTRY",
        });
        new BuildHistoryRepository(_fixture.Database).Add(new BuildRecord
        {
            ProjectId = project.Id, Command = "SECRET-BUILD-COMMAND", StartedAt = DateTimeOffset.Now,
        });
        new TestHistoryRepository(_fixture.Database).Add(new TestRecord
        {
            ProjectId = project.Id, Command = "SECRET-TEST-COMMAND", Result = TestResult.Passed,
            StartedAt = DateTimeOffset.Now,
        });

        var result = _service.Export(_sharedFolder, project, "desktop");
        var json = File.ReadAllText(result.FilePath);

        Assert.DoesNotContain("SECRET-RESOLUTION-DETAIL", json);
        Assert.DoesNotContain("SECRET-TIMELINE-ENTRY", json);
        Assert.DoesNotContain("SECRET-BUILD-COMMAND", json);
        Assert.DoesNotContain("SECRET-TEST-COMMAND", json);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit", json, StringComparison.OrdinalIgnoreCase);

        // The blocker entry shape is a fixed whitelist too — no local ids, no resolution.
        using var doc = JsonDocument.Parse(json);
        var allowed = new HashSet<string>
        {
            "stableId", "title", "description", "blockerClass", "severity", "status",
            "evidence", "nextSafestStep", "owner", "createdAt", "updatedAt",
            "resolvedAt", "tags", "sourceReference",
        };
        var blocker = doc.RootElement.GetProperty("blockers")[0];
        foreach (var property in blocker.EnumerateObject())
            Assert.Contains(property.Name, allowed);
    }
}
