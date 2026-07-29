using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// The handoff page: a generated markdown summary of current state, last
/// completed work, blockers, and the recommended next step. In later phases
/// this same document seeds AI prompts.
/// </summary>
public sealed partial class HandoffViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;

    [ObservableProperty]
    private string _handoffMarkdown = string.Empty;

    public HandoffViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        Regenerate();
    }

    [RelayCommand]
    private void Regenerate()
    {
        var context = _services.BuildHandoffContext(_project);
        HandoffMarkdown = _services.Handoff.Generate(context);
    }
}
