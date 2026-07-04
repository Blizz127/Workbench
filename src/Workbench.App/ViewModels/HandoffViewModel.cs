using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.App.ViewModels;

/// <summary>
/// The handoff page: a generated markdown summary of current state, last
/// completed work, blockers, and the recommended next step — plus two
/// export actions. "Append Handoff Checkpoint" writes a preview-then-confirm
/// entry into the project's own <c>docs/ai_context/ACTIVE_HANDOFF.md</c>;
/// "Generate Model Prompt" assembles a separate, task-focused prompt text.
/// Neither sends anything anywhere — both just produce text for the user to
/// copy, paste, or (for the checkpoint, only after explicit confirmation)
/// append to a file.
/// </summary>
public sealed partial class HandoffViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;

    [ObservableProperty]
    private string _handoffMarkdown = string.Empty;

    // Checkpoint preview/confirm
    [ObservableProperty] private bool _isCheckpointPreviewOpen;
    [ObservableProperty] private string _checkpointPreviewText = string.Empty;
    [ObservableProperty] private string _checkpointStatusMessage = string.Empty;
    public bool CanAppendCheckpoint => !string.IsNullOrWhiteSpace(_project.RepositoryPath);

    // Model prompt
    [ObservableProperty] private string _modelPromptText = string.Empty;

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

    [RelayCommand(CanExecute = nameof(CanAppendCheckpoint))]
    private void PrepareCheckpoint()
    {
        CheckpointPreviewText = _services.CheckpointWriter.FormatCheckpoint(HandoffMarkdown, DateTimeOffset.Now);
        IsCheckpointPreviewOpen = true;
        CheckpointStatusMessage = string.Empty;
    }

    [RelayCommand]
    private void ConfirmCheckpoint()
    {
        try
        {
            _services.CheckpointWriter.Append(_project.RepositoryPath!, CheckpointPreviewText);
            CheckpointStatusMessage = $"Appended to {_services.CheckpointWriter.RelativePath} at {DateTime.Now:HH:mm:ss}";

            _services.Timeline.Add(new TimelineEntry
            {
                ProjectId = _project.Id,
                Category = TimelineCategory.Note,
                Message = $"Handoff checkpoint appended to {_services.CheckpointWriter.RelativePath}",
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            CheckpointStatusMessage = $"Failed to write checkpoint: {ex.Message}";
        }
        finally
        {
            IsCheckpointPreviewOpen = false;
        }
    }

    [RelayCommand]
    private void CancelCheckpoint()
    {
        IsCheckpointPreviewOpen = false;
        CheckpointStatusMessage = "Checkpoint discarded — nothing was written.";
    }

    [RelayCommand]
    private void GenerateModelPrompt()
    {
        var failed = _services.FindLastFailedCommand(_project.Id);
        var repository = _services.Git.GetSnapshot(_project.RepositoryPath);
        var findings = failed is null
            ? Array.Empty<LogPatternMatch>()
            : _services.LogPatterns.Analyze(failed.OutputLog, _project.CustomLogPatterns);

        var context = new PromptContext(
            Project: _project,
            CurrentFrontier: _services.Frontiers.GetCurrent(_project.Id),
            LastFailedCommand: failed,
            LogFindings: findings,
            Repository: repository);

        ModelPromptText = _services.PromptGenerator.Generate(context);
    }
}
