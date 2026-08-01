using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Project workspace: header (name, status, git, shared state, primary
/// actions) plus a left section nav over the section view models. The
/// sections share the project instance and services; header git/shared data
/// is enriched on a background thread like the dashboard and top bar.
/// </summary>
public sealed partial class ProjectDetailViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _goBack;

    public Project Project { get; }

    public OverviewViewModel Overview { get; }
    public TimelineViewModel Timeline { get; }
    public MilestonesViewModel Milestones { get; }
    public NotesViewModel Notes { get; }
    public HistoryViewModel History { get; }
    public DockerOperationsViewModel Docker { get; }
    public HandoffViewModel Handoff { get; }
    public ProjectSettingsViewModel Settings { get; }

    [ObservableProperty]
    private string _headerName;

    [ObservableProperty]
    private string _headerStatus;

    [ObservableProperty]
    private string _selectedSection = "Overview";

    [ObservableProperty]
    private string _headerGitText = string.Empty;

    [ObservableProperty]
    private bool _hasHeaderGit;

    [ObservableProperty]
    private string? _headerSharedBadge;

    public bool HasHeaderSharedBadge => HeaderSharedBadge is not null;
    public bool IsHeaderSharedBadgeWarning => SharedStatusBadge.IsWarning(HeaderSharedBadge);

    /// <summary>Reduced-motion switch for the section fade (re-read whenever a workspace is created).</summary>
    public bool IsReducedMotion => _services.AppSettings.Settings.ReducedMotion;

    /// <summary>Header Start button: a launch command exists on this project.</summary>
    public bool HasRunCommand => !string.IsNullOrWhiteSpace(Project.RunCommand);

    // Section selection booleans drive the nav buttons and section visibility.
    public bool IsOverviewSelected => SelectedSection == "Overview";
    public bool IsRunSelected => SelectedSection == "Run";
    public bool IsDockerSelected => SelectedSection == "Docker";
    public bool IsProgressSelected => SelectedSection == "Progress";
    public bool IsGitHubSelected => SelectedSection == "GitHub";
    public bool IsSharedStateSelected => SelectedSection == "Shared State";
    public bool IsHistorySelected => SelectedSection == "History";
    public bool IsTimelineSelected => SelectedSection == "Timeline";
    public bool IsNotesSelected => SelectedSection == "Notes";
    public bool IsHandoffSelected => SelectedSection == "Handoff";
    public bool IsSettingsSelected => SelectedSection == "Settings";

    // Status → badge variant (neutral when none applies, e.g. Complete).
    public bool StatusIsSuccess => Project.Status == ProjectStatus.Active;
    public bool StatusIsWarning => Project.Status == ProjectStatus.Waiting;
    public bool StatusIsDanger => Project.Status == ProjectStatus.Broken;
    public bool StatusIsInfo => Project.Status == ProjectStatus.Research;

    private ProjectDetailViewModel(AppServices services, Project project, Action goBack, Action? afterGitSync)
    {
        _services = services;
        Project = project;
        _goBack = goBack;
        _headerName = project.Name;
        _headerStatus = project.Status.ToString();

        Overview = new OverviewViewModel(services, project, afterGitSync);
        Timeline = new TimelineViewModel(services, project);
        Milestones = new MilestonesViewModel(services, project);
        Notes = new NotesViewModel(services, project);
        History = new HistoryViewModel(services, project);
        Docker = new DockerOperationsViewModel(services.Docker, project, services.Timeline);
        Handoff = new HandoffViewModel(services, project);
        Settings = new ProjectSettingsViewModel(services, project, goBack, RefreshHeader);

        EnrichHeader();
    }

    public static ProjectDetailViewModel? TryCreate(
        AppServices services, long projectId, Action goBack, Action? afterGitSync = null)
    {
        var project = services.Projects.GetById(projectId);
        return project is null ? null : new ProjectDetailViewModel(services, project, goBack, afterGitSync);
    }

    /// <summary>Reads git + shared state off the UI thread for the header strip.</summary>
    private void EnrichHeader()
    {
        var project = Project;
        var sharedFolder = _services.AppSettings.SharedStateFolder;
        var machineName = _services.AppSettings.MachineName;
        _ = Task.Run(() =>
        {
            var snapshot = _services.Git.GetSnapshot(project.RepositoryPath, recentCommitCount: 0);
            var badge = sharedFolder is null
                ? null
                : SharedStatusBadge.For(
                    _services.SharedState, project, _services.SharedState.LoadAll(sharedFolder), machineName);
            Dispatcher.UIThread.Post(() =>
            {
                HasHeaderGit = snapshot.IsValid;
                HeaderGitText = snapshot.IsValid ? FormatGit(snapshot) : string.Empty;
                HeaderSharedBadge = badge;
            });
        });
    }

    private static string FormatGit(RepositorySnapshot snapshot)
    {
        var text = (snapshot.BranchName ?? "?") + (snapshot.IsDirty ? "*" : "");
        if (snapshot.AheadBy is int ahead and > 0)
            text += $" ↑{ahead}";
        if (snapshot.BehindBy is int behind and > 0)
            text += $" ↓{behind}";
        return text;
    }

    private void RefreshHeader()
    {
        HeaderName = Project.Name;
        HeaderStatus = Project.Status.ToString();
        OnPropertyChanged(nameof(StatusIsSuccess));
        OnPropertyChanged(nameof(StatusIsWarning));
        OnPropertyChanged(nameof(StatusIsDanger));
        OnPropertyChanged(nameof(StatusIsInfo));
        OnPropertyChanged(nameof(HasRunCommand));
    }

    [RelayCommand]
    private void GoBack() => _goBack();

    [RelayCommand]
    private void SelectSection(string section) => SelectedSection = section;

    /// <summary>Header Start: opens the Run section — execution stays explicit, nothing starts.</summary>
    [RelayCommand]
    private void Start() => SelectedSection = "Run";

    [RelayCommand]
    private void OpenFolder()
    {
        if (Project.RepositoryPath is not { Length: > 0 } path || !Directory.Exists(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception)
        {
            // The folder exists but the OS refused to open it — nothing sensible to surface here.
        }
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverviewSelected));
        OnPropertyChanged(nameof(IsRunSelected));
        OnPropertyChanged(nameof(IsDockerSelected));
        OnPropertyChanged(nameof(IsProgressSelected));
        OnPropertyChanged(nameof(IsGitHubSelected));
        OnPropertyChanged(nameof(IsSharedStateSelected));
        OnPropertyChanged(nameof(IsHistorySelected));
        OnPropertyChanged(nameof(IsTimelineSelected));
        OnPropertyChanged(nameof(IsNotesSelected));
        OnPropertyChanged(nameof(IsHandoffSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    partial void OnHeaderSharedBadgeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasHeaderSharedBadge));
        OnPropertyChanged(nameof(IsHeaderSharedBadgeWarning));
    }
}
