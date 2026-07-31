using System.Collections.ObjectModel;
using Avalonia.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// Application shell: left navigation rail, top context bar (<see cref="TopBar"/>),
/// the current page (dashboard or project detail), the collapsible activity
/// panel, and the global search that spans projects, notes, milestones,
/// timeline, and frontiers. Restores the persisted page/project/panel state
/// on startup and saves it on every navigation.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private bool _isRightPanelVisible;

    [ObservableProperty]
    private bool _isDashboardSelected;

    [ObservableProperty]
    private bool _isSettingsSelected;

    /// <summary>True while a project detail page is open; drives the activity panel and top bar.</summary>
    [ObservableProperty]
    private bool _hasProjectContext;

    public TopBarViewModel TopBar { get; }

    public ObservableCollection<SearchHitViewModel> SearchResults { get; } = [];

    /// <summary>Latest timeline entries for the open project, shown in the activity panel.</summary>
    public ObservableCollection<TimelineEntryViewModel> ActivityEntries { get; } = [];

    /// <summary>UI-state persistence: window geometry, page/project, and panel state.</summary>
    public IAppSettingsService AppSettings => _services.AppSettings;

    private static readonly CrossFade PageFade = new(TimeSpan.FromMilliseconds(180));

    /// <summary>Shell page transition; null when reduced motion is on (bound by the TransitioningContentControl).</summary>
    public IPageTransition? PageTransition =>
        _services.AppSettings.Settings.ReducedMotion ? null : PageFade;

    /// <summary>Re-reads motion-dependent properties after settings are saved.</summary>
    private void OnSettingsSaved() => OnPropertyChanged(nameof(PageTransition));

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        TopBar = new TopBarViewModel(services);

        var settings = services.AppSettings.Settings;
        _isRightPanelVisible = settings.RightPanelVisible;
        _currentPage = RestoreLastPage(settings);
    }

    /// <summary>Rebuilds the page the user last had open; falls back to the dashboard silently.</summary>
    private ViewModelBase RestoreLastPage(AppSettings settings)
    {
        if (settings.LastPage == "settings")
        {
            IsSettingsSelected = true;
            return new AppSettingsViewModel(_services, NavigateHome, OnSettingsSaved);
        }

        if (settings.LastPage == "project" && settings.LastSelectedProjectId is { } projectId
            && ProjectDetailViewModel.TryCreate(_services, projectId, NavigateHome, TopBar.RefreshStatus) is { } detail)
        {
            SetProjectContext(detail);
            return detail;
        }

        IsDashboardSelected = true;
        return new DashboardViewModel(_services, OpenProject);
    }

    [RelayCommand]
    private void NavigateHome()
    {
        IsSearchOpen = false;
        IsDashboardSelected = true;
        IsSettingsSelected = false;
        ClearProjectContext();
        CurrentPage = new DashboardViewModel(_services, OpenProject);
        PersistNavigation("dashboard", null);
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        IsSearchOpen = false;
        IsSettingsSelected = true;
        IsDashboardSelected = false;
        ClearProjectContext();
        CurrentPage = new AppSettingsViewModel(_services, NavigateHome, OnSettingsSaved);
        PersistNavigation("settings", null);
    }

    public void OpenProject(long projectId)
    {
        IsSearchOpen = false;
        var detail = ProjectDetailViewModel.TryCreate(_services, projectId, NavigateHome, TopBar.RefreshStatus);
        if (detail is null)
            return;

        IsDashboardSelected = false;
        IsSettingsSelected = false;
        SetProjectContext(detail);
        CurrentPage = detail;
        PersistNavigation("project", projectId);
    }

    [RelayCommand]
    private void ToggleRightPanel()
    {
        IsRightPanelVisible = !IsRightPanelVisible;
        _services.AppSettings.Settings.RightPanelVisible = IsRightPanelVisible;
        _services.AppSettings.Save();
    }

    private void SetProjectContext(ProjectDetailViewModel detail)
    {
        HasProjectContext = true;
        TopBar.ShowProjectContext(detail.Project);
        LoadActivity(detail.Project.Id);
    }

    private void ClearProjectContext()
    {
        HasProjectContext = false;
        TopBar.ClearContext();
        ActivityEntries.Clear();
    }

    private void LoadActivity(long projectId)
    {
        ActivityEntries.Clear();
        foreach (var entry in _services.Timeline.GetForProject(projectId, 20))
            ActivityEntries.Add(new TimelineEntryViewModel(entry));
    }

    private void PersistNavigation(string page, long? projectId)
    {
        var settings = _services.AppSettings.Settings;
        settings.LastPage = page;
        settings.LastSelectedProjectId = projectId;
        _services.AppSettings.Save();
    }

    [RelayCommand]
    private void RunSearch()
    {
        SearchResults.Clear();
        var query = SearchText;
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearchOpen = false;
            return;
        }

        foreach (var hit in _services.Search.Search(query, 50))
            SearchResults.Add(new SearchHitViewModel(hit, OpenProject));

        IsSearchOpen = true;
    }

    [RelayCommand]
    private void CloseSearch() => IsSearchOpen = false;

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            IsSearchOpen = false;
    }
}

/// <summary>One global-search hit, self-contained so item templates need no ancestor bindings.</summary>
public sealed partial class SearchHitViewModel(SearchResult hit, Action<long> openProject) : ViewModelBase
{
    public string Domain => hit.Domain.ToString();
    public string Title => hit.Title;
    public string ProjectName => hit.ProjectName;
    public string Snippet => Truncate(hit.Snippet, 120);

    [RelayCommand]
    private void Open() => openProject(hit.ProjectId);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
