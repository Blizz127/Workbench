using System.Text.Json;
using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class SharedStateServiceTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly string _sharedFolder =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "shared-" + Guid.NewGuid().ToString("N"));
    private readonly string _repoDirectory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "repo-" + Guid.NewGuid().ToString("N"));

    private readonly ProjectRepository _projects;
    private readonly MilestoneRepository _milestones;
    private readonly FrontierRepository _frontiers;
    private readonly SharedStateService _service;

    public SharedStateServiceTests()
    {
        Directory.CreateDirectory(_sharedFolder);
        Directory.CreateDirectory(_repoDirectory);
        _projects = new ProjectRepository(_fixture.Database);
        _milestones = new MilestoneRepository(_fixture.Database);
        _frontiers = new FrontierRepository(_fixture.Database);
        _service = new SharedStateService(_projects, _milestones, _frontiers);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        foreach (var dir in new[] { _sharedFolder, _repoDirectory })
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private Project AddFullProject()
    {
        var project = new Project
        {
            Name = "Xenogears Decompilation",
            Status = ProjectStatus.Active,
            Description = "Native PC port",
            RepositoryPath = _repoDirectory,
            BuildCommand = "./pc_port/build_port.sh",
            RunCommand = "./build_native/xeno-port",
            TestCommand = "python3 tools/check_matches.py",
            CurrentObjective = "First recognizable field scene",
            ActiveTask = "Camera Y chain",
            BlockerClass = "camera math",
            Evidence = "captures/render_diag/permfix_*.log",
            NextSafestStep = "Read-only pass over func_8007CD80",
            PreferredAiProvider = "claude-fable-5",
            PromptRules = ["Never bypass camera clamp", "Verify with nm, not LINK OK"],
            AiContextPaths = ["docs/ai_context/ACTIVE_HANDOFF.md"],
            DocumentationPaths = ["docs/"],
            CustomLogPatterns = ["Map0 guard", "RUN_RC="],
            Tags = ["PSX", "Decompilation"],
            GitHubRepoUrl = "https://github.com/Blizz127/xenogears-decomp-ai",
            DefaultBranch = "ai-private-main",
        };
        _projects.Add(project);

        _milestones.Add(new Milestone { ProjectId = project.Id, Title = "Textured field geometry", IsCompleted = true, SortOrder = 0 });
        _milestones.Add(new Milestone { ProjectId = project.Id, Title = "Correct framing", SortOrder = 1 });
        _frontiers.Add(new Frontier { ProjectId = project.Id, Title = "eye/at Y pitch chain", Status = FrontierStatus.Current, CreatedAt = DateTimeOffset.Now });
        _frontiers.Add(new Frontier { ProjectId = project.Id, Title = "retail-shaped 0xBB upload", Status = FrontierStatus.Next, CreatedAt = DateTimeOffset.Now });

        return project;
    }

    [Fact]
    public void Export_writes_shared_json_under_projects_folder()
    {
        var project = AddFullProject();

        var result = _service.Export(_sharedFolder, project, "desktop");

        Assert.True(result.Success);
        Assert.Equal(
            Path.Combine(_sharedFolder, "projects", "xenogears-decompilation.workbench.json"),
            result.FilePath);
        Assert.True(File.Exists(result.FilePath));

        var state = _service.LoadFile(result.FilePath);
        Assert.NotNull(state);
        Assert.Equal(project.SharedId, state.Id);
        Assert.False(string.IsNullOrEmpty(state.Id));
        Assert.Equal("Xenogears Decompilation", state.Name);
        Assert.Equal("eye/at Y pitch chain", state.CurrentFrontier);
        Assert.Equal("desktop", state.UpdatedBy);

        var persisted = _projects.GetById(project.Id)!;
        Assert.Equal(state.Id, persisted.SharedId);
        Assert.NotNull(persisted.SharedLastExportedAt);
        Assert.Equal(state.UpdatedAt, persisted.SharedLastSyncedStamp);
    }

    [Fact]
    public void Shared_json_round_trips_all_shared_fields()
    {
        var source = AddFullProject();
        var result = _service.Export(_sharedFolder, source, "desktop");
        var state = _service.LoadFile(result.FilePath)!;

        // Simulate a fresh machine: the local project is gone, only the file remains.
        _projects.Delete(source.Id);

        var imported = _service.CreateFromShared(state, "desktop").Project;
        var loaded = _projects.GetById(imported.Id)!;

        Assert.Equal(source.Name, loaded.Name);
        Assert.Equal(source.PromptRules, loaded.PromptRules);
        Assert.Equal(source.AiContextPaths, loaded.AiContextPaths);
        Assert.Equal(source.DocumentationPaths, loaded.DocumentationPaths);
        Assert.Equal(source.CustomLogPatterns, loaded.CustomLogPatterns);
        Assert.Equal(source.BuildCommand, loaded.BuildCommand);
        Assert.Equal(source.RunCommand, loaded.RunCommand);
        Assert.Equal(source.TestCommand, loaded.TestCommand);
        Assert.Equal(source.BlockerClass, loaded.BlockerClass);
        Assert.Equal(source.Evidence, loaded.Evidence);
        Assert.Equal(source.NextSafestStep, loaded.NextSafestStep);
        Assert.Equal(source.CurrentObjective, loaded.CurrentObjective);
        Assert.Equal(source.PreferredAiProvider, loaded.PreferredAiProvider);
        Assert.Equal(source.GitHubRepoUrl, loaded.GitHubRepoUrl);
        Assert.Equal(source.DefaultBranch, loaded.DefaultBranch);
        Assert.Equal(source.Tags.Order(), loaded.Tags.Order()); // tags are a set; the store keeps them alphabetical
        Assert.Equal(source.RepositoryPath, loaded.RepositoryPath);

        var milestones = _milestones.GetForProject(loaded.Id);
        Assert.Equal(["Textured field geometry", "Correct framing"], milestones.OrderBy(m => m.SortOrder).Select(m => m.Title));
        Assert.True(milestones.Single(m => m.Title == "Textured field geometry").IsCompleted);

        var frontiers = _frontiers.GetForProject(loaded.Id);
        Assert.Equal(FrontierStatus.Current, frontiers.Single(f => f.Title == "eye/at Y pitch chain").Status);
        Assert.Equal(FrontierStatus.Next, frontiers.Single(f => f.Title == "retail-shaped 0xBB upload").Status);
    }

    [Fact]
    public void Machine_profile_is_selected_by_machine_name()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");

        // A laptop with its own checkout exports its profile into the same file.
        var laptopRepo = Path.Combine(_repoDirectory, "laptop-checkout");
        Directory.CreateDirectory(laptopRepo);
        var path = _service.GetSharedFilePath(_sharedFolder, project);
        var state = _service.LoadFile(path)!;
        state.MachineProfiles.Add(new MachineProfile { MachineName = "laptop", RepositoryPath = laptopRepo });
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var reloaded = _service.LoadFile(path)!;
        _projects.Delete(project.Id); // simulate the laptop: no local copy yet

        var imported = _service.CreateFromShared(reloaded, "laptop");
        Assert.Equal(laptopRepo, imported.Project.RepositoryPath);
        Assert.True(imported.RepositoryAvailable);

        var desktopPreview = _service.PreviewImport(reloaded, new Project { Name = "x" }, "desktop");
        Assert.Equal("desktop", desktopPreview.MatchedProfile?.MachineName);
    }

    [Fact]
    public void Import_with_no_profile_for_this_machine_marks_unavailable_not_broken()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");
        var state = _service.LoadFile(_service.GetSharedFilePath(_sharedFolder, project))!;
        _projects.Delete(project.Id); // simulate a machine without the checkout

        var imported = _service.CreateFromShared(state, "some-other-machine");

        Assert.False(imported.RepositoryAvailable);
        // The project imported fine; only the repository is absent here.
        var loaded = _projects.GetById(imported.Project.Id)!;
        Assert.Equal("Xenogears Decompilation", loaded.Name);
        Assert.Null(loaded.RepositoryPath);
    }

    [Fact]
    public void Export_warns_when_shared_file_is_newer()
    {
        var project = AddFullProject();
        Assert.True(_service.Export(_sharedFolder, project, "desktop").Success);

        // Another machine exports afterwards: the file's updatedAt moves past our stamp.
        var path = _service.GetSharedFilePath(_sharedFolder, project);
        var state = _service.LoadFile(path)!;
        state.UpdatedAt = state.UpdatedAt.AddMinutes(5);
        state.UpdatedBy = "laptop";
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var blocked = _service.Export(_sharedFolder, project, "desktop");
        Assert.False(blocked.Success);
        Assert.Contains("newer", blocked.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("laptop", _service.LoadFile(path)!.UpdatedBy); // nothing was written

        var forced = _service.Export(_sharedFolder, project, "desktop", force: true);
        Assert.True(forced.Success);
        Assert.Equal("desktop", _service.LoadFile(path)!.UpdatedBy);
    }

    [Fact]
    public void Export_warns_when_file_exists_but_machine_never_synced()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");

        // A different local project that never synced but slugs to the same file.
        var stranger = new Project { Name = "Xenogears Decompilation" };
        _projects.Add(stranger);

        var blocked = _service.Export(_sharedFolder, stranger, "desktop");
        Assert.False(blocked.Success);
        Assert.NotNull(blocked.Warning);
    }

    [Fact]
    public void Import_preview_shows_changed_fields_only()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");
        var state = _service.LoadFile(_service.GetSharedFilePath(_sharedFolder, project))!;

        // Reload so list instances match persisted state, then diff against edits.
        var local = _projects.GetById(project.Id)!;
        state.Name = "Xenogears Decompilation (renamed)";
        state.CommandTemplates.Build = "make -j8";

        var preview = _service.PreviewImport(state, local, "desktop");

        Assert.Contains(preview.Changes, c => c.Field == "Name" && c.SharedValue.Contains("renamed"));
        Assert.Contains(preview.Changes, c => c.Field == "Build command" && c.SharedValue == "make -j8");
        Assert.DoesNotContain(preview.Changes, c => c.Field == "Tags");
        Assert.DoesNotContain(preview.Changes, c => c.Field == "Milestones");
    }

    [Fact]
    public void Preview_flags_conflict_when_both_sides_changed()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");

        // Local edit after the export…
        var local = _projects.GetById(project.Id)!;
        local.ActiveTask = "changed locally";
        _projects.Update(local);
        local = _projects.GetById(project.Id)!;

        // …and a shared-side change too.
        var state = _service.LoadFile(_service.GetSharedFilePath(_sharedFolder, project))!;
        state.ActiveTask = "changed remotely";
        state.UpdatedAt = state.UpdatedAt.AddMinutes(1);

        var preview = _service.PreviewImport(state, local, "desktop");
        Assert.True(preview.LocalChangedSinceSync);
        Assert.True(preview.SharedChangedSinceSync);
        Assert.True(preview.HasConflict);

        // Right after a clean export there is no conflict.
        _service.Export(_sharedFolder, local, "desktop", force: true);
        local = _projects.GetById(project.Id)!;
        var freshState = _service.LoadFile(_service.GetSharedFilePath(_sharedFolder, project))!;
        var cleanPreview = _service.PreviewImport(freshState, local, "desktop");
        Assert.False(cleanPreview.HasConflict);
    }

    [Fact]
    public void Local_command_history_is_never_exported()
    {
        var project = AddFullProject();
        var builds = new BuildHistoryRepository(_fixture.Database);
        builds.Add(new BuildRecord
        {
            ProjectId = project.Id,
            Kind = ExecutionKind.Build,
            Command = "secret local command run",
            StartedAt = DateTimeOffset.Now,
            ExitCode = 0,
            OutputLog = "SECRET-LOCAL-OUTPUT",
        });

        var result = _service.Export(_sharedFolder, project, "desktop");
        var json = File.ReadAllText(result.FilePath);

        Assert.DoesNotContain("SECRET-LOCAL-OUTPUT", json);

        // The file's top-level shape is a fixed whitelist — history can never leak in.
        using var doc = JsonDocument.Parse(json);
        var allowed = new HashSet<string>
        {
            "schemaVersion", "id", "name", "description", "objective", "status",
            "activeTask", "currentFrontier", "blockerClass", "evidence",
            "nextSafestStep", "assignedModel", "promptRules", "aiContextPaths",
            "documentationPaths", "commandTemplates", "customLogPatterns",
            "milestones", "frontierChecklist", "tags", "gitHubRepoUrl",
            "defaultBranch", "machineProfiles", "updatedAt", "updatedBy",
        };
        foreach (var property in doc.RootElement.EnumerateObject())
            Assert.Contains(property.Name, allowed);
    }

    [Fact]
    public void Stable_id_prevents_duplicates_on_repeated_import_all()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");
        _projects.Delete(project.Id);
        Assert.Empty(_projects.GetAll());

        var first = _service.ImportAll(_sharedFolder, "desktop");
        Assert.Single(first.Applied);
        Assert.Single(_projects.GetAll());

        var second = _service.ImportAll(_sharedFolder, "desktop");
        Assert.Empty(second.Applied);
        Assert.Single(_projects.GetAll());
        Assert.Single(second.Skipped); // up to date, not duplicated
    }

    [Fact]
    public void Import_all_never_overwrites_existing_projects()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");

        // Shared side moves ahead.
        var path = _service.GetSharedFilePath(_sharedFolder, project);
        var state = _service.LoadFile(path)!;
        state.ActiveTask = "changed remotely";
        state.UpdatedAt = state.UpdatedAt.AddMinutes(5);
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var summary = _service.ImportAll(_sharedFolder, "desktop");

        Assert.Empty(summary.Applied);
        Assert.Single(summary.Warnings); // flagged for per-project review
        Assert.Equal("Camera Y chain", _projects.GetById(project.Id)!.ActiveTask); // untouched
    }

    [Fact]
    public void Export_preserves_other_machines_profiles()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");

        var path = _service.GetSharedFilePath(_sharedFolder, project);
        var state = _service.LoadFile(path)!;
        state.MachineProfiles.Add(new MachineProfile { MachineName = "laptop", RepositoryPath = "/home/blizz/laptop/xeno", Notes = "slow disk" });
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        // Re-sync our stamp, then export again from the desktop.
        var local = _projects.GetById(project.Id)!;
        _service.ApplyImport(_service.LoadFile(path)!, local, "desktop");
        local = _projects.GetById(project.Id)!;
        Assert.True(_service.Export(_sharedFolder, local, "desktop").Success);

        var final = _service.LoadFile(path)!;
        var laptop = final.MachineProfiles.Single(p => p.MachineName == "laptop");
        Assert.Equal("/home/blizz/laptop/xeno", laptop.RepositoryPath);
        Assert.Equal("slow disk", laptop.Notes);
        Assert.Contains(final.MachineProfiles, p => p.MachineName == "desktop");
    }

    [Fact]
    public void Project_shared_fields_round_trip_through_repository()
    {
        var project = AddFullProject();
        project.SharedId = "abc123";
        project.SharedLastImportedAt = DateTimeOffset.Now.AddMinutes(-2);
        project.SharedLastExportedAt = DateTimeOffset.Now.AddMinutes(-1);
        project.SharedLastSyncedStamp = DateTimeOffset.Now;
        _projects.Update(project);

        var loaded = _projects.GetById(project.Id)!;
        Assert.Equal("abc123", loaded.SharedId);
        Assert.Equal(project.PromptRules, loaded.PromptRules);
        Assert.Equal(project.CustomLogPatterns, loaded.CustomLogPatterns);
        Assert.Equal(project.BlockerClass, loaded.BlockerClass);
        Assert.Equal(project.Evidence, loaded.Evidence);
        Assert.Equal(project.NextSafestStep, loaded.NextSafestStep);
        Assert.Equal(project.GitHubRepoUrl, loaded.GitHubRepoUrl);
        Assert.Equal(project.DefaultBranch, loaded.DefaultBranch);
        Assert.Equal(project.SharedLastImportedAt, loaded.SharedLastImportedAt);
        Assert.Equal(project.SharedLastExportedAt, loaded.SharedLastExportedAt);
        Assert.Equal(project.SharedLastSyncedStamp, loaded.SharedLastSyncedStamp);
    }

    [Fact]
    public void UpdateSharedSync_does_not_bump_updated_at()
    {
        var project = AddFullProject();
        var before = _projects.GetById(project.Id)!.UpdatedAt;

        project.SharedId = "sync-only";
        project.SharedLastExportedAt = DateTimeOffset.Now;
        _projects.UpdateSharedSync(project);

        var after = _projects.GetById(project.Id)!;
        Assert.Equal(before, after.UpdatedAt);
        Assert.Equal("sync-only", after.SharedId);
    }

    [Fact]
    public void CheckSharedFolder_reports_missing_folder()
    {
        var missing = Path.Combine(_sharedFolder, "does-not-exist");

        var status = _service.CheckSharedFolder(missing);

        Assert.False(status.Exists);
        Assert.False(status.Writable);
        Assert.Equal(0, status.ProjectFileCount);
        Assert.Single(status.Warnings);
        Assert.Contains("missing or unavailable", status.Warnings[0]);
    }

    [Fact]
    public void CheckSharedFolder_creates_projects_folder_and_counts_files()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");
        Directory.Delete(_service.GetProjectsFolder(_sharedFolder), recursive: true);

        var status = _service.CheckSharedFolder(_sharedFolder);

        Assert.True(status.Exists);
        Assert.True(status.Writable);
        Assert.True(status.ProjectsFolderExists);
        Assert.True(Directory.Exists(_service.GetProjectsFolder(_sharedFolder)));
        Assert.Equal(0, status.ProjectFileCount);
        Assert.Empty(status.Warnings);

        _service.Export(_sharedFolder, project, "desktop", force: true);
        status = _service.CheckSharedFolder(_sharedFolder);
        Assert.Equal(1, status.ProjectFileCount);
        Assert.Empty(status.Warnings);
    }

    [Fact]
    public void CheckSharedFolder_warns_about_dropbox_conflicted_copies()
    {
        var project = AddFullProject();
        var result = _service.Export(_sharedFolder, project, "desktop");
        File.Copy(result.FilePath, Path.Combine(
            _service.GetProjectsFolder(_sharedFolder),
            "xenogears-decompilation (laptop's conflicted copy 2026-07-30).workbench.json"));

        var status = _service.CheckSharedFolder(_sharedFolder);

        Assert.Equal(2, status.ProjectFileCount);
        var warning = Assert.Single(status.Warnings);
        Assert.Contains("conflicted copy", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckSharedFolder_warns_about_unreadable_files()
    {
        var project = AddFullProject();
        _service.Export(_sharedFolder, project, "desktop");
        File.WriteAllText(
            Path.Combine(_service.GetProjectsFolder(_sharedFolder), "broken.workbench.json"),
            "{ not json ]");
        File.WriteAllText(
            Path.Combine(_service.GetProjectsFolder(_sharedFolder), "empty.workbench.json"),
            string.Empty);

        var status = _service.CheckSharedFolder(_sharedFolder);

        Assert.Equal(3, status.ProjectFileCount);
        Assert.Equal(2, status.Warnings.Count);
        Assert.Contains(status.Warnings, w => w.Contains("broken.workbench.json") && w.Contains("unreadable"));
        Assert.Contains(status.Warnings, w => w.Contains("empty.workbench.json") && w.Contains("unreadable"));
    }
}
