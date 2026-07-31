using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

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
    private bool _isRunning;

    [ObservableProperty] private string _runStatus = string.Empty;

    public bool CanRunBuild => !IsRunning && !string.IsNullOrWhiteSpace(_project.BuildCommand);
    public bool CanRunRun => !IsRunning && !string.IsNullOrWhiteSpace(_project.RunCommand);
    public bool CanRunTests => !IsRunning && !string.IsNullOrWhiteSpace(_project.TestCommand);
    public bool CanStop => IsRunning;
    public bool CanRestartRun => !string.IsNullOrWhiteSpace(_project.RunCommand);

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

        var lastTest = Tests.FirstOrDefault()?.Record;
        LastTestOutcome = lastTest is null ? null : lastTest.Result == TestResult.Passed;
        LastTestSummary = lastTest is null
            ? "no test runs yet"
            : $"{lastTest.Result} · {lastTest.StartedAt.LocalDateTime:MM-dd HH:mm}";
    }

    private static string OutcomeText(bool succeeded, DateTimeOffset startedAt) =>
        $"{(succeeded ? "OK" : "FAIL")} · {startedAt.LocalDateTime:MM-dd HH:mm}";

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
    private async Task RunRun() => await (_activeRun = Execute(
        ct => _services.Execution.RunBuildAsync(_project, ExecutionKind.Run, cancellationToken: ct), "Running…"));

    [RelayCommand(CanExecute = nameof(CanRunTests))]
    private async Task RunTests() => await (_activeRun = Execute(
        ct => _services.Execution.RunTestsAsync(_project, cancellationToken: ct), "Testing…"));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _runCts?.Cancel();

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

        await RunRun();
    }

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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsRunning = false;
                RunStatus = cts.IsCancellationRequested ? "Stopped by user" : string.Empty;
                Reload();
            });
            if (ReferenceEquals(_runCts, cts))
                _runCts = null;
            cts.Dispose();
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
