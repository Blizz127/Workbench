using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// The "what is going on" tab: repository state, frontiers, blockers, open
/// TODOs, recent notes, current milestone and objective.
/// </summary>
public sealed partial class OverviewViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;

    // Repository
    [ObservableProperty] private bool _repoValid;
    [ObservableProperty] private string _repoSummary = "Loading repository…";
    [ObservableProperty] private string _branch = "—";
    [ObservableProperty] private string _lastCommitText = "—";
    public ObservableCollection<CommitInfo> RecentCommits { get; } = [];
    public ObservableCollection<FileChange> ModifiedFiles { get; } = [];
    public ObservableCollection<TodoItem> Todos { get; } = [];
    [ObservableProperty] private bool _isScanning;

    // Progress
    [ObservableProperty] private string _currentFrontier = "—";
    [ObservableProperty] private string _nextFrontier = "—";
    [ObservableProperty] private string _currentMilestone = "—";
    [ObservableProperty] private string _newFrontierTitle = string.Empty;

    // Blockers
    public ObservableCollection<BlockerItemViewModel> OpenBlockers { get; } = [];
    [ObservableProperty] private string _newBlockerText = string.Empty;

    // Objective / active task (editable in place)
    [ObservableProperty] private string _activeTask = string.Empty;
    [ObservableProperty] private string _currentObjective = string.Empty;

    public ObservableCollection<Note> RecentNotes { get; } = [];

    public OverviewViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        _activeTask = project.ActiveTask ?? string.Empty;
        _currentObjective = project.CurrentObjective ?? string.Empty;

        LoadProgress();
        LoadNotes();
        RefreshRepository();
    }

    private void LoadProgress()
    {
        CurrentFrontier = _services.Frontiers.GetCurrent(_project.Id)?.Title ?? "—";
        NextFrontier = _services.Frontiers.GetNext(_project.Id)?.Title ?? "—";
        CurrentMilestone = _services.Milestones.GetForProject(_project.Id)
            .FirstOrDefault(m => !m.IsCompleted)?.Title ?? "—";

        OpenBlockers.Clear();
        foreach (var blocker in _services.Blockers.GetOpen(_project.Id))
            OpenBlockers.Add(new BlockerItemViewModel(blocker, ResolveBlocker));
    }

    private void LoadNotes()
    {
        RecentNotes.Clear();
        foreach (var note in _services.Notes.GetForProject(_project.Id).Take(5))
            RecentNotes.Add(note);
    }

    [RelayCommand]
    private void RefreshRepository()
    {
        IsScanning = true;
        var repoPath = _project.RepositoryPath;

        _ = Task.Run(() =>
        {
            var snapshot = _services.Git.GetSnapshot(repoPath, recentCommitCount: 10);
            var todos = _services.Todos.Scan(repoPath, maxItems: 50);

            Dispatcher.UIThread.Post(() =>
            {
                RepoValid = snapshot.IsValid;
                if (!snapshot.IsValid)
                {
                    RepoSummary = snapshot.Error ?? "Repository unavailable.";
                    Branch = "—";
                    LastCommitText = "—";
                }
                else
                {
                    Branch = snapshot.BranchName ?? "?";
                    LastCommitText = snapshot.LastCommit is { } c
                        ? $"{c.ShortSha} — {c.MessageSummary} ({c.Author}, {c.When.LocalDateTime:yyyy-MM-dd HH:mm})"
                        : "no commits";
                    RepoSummary = snapshot.IsDirty
                        ? $"{snapshot.ModifiedFiles.Count} uncommitted change(s)"
                        : "Working tree clean";
                }

                RecentCommits.Clear();
                foreach (var commit in snapshot.RecentCommits)
                    RecentCommits.Add(commit);

                ModifiedFiles.Clear();
                foreach (var change in snapshot.ModifiedFiles)
                    ModifiedFiles.Add(change);

                Todos.Clear();
                foreach (var todo in todos)
                    Todos.Add(todo);

                IsScanning = false;
            });
        });
    }

    [RelayCommand]
    private void AdvanceFrontier()
    {
        var title = NewFrontierTitle.Trim();
        if (title.Length == 0)
            return;

        // Complete the current frontier (if any), then promote the new title to Current.
        if (_services.Frontiers.GetCurrent(_project.Id) is { } current)
        {
            current.Status = FrontierStatus.Completed;
            current.CompletedAt = DateTimeOffset.Now;
            _services.Frontiers.Update(current);
        }

        _services.Frontiers.Add(new Frontier
        {
            ProjectId = _project.Id,
            Title = title,
            Status = FrontierStatus.Current,
        });

        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Category = TimelineCategory.Frontier,
            Message = $"Advanced to {title}",
        });

        NewFrontierTitle = string.Empty;
        LoadProgress();
    }

    [RelayCommand]
    private void AddBlocker()
    {
        var text = NewBlockerText.Trim();
        if (text.Length == 0)
            return;

        _services.Blockers.Add(new Blocker { ProjectId = _project.Id, Description = text });
        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Category = TimelineCategory.Blocker,
            Message = $"Blocked: {text}",
        });

        NewBlockerText = string.Empty;
        LoadProgress();
    }

    private void ResolveBlocker(BlockerItemViewModel item)
    {
        var blocker = item.Blocker;
        blocker.IsResolved = true;
        blocker.ResolvedAt = DateTimeOffset.Now;
        blocker.Resolution = string.IsNullOrWhiteSpace(item.ResolutionText) ? null : item.ResolutionText.Trim();
        _services.Blockers.Update(blocker);

        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Category = TimelineCategory.Blocker,
            Message = $"Blocker resolved: {blocker.Description}",
            Detail = blocker.Resolution,
        });

        LoadProgress();
    }

    [RelayCommand]
    private void SaveFocus()
    {
        _project.ActiveTask = string.IsNullOrWhiteSpace(ActiveTask) ? null : ActiveTask.Trim();
        _project.CurrentObjective = string.IsNullOrWhiteSpace(CurrentObjective) ? null : CurrentObjective.Trim();
        _services.Projects.Update(_project);
    }
}

/// <summary>An open blocker row with an inline resolution box.</summary>
public sealed partial class BlockerItemViewModel(Blocker blocker, Action<BlockerItemViewModel> resolve) : ViewModelBase
{
    public Blocker Blocker { get; } = blocker;

    public string Description => Blocker.Description;
    public string Since => $"since {Blocker.CreatedAt.LocalDateTime:yyyy-MM-dd}";

    [ObservableProperty]
    private string _resolutionText = string.Empty;

    [RelayCommand]
    private void Resolve() => resolve(this);
}
