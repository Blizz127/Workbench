using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>User-defined milestones rendered as a checklist with a progress bar.</summary>
public sealed partial class MilestonesViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;

    public ObservableCollection<MilestoneItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _newMilestoneTitle = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    public MilestonesViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        Reload();
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var milestone in _services.Milestones.GetForProject(_project.Id))
            Items.Add(new MilestoneItemViewModel(milestone, Toggle, Remove));
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var total = Items.Count;
        var done = Items.Count(i => i.IsCompleted);
        Progress = total == 0 ? 0 : (double)done / total * 100.0;
        ProgressText = total == 0 ? "No milestones yet" : $"{done} of {total} complete";
    }

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
            SortOrder = Items.Count,
        });

        NewMilestoneTitle = string.Empty;
        Reload();
    }

    private void Toggle(MilestoneItemViewModel item)
    {
        var milestone = item.Milestone;
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

        UpdateProgress();
    }

    private void Remove(MilestoneItemViewModel item)
    {
        _services.Milestones.Delete(item.Milestone.Id);
        Reload();
    }
}

public sealed partial class MilestoneItemViewModel : ViewModelBase
{
    private readonly Action<MilestoneItemViewModel> _toggle;
    private readonly Action<MilestoneItemViewModel> _remove;

    public Milestone Milestone { get; }

    public string Title => Milestone.Title;

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
