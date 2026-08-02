using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>
/// One dashboard card — a thin display wrapper over the shared
/// <see cref="ProjectDashboardCard"/> projection. All health/progress math
/// happened in Core; this class only formats.
/// </summary>
public sealed partial class ProjectCardViewModel : ViewModelBase
{
    private readonly Action<long> _openProject;
    private readonly Action<long> _openRun;

    public Project Project { get; }
    public ProjectDashboardCard Card { get; }

    public string Name => Card.Name;
    public string Status => Card.Status.ToString();

    /// <summary>First letter of the projection's accent hint, shown in the card's accent tile.</summary>
    public string Initial => Card.AccentOrIcon.Length > 0
        ? char.ToUpperInvariant(Card.AccentOrIcon[0]).ToString()
        : "?";

    /// <summary>
    /// A missing repository is a normal state for a shared project (its files
    /// live on another machine), so it renders as availability — never as an error.
    /// </summary>
    public string RepositoryText =>
        Project.RepositoryPath is { Length: > 0 } path
            ? Card.RepositoryAvailable ? path : "Not available on this machine"
            : Project.SharedId is not null ? "Not available on this machine" : "no repository";

    public bool IsUnavailable => !Card.RepositoryAvailable;

    /// <summary>One-line git state: branch + dirty marker + ahead/behind counts.</summary>
    public string GitText
    {
        get
        {
            if (!Card.RepositoryAvailable)
                return "—";
            var text = (Card.BranchName ?? "?") + (Card.IsDirty ? "*" : "");
            if (Card.AheadBy is int ahead and > 0)
                text += $" ↑{ahead}";
            if (Card.BehindBy is int behind and > 0)
                text += $" ↓{behind}";
            return text;
        }
    }

    public string DirtyText => Card.IsDirty ? "uncommitted changes" : string.Empty;
    public bool IsDirty => Card.IsDirty;

    // Frontier
    public bool HasFrontier => Card.FrontierProgress.TotalCount > 0;
    public string FrontierText => Card.CurrentFrontierTitle ?? "no frontier set";
    public string FrontierCountText => Card.FrontierProgress.TotalCount == 0
        ? string.Empty
        : $"{Card.FrontierProgress.CompletedCount} of {Card.FrontierProgress.TotalCount} frontiers completed";

    // Next safest step
    public bool HasNextStep => Card.NextSafestStep is { Length: > 0 };
    public string NextStepText => Card.NextSafestStep ?? "—";

    // Milestone progress: null projection means "not configured", never 0%.
    public bool HasProgress => Card.MilestoneProgress is not null;
    public double ProgressPercent => Card.MilestoneProgress?.Percent ?? 0;
    public string ProgressText => Card.MilestoneProgress is { } progress
        ? $"{progress.CompletedCount} of {progress.TotalCount} milestones · {progress.Percent:F0}%"
        : string.Empty;
    public string ProgressEmptyText => "Progress not configured — add milestones in Settings";

    // Blockers
    public bool HasActiveBlockers => Card.ActiveBlockerCount > 0;
    public string BlockerText
    {
        get
        {
            if (Card.ActiveBlockerCount == 0)
                return Card.MonitoringBlockerCount > 0
                    ? $"no active blockers · {Card.MonitoringBlockerCount} monitoring"
                    : "no active blockers";
            var text = $"{Card.ActiveBlockerCount} active {(Card.ActiveBlockerCount == 1 ? "blocker" : "blockers")}";
            if (Card.HighestActiveSeverity is { } severity)
                text += $" · highest {severity}";
            if (Card.MonitoringBlockerCount > 0)
                text += $" · {Card.MonitoringBlockerCount} monitoring";
            return text;
        }
    }

    // Last build/test/launch verdicts (no output text anywhere in the projection).
    public bool HasBuild => Card.LastBuildResult is not null;
    public bool HasTest => Card.LastTestResult is not null;
    public bool HasLaunch => Card.LastLaunchResult is not null;
    public string BuildText => ResultText(Card.LastBuildResult, "no builds yet");
    public string TestText => ResultText(Card.LastTestResult, "no test runs yet");
    public string LaunchText => ResultText(Card.LastLaunchResult, "no runs yet");
    public bool? BuildOutcome => Card.LastBuildResult?.Succeeded;
    public bool? TestOutcome => Card.LastTestResult?.Succeeded;
    public bool? LaunchOutcome => Card.LastLaunchResult?.Succeeded;

    // Runtime health
    public bool HasRuntimeHealth => Card.RuntimeHealth is not null;
    public string RuntimeHealthText => Card.RuntimeHealth?.ToString() ?? "—";
    public Core.Models.RuntimeHealth? RuntimeHealth => Card.RuntimeHealth;

    public string LastActivityText => Card.LastActivityAt is { } at
        ? at.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
        : "no activity yet";

    // Health badge: the state name is always shown as TEXT — never color alone.
    public string HealthText => Card.HealthAssessment.State.ToString();
    public string HealthReasonsText => string.Join(Environment.NewLine, Card.HealthAssessment.Reasons);
    public bool HealthIsSuccess => Card.HealthAssessment.State == ProjectHealthState.Healthy;
    public bool HealthIsWarning => Card.HealthAssessment.State == ProjectHealthState.Attention;
    public bool HealthIsDanger => Card.HealthAssessment.State
        is ProjectHealthState.Blocked or ProjectHealthState.Failing or ProjectHealthState.Unavailable;

    // Primary action: Run/Start when the projection says a launch profile exists
    // AND the repository is here; otherwise the card just opens the project.
    public bool IsRunAction =>
        Card.PrimaryAction == DashboardPrimaryAction.RunOrStart && Card.RepositoryAvailable;
    public string PrimaryActionText => IsRunAction ? "▶ Run" : "Open Project";
    public string PrimaryActionTip => IsRunAction
        ? "Open the workspace on the Run section — execution stays explicit"
        : "Open the project workspace";

    public ProjectCardViewModel(
        Project project, ProjectDashboardCard card, Action<long> openProject, Action<long> openRun)
    {
        Project = project;
        Card = card;
        _openProject = openProject;
        _openRun = openRun;
    }

    [RelayCommand]
    private void Open() => _openProject(Project.Id);

    /// <summary>The card's main button: the Run section when runnable, else the workspace.</summary>
    [RelayCommand]
    private void Primary()
    {
        if (IsRunAction)
            _openRun(Project.Id);
        else
            _openProject(Project.Id);
    }

    private static string ResultText(DashboardExecutionResult? result, string emptyText) =>
        result is null
            ? emptyText
            : $"{(result.TimedOut ? "TIMEOUT" : result.Succeeded ? "OK" : "FAIL")} · {result.When.LocalDateTime:MM-dd HH:mm}";
}
