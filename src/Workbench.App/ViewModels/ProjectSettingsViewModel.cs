using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// Per-project configuration: repository path, commands, documentation and AI
/// context paths, tags, and status. Can export/import <c>workbench.json</c>.
/// </summary>
public sealed partial class ProjectSettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;
    private readonly Action _goBack;
    private readonly Action _headerChanged;

    public ProjectStatus[] Statuses { get; } = Enum.GetValues<ProjectStatus>();

    [ObservableProperty] private string _name;
    [ObservableProperty] private ProjectStatus _status;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _repositoryPath;
    [ObservableProperty] private string _buildCommand;
    [ObservableProperty] private string _runCommand;
    [ObservableProperty] private string _testCommand;
    [ObservableProperty] private string _documentationPaths;
    [ObservableProperty] private string _aiContextPaths;
    [ObservableProperty] private string _tags;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _confirmDelete;

    // Shared Workbench State (cross-machine sync via the Shared State Folder).
    [ObservableProperty] private string _sharedStatusMessage = string.Empty;
    [ObservableProperty] private bool _confirmSharedOverwrite;
    [ObservableProperty] private bool _isImportPreviewOpen;
    [ObservableProperty] private string _importConflictMessage = string.Empty;
    [ObservableProperty] private string _importAvailabilityNote = string.Empty;

    /// <summary>The Shared Blockers section on this page (same component as the Progress page).</summary>
    public ProjectBlockersViewModel Blockers { get; }

    public ObservableCollection<SharedFieldChange> ImportChanges { get; } = [];

    /// <summary>Folder probe warnings (missing/unwritable folder, conflicted copies) for the Shared State section.</summary>
    public ObservableCollection<string> SharedFolderWarnings { get; } = [];

    public bool HasSharedFolderWarnings => SharedFolderWarnings.Count > 0;

    public string SharedFolderPath =>
        _services.AppSettings.SharedStateFolder ?? "not configured — set it in App Settings";

    private SharedProjectState? _pendingImport;

    public bool HasSharedFolder => _services.AppSettings.SharedStateFolder is not null;

    public string SharedFilePath =>
        _services.AppSettings.SharedStateFolder is { } folder
            ? _services.SharedState.GetSharedFilePath(folder, _project)
            : "Set the Shared State Folder in App Settings first.";

    public string SharedLastExportedText =>
        _project.SharedLastExportedAt is { } t ? t.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "never";

    public string SharedLastImportedText =>
        _project.SharedLastImportedAt is { } t ? t.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "never";

    public ProjectSettingsViewModel(AppServices services, Project project, Action goBack, Action headerChanged)
    {
        _services = services;
        _project = project;
        _goBack = goBack;
        _headerChanged = headerChanged;

        _name = project.Name;
        _status = project.Status;
        _description = project.Description ?? string.Empty;
        _repositoryPath = project.RepositoryPath ?? string.Empty;
        _buildCommand = project.BuildCommand ?? string.Empty;
        _runCommand = project.RunCommand ?? string.Empty;
        _testCommand = project.TestCommand ?? string.Empty;
        _documentationPaths = string.Join(Environment.NewLine, project.DocumentationPaths);
        _aiContextPaths = string.Join(Environment.NewLine, project.AiContextPaths);
        _tags = string.Join(", ", project.Tags);
        Blockers = new ProjectBlockersViewModel(services, project);
        RefreshSharedFolderWarnings();
    }

    /// <summary>Re-probes the shared folder (existence, writability, conflicted copies) and refreshes the warnings banner.</summary>
    [RelayCommand]
    private void ReloadSharedState() => RefreshSharedFolderWarnings();

    private void RefreshSharedFolderWarnings()
    {
        SharedFolderWarnings.Clear();
        if (_services.AppSettings.SharedStateFolder is not { } folder)
            SharedFolderWarnings.Add("Set the Shared State Folder in App Settings first.");
        else
            foreach (var warning in _services.SharedState.CheckSharedFolder(folder).Warnings)
                SharedFolderWarnings.Add(warning);
        OnPropertyChanged(nameof(HasSharedFolderWarnings));
    }

    [RelayCommand]
    private void Save()
    {
        var previousStatus = _project.Status;

        _project.Name = string.IsNullOrWhiteSpace(Name) ? _project.Name : Name.Trim();
        _project.Status = Status;
        _project.Description = NullIfEmpty(Description);
        _project.RepositoryPath = NullIfEmpty(RepositoryPath);
        _project.BuildCommand = NullIfEmpty(BuildCommand);
        _project.RunCommand = NullIfEmpty(RunCommand);
        _project.TestCommand = NullIfEmpty(TestCommand);
        _project.DocumentationPaths = SplitLines(DocumentationPaths);
        _project.AiContextPaths = SplitLines(AiContextPaths);
        _project.Tags = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        _services.Projects.Update(_project);

        if (previousStatus != _project.Status)
        {
            _services.Timeline.Add(new TimelineEntry
            {
                ProjectId = _project.Id,
                Category = TimelineCategory.Status,
                Message = $"Status changed: {previousStatus} → {_project.Status}",
            });
        }

        _headerChanged();
        StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private void ExportConfig()
    {
        try
        {
            _services.Config.Save(_project);
            StatusMessage = $"Wrote {_services.Config.GetConfigPath(_project.RepositoryPath!)}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ImportConfig()
    {
        var path = RepositoryPath.Trim();
        if (path.Length == 0 || _services.Config.Load(path) is not { } config)
        {
            StatusMessage = "No workbench.json found at the repository path.";
            return;
        }

        _services.Config.ApplyTo(config, _project);
        _services.Projects.Update(_project);

        Name = _project.Name;
        Description = _project.Description ?? string.Empty;
        BuildCommand = _project.BuildCommand ?? string.Empty;
        RunCommand = _project.RunCommand ?? string.Empty;
        TestCommand = _project.TestCommand ?? string.Empty;
        DocumentationPaths = string.Join(Environment.NewLine, _project.DocumentationPaths);
        AiContextPaths = string.Join(Environment.NewLine, _project.AiContextPaths);
        Tags = string.Join(", ", _project.Tags);

        _headerChanged();
        StatusMessage = "Configuration imported from workbench.json.";
    }

    /// <summary>
    /// Writes this project's shared file. If the shared file changed since this
    /// machine last synced it, the first attempt warns and nothing is written;
    /// ticking the overwrite confirmation makes the next attempt forceful.
    /// </summary>
    [RelayCommand]
    private void ExportToShared()
    {
        if (_services.AppSettings.SharedStateFolder is not { } folder)
        {
            SharedStatusMessage = "Set the Shared State Folder in App Settings first.";
            return;
        }

        try
        {
            var result = _services.SharedState.Export(
                folder, _project, _services.AppSettings.MachineName, force: ConfirmSharedOverwrite);

            if (!result.Success)
            {
                SharedStatusMessage = $"⚠ {result.Warning} Tick \"Overwrite shared file\" to export anyway.";
                return;
            }

            ConfirmSharedOverwrite = false;
            SharedStatusMessage = $"Exported to {result.FilePath}";
            NotifySharedInfoChanged();
            Blockers.Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SharedStatusMessage = ex.Message;
        }
    }

    /// <summary>Loads the shared file and shows what applying it would change. Nothing is applied yet.</summary>
    [RelayCommand]
    private void PreviewSharedImport()
    {
        if (_services.AppSettings.SharedStateFolder is not { } folder)
        {
            SharedStatusMessage = "Set the Shared State Folder in App Settings first.";
            return;
        }

        var state = _services.SharedState.LoadFile(_services.SharedState.GetSharedFilePath(folder, _project));
        if (state is null)
        {
            SharedStatusMessage = "No shared file found for this project.";
            return;
        }

        var preview = _services.SharedState.PreviewImport(state, _project, _services.AppSettings.MachineName);

        ImportChanges.Clear();
        foreach (var change in preview.Changes)
            ImportChanges.Add(change);

        var conflictLines = new List<string>();
        if (preview.HasConflict)
            conflictLines.Add(
                "⚠ Conflict: this project changed locally AND the shared file changed since the last sync. " +
                "Applying keeps the shared version and discards local edits; Cancel keeps local.");
        if (preview.BlockerConflicts.Count > 0)
            conflictLines.Add(
                "⚠ Blocker conflicts (changed on both sides — kept local unless \"Overwrite shared file\" is ticked): " +
                string.Join("; ", preview.BlockerConflicts));
        ImportConflictMessage = string.Join(Environment.NewLine, conflictLines);

        ImportAvailabilityNote = preview.RepositoryAvailable
            ? string.Empty
            : "Repository is not available on this machine — the project imports fine without it.";

        SharedStatusMessage = preview.Changes.Count == 0 ? "Shared file matches this project." : string.Empty;
        _pendingImport = state;
        IsImportPreviewOpen = true;
    }

    [RelayCommand]
    private void ApplySharedImport()
    {
        if (_pendingImport is not { } state)
            return;

        var result = _services.SharedState.ApplyImport(
            state, _project, _services.AppSettings.MachineName,
            overwriteBlockerConflicts: ConfirmSharedOverwrite);
        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Category = TimelineCategory.Status,
            Message = "Applied shared state from the Shared State Folder",
        });

        CancelSharedImport();
        RefreshFieldsFromProject();
        _headerChanged();
        SharedStatusMessage = result.SkippedBlockerConflicts.Count > 0
            ? $"Shared state applied; kept local for conflicting blockers: {string.Join("; ", result.SkippedBlockerConflicts)}"
            : "Shared state applied.";
        NotifySharedInfoChanged();
        Blockers.Refresh();
    }

    [RelayCommand]
    private void CancelSharedImport()
    {
        _pendingImport = null;
        IsImportPreviewOpen = false;
        ImportChanges.Clear();
        ImportConflictMessage = string.Empty;
        ImportAvailabilityNote = string.Empty;
    }

    private void RefreshFieldsFromProject()
    {
        Name = _project.Name;
        Status = _project.Status;
        Description = _project.Description ?? string.Empty;
        RepositoryPath = _project.RepositoryPath ?? string.Empty;
        BuildCommand = _project.BuildCommand ?? string.Empty;
        RunCommand = _project.RunCommand ?? string.Empty;
        TestCommand = _project.TestCommand ?? string.Empty;
        DocumentationPaths = string.Join(Environment.NewLine, _project.DocumentationPaths);
        AiContextPaths = string.Join(Environment.NewLine, _project.AiContextPaths);
        Tags = string.Join(", ", _project.Tags);
    }

    private void NotifySharedInfoChanged()
    {
        OnPropertyChanged(nameof(SharedFilePath));
        OnPropertyChanged(nameof(SharedLastExportedText));
        OnPropertyChanged(nameof(SharedLastImportedText));
    }

    [RelayCommand]
    private void DeleteProject()
    {
        if (!ConfirmDelete)
        {
            StatusMessage = "Tick the confirmation box to delete this project and all its history.";
            return;
        }

        _services.Projects.Delete(_project.Id);
        _goBack();
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
