using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>One dashboard card. Git fields fill in asynchronously after load.</summary>
public sealed partial class ProjectCardViewModel : ViewModelBase
{
    private readonly Action<long> _openProject;

    public Project Project { get; }

    public string Name => Project.Name;
    public string Status => Project.Status.ToString();
    public string RepositoryPath => Project.RepositoryPath ?? "no repository";
    public string ActiveTask => Project.ActiveTask ?? "—";
    public string LastUpdated => Project.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public IReadOnlyList<string> Tags => Project.Tags;

    public string FrontierText { get; }
    public string LastBuildText { get; }
    public bool LastBuildOk { get; }
    public bool HasBuild { get; }
    public string LastTestText { get; }
    public bool LastTestOk { get; }
    public bool HasTest { get; }

    [ObservableProperty]
    private string _branch = "…";

    [ObservableProperty]
    private string _lastCommit = "…";

    public ProjectCardViewModel(AppServices services, Project project, Action<long> openProject)
    {
        Project = project;
        _openProject = openProject;

        FrontierText = services.Frontiers.GetCurrent(project.Id)?.Title ?? "—";

        var build = services.Builds.GetLatest(project.Id);
        HasBuild = build is not null;
        LastBuildOk = build?.Succeeded == true;
        LastBuildText = build is null
            ? "no builds"
            : build.Succeeded
                ? $"OK · {build.StartedAt.LocalDateTime:MM-dd HH:mm}"
                : $"FAIL · {build.StartedAt.LocalDateTime:MM-dd HH:mm}";

        var test = services.Tests.GetLatest(project.Id);
        HasTest = test is not null;
        LastTestOk = test?.Result == TestResult.Passed;
        LastTestText = test is null ? "no test runs" : $"{test.Result} · {test.StartedAt.LocalDateTime:MM-dd HH:mm}";
    }

    public void ApplyGitSnapshot(RepositorySnapshot snapshot)
    {
        if (!snapshot.IsValid)
        {
            Branch = "—";
            LastCommit = "—";
            return;
        }

        Branch = snapshot.BranchName ?? "?";
        LastCommit = snapshot.LastCommit is { } c
            ? $"{c.ShortSha} {Truncate(c.MessageSummary, 46)}"
            : "no commits";
    }

    [RelayCommand]
    private void Open() => _openProject(Project.Id);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
