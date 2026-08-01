using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// The dashboard's Development Sessions panel: every tmux session with its
/// attached/detached state, plus explicit create/attach/rename/kill actions.
/// Only the inspection is automatic (read-only, like the dashboard's git and
/// docker probes); every mutation previews the exact tmux command, and
/// kill/rename additionally require Confirm — enforced again in
/// <see cref="ITmuxService.ExecuteAsync"/>.
/// </summary>
public sealed partial class TmuxSessionsViewModel : ViewModelBase
{
    private readonly ITmuxService _tmux;
    private readonly ITerminalLauncher _terminal;
    private TmuxOperationKind _pendingKind;
    private string _pendingSession = string.Empty;

    public ObservableCollection<TmuxSessionViewModel> Sessions { get; } = [];

    [ObservableProperty] private string _statusText = "Inspecting tmux…";
    [ObservableProperty] private bool _hasSessions;
    [ObservableProperty] private bool _tmuxUsable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmOperationCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _busyText = string.Empty;

    // Create row.
    [ObservableProperty] private string _newSessionName = string.Empty;

    // Preview/confirm panel: no mutating command runs before Confirm.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmOperationCommand))]
    private bool _isPreviewOpen;
    [ObservableProperty] private string _previewTitle = string.Empty;
    [ObservableProperty] private string _previewDescription = string.Empty;
    [ObservableProperty] private string _previewCommand = string.Empty;
    [ObservableProperty] private bool _isPreviewDanger;
    [ObservableProperty] private bool _previewShowsNewName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewNameInvalid))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmOperationCommand))]
    private string _previewNewName = string.Empty;

    /// <summary>Rename validation feedback: set when the textbox holds an unusable name.</summary>
    public bool IsNewNameInvalid =>
        IsPreviewOpen && PreviewShowsNewName && !TmuxCommandBuilder.IsValidSessionName(PreviewNewName);

    [ObservableProperty] private string _lastResultText = string.Empty;
    [ObservableProperty] private bool _lastResultWasError;

    public TmuxSessionsViewModel(ITmuxService tmux, ITerminalLauncher terminal)
    {
        _tmux = tmux;
        _terminal = terminal;
        _ = Refresh();
    }

    private bool CanAct => !IsBusy;
    private bool CanConfirm => IsPreviewOpen && !IsBusy && !IsNewNameInvalid;

    /// <summary>Read-only re-inspection of the tmux server.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    public async Task Refresh()
    {
        IsBusy = true;
        BusyText = "Inspecting…";
        try
        {
            var status = await _tmux.InspectAsync();
            TmuxUsable = status.TmuxAvailable;
            StatusText = !status.TmuxAvailable
                ? status.Error ?? "tmux is not available on this machine."
                : !status.ServerRunning
                    ? status.Error ?? "No tmux server running — no sessions yet."
                    : status.Sessions.Count == 0
                        ? "tmux server running, no sessions."
                        : $"{status.Sessions.Count} session{(status.Sessions.Count == 1 ? "" : "s")}";

            Sessions.Clear();
            foreach (var session in status.Sessions)
                Sessions.Add(new TmuxSessionViewModel(session, this));
            HasSessions = Sessions.Count > 0;
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareCreate()
    {
        if (!TmuxCommandBuilder.IsValidSessionName(NewSessionName))
        {
            LastResultWasError = true;
            LastResultText = "Invalid session name — letters, digits, '_' and '-' only.";
            return;
        }

        TmuxOperation operation;
        try
        {
            operation = TmuxCommandBuilder.BuildCreate(NewSessionName);
        }
        catch (ArgumentException ex)
        {
            LastResultWasError = true;
            LastResultText = ex.Message;
            return;
        }

        OpenPreview(TmuxOperationKind.CreateSession, operation.SessionName, operation);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareRename(string session)
    {
        PreviewNewName = session;
        _pendingKind = TmuxOperationKind.RenameSession;
        _pendingSession = session;
        PreviewTitle = $"Rename '{session}'";
        PreviewDescription = $"Rename session '{session}'. Anything attached keeps running.";
        PreviewShowsNewName = true;
        IsPreviewDanger = true;
        UpdateRenamePreviewCommand();
        IsPreviewOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareKill(string session)
    {
        try
        {
            OpenPreview(TmuxOperationKind.KillSession, session, TmuxCommandBuilder.BuildKill(session));
        }
        catch (ArgumentException ex)
        {
            LastResultWasError = true;
            LastResultText = ex.Message;
        }
    }

    private void OpenPreview(TmuxOperationKind kind, string session, TmuxOperation operation)
    {
        _pendingKind = kind;
        _pendingSession = session;
        PreviewTitle = operation.Kind == TmuxOperationKind.KillSession
            ? $"Kill '{session}'"
            : $"Create '{operation.SessionName}'";
        PreviewDescription = operation.Description;
        PreviewCommand = operation.Command;
        PreviewShowsNewName = false;
        IsPreviewDanger = operation.RequiresConfirmation;
        IsPreviewOpen = true;
    }

    partial void OnPreviewNewNameChanged(string value)
    {
        if (_pendingKind == TmuxOperationKind.RenameSession)
            UpdateRenamePreviewCommand();
    }

    /// <summary>The rename preview always shows the exact command the current input would run.</summary>
    private void UpdateRenamePreviewCommand() =>
        PreviewCommand = TmuxCommandBuilder.IsValidSessionName(PreviewNewName)
            ? $"tmux rename-session -t {_pendingSession} {PreviewNewName}"
            : "(enter a valid new name — letters, digits, '_' and '-')";

    [RelayCommand]
    private void CancelPreview() => IsPreviewOpen = false;

    /// <summary>Executes exactly the operation shown in the preview panel.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmOperation()
    {
        TmuxOperation operation;
        try
        {
            operation = _pendingKind switch
            {
                TmuxOperationKind.CreateSession => TmuxCommandBuilder.BuildCreate(_pendingSession),
                TmuxOperationKind.RenameSession => TmuxCommandBuilder.BuildRename(_pendingSession, PreviewNewName),
                TmuxOperationKind.KillSession => TmuxCommandBuilder.BuildKill(_pendingSession),
                _ => throw new InvalidOperationException("No pending operation."),
            };
        }
        catch (ArgumentException ex)
        {
            LastResultWasError = true;
            LastResultText = ex.Message;
            IsPreviewOpen = false;
            return;
        }

        IsPreviewOpen = false;
        IsBusy = true;
        BusyText = $"{PreviewTitle}…";
        try
        {
            var result = await _tmux.ExecuteAsync(operation, confirmed: true);
            LastResultWasError = !result.Succeeded;
            LastResultText = result.WasBlocked
                ? $"Blocked: {result.Output}"
                : result.Succeeded
                    ? $"{PreviewTitle}: OK"
                    : $"{PreviewTitle}: failed{(result.ExitCode is { } code ? $" (exit {code})" : "")} — {result.Output.Trim()}";

            if (result.Succeeded)
            {
                if (operation.Kind == TmuxOperationKind.CreateSession)
                    NewSessionName = string.Empty;
                await Refresh();
            }
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>Opens a terminal attached to the session. Not a mutation.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Attach(string session)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!_terminal.Open(home, TmuxCommandBuilder.AttachCommand(session)))
        {
            LastResultWasError = true;
            LastResultText = "No terminal emulator found (set the TERMINAL environment variable).";
        }
    }
}

/// <summary>One tmux session row: typed status plus the safe actions for it.</summary>
public sealed class TmuxSessionViewModel
{
    private readonly TmuxSession _session;

    public TmuxSessionViewModel(TmuxSession session, TmuxSessionsViewModel parent)
    {
        _session = session;
        AttachCommand = parent.AttachCommand;
        RenameCommand = parent.PrepareRenameCommand;
        KillCommand = parent.PrepareKillCommand;
    }

    public string Name => _session.Name;
    public bool IsAttached => _session.IsAttached;
    public string StateText => _session.IsAttached ? "attached" : "detached";
    public string CreatedText => _session.Created is { } c ? c.LocalDateTime.ToString("MM-dd HH:mm") : "—";

    public string DetailText =>
        $"{_session.WindowCount} window{(_session.WindowCount == 1 ? "" : "s")}" +
        (_session.ActiveWindowName is { } window ? $" · {window}" : string.Empty);

    public IRelayCommand<string> AttachCommand { get; }
    public IRelayCommand<string> RenameCommand { get; }
    public IRelayCommand<string> KillCommand { get; }
}
