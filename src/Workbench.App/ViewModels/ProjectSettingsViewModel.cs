using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

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
    [ObservableProperty] private string _matchingBuildCommand;
    [ObservableProperty] private string _documentationPaths;
    [ObservableProperty] private string _aiContextPaths;
    [ObservableProperty] private string _promptRules;
    [ObservableProperty] private string _tags;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _confirmDelete;

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
        _matchingBuildCommand = project.MatchingBuildCommand ?? string.Empty;
        _documentationPaths = string.Join(Environment.NewLine, project.DocumentationPaths);
        _aiContextPaths = string.Join(Environment.NewLine, project.AiContextPaths);
        _promptRules = project.PromptRules ?? string.Empty;
        _tags = string.Join(", ", project.Tags);
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
        _project.MatchingBuildCommand = NullIfEmpty(MatchingBuildCommand);
        _project.DocumentationPaths = SplitLines(DocumentationPaths);
        _project.AiContextPaths = SplitLines(AiContextPaths);
        _project.PromptRules = NullIfEmpty(PromptRules);
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
        MatchingBuildCommand = _project.MatchingBuildCommand ?? string.Empty;
        DocumentationPaths = string.Join(Environment.NewLine, _project.DocumentationPaths);
        AiContextPaths = string.Join(Environment.NewLine, _project.AiContextPaths);
        PromptRules = _project.PromptRules ?? string.Empty;
        Tags = string.Join(", ", _project.Tags);

        _headerChanged();
        StatusMessage = "Configuration imported from workbench.json.";
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
