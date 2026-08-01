using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// Docker Operations section: read-only compose inspection plus safe,
/// explicitly user-triggered actions. Every mutating action opens a preview
/// panel showing the exact command; Stop/Restart additionally require the
/// Confirm button (enforced again in <see cref="IDockerComposeService"/>).
/// Nothing here runs at startup except the read-only inspection. All output
/// shown or journaled has already been secret-redacted by the service.
/// </summary>
public sealed partial class DockerOperationsViewModel : ViewModelBase
{
    private readonly IDockerComposeService _docker;
    private readonly Project _project;
    private readonly ITimelineRepository? _timeline;
    private ComposeOperation? _pending;

    public ObservableCollection<ComposeServiceViewModel> Services { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoComposeFile))]
    private bool _hasComposeFile;

    public bool HasNoComposeFile => !HasComposeFile;

    [ObservableProperty] private string _composeFileText = string.Empty;
    [ObservableProperty] private string _projectNameText = string.Empty;
    [ObservableProperty] private string _summaryText = "not inspected yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    public bool HasError => ErrorText is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareStartAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareStopAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareRestartAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(ViewAllLogsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmOperationCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _busyText = string.Empty;

    // Preview/confirm panel: no mutating command runs before Confirm.
    [ObservableProperty] private bool _isPreviewOpen;
    [ObservableProperty] private string _previewTitle = string.Empty;
    [ObservableProperty] private string _previewDescription = string.Empty;
    [ObservableProperty] private string _previewCommand = string.Empty;
    [ObservableProperty] private bool _isPreviewDanger;

    // Logs panel.
    [ObservableProperty] private bool _isLogOpen;
    [ObservableProperty] private string _logTitle = string.Empty;
    [ObservableProperty] private string _logText = string.Empty;

    [ObservableProperty] private string _lastResultText = string.Empty;
    [ObservableProperty] private bool _lastResultWasError;

    public DockerOperationsViewModel(IDockerComposeService docker, Project project, ITimelineRepository? timeline = null)
    {
        _docker = docker;
        _project = project;
        _timeline = timeline;

        var file = docker.ResolveComposeFile(project);
        HasComposeFile = file is not null;
        ComposeFileText = file ?? string.Empty;
        if (HasComposeFile)
            _ = Refresh();
    }

    private bool CanAct => !IsBusy && HasComposeFile;
    private bool CanConfirm => IsPreviewOpen && !IsBusy;

    /// <summary>Read-only re-inspection of the compose project.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    public async Task Refresh()
    {
        IsBusy = true;
        BusyText = "Inspecting…";
        try
        {
            var status = await _docker.InspectAsync(_project);
            if (status is null)
            {
                HasComposeFile = false;
                return;
            }

            ErrorText = status.Error;
            ProjectNameText = status.ProjectName ?? "—";
            SummaryText = status.Error is null ? status.Summary : "inspection failed";

            Services.Clear();
            foreach (var service in status.Services)
                Services.Add(new ComposeServiceViewModel(service, this));
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareStartAll() => Prepare(ComposeOperationKind.StartAll);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareStopAll() => Prepare(ComposeOperationKind.StopAll);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareRestartAll() => Prepare(ComposeOperationKind.RestartAll);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareStartService(string service) => Prepare(ComposeOperationKind.StartService, service);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareStopService(string service) => Prepare(ComposeOperationKind.StopService, service);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PrepareRestartService(string service) => Prepare(ComposeOperationKind.RestartService, service);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ViewAllLogs() => ViewLogs(ComposeOperationKind.LogsAll, "Logs — all services");

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ViewServiceLogs(string service) => ViewLogs(ComposeOperationKind.LogsService, $"Logs — {service}", service);

    /// <summary>Opens the preview panel with the exact command; nothing runs yet.</summary>
    private void Prepare(ComposeOperationKind kind, string? service = null)
    {
        if (!TryBuild(kind, service, out var operation))
            return;

        _pending = operation;
        PreviewTitle = TitleFor(kind, service);
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
        BusyText = $"{TitleFor(operation.Kind, operation.ServiceName)}…";
        try
        {
            var result = await _docker.ExecuteAsync(_project, operation, confirmed: true);
            ReportResult(result);
            if (!result.Succeeded && result.Output.Length > 0)
                OpenLog(TitleFor(operation.Kind, operation.ServiceName), result.Output);

            if (result.Succeeded)
                await Refresh();
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    /// <summary>Read-only logs fetch; output arrives already secret-redacted.</summary>
    private async Task ViewLogs(ComposeOperationKind kind, string title, string? service = null)
    {
        if (!TryBuild(kind, service, out var operation))
            return;

        IsBusy = true;
        BusyText = "Fetching logs…";
        try
        {
            var result = await _docker.ExecuteAsync(_project, operation);
            if (result.Succeeded)
            {
                OpenLog(title, result.Output.Length > 0 ? result.Output : "(no log output)");
            }
            else
            {
                LastResultWasError = true;
                LastResultText = "Fetching logs failed.";
                if (result.Output.Length > 0)
                    OpenLog(title, result.Output);
            }
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    private void OpenLog(string title, string text)
    {
        LogTitle = title;
        LogText = text;
        IsLogOpen = true;
    }

    [RelayCommand]
    private void CloseLog() => IsLogOpen = false;

    private bool TryBuild(ComposeOperationKind kind, string? service, out ComposeOperation operation)
    {
        operation = null!;
        var composeFile = _docker.ResolveComposeFile(_project);
        if (composeFile is null)
        {
            HasComposeFile = false;
            return false;
        }

        try
        {
            operation = ComposeCommandBuilder.Build(kind, composeFile, service);
            return true;
        }
        catch (ArgumentException ex)
        {
            LastResultWasError = true;
            LastResultText = ex.Message;
            return false;
        }
    }

    private void ReportResult(ComposeOperationResult result)
    {
        var label = TitleFor(result.Operation.Kind, result.Operation.ServiceName);
        LastResultWasError = !result.Succeeded;
        LastResultText = result.WasBlocked
            ? $"Blocked: {result.Output}"
            : result.Succeeded
                ? $"{label}: OK"
                : $"{label}: failed{(result.ExitCode is { } code ? $" (exit {code})" : " — cancelled or timed out")}";

        _timeline?.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Timestamp = DateTimeOffset.Now,
            Category = TimelineCategory.Run,
            Message = $"Docker {label}: {(result.Succeeded ? "OK" : result.WasBlocked ? "blocked" : "failed")}",
            Detail = result.Succeeded ? null : Tail(result.Output),
        });
    }

    private static string TitleFor(ComposeOperationKind kind, string? service) => kind switch
    {
        ComposeOperationKind.StartAll => "Start all",
        ComposeOperationKind.StopAll => "Stop all",
        ComposeOperationKind.RestartAll => "Restart all",
        ComposeOperationKind.StartService => $"Start '{service}'",
        ComposeOperationKind.StopService => $"Stop '{service}'",
        ComposeOperationKind.RestartService => $"Restart '{service}'",
        ComposeOperationKind.LogsAll => "Logs — all services",
        ComposeOperationKind.LogsService => $"Logs — {service}",
        _ => kind.ToString(),
    };

    private static string Tail(string output, int maxChars = 2000) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];
}

/// <summary>
/// One service row in the Docker Operations section: typed status plus the
/// safe actions valid for that status. Action availability reflects the
/// service state (e.g. Stop only while running); the parent's busy flag
/// disables every action while another one is in flight.
/// </summary>
public sealed class ComposeServiceViewModel : ViewModelBase
{
    private readonly ComposeServiceStatus _status;

    public ComposeServiceViewModel(ComposeServiceStatus status, DockerOperationsViewModel parent)
    {
        _status = status;
        StartCommand = parent.PrepareStartServiceCommand;
        StopCommand = parent.PrepareStopServiceCommand;
        RestartCommand = parent.PrepareRestartServiceCommand;
        LogsCommand = parent.ViewServiceLogsCommand;
    }

    public string Name => _status.Name;
    public string StatusText => _status.StatusText;

    public string StateText => _status.State switch
    {
        ComposeServiceState.Running => "running",
        ComposeServiceState.Paused => "paused",
        ComposeServiceState.Restarting => "restarting",
        ComposeServiceState.Created => "created",
        ComposeServiceState.Completed => "completed",
        ComposeServiceState.Failed => "failed",
        _ => "unknown",
    };

    public string? HealthText => _status.Health switch
    {
        ComposeHealth.Healthy => "healthy",
        ComposeHealth.Unhealthy => "unhealthy",
        ComposeHealth.Starting => "starting",
        _ => null,
    };

    public bool HasHealth => HealthText is not null;
    public bool IsHealthy => _status.Health == ComposeHealth.Healthy;
    public bool IsUnhealthy => _status.Health == ComposeHealth.Unhealthy;
    public bool IsRunning => _status.IsRunning;
    public bool IsCompleted => _status.State == ComposeServiceState.Completed;
    public bool IsFailed => _status.State is ComposeServiceState.Failed or ComposeServiceState.Restarting;

    // Start re-runs a completed one-shot service (e.g. db-update) too.
    public bool CanStart => !_status.IsRunning && _status.State is not ComposeServiceState.Restarting;
    public bool CanStop => _status.IsRunning || _status.State == ComposeServiceState.Restarting;
    public bool CanRestart => CanStop;

    public IRelayCommand<string> StartCommand { get; }
    public IRelayCommand<string> StopCommand { get; }
    public IRelayCommand<string> RestartCommand { get; }
    public IAsyncRelayCommand<string> LogsCommand { get; }
}
