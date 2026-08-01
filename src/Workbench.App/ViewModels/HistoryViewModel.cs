using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// Build, run, and test history — plus the buttons that create new entries by
/// actually executing the project's configured commands, and the operations
/// panel (per-service lights, live metrics, stop/logs/update/shell actions).
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;
    private CancellationTokenSource? _runCts;
    private Task? _activeRun;
    private int _metricsInFlight;

    public ObservableCollection<BuildRecordViewModel> Builds { get; } = [];
    public ObservableCollection<TestRecordViewModel> Tests { get; } = [];

    /// <summary>Per-service status lights, parsed from the latest health assessment's output.</summary>
    public ObservableCollection<ServiceStateViewModel> Services { get; } = [];

    /// <summary>Live metric tiles (players, CPU, RAM, ...) from the metrics command.</summary>
    public ObservableCollection<MetricViewModel> Metrics { get; } = [];

    [ObservableProperty] private bool _hasServices;
    [ObservableProperty] private bool _hasMetrics;

    /// <summary>Branch · short commit of the repository HEAD, refreshed with every reload.</summary>
    [ObservableProperty] private string _gitInfoText = string.Empty;
    public bool HasGitInfo => GitInfoText.Length > 0;

    [ObservableProperty] private BuildRecordViewModel? _selectedBuild;
    [ObservableProperty] private TestRecordViewModel? _selectedTest;
    [ObservableProperty] private string _selectedLog = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunBuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunTestsCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecheckHealthCommand))]
    [NotifyCanExecuteChangedFor(nameof(ViewFailedServiceLogsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenLogsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunUpdateCommand))]
    private bool _isRunning;

    [ObservableProperty] private string _runStatus = string.Empty;

    // Launch command outcome and runtime health are tracked separately:
    // exit 0 from a detached launcher says nothing about service health.
    [ObservableProperty] private string _commandResultText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuntimeHealthText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private RuntimeHealth _currentHealth = RuntimeHealth.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailedComponentText))]
    [NotifyPropertyChangedFor(nameof(HasFailedComponent))]
    [NotifyCanExecuteChangedFor(nameof(ViewFailedServiceLogsCommand))]
    private string? _failedComponent;

    public string RuntimeHealthText => $"Runtime health: {CurrentHealth}";
    public string? FailedComponentText => FailedComponent is null ? null : $"Failed component: {FailedComponent}";
    public bool HasFailedComponent => FailedComponent is not null;

    public bool CanRunBuild => !IsRunning && !string.IsNullOrWhiteSpace(_project.BuildCommand);
    public bool CanRunRun => !IsRunning && !string.IsNullOrWhiteSpace(_project.RunCommand);
    public bool CanRunTests => !IsRunning && !string.IsNullOrWhiteSpace(_project.TestCommand);
    public bool CanStop => IsRunning || HasStopCommand
        || CurrentHealth is RuntimeHealth.Starting or RuntimeHealth.Healthy;
    public bool CanRestartRun => !string.IsNullOrWhiteSpace(_project.RunCommand);
    public bool CanRecheckHealth => !IsRunning && !string.IsNullOrWhiteSpace(_project.Launch.StatusCommand);
    public bool CanViewFailedServiceLogs =>
        !IsRunning && HasFailedComponent && !string.IsNullOrWhiteSpace(_project.Launch.ServiceLogCommand);
    public bool CanOpenLogs => !IsRunning && !string.IsNullOrWhiteSpace(_project.Launch.LogsCommand);
    public bool CanRunUpdate => !IsRunning && !string.IsNullOrWhiteSpace(_project.Launch.UpdateCommand);
    public bool CanViewServiceLogs => !string.IsNullOrWhiteSpace(_project.Launch.ServiceLogCommand);

    private bool HasStopCommand => !string.IsNullOrWhiteSpace(_project.Launch.StopCommand);

    /// <summary>Running-state badge text for the Run section.</summary>
    public string RunStateText => IsRunning ? "running" : "idle";

    // Exact command previews for the launch-profile cards.
    public string BuildCommandText => CommandText(_project.BuildCommand);
    public string RunCommandText => CommandText(_project.RunCommand);
    public string TestCommandText => CommandText(_project.TestCommand);

    private static string CommandText(string? command) =>
        string.IsNullOrWhiteSpace(command) ? "not configured — set it in the Settings section" : command;

    // Last outcome per launch profile, recomputed by Reload().
    [ObservableProperty] private string _lastBuildSummary = "no builds yet";
    [ObservableProperty] private bool? _lastBuildOutcome;
    [ObservableProperty] private string _lastRunSummary = "no runs yet";
    [ObservableProperty] private bool? _lastRunOutcome;
    [ObservableProperty] private string _lastTestSummary = "no test runs yet";
    [ObservableProperty] private bool? _lastTestOutcome;

    public HistoryViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        Reload();
    }

    private void Reload()
    {
        Builds.Clear();
        foreach (var record in _services.Builds.GetForProject(_project.Id, 100))
            Builds.Add(new BuildRecordViewModel(record));

        Tests.Clear();
        foreach (var record in _services.Tests.GetForProject(_project.Id, 100))
            Tests.Add(new TestRecordViewModel(record));

        RefreshProfileSummaries();
        RefreshGitInfo();
        RefreshMetrics();

        // With nothing explicitly selected, the output panel shows the newest run's log.
        if (SelectedBuild is null && SelectedTest is null)
        {
            var newestBuild = Builds.FirstOrDefault()?.Record;
            var newestTest = Tests.FirstOrDefault()?.Record;
            SelectedLog = (newestBuild, newestTest) switch
            {
                (null, null) => string.Empty,
                ({ } b, null) => b.OutputLog,
                (null, { } t) => t.OutputLog,
                ({ } b, { } t) => b.StartedAt >= t.StartedAt ? b.OutputLog : t.OutputLog,
            };
        }
    }

    private void RefreshProfileSummaries()
    {
        var lastBuild = Builds.FirstOrDefault(b => b.Record.Kind == ExecutionKind.Build)?.Record;
        LastBuildOutcome = lastBuild?.Succeeded;
        LastBuildSummary = lastBuild is null ? "no builds yet" : OutcomeText(lastBuild.Succeeded, lastBuild.StartedAt);

        var lastRun = Builds.FirstOrDefault(b => b.Record.Kind == ExecutionKind.Run)?.Record;
        LastRunOutcome = lastRun?.Succeeded;
        LastRunSummary = lastRun is null ? "no runs yet" : OutcomeText(lastRun.Succeeded, lastRun.StartedAt);
        CommandResultText = lastRun is null ? string.Empty : $"Start command: {CommandOutcomeText(lastRun)}";

        var health = _services.Health.GetCurrent(_project.Id);
        CurrentHealth = health?.Health ?? RuntimeHealth.Unknown;
        FailedComponent = health?.FailedComponent;
        RefreshServices(health);

        var lastTest = Tests.FirstOrDefault()?.Record;
        LastTestOutcome = lastTest is null ? null : lastTest.Result == TestResult.Passed;
        LastTestSummary = lastTest is null
            ? "no test runs yet"
            : $"{lastTest.Result} · {lastTest.StartedAt.LocalDateTime:MM-dd HH:mm}";
    }

    private static string OutcomeText(bool succeeded, DateTimeOffset startedAt) =>
        $"{(succeeded ? "OK" : "FAIL")} · {startedAt.LocalDateTime:MM-dd HH:mm}";

    private static string CommandOutcomeText(BuildRecord record) =>
        record.Succeeded
            ? "Succeeded"
            : record.ExitCode is null
                ? "Cancelled or timed out"
                : $"Failed (exit {record.ExitCode})";

    partial void OnSelectedBuildChanged(BuildRecordViewModel? value)
    {
        if (value is not null)
        {
            SelectedTest = null;
            SelectedLog = value.Record.OutputLog;
        }
    }

    partial void OnSelectedTestChanged(TestRecordViewModel? value)
    {
        if (value is not null)
        {
            SelectedBuild = null;
            SelectedLog = value.Record.OutputLog;
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunBuild));
        OnPropertyChanged(nameof(CanRunRun));
        OnPropertyChanged(nameof(CanRunTests));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanOpenLogs));
        OnPropertyChanged(nameof(CanRunUpdate));
        OnPropertyChanged(nameof(RunStateText));
    }

    partial void OnGitInfoTextChanged(string value) => OnPropertyChanged(nameof(HasGitInfo));

    [RelayCommand(CanExecute = nameof(CanRunBuild))]
    private async Task RunBuild() => await (_activeRun = Execute(
        ct => _services.Execution.RunBuildAsync(_project, ExecutionKind.Build, cancellationToken: ct), "Building…"));

    [RelayCommand(CanExecute = nameof(CanRunRun))]
    private async Task RunRun() => await (_activeRun = Execute(StartRun, "Running…"));

    /// <summary>
    /// Start the runtime, then — when a launch profile is configured — wait
    /// out the grace period and validate post-launch health. A duplicate
    /// start (runtime already tracked as Starting/Healthy) is blocked: a
    /// detached launcher exits long before its services, so the in-flight
    /// flag alone cannot prevent double starts.
    /// </summary>
    private async Task<object?> StartRun(CancellationToken ct)
    {
        if (_services.Health.GetStartBlockReason(_project.Id) is { } blockReason)
        {
            await Dispatcher.UIThread.InvokeAsync(() => CommandResultText = $"Start blocked: {blockReason}");
            return null;
        }

        var record = await _services.Execution.RunBuildAsync(_project, ExecutionKind.Run, cancellationToken: ct);
        if (record is not null)
        {
            var text = $"Start command: {CommandOutcomeText(record)}";
            await Dispatcher.UIThread.InvokeAsync(() => CommandResultText = text);
        }

        if (record is not { Succeeded: true } || ct.IsCancellationRequested
            || string.IsNullOrWhiteSpace(_project.Launch.StatusCommand))
            return record;

        var markedStarting = _services.Health.MarkStarting(_project.Id);
        ApplyHealth(markedStarting);
        try
        {
            var report = await _services.Health.CheckAfterLaunchAsync(_project, ct);
            ApplyHealth(report);
        }
        catch (OperationCanceledException)
        {
            // Stopped while waiting out the grace period / checking.
            ApplyHealth(_services.Health.MarkStopped(_project.Id));
        }

        return record;
    }

    [RelayCommand(CanExecute = nameof(CanRunTests))]
    private async Task RunTests() => await (_activeRun = Execute(
        ct => _services.Execution.RunTestsAsync(_project, cancellationToken: ct), "Testing…"));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task Stop() => StopRuntimeAsync(showOutput: true);

    /// <summary>Stop the runtime (or in-flight run), wait for it to wind down, then start Run again.</summary>
    [RelayCommand(CanExecute = nameof(CanRestartRun))]
    private async Task RestartRun()
    {
        await StopRuntimeAsync(showOutput: false);
        await RunRun();
    }

    /// <summary>
    /// Cancels the in-flight run, then really tears the runtime down when a
    /// stop command is configured — without one, Stop on a detached launcher
    /// can only mean "stop tracking this runtime as up".
    /// </summary>
    private async Task StopRuntimeAsync(bool showOutput)
    {
        if (IsRunning)
        {
            _runCts?.Cancel();
            if (_activeRun is not null)
                await _activeRun;
        }

        if (HasStopCommand)
        {
            string? output = null;
            await (_activeRun = Execute(async ct =>
            {
                var command = _project.Launch.StopCommand!;
                var result = await _services.Runner.RunAsync(
                    command, _project.RepositoryPath, TimeSpan.FromMinutes(5), ct);
                JournalOperation("Stop", result);
                output = result.Output;
                ApplyHealth(_services.Health.MarkStopped(_project.Id));
                return (object?)null;
            }, "Stopping…"));

            if (showOutput && output is not null)
                SelectedLog = output;
        }
        else if (CurrentHealth is RuntimeHealth.Starting or RuntimeHealth.Healthy)
        {
            ApplyHealth(_services.Health.MarkStopped(_project.Id));
        }
    }

    /// <summary>Fetches recent logs across all services into the output panel.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenLogs))]
    private async Task OpenLogs()
    {
        string? output = null;
        await (_activeRun = Execute(async ct =>
        {
            var result = await _services.Runner.RunAsync(
                _project.Launch.LogsCommand!, _project.RepositoryPath, TimeSpan.FromMinutes(2), ct);
            output = result.Output;
            return (object?)null;
        }, "Fetching logs…"));

        if (output is not null)
            SelectedLog = output;
    }

    /// <summary>Fetches one service's logs into the output panel (service-light click).</summary>
    [RelayCommand(CanExecute = nameof(CanViewServiceLogs))]
    private async Task ViewServiceLogs(string service)
    {
        var command = HealthCheckService.BuildServiceLogCommand(_project.Launch, service);
        if (command is null)
            return;

        string? output = null;
        await (_activeRun = Execute(async ct =>
        {
            var result = await _services.Runner.RunAsync(
                command, _project.RepositoryPath, TimeSpan.FromMinutes(1), ct);
            output = result.Output;
            return (object?)null;
        }, $"Fetching {service} logs…"));

        if (output is not null)
            SelectedLog = output;
    }

    /// <summary>One-click update (pull + redeploy), then a health reassessment.</summary>
    [RelayCommand(CanExecute = nameof(CanRunUpdate))]
    private async Task RunUpdate()
    {
        string? output = null;
        await (_activeRun = Execute(async ct =>
        {
            var command = _project.Launch.UpdateCommand!;
            var result = await _services.Runner.RunAsync(
                command, _project.RepositoryPath, TimeSpan.FromMinutes(20), ct);
            JournalOperation("Update", result);
            output = result.Output;
            return (object?)null;
        }, "Updating…"));

        if (output is not null)
            SelectedLog = output;

        if (CanRecheckHealth)
            await RecheckHealth();
    }

    /// <summary>Opens an interactive terminal at the repository root.</summary>
    [RelayCommand]
    private void OpenShell()
    {
        if (_project.RepositoryPath is not { Length: > 0 } path || !Directory.Exists(path))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = path,
                    UseShellExecute = true,
                });
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo { FileName = "open", ArgumentList = { "-a", "Terminal", path } });
                return;
            }

            string?[] candidates =
            [
                Environment.GetEnvironmentVariable("TERMINAL"),
                "x-terminal-emulator", "ptyxis", "gnome-terminal", "konsole",
                "xfce4-terminal", "kitty", "alacritty", "foot",
            ];
            foreach (var terminal in candidates)
            {
                if (string.IsNullOrWhiteSpace(terminal))
                    continue;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = terminal, WorkingDirectory = path });
                    return;
                }
                catch (Exception)
                {
                    // Not installed; try the next candidate.
                }
            }

            RunStatus = "No terminal emulator found (set the TERMINAL environment variable).";
        }
        catch (Exception)
        {
            // The OS refused to open a terminal — nothing sensible to surface here.
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecheckHealth))]
    private async Task RecheckHealth() => await (_activeRun = Execute(async ct =>
    {
        try
        {
            ApplyHealth(await _services.Health.CheckAsync(_project, ct));
        }
        catch (OperationCanceledException)
        {
            // cancelled while checking; keep the last known state
        }

        return (object?)null;
    }, "Checking health…"));

    /// <summary>Runs the configured per-service log command for the failed component.</summary>
    [RelayCommand(CanExecute = nameof(CanViewFailedServiceLogs))]
    private async Task ViewFailedServiceLogs()
    {
        string? output = null;
        await (_activeRun = Execute(async ct =>
        {
            var command = HealthCheckService.BuildServiceLogCommand(_project.Launch, FailedComponent)!;
            var result = await _services.Runner.RunAsync(
                command, _project.RepositoryPath, TimeSpan.FromMinutes(1), ct);
            output = result.Output;
            return (object?)null;
        }, "Fetching service logs…"));

        // Set after Execute's reload, which would otherwise reset the panel
        // to the newest run's log.
        if (output is not null)
            SelectedLog = output;
    }

    /// <summary>Reflects a health assessment on the UI thread.</summary>
    private void ApplyHealth(RuntimeHealthRecord record) =>
        Dispatcher.UIThread.Post(() =>
        {
            CurrentHealth = record.Health;
            FailedComponent = record.FailedComponent;
            RefreshServices(record);
        });

    /// <summary>Rebuilds the per-service lights from a health assessment's raw status output.</summary>
    private void RefreshServices(RuntimeHealthRecord? health)
    {
        Services.Clear();
        var profile = _project.Launch;
        if (profile.RequiredServices.Count + profile.OptionalServices.Count > 0)
        {
            // Before the first real assessment the states are unknown, not down.
            var assessed = health is not null && health.Health is not RuntimeHealth.Unknown;
            foreach (var state in HealthCheckService.GetServiceStates(profile, health?.Output ?? string.Empty))
                Services.Add(new ServiceStateViewModel(state.Name, assessed ? state.IsUp : null, ViewServiceLogsCommand));
        }

        HasServices = Services.Count > 0;
    }

    /// <summary>Reads branch and HEAD commit off the UI thread for the Run card.</summary>
    private void RefreshGitInfo()
    {
        var path = _project.RepositoryPath;
        _ = Task.Run(() =>
        {
            var snapshot = _services.Git.GetSnapshot(path, recentCommitCount: 0);
            var text = snapshot.IsValid
                ? $"{snapshot.BranchName ?? "?"} · {snapshot.LastCommit?.ShortSha ?? "?"}{(snapshot.IsDirty ? " · dirty" : "")}"
                : string.Empty;
            Dispatcher.UIThread.Post(() => GitInfoText = text);
        });
    }

    /// <summary>
    /// Refreshes the metric tiles from the metrics command in the background.
    /// Best-effort and non-blocking: metrics never gate any other action.
    /// </summary>
    private void RefreshMetrics()
    {
        var command = _project.Launch.MetricsCommand;
        if (string.IsNullOrWhiteSpace(command) || Interlocked.Exchange(ref _metricsInFlight, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _services.Runner.RunAsync(
                    command, _project.RepositoryPath, TimeSpan.FromMinutes(1), CancellationToken.None);
                List<MetricViewModel> metrics = result.ExitCode == 0 ? ParseMetrics(result.Output) : [];
                Dispatcher.UIThread.Post(() =>
                {
                    Metrics.Clear();
                    foreach (var metric in metrics)
                        Metrics.Add(metric);
                    HasMetrics = Metrics.Count > 0;
                });
            }
            catch (Exception)
            {
                // Metrics are decorative; a failed read just keeps the last values.
            }
            finally
            {
                Interlocked.Exchange(ref _metricsInFlight, 0);
            }
        });
    }

    /// <summary>Parses <c>Label: value</c> lines from metrics-command output.</summary>
    internal static List<MetricViewModel> ParseMetrics(string output) =>
        output.Split('\n')
            .Select(line => line.Trim())
            .Select(line => (Line: line, Index: line.IndexOf(':')))
            .Where(x => x.Index > 0 && x.Index < x.Line.Length - 1)
            .Select(x => new MetricViewModel(x.Line[..x.Index].Trim(), x.Line[(x.Index + 1)..].Trim()))
            .Where(m => m.Label.Length > 0 && m.Value.Length > 0)
            .ToList();

    /// <summary>Journals a stop/update operation to the timeline (output tail on failure).</summary>
    private void JournalOperation(string label, ProcessResult result)
    {
        _services.Timeline.Add(new TimelineEntry
        {
            ProjectId = _project.Id,
            Timestamp = DateTimeOffset.Now,
            Category = TimelineCategory.Run,
            Message = $"{label} command: {(result.ExitCode == 0 ? "OK" : result.ExitCode is null ? "cancelled or timed out" : $"exit {result.ExitCode}")}",
            Detail = result.ExitCode == 0 ? null : Tail(result.Output),
        });
    }

    private static string Tail(string output, int maxChars = 2000) =>
        output.Length <= maxChars ? output : "…" + output[^maxChars..];

    private async Task Execute<T>(Func<CancellationToken, Task<T?>> action, string status) where T : class
    {
        IsRunning = true;
        RunStatus = status;
        var cts = _runCts = new CancellationTokenSource();
        try
        {
            await Task.Run(() => action(cts.Token));
        }
        finally
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsRunning = false;
                    RunStatus = cts.IsCancellationRequested ? "Stopped by user" : string.Empty;
                    Reload();
                });
            }
            catch (OperationCanceledException)
            {
                // The dispatcher was shut down while this execution finished
                // (window closed mid-run): the UI is gone, nothing to update.
            }
            if (ReferenceEquals(_runCts, cts))
                _runCts = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// Cancels any in-flight execution and waits for it to wind down. Called
    /// while the window is closing so no run (or its process-tree kill) is
    /// left racing application teardown.
    /// </summary>
    public async Task ShutdownAsync()
    {
        _runCts?.Cancel();
        if (_activeRun is { } active)
        {
            try
            {
                await active;
            }
            catch
            {
                // Shutting down; the failure (if any) is already recorded.
            }
        }
    }
}

public sealed class BuildRecordViewModel(BuildRecord record)
{
    public BuildRecord Record { get; } = record;

    public string Kind => Record.Kind.ToString();
    public string StartedAt => Record.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string Command => Record.Command;
    public string Outcome => Record.ExitCode is null
        ? "no exit code"
        : Record.Succeeded ? "OK" : $"exit {Record.ExitCode}";
    public bool Succeeded => Record.Succeeded;
    public string Duration => $"{Record.Duration.TotalSeconds:0.#}s";
    public string Commit => Record.CommitSha is { Length: >= 7 } sha ? sha[..7] : "—";
}

/// <summary>
/// One service light on the Run card. Null <paramref name="isUp"/> means no
/// assessment yet. Carries the parent's per-service log command so XAML never
/// needs ancestor bindings.
/// </summary>
public sealed class ServiceStateViewModel(string name, bool? isUp, IAsyncRelayCommand<string> viewLogs)
{
    public string Name { get; } = name;
    public bool? IsUp { get; } = isUp;
    public string StateText => IsUp switch { true => "up", false => "down", null => "—" };
    public IAsyncRelayCommand<string> ViewLogsCommand { get; } = viewLogs;
}

/// <summary>One metric tile (e.g. Players / CPU / RAM) on the Run card.</summary>
public sealed record MetricViewModel(string Label, string Value);

public sealed class TestRecordViewModel(TestRecord record)
{
    public TestRecord Record { get; } = record;

    public string StartedAt => Record.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string Command => Record.Command;
    public string Result => Record.Result.ToString();
    public bool Passed => Record.Result == TestResult.Passed;
    public string Duration => $"{Record.Duration.TotalSeconds:0.#}s";
    public string Commit => Record.CommitSha is { Length: >= 7 } sha ? sha[..7] : "—";
    public string Notes => Record.Notes ?? string.Empty;
}
