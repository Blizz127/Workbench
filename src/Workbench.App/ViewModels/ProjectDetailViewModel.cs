using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Detail page for one project. Each tab is its own view model; they share
/// the project instance and services.
/// </summary>
public sealed partial class ProjectDetailViewModel : ViewModelBase
{
    private readonly Action _goBack;

    public Project Project { get; }

    public OverviewViewModel Overview { get; }
    public TimelineViewModel Timeline { get; }
    public MilestonesViewModel Milestones { get; }
    public NotesViewModel Notes { get; }
    public HistoryViewModel History { get; }
    public HandoffViewModel Handoff { get; }
    public ProjectSettingsViewModel Settings { get; }

    [ObservableProperty]
    private string _headerName;

    [ObservableProperty]
    private string _headerStatus;

    private ProjectDetailViewModel(AppServices services, Project project, Action goBack)
    {
        Project = project;
        _goBack = goBack;
        _headerName = project.Name;
        _headerStatus = project.Status.ToString();

        Overview = new OverviewViewModel(services, project);
        Timeline = new TimelineViewModel(services, project);
        Milestones = new MilestonesViewModel(services, project);
        Notes = new NotesViewModel(services, project);
        History = new HistoryViewModel(services, project);
        Handoff = new HandoffViewModel(services, project);
        Settings = new ProjectSettingsViewModel(services, project, goBack, RefreshHeader);
    }

    public static ProjectDetailViewModel? TryCreate(AppServices services, long projectId, Action goBack)
    {
        var project = services.Projects.GetById(projectId);
        return project is null ? null : new ProjectDetailViewModel(services, project, goBack);
    }

    private void RefreshHeader()
    {
        HeaderName = Project.Name;
        HeaderStatus = Project.Status.ToString();
    }

    [RelayCommand]
    private void GoBack() => _goBack();
}
