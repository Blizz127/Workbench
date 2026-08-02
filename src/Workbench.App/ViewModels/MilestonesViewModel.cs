using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// User-defined milestones rendered as a checklist GROUPED by status (Active /
/// Blocked / Not Started / Done; Dismissed collapsed behind a toggle), with a
/// weighted progress bar from <see cref="ProgressCalculator"/> and the
/// frontier checklist. Edit actions (toggle, add, remove) are unchanged.
/// </summary>
public sealed partial class MilestonesViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;
    private readonly List<MilestoneItemViewModel> _items = [];

    public ObservableCollection<MilestoneItemViewModel> ActiveItems { get; } = [];
    public ObservableCollection<MilestoneItemViewModel> BlockedItems { get; } = [];
    public ObservableCollection<MilestoneItemViewModel> NotStartedItems { get; } = [];
    public ObservableCollection<MilestoneItemViewModel> DoneItems { get; } = [];
    public ObservableCollection<MilestoneItemViewModel> DismissedItems { get; } = [];

    /// <summary>The frontier checklist (read-only; Advance lives on the Frontier card).</summary>
    public ObservableCollection<FrontierItemViewModel> FrontierItems { get; } = [];

    [ObservableProperty]
    private string _newMilestoneTitle = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDismissedSection))]
    [NotifyPropertyChangedFor(nameof(DismissedToggleText))]
    private bool _showDismissed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDismissedSection))]
    [NotifyPropertyChangedFor(nameof(DismissedToggleText))]
    private int _dismissedCount;

    [ObservableProperty]
    private bool _hasMilestones;

    [ObservableProperty]
    private string _frontierProgressText = string.Empty;

    [ObservableProperty]
    private bool _hasFrontiers;

    /// <summary>Dismissed milestones are hidden by default; the toggle expands them.</summary>
    public bool ShowDismissedSection => ShowDismissed && DismissedCount > 0;
    public string DismissedToggleText =>
        $"{(ShowDismissed ? "Hide" : "Show")} dismissed ({DismissedCount})";

    public MilestonesViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        Reload();
    }

    private void Reload()
    {
        _items.Clear();
        foreach (var milestone in _services.Milestones.GetForProject(_project.Id))
            _items.Add(new MilestoneItemViewModel(milestone, Toggle, Remove));
        Regroup();
        LoadFrontiers();
    }

    /// <summary>Redistributes the master list into the status groups and recomputes progress.</summary>
    private void Regroup()
    {
        ActiveItems.Clear();
        BlockedItems.Clear();
        NotStartedItems.Clear();
        DoneItems.Clear();
        DismissedItems.Clear();

        foreach (var item in _items)
        {
            (item.Milestone.Status switch
            {
                MilestoneStatus.Active => ActiveItems,
                MilestoneStatus.Blocked => BlockedItems,
                MilestoneStatus.Done => DoneItems,
                MilestoneStatus.Dismissed => DismissedItems,
                _ => NotStartedItems,
            }).Add(item);
        }

        HasMilestones = _items.Count > 0;
        DismissedCount = DismissedItems.Count;
        UpdateProgress();
    }

    /// <summary>Weighted progress from the shared calculator; null means "not configured".</summary>
    private void UpdateProgress()
    {
        var progress = ProgressCalculator.Calculate([.. _items.Select(i => i.Milestone)]);
        Progress = progress?.Percent ?? 0;
        ProgressText = progress is null
            ? "Progress not configured — add milestones below"
            : $"{progress.CompletedCount} of {progress.TotalCount} complete · {progress.Percent:F0}%";
    }

    private void LoadFrontiers()
    {
        var frontiers = _services.Frontiers.GetForProject(_project.Id);
        FrontierItems.Clear();
        foreach (var frontier in frontiers)
            FrontierItems.Add(new FrontierItemViewModel(frontier));

        var progress = ProgressCalculator.CalculateFrontiers(frontiers);
        HasFrontiers = progress.TotalCount > 0;
        FrontierProgressText = progress.TotalCount == 0
            ? string.Empty
            : $"{progress.CompletedCount} of {progress.TotalCount} completed";
    }

    [RelayCommand]
    private void ToggleDismissed() => ShowDismissed = !ShowDismissed;

    [RelayCommand]
    private void AddMilestone()
    {
        var title = NewMilestoneTitle.Trim();
        if (title.Length == 0)
            return;

        _services.Milestones.Add(new Milestone
        {
            ProjectId = _project.Id,
            Title = title,
            SortOrder = _items.Count,
        });

        NewMilestoneTitle = string.Empty;
        Reload();
    }

    private void Toggle(MilestoneItemViewModel item)
    {
        var milestone = item.Milestone;
        milestone.Status = item.IsCompleted ? MilestoneStatus.Done : MilestoneStatus.NotStarted;
        milestone.IsCompleted = item.IsCompleted;
        milestone.CompletedAt = item.IsCompleted ? DateTimeOffset.Now : null;
        _services.Milestones.Update(milestone);

        if (item.IsCompleted)
        {
            _services.Timeline.Add(new TimelineEntry
            {
                ProjectId = _project.Id,
                Category = TimelineCategory.Milestone,
                Message = $"Milestone completed: {milestone.Title}",
            });
        }

        Regroup();
    }

    private void Remove(MilestoneItemViewModel item)
    {
        _services.Milestones.Delete(item.Milestone.Id);
        _items.Remove(item);
        Regroup();
    }
}

public sealed partial class MilestoneItemViewModel : ViewModelBase
{
    private readonly Action<MilestoneItemViewModel> _toggle;
    private readonly Action<MilestoneItemViewModel> _remove;

    public Milestone Milestone { get; }

    public string Title => Milestone.Title;

    /// <summary>Status badge text; Done/NotStarted milestones need no badge (the checkbox says it).</summary>
    public string StatusText => Milestone.Status.ToString();
    public bool ShowStatusBadge => Milestone.Status is MilestoneStatus.Active or MilestoneStatus.Blocked;
    public bool StatusIsBlocked => Milestone.Status == MilestoneStatus.Blocked;

    public string CompletedText => Milestone.CompletedAt is { } at
        ? $"done {at.LocalDateTime:yyyy-MM-dd}"
        : string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    public MilestoneItemViewModel(
        Milestone milestone, Action<MilestoneItemViewModel> toggle, Action<MilestoneItemViewModel> remove)
    {
        Milestone = milestone;
        _toggle = toggle;
        _remove = remove;
        _isCompleted = milestone.IsCompleted;
    }

    partial void OnIsCompletedChanged(bool value) => _toggle(this);

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>One read-only frontier checklist row.</summary>
public sealed class FrontierItemViewModel(Frontier frontier)
{
    public string Title => frontier.Title;
    public string StatusText => frontier.Status.ToString();
    public bool IsCompleted => frontier.Status == FrontierStatus.Completed;
    public bool IsCurrent => frontier.Status == FrontierStatus.Current;
}
