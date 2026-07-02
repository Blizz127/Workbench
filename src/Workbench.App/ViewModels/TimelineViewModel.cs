using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>The project's engineering journal: append-only, newest first.</summary>
public sealed partial class TimelineViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;

    public ObservableCollection<TimelineEntryViewModel> Entries { get; } = [];

    public TimelineCategory[] Categories { get; } = Enum.GetValues<TimelineCategory>();

    [ObservableProperty]
    private TimelineCategory _newCategory = TimelineCategory.General;

    [ObservableProperty]
    private string _newMessage = string.Empty;

    public TimelineViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        Reload();
    }

    private void Reload()
    {
        Entries.Clear();
        foreach (var entry in _services.Timeline.GetForProject(_project.Id, 300))
            Entries.Add(new TimelineEntryViewModel(entry));
    }

    [RelayCommand]
    private void AddEntry()
    {
        var message = NewMessage.Trim();
        if (message.Length == 0)
            return;

        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Category = NewCategory,
            Message = message,
        });

        NewMessage = string.Empty;
        Reload();
    }
}

public sealed class TimelineEntryViewModel(TimelineEntry entry)
{
    public string Timestamp => entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Category => entry.Category.ToString();
    public string Message => entry.Message;
    public string? Detail => entry.Detail;
    public bool HasDetail => !string.IsNullOrWhiteSpace(entry.Detail);
}
