using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Mission control: every registered project as a card, filterable by status.
/// Git information is loaded asynchronously after the cards appear so the
/// dashboard stays responsive with dozens of projects.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<long> _openProject;
    private List<ProjectCardViewModel> _allCards = [];

    public ObservableCollection<ProjectCardViewModel> Cards { get; } = [];

    public string[] StatusFilters { get; } = ["All", "Active", "Waiting", "Broken", "Research", "Complete"];

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    // "Add project" panel
    [ObservableProperty]
    private bool _isAddPanelOpen;

    [ObservableProperty]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    private string _newRepositoryPath = string.Empty;

    [ObservableProperty]
    private string? _addProjectError;

    public DashboardViewModel(AppServices services, Action<long> openProject)
    {
        _services = services;
        _openProject = openProject;
        Reload();
    }

    private void Reload()
    {
        var projects = _services.Projects.GetAll();
        _allCards = projects
            .Select(p => new ProjectCardViewModel(_services, p, _openProject))
            .ToList();

        ApplyFilter();
        UpdateSummary(projects);

        // Enrich with git state off the UI thread, one repo at a time.
        var cards = _allCards.ToList();
        _ = Task.Run(() =>
        {
            foreach (var card in cards)
            {
                var snapshot = _services.Git.GetSnapshot(card.Project.RepositoryPath, recentCommitCount: 1);
                Dispatcher.UIThread.Post(() => card.ApplyGitSnapshot(snapshot));
            }
        });
    }

    private void UpdateSummary(IReadOnlyList<Project> projects)
    {
        var active = projects.Count(p => p.Status == ProjectStatus.Active);
        var broken = projects.Count(p => p.Status == ProjectStatus.Broken);
        var waiting = projects.Count(p => p.Status == ProjectStatus.Waiting);
        SummaryText = $"{projects.Count} projects · {active} active · {waiting} waiting · {broken} broken";
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Cards.Clear();
        foreach (var card in _allCards)
        {
            if (SelectedFilter == "All" || card.Status == SelectedFilter)
                Cards.Add(card);
        }
    }

    [RelayCommand]
    private void ToggleAddPanel()
    {
        IsAddPanelOpen = !IsAddPanelOpen;
        AddProjectError = null;
    }

    [RelayCommand]
    private void AddProject()
    {
        var name = NewProjectName.Trim();
        var repoPath = NewRepositoryPath.Trim();

        if (name.Length == 0 && repoPath.Length == 0)
        {
            AddProjectError = "Provide a project name or a repository path.";
            return;
        }

        var project = new Project
        {
            Name = name.Length > 0 ? name : Path.GetFileName(repoPath.TrimEnd(Path.DirectorySeparatorChar)),
            RepositoryPath = repoPath.Length > 0 ? repoPath : null,
            Status = ProjectStatus.Active,
        };

        // If the repository already carries a workbench.json, adopt its configuration.
        if (project.RepositoryPath is { } path && Directory.Exists(path) &&
            _services.Config.Load(path) is { } config)
        {
            _services.Config.ApplyTo(config, project);
            if (name.Length > 0)
                project.Name = name;
        }

        _services.Projects.Add(project);
        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = project.Id,
            Category = TimelineCategory.Status,
            Message = "Project registered in Workbench",
        });

        NewProjectName = string.Empty;
        NewRepositoryPath = string.Empty;
        IsAddPanelOpen = false;
        AddProjectError = null;
        Reload();
    }
}
