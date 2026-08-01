using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// Build, run, and test history — plus the buttons that create new entries by
/// actually executing the project's configured commands.
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;
    private CancellationTokenSource? _runCts;
    private Task? _activeRun;

    public ObservableCollection<BuildRecordViewModel> Builds { get; } = [];
    public ObservableCollection<TestRecordViewModel> Tests { get; } = [];

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
    public bool CanStop => IsRunning || CurrentHealth is RuntimeHealth.Starting or RuntimeHealth.Healthy;
    public bool CanRestartRun => !string.IsNullOrWhiteSpace(_project.RunCommand);
    public bool CanRecheckHealth => !IsRunning && !string.IsNullOrWhiteSpace(_project.Launch.StatusCommand);
    public bool CanViewFailedServiceLogs =>
        !IsRunning && HasFailedComponent && !string.IsNullOrWhiteSpace(_project.Launch.ServiceLogCommand);

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
        OnPropertyChanged(nameof(RunStateText));
    }

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
    private void Stop()
    {
        _runCts?.Cancel();
        // Not running means the launcher already exited (detached start):
        // Stop then means "stop tracking this runtime as up".
        if (!IsRunning && CurrentHealth is RuntimeHealth.Starting or RuntimeHealth.Healthy)
            ApplyHealth(_services.Health.MarkStopped(_project.Id));
    }

    /// <summary>Stop the in-flight run (if any), wait for it to wind down, then start Run again.</summary>
    [RelayCommand(CanExecute = nameof(CanRestartRun))]
    private async Task RestartRun()
    {
        if (IsRunning)
        {
            _runCts?.Cancel();
            if (_activeRun is not null)
                await _activeRun;
        }
        else if (CurrentHealth is RuntimeHealth.Starting or RuntimeHealth.Healthy)
        {
            // Detached runtime tracked as up; clear the duplicate-start guard first.
            ApplyHealth(_services.Health.MarkStopped(_project.Id));
        }

        await RunRun();
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
        });

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
