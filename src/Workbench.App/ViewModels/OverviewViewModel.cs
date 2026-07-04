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
    [ObservableProperty] private string _aheadBehindText = "—";
    [ObservableProperty] private string? _diffStatText;
    public ObservableCollection<CommitInfo> RecentCommits { get; } = [];
    public ObservableCollection<FileChange> ModifiedFiles { get; } = [];
    public ObservableCollection<DiffCheckWarning> DiffCheckWarnings { get; } = [];
    public ObservableCollection<TodoItem> Todos { get; } = [];
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _lastRefreshedText = "Never refreshed";

    // Progress
    [ObservableProperty] private string _nextFrontier = "—";
    [ObservableProperty] private string _currentMilestone = "—";
    public FrontierEditorViewModel Frontier { get; }
    public GitSyncViewModel GitSync { get; }

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
        _lastRefreshedText = project.LastRefreshedAt is { } at
            ? $"Last refreshed {at.LocalDateTime:yyyy-MM-dd HH:mm}"
            : "Never refreshed";
        Frontier = new FrontierEditorViewModel(services, project, LoadProgress);
        GitSync = new GitSyncViewModel(services, project, RefreshProject);

        LoadProgress();
        LoadNotes();
        RefreshProject();
    }

    private void LoadProgress()
    {
        Frontier.LoadCurrent();
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

    /// <summary>
    /// Reads real local project state — branch, HEAD, ahead/behind, dirty
    /// status, diff --stat, diff --check — and updates the display. If the
    /// state differs meaningfully from what was last recorded, persists the
    /// new baseline on the project and journals what changed. Read-only: this
    /// never modifies the repository itself, only Workbench's own records.
    /// </summary>
    [RelayCommand]
    private void RefreshProject()
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
                    AheadBehindText = "—";
                    DiffStatText = null;
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
                    AheadBehindText = snapshot.AheadBy is { } ahead && snapshot.BehindBy is { } behind
                        ? $"{ahead} ahead, {behind} behind upstream"
                        : "no upstream tracking branch";
                    DiffStatText = snapshot.DiffStat;
                }

                RecentCommits.Clear();
                foreach (var commit in snapshot.RecentCommits)
                    RecentCommits.Add(commit);

                ModifiedFiles.Clear();
                foreach (var change in snapshot.ModifiedFiles)
                    ModifiedFiles.Add(change);

                DiffCheckWarnings.Clear();
                foreach (var warning in snapshot.DiffCheckWarnings ?? [])
                    DiffCheckWarnings.Add(warning);

                Todos.Clear();
                foreach (var todo in todos)
                    Todos.Add(todo);

                if (snapshot.IsValid)
                    RecordMeaningfulChanges(snapshot);

                GitSync.UpdateFromSnapshot(snapshot);

                IsScanning = false;
            });
        });
    }

    /// <summary>
    /// Compares the freshly read snapshot against the project's last known
    /// state. On the very first refresh there is nothing to compare against,
    /// so a single baseline entry is journaled instead of a diff.
    /// </summary>
    private void RecordMeaningfulChanges(RepositorySnapshot snapshot)
    {
        var now = DateTimeOffset.Now;
        var isFirstRefresh = _project.LastRefreshedAt is null;
        var changeMessages = new List<string>();

        if (isFirstRefresh)
        {
            changeMessages.Add(
                $"Initial repository state captured: branch '{snapshot.BranchName}', HEAD {ShortSha(snapshot)}" +
                (snapshot.IsDirty ? ", working tree dirty" : ", working tree clean"));
        }
        else
        {
            if (_project.LastKnownBranch != snapshot.BranchName)
                changeMessages.Add($"Branch changed: {_project.LastKnownBranch ?? "(none)"} → {snapshot.BranchName}");

            if (_project.LastKnownCommitSha != snapshot.LastCommit?.Sha)
                changeMessages.Add($"HEAD advanced to {ShortSha(snapshot)}");

            if (_project.LastKnownIsDirty != snapshot.IsDirty)
            {
                changeMessages.Add(snapshot.IsDirty
                    ? $"Working tree became dirty ({snapshot.ModifiedFiles.Count} file(s))"
                    : "Working tree is now clean");
            }

            if (_project.LastKnownAheadBy != snapshot.AheadBy || _project.LastKnownBehindBy != snapshot.BehindBy)
            {
                if (snapshot.AheadBy is { } ahead && snapshot.BehindBy is { } behind)
                    changeMessages.Add($"Now {ahead} ahead / {behind} behind upstream");
            }
        }

        _project.LastKnownBranch = snapshot.BranchName;
        _project.LastKnownCommitSha = snapshot.LastCommit?.Sha;
        _project.LastKnownIsDirty = snapshot.IsDirty;
        _project.LastKnownAheadBy = snapshot.AheadBy;
        _project.LastKnownBehindBy = snapshot.BehindBy;
        _project.LastRefreshedAt = now;
        _services.Projects.Update(_project);

        LastRefreshedText = $"Last refreshed {now.LocalDateTime:yyyy-MM-dd HH:mm}";

        foreach (var message in changeMessages)
        {
            _services.Timeline.Add(new TimelineEntry
            {
                ProjectId = _project.Id,
                Category = TimelineCategory.Status,
                Message = message,
            });
        }
    }

    private static string ShortSha(RepositorySnapshot snapshot) =>
        snapshot.LastCommit is { } c ? c.ShortSha : "n/a";

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

/// <summary>
/// Full editor for the project's current frontier: title, exact
/// function/symbol, evidence, blocker class, next safest step, assigned
/// model, and status — the detail a handoff or model prompt actually needs,
/// beyond the one-line title the dashboard shows.
/// </summary>
public sealed partial class FrontierEditorViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;
    private readonly Action _onChanged;
    private Frontier? _current;

    public FrontierStatus[] Statuses { get; } = Enum.GetValues<FrontierStatus>();

    [ObservableProperty] private bool _hasCurrent;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _symbol = string.Empty;
    [ObservableProperty] private string _evidence = string.Empty;
    [ObservableProperty] private string _blockerClass = string.Empty;
    [ObservableProperty] private string _nextSafestStep = string.Empty;
    [ObservableProperty] private string _assignedModel = string.Empty;
    [ObservableProperty] private FrontierStatus _status = FrontierStatus.Current;
    [ObservableProperty] private string _newFrontierTitle = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public FrontierEditorViewModel(AppServices services, Project project, Action onChanged)
    {
        _services = services;
        _project = project;
        _onChanged = onChanged;
    }

    public void LoadCurrent()
    {
        _current = _services.Frontiers.GetCurrent(_project.Id);
        HasCurrent = _current is not null;

        Title = _current?.Title ?? string.Empty;
        Symbol = _current?.Symbol ?? string.Empty;
        Evidence = _current?.Evidence ?? string.Empty;
        BlockerClass = _current?.BlockerClass ?? string.Empty;
        NextSafestStep = _current?.NextSafestStep ?? string.Empty;
        AssignedModel = _current?.AssignedModel ?? string.Empty;
        Status = _current?.Status ?? FrontierStatus.Current;
    }

    [RelayCommand]
    private void SaveDetails()
    {
        if (_current is null)
            return;

        _current.Title = string.IsNullOrWhiteSpace(Title) ? _current.Title : Title.Trim();
        _current.Symbol = NullIfEmpty(Symbol);
        _current.Evidence = NullIfEmpty(Evidence);
        _current.BlockerClass = NullIfEmpty(BlockerClass);
        _current.NextSafestStep = NullIfEmpty(NextSafestStep);
        _current.AssignedModel = NullIfEmpty(AssignedModel);

        if (Status != _current.Status)
        {
            _current.Status = Status;
            if (Status == FrontierStatus.Completed)
                _current.CompletedAt = DateTimeOffset.Now;
        }

        _services.Frontiers.Update(_current);
        StatusMessage = $"Saved at {DateTime.Now:HH:mm:ss}";
        _onChanged();
    }

    /// <summary>Completes the current frontier (if any) and starts a new one with just a title.</summary>
    [RelayCommand]
    private void Advance()
    {
        var title = NewFrontierTitle.Trim();
        if (title.Length == 0)
            return;

        if (_current is { } current)
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
        LoadCurrent();
        _onChanged();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Explicit GitHub fetch/pull/push actions for one project. Every write
/// (pull, push) goes through a preview panel before anything runs; fetch is
/// read-only so it runs immediately on click. Button enablement is driven
/// by whatever <see cref="RepositorySnapshot"/> the Overview tab's Refresh
/// Project action last read — <see cref="UpdateFromSnapshot"/> is that tab's
/// single source of truth for git state, so this view model never reads git
/// state on its own.
/// </summary>
public sealed partial class GitSyncViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;
    private readonly Action _onSyncCompleted;
    private RepositorySnapshot? _lastSnapshot;
    private CancellationTokenSource? _runCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmPullCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmPushCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _runStatus = string.Empty;
    [ObservableProperty] private string _syncStateText = "Refresh the project to check sync state.";
    [ObservableProperty] private bool _canPull;
    [ObservableProperty] private bool _canPush;

    [ObservableProperty] private bool _isPullPreviewOpen;
    [ObservableProperty] private string _pullPreviewText = string.Empty;
    [ObservableProperty] private bool _isPushPreviewOpen;
    [ObservableProperty] private string _pushPreviewText = string.Empty;

    [ObservableProperty] private string _lastResultMessage = string.Empty;
    [ObservableProperty] private bool _lastResultWasError;

    public GitSyncViewModel(AppServices services, Project project, Action onSyncCompleted)
    {
        _services = services;
        _project = project;
        _onSyncCompleted = onSyncCompleted;
    }

    /// <summary>Called by the Overview tab whenever it re-reads git state, to keep button enablement current.</summary>
    public void UpdateFromSnapshot(RepositorySnapshot snapshot)
    {
        _lastSnapshot = snapshot;

        if (!snapshot.IsValid)
        {
            CanPull = false;
            CanPush = false;
            SyncStateText = "Repository unavailable.";
            return;
        }

        var hasUpstream = snapshot.AheadBy is not null && snapshot.BehindBy is not null;
        CanPull = hasUpstream && !snapshot.IsDirty && snapshot.AheadBy == 0 && snapshot.BehindBy > 0;
        CanPush = hasUpstream && snapshot.AheadBy > 0;

        SyncStateText = !hasUpstream
            ? "No upstream tracking branch configured."
            : snapshot.AheadBy == 0 && snapshot.BehindBy == 0
                ? "Up to date with upstream."
                : $"{snapshot.AheadBy} ahead, {snapshot.BehindBy} behind upstream.";
    }

    private bool CanRunSync => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunSync))]
    private async Task Fetch() =>
        await Run(token => _services.GitSync.FetchAsync(_project, token), "Fetching from GitHub…");

    [RelayCommand]
    private void PreparePull()
    {
        var behind = _lastSnapshot?.BehindBy ?? 0;
        PullPreviewText =
            $"This will run `git pull --ff-only`, fast-forwarding {behind} commit(s) from upstream. " +
            "Fast-forward only: if a true fast-forward isn't possible when this runs, git refuses and nothing local changes.";
        IsPullPreviewOpen = true;
    }

    [RelayCommand]
    private void CancelPullPreview() => IsPullPreviewOpen = false;

    [RelayCommand(CanExecute = nameof(CanRunSync))]
    private async Task ConfirmPull()
    {
        IsPullPreviewOpen = false;
        await Run(token => _services.GitSync.PullFastForwardAsync(_project, token), "Pulling (fast-forward)…");
    }

    [RelayCommand]
    private void PreparePush()
    {
        var ahead = _lastSnapshot?.AheadBy ?? 0;
        var dirtyWarning = _lastSnapshot?.IsDirty == true
            ? "\n\nYour working tree has uncommitted changes — those will NOT be pushed, only committed changes."
            : string.Empty;
        PushPreviewText = $"This will run `git push`, sending {ahead} committed local commit(s) to the upstream remote." +
                          dirtyWarning;
        IsPushPreviewOpen = true;
    }

    [RelayCommand]
    private void CancelPushPreview() => IsPushPreviewOpen = false;

    [RelayCommand(CanExecute = nameof(CanRunSync))]
    private async Task ConfirmPush()
    {
        IsPushPreviewOpen = false;
        await Run(token => _services.GitSync.PushAsync(_project, token), "Pushing to GitHub…");
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelRun()
    {
        RunStatus = "Cancelling…";
        _runCts?.Cancel();
    }

    private async Task Run(Func<CancellationToken, Task<GitSyncResult>> action, string status)
    {
        using var cts = new CancellationTokenSource();
        _runCts = cts;
        IsBusy = true;
        RunStatus = status;
        try
        {
            var result = await Task.Run(() => action(cts.Token));
            await Dispatcher.UIThread.InvokeAsync(() => ReportResult(result));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _runCts = null;
                IsBusy = false;
                RunStatus = string.Empty;
            });
        }
    }

    private void ReportResult(GitSyncResult result)
    {
        LastResultWasError = result.Status == GitSyncStatus.Failed;
        LastResultMessage = result.Status switch
        {
            GitSyncStatus.Blocked => $"Blocked: {result.BlockReason}",
            GitSyncStatus.Failed => $"Failed: {result.ErrorMessage}",
            _ when result.Warning is { } warning => $"Done — {warning}",
            _ => "Done.",
        };

        // Re-read git state through the same path Refresh Project uses, so
        // the Repository card, the journal, and this panel's button
        // enablement all reflect the same post-operation truth.
        _onSyncCompleted();
    }
}
