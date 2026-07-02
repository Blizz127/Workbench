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

    public ObservableCollection<BuildRecordViewModel> Builds { get; } = [];
    public ObservableCollection<TestRecordViewModel> Tests { get; } = [];

    [ObservableProperty] private BuildRecordViewModel? _selectedBuild;
    [ObservableProperty] private TestRecordViewModel? _selectedTest;
    [ObservableProperty] private string _selectedLog = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunBuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunTestsCommand))]
    private bool _isRunning;

    [ObservableProperty] private string _runStatus = string.Empty;

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
    }

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
    }

    [RelayCommand(CanExecute = nameof(CanRunBuild))]
    private async Task RunBuild() => await Execute(
        () => _services.Execution.RunBuildAsync(_project, ExecutionKind.Build), "Building…");

    [RelayCommand(CanExecute = nameof(CanRunRun))]
    private async Task RunRun() => await Execute(
        () => _services.Execution.RunBuildAsync(_project, ExecutionKind.Run), "Running…");

    [RelayCommand(CanExecute = nameof(CanRunTests))]
    private async Task RunTests() => await Execute(
        () => _services.Execution.RunTestsAsync(_project), "Testing…");

    private async Task Execute<T>(Func<Task<T?>> action, string status) where T : class
    {
        IsRunning = true;
        RunStatus = status;
        try
        {
            await Task.Run(action);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
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
