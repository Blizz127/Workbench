using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// The project detail's Sessions section: this project's tmux session (explicit
/// <c>tmuxSession</c> from workbench.json, or the slug synthesized from the
/// project name) with its live state and the safe project actions — Create
/// (rooted at the repository path), Attach / Open Terminal, Kill. Create/Kill
/// preview the exact command; Kill additionally requires Confirm (enforced
/// again in <see cref="ITmuxService.ExecuteAsync"/>). Nothing runs at startup
/// beyond the read-only inspection.
/// </summary>
public sealed partial class ProjectSessionsViewModel : ViewModelBase
{
    private readonly ITmuxService _tmux;
    private readonly ITerminalLauncher _terminal;
    private readonly Project _project;
    private readonly ITimelineRepository? _timeline;
    private TmuxOperation? _pending;

    public string SessionName { get; }

    [ObservableProperty] private string _stateText = "Inspecting…";
    [ObservableProperty] private string _detailText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareKillCommand))]
    [NotifyCanExecuteChangedFor(nameof(AttachCommand))]
    private bool _sessionExists;

    [ObservableProperty] private bool _isAttached;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepareCreateCommand))]
    private bool _tmuxAvailable = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareKillCommand))]
    [NotifyCanExecuteChangedFor(nameof(AttachCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmOperationCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _busyText = string.Empty;

    // Preview/confirm panel: no mutating command runs before Confirm.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmOperationCommand))]
    private bool _isPreviewOpen;
    [ObservableProperty] private string _previewTitle = string.Empty;
    [ObservableProperty] private string _previewDescription = string.Empty;
    [ObservableProperty] private string _previewCommand = string.Empty;
    [ObservableProperty] private bool _isPreviewDanger;

    [ObservableProperty] private string _lastResultText = string.Empty;
    [ObservableProperty] private bool _lastResultWasError;

    public ProjectSessionsViewModel(
        ITmuxService tmux, ITerminalLauncher terminal, Project project, ITimelineRepository? timeline = null)
    {
        _tmux = tmux;
        _terminal = terminal;
        _project = project;
        _timeline = timeline;
        SessionName = tmux.ProjectSessionName(project);
        _ = Refresh();
    }

    public bool CanCreate => !IsBusy && !SessionExists && TmuxAvailable;
    public bool CanKill => !IsBusy && SessionExists;
    public bool CanAttach => !IsBusy && SessionExists;
    private bool CanConfirm => IsPreviewOpen && !IsBusy;

    /// <summary>Read-only re-inspection of this project's session state.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    public async Task Refresh()
    {
        IsBusy = true;
        BusyText = "Inspecting…";
        try
        {
            var status = await _tmux.InspectAsync();
            TmuxAvailable = status.TmuxAvailable;

            var session = status.Sessions.FirstOrDefault(
                s => string.Equals(s.Name, SessionName, StringComparison.Ordinal));
            SessionExists = session is not null;
            IsAttached = session?.IsAttached ?? false;
            StateText = !status.TmuxAvailable
                ? "tmux is not available on this machine."
                : session is null
                    ? "no session"
                    : session.IsAttached ? "attached" : "detached";
            DetailText = session is null
                ? string.Empty
                : $"{session.WindowCount} window{(session.WindowCount == 1 ? "" : "s")}" +
                  (session.ActiveWindowName is { } window ? $" · {window}" : string.Empty);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void PrepareCreate() => Prepare(() =>
        TmuxCommandBuilder.BuildCreate(SessionName, ExistingRepositoryPath()));

    [RelayCommand(CanExecute = nameof(CanKill))]
    private void PrepareKill() => Prepare(() => TmuxCommandBuilder.BuildKill(SessionName));

    private string? ExistingRepositoryPath() =>
        _project.RepositoryPath is { Length: > 0 } path && Directory.Exists(path) ? path : null;

    private void Prepare(Func<TmuxOperation> build)
    {
        TmuxOperation operation;
        try
        {
            operation = build();
        }
        catch (ArgumentException ex)
        {
            LastResultWasError = true;
            LastResultText = ex.Message;
            return;
        }

        _pending = operation;
        PreviewTitle = TitleFor(operation);
        PreviewDescription = operation.Description;
        PreviewCommand = operation.Command;
        IsPreviewDanger = operation.RequiresConfirmation;
        IsPreviewOpen = true;
    }

    [RelayCommand]
    private void CancelPreview()
    {
        _pending = null;
        IsPreviewOpen = false;
    }

    /// <summary>Executes exactly the operation shown in the preview panel.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmOperation()
    {
        var operation = _pending;
        if (operation is null)
            return;

        _pending = null;
        IsPreviewOpen = false;
        IsBusy = true;
        BusyText = $"{TitleFor(operation)}…";
        try
        {
            var result = await _tmux.ExecuteAsync(operation, confirmed: true);
            var title = TitleFor(operation);
            LastResultWasError = !result.Succeeded;
            LastResultText = result.WasBlocked
                ? $"Blocked: {result.Output}"
                : result.Succeeded
                    ? $"{title}: OK"
                    : $"{title}: failed{(result.ExitCode is { } code ? $" (exit {code})" : "")} — {result.Output.Trim()}";

            Journal(operation, result);

            if (result.Succeeded)
                await Refresh();
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>Opens a terminal attached to this project's session. Not a mutation.</summary>
    [RelayCommand(CanExecute = nameof(CanAttach))]
    private void Attach()
    {
        var directory = ExistingRepositoryPath()
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!_terminal.Open(directory, TmuxCommandBuilder.AttachCommand(SessionName)))
        {
            LastResultWasError = true;
            LastResultText = "No terminal emulator found (set the TERMINAL environment variable).";
        }
    }

    private void Journal(TmuxOperation operation, TmuxOperationResult result)
    {
        _timeline?.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Timestamp = DateTimeOffset.Now,
            Category = TimelineCategory.Run,
            Message = $"tmux {TitleFor(operation)}: {(result.Succeeded ? "OK" : result.WasBlocked ? "blocked" : "failed")}",
            Detail = result.Succeeded ? null : result.Output.Trim(),
        });
    }

    private string TitleFor(TmuxOperation operation) => operation.Kind switch
    {
        TmuxOperationKind.CreateSession => $"Create '{operation.SessionName}'",
        TmuxOperationKind.RenameSession => $"Rename '{operation.SessionName}'",
        TmuxOperationKind.KillSession => $"Kill '{operation.SessionName}'",
        _ => operation.Kind.ToString(),
    };
}
