using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Application shell: hosts the current page (dashboard or project detail)
/// and the global search that spans projects, notes, milestones, timeline,
/// and frontiers.
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

    public ObservableCollection<SearchHitViewModel> SearchResults { get; } = [];

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        _currentPage = new DashboardViewModel(_services, OpenProject);
    }

    [RelayCommand]
    private void NavigateHome()
    {
        IsSearchOpen = false;
        CurrentPage = new DashboardViewModel(_services, OpenProject);
    }

    public void OpenProject(long projectId)
    {
        IsSearchOpen = false;
        var detail = ProjectDetailViewModel.TryCreate(_services, projectId, NavigateHome);
        if (detail is not null)
            CurrentPage = detail;
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
