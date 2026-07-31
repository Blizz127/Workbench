using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Context strip in the shell's top bar: current project, machine name,
/// repository availability, git branch/dirty state, and shared-state sync
/// status. Repo/git/shared data is read off the UI thread on navigation;
/// <see cref="RefreshStatus"/> can be re-triggered after git sync actions.
/// </summary>
public sealed partial class TopBarViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private Project? _project;

    [ObservableProperty] private string _projectName = "Workbench";
    [ObservableProperty] private string _machineName;
    [ObservableProperty] private bool _hasProjectContext;
    [ObservableProperty] private bool _isRepoAvailable;
    [ObservableProperty] private string _repoStatusText = string.Empty;
    [ObservableProperty] private string _gitText = string.Empty;
    [ObservableProperty] private bool _hasGit;
    [ObservableProperty] private string? _syncBadge;
    [ObservableProperty] private bool _isSyncBadgeWarning;

    public bool HasSyncBadge => SyncBadge is not null;

    public TopBarViewModel(AppServices services)
    {
        _services = services;
        _machineName = services.AppSettings.MachineName;
    }

    public void ShowProjectContext(Project project)
    {
        _project = project;
        ProjectName = project.Name;
        HasProjectContext = true;
        RefreshStatus();
    }

    /// <summary>Non-project pages (dashboard, settings) show no repo/git/sync state.</summary>
    public void ClearContext()
    {
        _project = null;
        ProjectName = "Workbench";
        HasProjectContext = false;
        RepoStatusText = string.Empty;
        GitText = string.Empty;
        HasGit = false;
        SyncBadge = null;
    }

    /// <summary>Re-reads repo/git/shared state off the UI thread for the current project, if any.</summary>
    public void RefreshStatus()
    {
        if (_project is not { } project)
            return;

        var sharedFolder = _services.AppSettings.SharedStateFolder;
        var machineName = _services.AppSettings.MachineName;
        _ = Task.Run(() =>
        {
            var repoAvailable = project.RepositoryPath is { Length: > 0 } path && Directory.Exists(path);
            var snapshot = _services.Git.GetSnapshot(project.RepositoryPath, recentCommitCount: 0);
            var badge = SharedBadgeFor(project, sharedFolder, machineName);
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_project, project))
                    return; // context changed while the snapshot was being read

                IsRepoAvailable = repoAvailable;
                RepoStatusText = project.RepositoryPath is null
                    ? "no repository"
                    : repoAvailable ? "repo available" : "repo missing";
                HasGit = snapshot.IsValid;
                GitText = snapshot.IsValid ? FormatGit(snapshot) : string.Empty;
                SyncBadge = badge;
                IsSyncBadgeWarning = SharedStatusBadge.IsWarning(badge);
            });
        });
    }

    /// <summary>Same badge rules as the dashboard cards; null means in sync (or not shared).</summary>
    private string? SharedBadgeFor(Project project, string? sharedFolder, string machineName)
    {
        if (project.SharedId is null || sharedFolder is null)
            return null;

        return SharedStatusBadge.For(
            _services.SharedState, project, _services.SharedState.LoadAll(sharedFolder), machineName);
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

    partial void OnSyncBadgeChanged(string? value) => OnPropertyChanged(nameof(HasSyncBadge));
}
