using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Mission control: every registered project as a card, filterable by status,
/// tag, and machine availability, sortable by activity/blockers/failures/name,
/// in comfortable or compact density. Git, milestone, blocker, and run-history
/// data is loaded asynchronously after the cards appear so the dashboard stays
/// responsive with dozens of projects.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private const string AllTagsLabel = "All tags";

    private readonly AppServices _services;
    private readonly Action<long> _openProject;
    private List<ProjectCardViewModel> _allCards = [];

    public ObservableCollection<ProjectCardViewModel> Cards { get; } = [];

    /// <summary>Development Sessions panel: tmux sessions with explicit create/attach/rename/kill.</summary>
    public TmuxSessionsViewModel TmuxSessions { get; }

    public string[] StatusFilters { get; } = ["All", "Active", "Waiting", "Broken", "Research", "Complete"];

    public ObservableCollection<string> TagFilters { get; } = [AllTagsLabel];

    public string[] AvailabilityFilters { get; } = ["All", "Available here", "Missing here"];

    public string[] SortOptions { get; } = ["Recently active", "Blocked", "Failing", "Alphabetical"];

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private string _selectedTag = AllTagsLabel;

    [ObservableProperty]
    private string _selectedAvailability = "All";

    [ObservableProperty]
    private string _selectedSort = "Recently active";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _cpuText = "Loading…";

    [ObservableProperty]
    private string _memoryText = "Loading…";

    [ObservableProperty]
    private string _diskText = "Loading…";

    [ObservableProperty]
    private string _uptimeText = "Loading…";

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private double _diskPercent;

    /// <summary>Compact density; persisted as AppSettings.DashboardDensity.</summary>
    [ObservableProperty]
    private bool _isCompact;

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
        IsCompact = services.AppSettings.Settings.DashboardDensity == "compact";
        TmuxSessions = new TmuxSessionsViewModel(services.Tmux, services.Terminal);
        Reload();
        _ = LoadSystemHealthAsync();
    }

    private async Task LoadSystemHealthAsync()
    {
        try
        {
            var health = await _services.SystemHealth.GetSnapshotAsync();
            CpuPercent = health.CpuUsagePercent;
            MemoryPercent = health.MemoryUsagePercent;
            DiskPercent = health.DiskUsagePercent;
            CpuText = $"{health.CpuUsagePercent:F0}%";
            MemoryText = $"{FormatBytes(health.UsedMemoryBytes)} / {FormatBytes(health.TotalMemoryBytes)}";
            DiskText = $"{FormatBytes(health.UsedDiskBytes)} / {FormatBytes(health.TotalDiskBytes)}";
            UptimeText = FormatUptime(health.Uptime);
        }
        catch (Exception)
        {
            CpuText = MemoryText = DiskText = UptimeText = "Unavailable";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:F1} {units[unit]}";
    }

    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
        : $"{uptime.Hours}h {uptime.Minutes}m";

    private void Reload()
    {
        var projects = _services.Projects.GetAll();
        _allCards = projects
            .Select(p => new ProjectCardViewModel(_services, p, _openProject) { IsCompact = IsCompact })
            .ToList();

        RebuildTagFilters(projects);
        ApplyFilter();
        UpdateSummary(projects);

        // Enrich with git state off the UI thread, one repo at a time; shared
        // state files are read (never written) in the same pass — but only in
        // Enabled mode; Manual/Disabled never touch the shared folder here.
        // Milestone, blocker, and run-history reads are cheap local queries
        // batched in.
        var cards = _allCards.ToList();
        var sharedFolder = _services.AppSettings.SharedStateFolder;
        var machineName = _services.AppSettings.MachineName;
        var sharedMode = _services.AppSettings.Mode;
        _ = Task.Run(async () =>
        {
            IReadOnlyList<Workbench.Core.Services.SharedStateFileInfo> sharedFiles =
                Workbench.Core.Services.SharedStateModeRules.LoadAllForBadges(
                    _services.SharedState, sharedMode, sharedFolder);

            foreach (var card in cards)
            {
                var snapshot = _services.Git.GetSnapshot(card.Project.RepositoryPath, recentCommitCount: 1);
                var badge = Workbench.Core.Services.SharedStateModeRules.Badges(sharedMode)
                    ? SharedBadgeFor(card.Project, sharedFiles, machineName)
                    : null;
                var milestones = _services.Milestones.GetForProject(card.Project.Id);
                var openBlockers = _services.Blockers.GetOpen(card.Project.Id).Count;
                var lastRun = _services.Builds.GetLatest(card.Project.Id, ExecutionKind.Run);
                var syncTimes = _services.GitSync.GetSyncTimes(card.Project);
                var (dockerText, dockerOutcome) = await ProbeDocker(card.Project);
                Dispatcher.UIThread.Post(() =>
                {
                    card.ApplyGitSnapshot(snapshot);
                    card.ApplyGitSyncTimes(syncTimes);
                    card.SharedBadge = badge;
                    card.DockerText = dockerText;
                    card.DockerOutcome = dockerOutcome;
                    card.ApplyEnrichment(milestones.Count, milestones.Count(m => m.IsCompleted), openBlockers, lastRun);
                });
            }
        });
    }

    /// <summary>
    /// One-shot compose probe for the card's docker row: counts services in
    /// state "running". Blank text means "no probe" (no compose file, no
    /// status command, or the probe itself failed).
    /// </summary>
    private async Task<(string Text, bool? Outcome)> ProbeDocker(Project project)
    {
        var path = project.RepositoryPath;
        if (path is null || !Directory.Exists(path))
            return (string.Empty, null);
        if (!Workbench.Core.Services.ComposeFileInspector.HasComposeFile(path) &&
            string.IsNullOrWhiteSpace(project.Launch.StatusCommand))
            return (string.Empty, null);

        var result = await _services.Runner.RunAsync(
            "docker compose ps --format '{{.Service}} {{.State}}'",
            path,
            TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0)
            return (string.Empty, null);

        var services = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var running = services.Count(l => l.Contains("running", StringComparison.OrdinalIgnoreCase));

        return running == 0
            ? ("stopped", false)
            : running == services.Length
                ? ($"{running}/{services.Length} containers up", true)
                : ($"{running}/{services.Length} up", false);
    }

    private void RebuildTagFilters(IReadOnlyList<Project> projects)
    {
        var tags = projects.SelectMany(p => p.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        TagFilters.Clear();
        TagFilters.Add(AllTagsLabel);
        foreach (var tag in tags)
            TagFilters.Add(tag);
        if (!TagFilters.Contains(SelectedTag))
            SelectedTag = AllTagsLabel;
    }

    /// <summary>Compares a project against its shared file; null means in sync (or not shared).</summary>
    private string? SharedBadgeFor(
        Project project,
        IReadOnlyList<Workbench.Core.Services.SharedStateFileInfo> sharedFiles,
        string machineName) =>
        SharedStatusBadge.For(_services.SharedState, project, sharedFiles, machineName);

    private void UpdateSummary(IReadOnlyList<Project> projects)
    {
        var active = projects.Count(p => p.Status == ProjectStatus.Active);
        var broken = projects.Count(p => p.Status == ProjectStatus.Broken);
        var waiting = projects.Count(p => p.Status == ProjectStatus.Waiting);
        SummaryText = $"{projects.Count} projects · {active} active · {waiting} waiting · {broken} broken";
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedTagChanged(string value) => ApplyFilter();
    partial void OnSelectedAvailabilityChanged(string value) => ApplyFilter();
    partial void OnSelectedSortChanged(string value) => ApplyFilter();

    partial void OnIsCompactChanged(bool value)
    {
        foreach (var card in _allCards)
            card.IsCompact = value;

        var density = value ? "compact" : "comfortable";
        if (_services.AppSettings.Settings.DashboardDensity != density)
        {
            _services.AppSettings.Settings.DashboardDensity = density;
            _services.AppSettings.Save();
        }
    }

    [RelayCommand]
    private void SetDensity(string density) => IsCompact = density == "compact";

    /// <summary>Re-scans the discovery folder for new projects, then reloads the cards.</summary>
    [RelayCommand]
    private void Rescan()
    {
        try
        {
            _services.Discovery.Scan();
        }
        catch (Exception)
        {
            // Discovery must never take the dashboard down; Reload still runs.
        }

        Reload();
    }

    private void ApplyFilter()
    {
        IEnumerable<ProjectCardViewModel> query = _allCards;

        if (SelectedFilter != "All")
            query = query.Where(c => c.Status == SelectedFilter);
        if (SelectedTag != AllTagsLabel)
            query = query.Where(c => c.Tags.Contains(SelectedTag));
        query = SelectedAvailability switch
        {
            "Available here" => query.Where(c => !c.IsUnavailable),
            "Missing here" => query.Where(c => c.IsUnavailable),
            _ => query,
        };
        query = SelectedSort switch
        {
            "Blocked" => query.OrderByDescending(c => c.OpenBlockerCount)
                .ThenByDescending(c => c.Project.UpdatedAt),
            "Failing" => query.OrderByDescending(c => c.IsFailing)
                .ThenByDescending(c => c.Project.UpdatedAt),
            "Alphabetical" => query.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(c => c.Project.UpdatedAt),
        };

        Cards.Clear();
        foreach (var card in query)
            Cards.Add(card);
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
