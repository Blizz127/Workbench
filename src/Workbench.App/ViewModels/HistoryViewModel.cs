using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// Build, run, test, matching-build, and custom-command history — plus the
/// buttons that create new entries by actually executing the project's
/// configured commands. Build/Run/Test use the original append-only tables;
/// matching-build and custom commands use the newer unified command_runs
/// table (see <see cref="CommandRunnerService"/>).
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;

    public ObservableCollection<BuildRecordViewModel> Builds { get; } = [];
    public ObservableCollection<TestRecordViewModel> Tests { get; } = [];
    public ObservableCollection<CommandRunViewModel> NamedCommandRuns { get; } = [];
    public ObservableCollection<CommandButtonViewModel> AvailableCommands { get; } = [];
    public ObservableCollection<LogFindingViewModel> LogFindings { get; } = [];

    [ObservableProperty] private BuildRecordViewModel? _selectedBuild;
    [ObservableProperty] private TestRecordViewModel? _selectedTest;
    [ObservableProperty] private CommandRunViewModel? _selectedCommandRun;
    [ObservableProperty] private string _selectedLog = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunBuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunTestsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    private bool _isRunning;

    private CancellationTokenSource? _runCts;

    [ObservableProperty] private string _runStatus = string.Empty;

    // Add-custom-command form
    [ObservableProperty] private string _newCommandName = string.Empty;
    [ObservableProperty] private string _newCommandText = string.Empty;

    public bool CanRunBuild => !IsRunning && !string.IsNullOrWhiteSpace(_project.BuildCommand);
    public bool CanRunRun => !IsRunning && !string.IsNullOrWhiteSpace(_project.RunCommand);
    public bool CanRunTests => !IsRunning && !string.IsNullOrWhiteSpace(_project.TestCommand);

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

        NamedCommandRuns.Clear();
        foreach (var record in _services.CommandRuns.GetForProject(_project.Id, 100))
            NamedCommandRuns.Add(new CommandRunViewModel(record));

        ReloadAvailableCommands();
    }

    private void ReloadAvailableCommands()
    {
        AvailableCommands.Clear();

        if (!string.IsNullOrWhiteSpace(_project.MatchingBuildCommand))
        {
            AvailableCommands.Add(new CommandButtonViewModel(
                "matching-build", _project.MatchingBuildCommand, this));
        }

        foreach (var custom in _services.ProjectCommands.GetForProject(_project.Id))
            AvailableCommands.Add(new CommandButtonViewModel(custom.Name, custom.CommandText, this));
    }

    // List queries no longer carry output logs; fetch the full log for the
    // selected record only (an indexed primary-key lookup).
    partial void OnSelectedBuildChanged(BuildRecordViewModel? value)
    {
        if (value is not null)
        {
            SelectedTest = null;
            SelectedCommandRun = null;
            SelectedLog = _services.Builds.GetOutputLog(value.Record.Id) ?? string.Empty;
        }
    }

    partial void OnSelectedTestChanged(TestRecordViewModel? value)
    {
        if (value is not null)
        {
            SelectedBuild = null;
            SelectedCommandRun = null;
            SelectedLog = _services.Tests.GetOutputLog(value.Record.Id) ?? string.Empty;
        }
    }

    partial void OnSelectedCommandRunChanged(CommandRunViewModel? value)
    {
        if (value is not null)
        {
            SelectedBuild = null;
            SelectedTest = null;
            SelectedLog = _services.CommandRuns.GetOutputLog(value.Record.Id) ?? string.Empty;
        }
    }

    // Whichever history table the log came from, scan it for known failure
    // signatures — generic toolchain/crash patterns plus this project's own.
    partial void OnSelectedLogChanged(string value)
    {
        LogFindings.Clear();
        foreach (var finding in _services.LogPatterns.Analyze(value, _project.CustomLogPatterns))
            LogFindings.Add(new LogFindingViewModel(finding));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunBuild));
        OnPropertyChanged(nameof(CanRunRun));
        OnPropertyChanged(nameof(CanRunTests));

        foreach (var button in AvailableCommands)
            button.IsRunning = value;
    }

    [RelayCommand(CanExecute = nameof(CanRunBuild))]
    private async Task RunBuild() => await Execute(
        token => _services.Execution.RunBuildAsync(_project, ExecutionKind.Build, cancellationToken: token),
        "Building…");

    [RelayCommand(CanExecute = nameof(CanRunRun))]
    private async Task RunRun() => await Execute(
        token => _services.Execution.RunBuildAsync(_project, ExecutionKind.Run, cancellationToken: token),
        "Running…");

    [RelayCommand(CanExecute = nameof(CanRunTests))]
    private async Task RunTests() => await Execute(
        token => _services.Execution.RunTestsAsync(_project, cancellationToken: token),
        "Testing…");

    /// <summary>Runs a matching-build or custom command by name; invoked from a <see cref="CommandButtonViewModel"/>.</summary>
    internal async Task RunNamedCommand(string name, string commandText)
    {
        if (IsRunning)
            return;

        await Execute(
            async token => (CommandRun?)await _services.CommandRunner.RunAsync(_project, name, commandText, cancellationToken: token),
            $"Running {name}…");
    }

    [RelayCommand]
    private void AddCustomCommand()
    {
        var name = NewCommandName.Trim();
        var text = NewCommandText.Trim();
        if (name.Length == 0 || text.Length == 0)
            return;

        _services.ProjectCommands.Add(new ProjectCommand
        {
            ProjectId = _project.Id,
            Name = name,
            CommandText = text,
            SortOrder = AvailableCommands.Count,
        });

        NewCommandName = string.Empty;
        NewCommandText = string.Empty;
        ReloadAvailableCommands();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void CancelRun()
    {
        RunStatus = "Cancelling…";
        _runCts?.Cancel();
    }

    private async Task Execute<T>(Func<CancellationToken, Task<T?>> action, string status) where T : class
    {
        using var cts = new CancellationTokenSource();
        _runCts = cts;
        IsRunning = true;
        RunStatus = status;
        try
        {
            // ProcessRunner never throws on cancellation — it returns a
            // Cancelled result that gets recorded like any other run.
            await Task.Run(() => action(cts.Token));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _runCts = null;
                IsRunning = false;
                RunStatus = string.Empty;
                Reload();
            });
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

public sealed class CommandRunViewModel(CommandRun record)
{
    public CommandRun Record { get; } = record;

    public string Name => Record.CommandName;
    public string StartedAt => Record.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string Command => Record.CommandText;
    public string Outcome => Record.Cancelled
        ? "cancelled"
        : Record.ExitCode is null
            ? "no exit code"
            : Record.Succeeded ? "OK" : $"exit {Record.ExitCode}";
    public bool Succeeded => Record.Succeeded;
    public string Duration => $"{Record.Duration.TotalSeconds:0.#}s";
    public string Commit => Record.GitHeadSha is { Length: >= 7 } sha ? sha[..7] : "—";
    public bool RegressionDetected => Record.RegressionDetected;
}

/// <summary>One button for a matching-build or custom command, on the History tab.</summary>
public sealed partial class CommandButtonViewModel(string name, string commandText, HistoryViewModel owner) : ViewModelBase
{
    public string Name { get; } = name;
    public string CommandText { get; } = commandText;

    [ObservableProperty] private bool _isRunning;

    [RelayCommand]
    private async Task Run() => await owner.RunNamedCommand(Name, CommandText);
}

/// <summary>One log-pattern hit, shown under the selected output log.</summary>
public sealed class LogFindingViewModel(LogPatternMatch match)
{
    public int LineNumber => match.LineNumber;
    public string Category => match.Category;
    public string PatternName => match.PatternName;
    public string LineText => match.LineText;
}
