using Workbench.Core.Models;

namespace Workbench.Core.Services;

/// <summary>
/// Executes visual test profiles: launches GUI applications on the VPS
/// virtual desktop, waits for checkpoints, captures screenshots via
/// scrot/xdotool, and records results.  All commands come from repository-
/// owned profile definitions — never from the web/mobile UI.
/// </summary>
public interface IVisualTestService
{
    Task<VisualTestRun> ExecuteAsync(VisualTestProfile profile, CancellationToken cancellationToken = default);
}

public interface IVisualTestRepository
{
    VisualTestProfile? GetProfile(long id);
    VisualTestProfile? GetProfileByStableId(long projectId, string stableId);
    List<VisualTestProfile> GetProfilesForProject(long projectId);
    List<VisualCaptureCheckpoint> GetCheckpoints(long profileId);
    void AddProfile(VisualTestProfile profile);
    void AddCaptureCheckpoint(VisualCaptureCheckpoint checkpoint);
    void UpdateProfile(VisualTestProfile profile);
    void DeleteProfile(long id);

    void AddRun(VisualTestRun run);
    void AddCapture(VisualTestCapture capture);
    VisualTestRun? GetRun(long id);
    List<VisualTestRun> GetRunsForProfile(long profileId, int limit = 20);
    List<VisualTestCapture> GetCapturesForRun(long runId);

    VisualTestBaseline? GetBaseline(long profileId);
    void SetBaseline(VisualTestBaseline baseline);

    /// <summary>Delete runs (and their captures) beyond the retention limit.</summary>
    void TrimRuns(long profileId, int retainCount);

    /// <summary>Check if a visual test is already running for this project.</summary>
    bool IsRunning(long projectId);
}
