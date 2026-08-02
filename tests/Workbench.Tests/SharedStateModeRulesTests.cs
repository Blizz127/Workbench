using Workbench.Core.Models;
using Workbench.Core.Services;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Mode gating (SharedStateModeRules) — the seam the desktop view models use.
/// The view models themselves are not unit-tested (the test project has no
/// Workbench.App reference and no view-model test pattern), so the counting
/// fake here proves the rules the view models apply: outside Enabled mode no
/// badge path touches the shared folder at all.
/// </summary>
public sealed class SharedStateModeRulesTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly string _sharedFolder =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "mode-" + Guid.NewGuid().ToString("N"));

    public SharedStateModeRulesTests() => Directory.CreateDirectory(_sharedFolder);

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_sharedFolder))
            Directory.Delete(_sharedFolder, recursive: true);
    }

    /// <summary>Counts service calls; everything but LoadAll/CheckSharedFolder throws.</summary>
    private sealed class CountingSharedStateService : ISharedStateService
    {
        public int LoadAllCalls { get; private set; }
        public int CheckSharedFolderCalls { get; private set; }

        public IReadOnlyList<SharedStateFileInfo> LoadAll(string sharedFolder)
        {
            LoadAllCalls++;
            return [];
        }

        public SharedFolderStatus CheckSharedFolder(string sharedFolder)
        {
            CheckSharedFolderCalls++;
            return new SharedFolderStatus { Folder = sharedFolder };
        }

        public string GetProjectsFolder(string sharedFolder) => throw new NotImplementedException();
        public string GetSharedFilePath(string sharedFolder, Project project) => throw new NotImplementedException();
        public IReadOnlyList<SharedStateFileRejection> LoadRejections(string sharedFolder) => throw new NotImplementedException();
        public SharedProjectState? LoadFile(string filePath) => throw new NotImplementedException();
        public bool TryLoadFile(string filePath, out SharedProjectState? state, out string? error) => throw new NotImplementedException();
        public Project? MatchProject(IReadOnlyList<Project> projects, SharedProjectState state) => throw new NotImplementedException();
        public SharedExportResult Export(string sharedFolder, Project project, string machineName, bool force = false) => throw new NotImplementedException();
        public SharedImportPreview PreviewImport(SharedProjectState state, Project project, string machineName) => throw new NotImplementedException();
        public SharedImportResult ApplyImport(SharedProjectState state, Project project, string machineName, bool overwriteBlockerConflicts = false) => throw new NotImplementedException();
        public SharedImportResult CreateFromShared(SharedProjectState state, string machineName) => throw new NotImplementedException();
        public SharedSyncSummary ImportAll(string sharedFolder, string machineName) => throw new NotImplementedException();
        public SharedSyncSummary ExportAll(string sharedFolder, string machineName) => throw new NotImplementedException();
    }

    [Fact]
    public void Resolver_migrates_absent_field_and_honors_explicit_values()
    {
        Assert.Equal(SharedStateMode.Enabled, SharedStateModeRules.Resolve(null, "/mnt/shared"));
        Assert.Equal(SharedStateMode.Disabled, SharedStateModeRules.Resolve(null, null));
        Assert.Equal(SharedStateMode.Disabled, SharedStateModeRules.Resolve(null, "  "));
        Assert.Equal(SharedStateMode.Manual, SharedStateModeRules.Resolve("manual", null));
        Assert.Equal(SharedStateMode.Enabled, SharedStateModeRules.Resolve("Enabled", null));
        Assert.Equal(SharedStateMode.Disabled, SharedStateModeRules.Resolve("disabled", "/mnt/shared"));
        // An unrecognized value counts as absent → migration default.
        Assert.Equal(SharedStateMode.Enabled, SharedStateModeRules.Resolve("banana", "/mnt/shared"));
        Assert.Equal(SharedStateMode.Disabled, SharedStateModeRules.Resolve("banana", null));
    }

    [Fact]
    public void Gates_match_the_mode_matrix()
    {
        Assert.False(SharedStateModeRules.AutomaticChecks(SharedStateMode.Disabled));
        Assert.False(SharedStateModeRules.AutomaticChecks(SharedStateMode.Manual));
        Assert.True(SharedStateModeRules.AutomaticChecks(SharedStateMode.Enabled));

        Assert.False(SharedStateModeRules.Badges(SharedStateMode.Disabled));
        Assert.False(SharedStateModeRules.Badges(SharedStateMode.Manual));
        Assert.True(SharedStateModeRules.Badges(SharedStateMode.Enabled));

        Assert.False(SharedStateModeRules.ActionsVisible(SharedStateMode.Disabled));
        Assert.True(SharedStateModeRules.ActionsVisible(SharedStateMode.Manual));
        Assert.True(SharedStateModeRules.ActionsVisible(SharedStateMode.Enabled));
    }

    [Fact]
    public void Badge_loading_never_touches_the_service_outside_enabled_mode()
    {
        var counting = new CountingSharedStateService();

        Assert.Empty(SharedStateModeRules.LoadAllForBadges(counting, SharedStateMode.Disabled, _sharedFolder));
        Assert.Empty(SharedStateModeRules.LoadAllForBadges(counting, SharedStateMode.Manual, _sharedFolder));
        Assert.Equal(0, counting.LoadAllCalls);

        SharedStateModeRules.LoadAllForBadges(counting, SharedStateMode.Enabled, _sharedFolder);
        Assert.Equal(1, counting.LoadAllCalls);

        // Even in Enabled mode a null folder means no folder access.
        Assert.Empty(SharedStateModeRules.LoadAllForBadges(counting, SharedStateMode.Enabled, null));
        Assert.Equal(1, counting.LoadAllCalls);
    }

    [Fact]
    public void Portable_backup_export_works_in_disabled_mode()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var service = new SharedStateService(
            projects,
            new MilestoneRepository(_fixture.Database),
            new FrontierRepository(_fixture.Database),
            new BlockerRepository(_fixture.Database));
        projects.Add(new Project { Name = "VPS Project", Status = ProjectStatus.Active });

        // The backup path used by the App Settings button: ExportAll is an
        // explicit user action, so the mode never gates it.
        var settingsPath = Path.Combine(
            Path.GetTempPath(), "workbench-tests", "settings-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = new AppSettingsService(settingsPath);
        Assert.Equal(SharedStateMode.Disabled, settings.Mode);

        var summary = service.ExportAll(_sharedFolder, "vps");

        Assert.Single(summary.Applied);
        Assert.Empty(summary.Warnings);
        Assert.True(File.Exists(
            Path.Combine(_sharedFolder, "projects", "vps-project.workbench.json")));
    }

    [Fact]
    public void Mode_switches_and_backup_export_leave_shared_files_untouched()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var milestones = new MilestoneRepository(_fixture.Database);
        var frontiers = new FrontierRepository(_fixture.Database);
        var blockers = new BlockerRepository(_fixture.Database);
        var service = new SharedStateService(projects, milestones, frontiers, blockers);

        // A "foreign" shared file: a project this machine no longer has locally.
        var foreign = new Project { Name = "Other Machine Project", Status = ProjectStatus.Active };
        projects.Add(foreign);
        var exported = service.Export(_sharedFolder, foreign, "desktop");
        Assert.True(exported.Success);
        projects.Delete(foreign.Id);

        var settingsPath = Path.Combine(
            Path.GetTempPath(), "workbench-tests", "settings-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = new AppSettingsService(settingsPath);
        settings.Settings.SharedStateFolder = _sharedFolder;
        settings.Save();

        string[] before = Directory
            .EnumerateFiles(_sharedFolder, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => p + "|" + File.ReadAllText(p).Length)
            .ToArray();

        // Cycle through every mode; switching modes must not touch folder config or files.
        foreach (var mode in new[] { "disabled", "manual", "enabled" })
        {
            settings.Settings.SharedStateMode = mode;
            settings.Save();
            Assert.Equal(_sharedFolder, new AppSettingsService(settingsPath).SharedStateFolder);
        }

        // A portable backup with no local projects left exports nothing and deletes nothing.
        var summary = service.ExportAll(_sharedFolder, "vps");
        Assert.Empty(summary.Applied);
        Assert.Empty(summary.Warnings);

        string[] after = Directory
            .EnumerateFiles(_sharedFolder, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => p + "|" + File.ReadAllText(p).Length)
            .ToArray();
        Assert.Equal(before, after);
        Assert.True(File.Exists(exported.FilePath));
    }
}
