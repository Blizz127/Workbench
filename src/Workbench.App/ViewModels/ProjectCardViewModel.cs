using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>One dashboard card. Git and milestone/blocker/history fields fill in asynchronously after load.</summary>
public sealed partial class ProjectCardViewModel : ViewModelBase
{
    private readonly Action<long> _openProject;

    public Project Project { get; }

    public string Name => Project.Name;
    public string Status => Project.Status.ToString();

    /// <summary>First letter of the name, shown in the card's accent tile.</summary>
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    /// <summary>
    /// A missing repository is a normal state for a shared project (its files
    /// live on another machine), so it renders as availability — never as an error.
    /// </summary>
    public string RepositoryText =>
        Project.RepositoryPath is { Length: > 0 } path
            ? Directory.Exists(path) ? path : "Not available on this machine"
            : Project.SharedId is not null ? "Not available on this machine" : "no repository";

    public bool IsUnavailable =>
        Project.RepositoryPath is { Length: > 0 } path
            ? !Directory.Exists(path)
            : Project.SharedId is not null;

    /// <summary>Prominent Start action: a launch command exists and the repo is on this machine.</summary>
    public bool CanStart => !string.IsNullOrWhiteSpace(Project.RunCommand) && !IsUnavailable;

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

    /// <summary>Sort key for "Failing": the latest build or test run went red.</summary>
    public bool IsFailing => (HasBuild && !LastBuildOk) || (HasTest && !LastTestOk);

    // Nullable outcomes for the results row: null (no run yet) renders muted, not red.
    public bool? BuildOutcome => HasBuild ? LastBuildOk : null;
    public bool? TestOutcome => HasTest ? LastTestOk : null;
    public bool? RunOutcome => HasRun ? LastRunOk : null;

    // Status → badge variant (neutral when none applies, e.g. Complete).
    public bool StatusIsSuccess => Project.Status == ProjectStatus.Active;
    public bool StatusIsWarning => Project.Status == ProjectStatus.Waiting;
    public bool StatusIsDanger => Project.Status == ProjectStatus.Broken;
    public bool StatusIsInfo => Project.Status == ProjectStatus.Research;

    /// <summary>Compact density toggle, mirrored from the dashboard VM.</summary>
    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private string _branch = "…";

    [ObservableProperty]
    private string _lastCommit = "…";

    /// <summary>One-line git state: branch + dirty marker + ahead/behind counts.</summary>
    [ObservableProperty]
    private string _gitText = "…";

    /// <summary>Last successful pull/push times (from the timeline journal); empty until enriched.</summary>
    [ObservableProperty]
    private string _gitSyncText = string.Empty;

    /// <summary>One-line compose state (e.g. "5/5 containers up", "stopped"); empty until probed.</summary>
    [ObservableProperty]
    private string _dockerText = string.Empty;

    /// <summary>Brush key for the docker row: null unknown/muted, true all up, false partial/stopped.</summary>
    [ObservableProperty]
    private bool? _dockerOutcome;

    [ObservableProperty]
    private bool _hasGit;

    /// <summary>Set asynchronously when the shared file differs from local state; null hides the chip.</summary>
    [ObservableProperty]
    private string? _sharedBadge;

    [ObservableProperty]
    private int _openBlockerCount;

    [ObservableProperty]
    private string _milestoneText = string.Empty;

    [ObservableProperty]
    private double _milestoneProgress;

    [ObservableProperty]
    private bool _hasMilestones;

    [ObservableProperty]
    private string _lastRunText = "no runs";

    [ObservableProperty]
    private bool _lastRunOk;

    [ObservableProperty]
    private bool _hasRun;

    public bool HasBlockers => OpenBlockerCount > 0;
    public string BlockerText => $"{OpenBlockerCount} {(OpenBlockerCount == 1 ? "blocker" : "blockers")}";

    /// <summary>Alert row: missing repository or open blockers.</summary>
    public bool HasAlerts => IsUnavailable || HasBlockers;

    public bool HasSharedBadge => SharedBadge is not null;

    /// <summary>Phase C convention: conflict/newer badges are warnings; the rest are neutral.</summary>
    public bool IsSharedBadgeWarning => SharedBadge is "conflict with shared" or "shared is newer";

    /// <summary>Shared timestamps row: comfortable density only, and only when shared at least once.</summary>
    public bool ShowSharedSync => HasSharedSync && !IsCompact;

    /// <summary>Docker status row: comfortable density only, and only once a probe produced text.</summary>
    public bool ShowDocker => DockerText.Length > 0 && !IsCompact;

    /// <summary>Git sync-times row: comfortable density only, for valid repos once enriched.</summary>
    public bool ShowGitSync => HasGit && GitSyncText.Length > 0 && !IsCompact;

    public bool HasSharedSync =>
        Project.SharedLastExportedAt is not null || Project.SharedLastImportedAt is not null;

    public string SharedSyncText
    {
        get
        {
            var parts = new List<string>(2);
            if (Project.SharedLastExportedAt is { } exported)
                parts.Add($"exported {exported.LocalDateTime:yyyy-MM-dd HH:mm}");
            if (Project.SharedLastImportedAt is { } imported)
                parts.Add($"imported {imported.LocalDateTime:yyyy-MM-dd HH:mm}");
            return string.Join(" · ", parts);
        }
    }

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

    /// <summary>Applied on the UI thread from the background enrichment pass.</summary>
    public void ApplyEnrichment(int milestoneTotal, int milestoneCompleted, int openBlockers, BuildRecord? lastRun)
    {
        HasMilestones = milestoneTotal > 0;
        MilestoneText = $"{milestoneCompleted} of {milestoneTotal} milestones";
        MilestoneProgress = milestoneTotal == 0 ? 0 : 100.0 * milestoneCompleted / milestoneTotal;
        OpenBlockerCount = openBlockers;

        HasRun = lastRun is not null;
        LastRunOk = lastRun?.Succeeded == true;
        LastRunText = lastRun is null
            ? "no runs"
            : lastRun.Succeeded
                ? $"OK · {lastRun.StartedAt.LocalDateTime:MM-dd HH:mm}"
                : $"FAIL · {lastRun.StartedAt.LocalDateTime:MM-dd HH:mm}";
    }

    public void ApplyGitSnapshot(RepositorySnapshot snapshot)
    {
        HasGit = snapshot.IsValid;
        if (!snapshot.IsValid)
        {
            Branch = "—";
            LastCommit = "—";
            GitText = "—";
            return;
        }

        Branch = snapshot.BranchName ?? "?";
        LastCommit = snapshot.LastCommit is { } c
            ? $"{c.ShortSha} {Truncate(c.MessageSummary, 46)}"
            : "no commits";

        var gitText = Branch + (snapshot.IsDirty ? "*" : "");
        if (snapshot.AheadBy is int ahead and > 0)
            gitText += $" ↑{ahead}";
        if (snapshot.BehindBy is int behind and > 0)
            gitText += $" ↓{behind}";
        if (snapshot.IsDirty)
            gitText += $" · {snapshot.ModifiedFiles.Count} dirty";
        GitText = gitText;
    }

    /// <summary>Applied on the UI thread from the background enrichment pass.</summary>
    public void ApplyGitSyncTimes(GitSyncTimes times) =>
        GitSyncText = $"pull {FormatTime(times.LastPull)} · push {FormatTime(times.LastPush)}";

    private static string FormatTime(DateTimeOffset? time) =>
        time?.LocalDateTime.ToString("MM-dd HH:mm") ?? "—";

    [RelayCommand]
    private void Open() => _openProject(Project.Id);

    partial void OnOpenBlockerCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasBlockers));
        OnPropertyChanged(nameof(BlockerText));
        OnPropertyChanged(nameof(HasAlerts));
    }

    partial void OnSharedBadgeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasSharedBadge));
        OnPropertyChanged(nameof(IsSharedBadgeWarning));
    }

    partial void OnIsCompactChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSharedSync));
        OnPropertyChanged(nameof(ShowDocker));
        OnPropertyChanged(nameof(ShowGitSync));
    }

    partial void OnHasGitChanged(bool value) => OnPropertyChanged(nameof(ShowGitSync));
    partial void OnGitSyncTextChanged(string value) => OnPropertyChanged(nameof(ShowGitSync));
    partial void OnDockerTextChanged(string value) => OnPropertyChanged(nameof(ShowDocker));

    partial void OnHasRunChanged(bool value) => OnPropertyChanged(nameof(RunOutcome));
    partial void OnLastRunOkChanged(bool value) => OnPropertyChanged(nameof(RunOutcome));

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
