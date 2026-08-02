using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// The "Shared Blockers" section shown on the Progress page and the Shared
/// State page: counts by lifecycle status, the full blocker list (all
/// statuses — resolved/dismissed are history), and CRUD. Blockers that have
/// not been exported yet (or changed since the last sync) are marked as
/// pending export so the user knows the shared file is behind.
/// </summary>
public sealed partial class ProjectBlockersViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;

    public ObservableCollection<SharedBlockerItemViewModel> Items { get; } = [];

    [ObservableProperty] private string _countsText = string.Empty;
    [ObservableProperty] private string _newTitle = string.Empty;
    [ObservableProperty] private string _newSeverity = string.Empty;
    [ObservableProperty] private string _newDescription = string.Empty;

    public ProjectBlockersViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        Refresh();
    }

    /// <summary>Reloads from the repository and re-marks pending-export rows.</summary>
    public void Refresh()
    {
        var shared = LoadSharedState();
        var all = _services.Blockers.GetForProject(_project.Id);

        Items.Clear();
        foreach (var blocker in all)
            Items.Add(new SharedBlockerItemViewModel(blocker, IsPendingExport(blocker, shared), this));

        CountsText = all.Count == 0
            ? string.Empty
            : $"{Count(all, BlockerStatus.Active)} active · {Count(all, BlockerStatus.Monitoring)} monitoring · " +
              $"{Count(all, BlockerStatus.Resolved)} resolved · {Count(all, BlockerStatus.Dismissed)} dismissed";
    }

    private static int Count(IReadOnlyList<Blocker> all, BlockerStatus status) => all.Count(b => b.Status == status);

    private SharedProjectState? LoadSharedState()
    {
        if (_services.AppSettings.SharedStateFolder is not { } folder)
            return null;
        try
        {
            return _services.SharedState.LoadFile(_services.SharedState.GetSharedFilePath(folder, _project));
        }
        catch (Exception)
        {
            // An unreadable shared folder must never break the page; badges just stay off.
            return null;
        }
    }

    /// <summary>Not yet exported, or changed locally since the last export.</summary>
    private bool IsPendingExport(Blocker blocker, SharedProjectState? shared)
    {
        if (blocker.StableId.Length == 0)
            return false;
        if (shared is not null && shared.Blockers.All(b => b.StableId != blocker.StableId))
            return true;
        return _project.SharedLastExportedAt is { } exported && blocker.UpdatedAt > exported;
    }

    [RelayCommand]
    private void AddBlocker()
    {
        var title = NewTitle.Trim();
        var description = NewDescription.Trim();
        if (title.Length == 0 && description.Length == 0)
            return;

        _services.Blockers.Add(new Blocker
        {
            ProjectId = _project.Id,
            Title = title.Length > 0 ? title : null,
            Description = description.Length > 0 ? description : title,
            Severity = string.IsNullOrWhiteSpace(NewSeverity) ? null : NewSeverity.Trim(),
        });
        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Category = TimelineCategory.Blocker,
            Message = $"Blocked: {(title.Length > 0 ? title : description)}",
        });

        NewTitle = string.Empty;
        NewSeverity = string.Empty;
        NewDescription = string.Empty;
        Refresh();
    }

    internal void Resolve(SharedBlockerItemViewModel item)
    {
        var blocker = item.Blocker;
        blocker.Status = BlockerStatus.Resolved;
        blocker.ResolvedAt = DateTimeOffset.Now;
        blocker.Resolution = string.IsNullOrWhiteSpace(item.ResolutionText) ? null : item.ResolutionText.Trim();
        _services.Blockers.Update(blocker);
        Journal($"Blocker resolved: {blocker.TitleOrDescription}", blocker.Resolution);
        Refresh();
    }

    internal void Reopen(SharedBlockerItemViewModel item)
    {
        var blocker = item.Blocker;
        blocker.Status = BlockerStatus.Active;
        blocker.ResolvedAt = null;
        _services.Blockers.Update(blocker);
        Journal($"Blocker reopened: {blocker.TitleOrDescription}", null);
        Refresh();
    }

    internal void Dismiss(SharedBlockerItemViewModel item)
    {
        var blocker = item.Blocker;
        blocker.Status = BlockerStatus.Dismissed;
        blocker.ResolvedAt = DateTimeOffset.Now;
        _services.Blockers.Update(blocker);
        Journal($"Blocker dismissed: {blocker.TitleOrDescription}", null);
        Refresh();
    }

    internal void ToggleMonitoring(SharedBlockerItemViewModel item)
    {
        var blocker = item.Blocker;
        blocker.Status = blocker.Status == BlockerStatus.Monitoring
            ? BlockerStatus.Active
            : BlockerStatus.Monitoring;
        _services.Blockers.Update(blocker);
        Journal($"Blocker {(blocker.Status == BlockerStatus.Monitoring ? "under monitoring" : "reactivated")}: {blocker.TitleOrDescription}", null);
        Refresh();
    }

    internal void SaveEdit(SharedBlockerItemViewModel item)
    {
        var blocker = item.Blocker;
        blocker.Title = NullIfEmpty(item.EditTitle);
        blocker.Description = item.EditDescription.Trim();
        blocker.BlockerClass = NullIfEmpty(item.EditClass);
        blocker.Severity = NullIfEmpty(item.EditSeverity);
        blocker.Evidence = NullIfEmpty(item.EditEvidence);
        blocker.NextSafestStep = NullIfEmpty(item.EditNextStep);
        blocker.Owner = NullIfEmpty(item.EditOwner);
        blocker.SourceReference = NullIfEmpty(item.EditSourceReference);
        blocker.Tags = item.EditTags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (blocker.Title is null && blocker.Description.Length == 0)
            return; // a blocker needs at least a headline or a description

        _services.Blockers.Update(blocker);
        Journal($"Blocker edited: {blocker.TitleOrDescription}", null);
        Refresh();
    }

    private void Journal(string message, string? detail) =>
        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Category = TimelineCategory.Blocker,
            Message = message,
            Detail = detail,
        });

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One blocker row with inline resolution and edit affordances.</summary>
public sealed partial class SharedBlockerItemViewModel : ViewModelBase
{
    private readonly ProjectBlockersViewModel _parent;

    public Blocker Blocker { get; }

    public SharedBlockerItemViewModel(Blocker blocker, bool pendingExport, ProjectBlockersViewModel parent)
    {
        Blocker = blocker;
        PendingExport = pendingExport;
        _parent = parent;
    }

    public string Title => Blocker.TitleOrDescription;
    public string StatusText => Blocker.Status.ToString();
    public string? Severity => Blocker.Severity;
    public bool HasSeverity => Blocker.Severity is { Length: > 0 };
    public string? Evidence => Blocker.Evidence;
    public bool HasEvidence => Blocker.Evidence is { Length: > 0 };
    public string? NextSafestStep => Blocker.NextSafestStep;
    public bool HasNextSafestStep => Blocker.NextSafestStep is { Length: > 0 };
    public string? Owner => Blocker.Owner;
    public bool HasOwner => Blocker.Owner is { Length: > 0 };
    public string? Resolution => Blocker.Resolution;
    public bool HasResolution => Blocker.Resolution is { Length: > 0 };
    public string UpdatedText => $"updated {Blocker.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";

    public bool IsOpen => Blocker.Status is BlockerStatus.Active or BlockerStatus.Monitoring;
    public bool IsActive => Blocker.Status == BlockerStatus.Active;
    public bool IsMonitoring => Blocker.Status == BlockerStatus.Monitoring;
    public string MonitorButtonText => IsMonitoring ? "Reactivate" : "Monitor";

    /// <summary>Local-only difference: not yet exported, or changed since the last sync.</summary>
    [ObservableProperty] private bool _pendingExport;

    [ObservableProperty] private string _resolutionText = string.Empty;
    [ObservableProperty] private bool _isEditing;

    // Edit form fields (copied from the blocker when edit begins).
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private string _editClass = string.Empty;
    [ObservableProperty] private string _editSeverity = string.Empty;
    [ObservableProperty] private string _editEvidence = string.Empty;
    [ObservableProperty] private string _editNextStep = string.Empty;
    [ObservableProperty] private string _editOwner = string.Empty;
    [ObservableProperty] private string _editTags = string.Empty;
    [ObservableProperty] private string _editSourceReference = string.Empty;

    [RelayCommand] private void Resolve() => _parent.Resolve(this);
    [RelayCommand] private void Reopen() => _parent.Reopen(this);
    [RelayCommand] private void Dismiss() => _parent.Dismiss(this);
    [RelayCommand] private void ToggleMonitoring() => _parent.ToggleMonitoring(this);

    [RelayCommand]
    private void BeginEdit()
    {
        EditTitle = Blocker.Title ?? string.Empty;
        EditDescription = Blocker.Description;
        EditClass = Blocker.BlockerClass ?? string.Empty;
        EditSeverity = Blocker.Severity ?? string.Empty;
        EditEvidence = Blocker.Evidence ?? string.Empty;
        EditNextStep = Blocker.NextSafestStep ?? string.Empty;
        EditOwner = Blocker.Owner ?? string.Empty;
        EditTags = string.Join(", ", Blocker.Tags);
        EditSourceReference = Blocker.SourceReference ?? string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        _parent.SaveEdit(this);
        IsEditing = false;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;
}
