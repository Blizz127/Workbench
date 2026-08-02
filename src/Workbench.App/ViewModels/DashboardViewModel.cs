using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// Mission control: the shared <see cref="DashboardProjectionBuilder"/>
/// projection rendered as a fleet summary bar, filter chips, a sort menu,
/// project cards, and the merged recent-activity stream. The projection is
/// built off the UI thread (it walks git repositories) and applied in one
/// pass, so the dashboard stays responsive with dozens of projects.
/// Filter/sort semantics live in <see cref="DashboardFilters"/> — this view
/// model only holds the selection state.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action<long> _openProject;
    private readonly Action<long> _openRun;
    private List<ProjectCardViewModel> _allCards = [];

    public ObservableCollection<ProjectCardViewModel> Cards { get; } = [];

    /// <summary>Merged timeline + mobile-action stream, newest first.</summary>
    public ObservableCollection<DashboardActivityItemViewModel> RecentActivity { get; } = [];

    /// <summary>Development Sessions panel: tmux sessions with explicit create/attach/rename/kill.</summary>
    public TmuxSessionsViewModel TmuxSessions { get; }

    /// <summary>Filter chips; exactly one is selected at a time.</summary>
    public ObservableCollection<DashboardFilterChipViewModel> FilterChips { get; } = [];

    public string[] SortOptions { get; } =
        ["Needs attention first", "Recently active", "Alphabetical", "Progress", "Blocker severity"];

    [ObservableProperty]
    private string _selectedSort = "Needs attention first";

    /// <summary>Fleet-wide totals from the projection (summary bar).</summary>
    [ObservableProperty]
    private ProjectDashboardSummary _summary = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    // Summary-bar alert flags: tint a badge only when its count is nonzero.
    [ObservableProperty] private bool _hasAttention;
    [ObservableProperty] private bool _hasBlocked;
    [ObservableProperty] private bool _hasFailing;
    [ObservableProperty] private bool _hasUnavailable;
    [ObservableProperty] private bool _hasActiveBlockers;
    [ObservableProperty] private bool _hasFailingServices;
    [ObservableProperty] private bool _hasDirty;

    [ObservableProperty]
    private bool _hasRecentActivity;

    // System metrics (host-level, orthogonal to the projection)
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

    // "Add project" panel
    [ObservableProperty]
    private bool _isAddPanelOpen;

    [ObservableProperty]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    private string _newRepositoryPath = string.Empty;

    [ObservableProperty]
    private string? _addProjectError;

    public DashboardViewModel(AppServices services, Action<long> openProject, Action<long> openRun)
    {
        _services = services;
        _openProject = openProject;
        _openRun = openRun;
        TmuxSessions = new TmuxSessionsViewModel(services.Tmux, services.Terminal);

        foreach (var filter in Enum.GetValues<DashboardFilter>())
            FilterChips.Add(new DashboardFilterChipViewModel(filter, SetFilter));
        FilterChips[0].IsSelected = true;

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

    private DashboardFilter SelectedFilter =>
        FilterChips.FirstOrDefault(c => c.IsSelected)?.Filter ?? DashboardFilter.All;

    private DashboardSort SelectedSortOrder => SelectedSort switch
    {
        "Recently active" => DashboardSort.RecentlyActive,
        "Alphabetical" => DashboardSort.Alphabetical,
        "Progress" => DashboardSort.Progress,
        "Blocker severity" => DashboardSort.BlockerSeverity,
        _ => DashboardSort.NeedsAttentionFirst,
    };

    [RelayCommand]
    private void SetFilter(DashboardFilter filter)
    {
        foreach (var chip in FilterChips)
            chip.IsSelected = chip.Filter == filter;
        ApplyFilter();
    }

    partial void OnSelectedSortChanged(string value) => ApplyFilter();

    private void Reload()
    {
        // The projection walks git repositories — build it off the UI thread
        // and apply the cards/summary/activity in one posted pass.
        _ = Task.Run(() =>
        {
            var projection = _services.Dashboard.Build();
            var projects = _services.Projects.GetAll().ToDictionary(p => p.Id);
            Dispatcher.UIThread.Post(() => ApplyProjection(projection, projects));
        });
    }

    private void ApplyProjection(DashboardProjection projection, IReadOnlyDictionary<long, Project> projects)
    {
        _allCards = projection.Cards
            .Where(c => projects.ContainsKey(c.ProjectId))
            .Select(c => new ProjectCardViewModel(projects[c.ProjectId], c, _openProject, _openRun))
            .ToList();
        Summary = projection.Summary;
        HasAttention = Summary.AttentionCount > 0;
        HasBlocked = Summary.BlockedCount > 0;
        HasFailing = Summary.FailingCount > 0;
        HasUnavailable = Summary.UnavailableCount > 0;
        HasActiveBlockers = Summary.ActiveBlockerCount > 0;
        HasFailingServices = Summary.FailingServices > 0;
        HasDirty = Summary.DirtyProjects > 0;

        RecentActivity.Clear();
        foreach (var item in projection.RecentActivity)
            RecentActivity.Add(new DashboardActivityItemViewModel(item, _openProject));
        HasRecentActivity = RecentActivity.Count > 0;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = DashboardFilters.Apply(
            [.. _allCards.Select(c => c.Card)], SelectedFilter, SelectedSortOrder);
        var order = selected.Select((card, index) => (card.ProjectId, index))
            .ToDictionary(x => x.ProjectId, x => x.index);

        Cards.Clear();
        foreach (var card in _allCards.Where(c => order.ContainsKey(c.Card.ProjectId))
                     .OrderBy(c => order[c.Card.ProjectId]))
            Cards.Add(card);
    }

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

/// <summary>One filter chip; the parent flips <see cref="IsSelected"/> so chips need no converters.</summary>
public sealed partial class DashboardFilterChipViewModel(
    DashboardFilter filter, Action<DashboardFilter> select) : ViewModelBase
{
    public DashboardFilter Filter { get; } = filter;

    public string Label { get; } = filter switch
    {
        DashboardFilter.NeedsAttention => "Needs attention",
        DashboardFilter.RecentlyActive => "Recently active",
        _ => filter.ToString(),
    };

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => select(Filter);
}

/// <summary>One row of the merged recent-activity stream; opens the project it belongs to.</summary>
public sealed partial class DashboardActivityItemViewModel(
    DashboardActivityItem item, Action<long> openProject) : ViewModelBase
{
    public string WhenText => item.When.LocalDateTime.ToString("MM-dd HH:mm");
    public string ProjectName => item.ProjectName;
    public string Category => item.Category;
    public string Message => item.Message;

    [RelayCommand]
    private void Open() => openProject(item.ProjectId);
}
