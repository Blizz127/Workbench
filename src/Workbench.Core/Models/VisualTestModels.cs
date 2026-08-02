namespace Workbench.Core.Models;

/// <summary>How the visual test launches the target application.</summary>
public enum VisualTestLaunchKind
{
    /// <summary>Native executable (e.g. a PC port or local dev build).</summary>
    Native,
    /// <summary>Emulator-wrapped (e.g. PCSX-Redux, DOSBox, RetroArch).</summary>
    Emulator,
    /// <summary>The project has no runnable target yet (matching decomp, research).</summary>
    Unavailable,
}

/// <summary>
/// A reusable visual-test profile: launches a GUI application, waits for
/// defined checkpoints, captures screenshots, and associates them with a
/// run/test result.  Profiles are project-scoped and reference repository-
/// owned launch/build definitions — never arbitrary commands from the UI.
/// </summary>
public sealed class VisualTestProfile
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    /// <summary>Stable id for code references (e.g. "pe-boot-smoke").</summary>
    public string StableId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>How the target is launched: Native, Emulator, or Unavailable.</summary>
    public VisualTestLaunchKind LaunchKind { get; set; } = VisualTestLaunchKind.Native;

    /// <summary>Optional: run this build command before launching.</summary>
    public string? BuildCommand { get; set; }

    /// <summary>Executable and arguments to launch.</summary>
    public string LaunchCommand { get; set; } = string.Empty;

    /// <summary>Working directory for the launched process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Environment variables (KEY=VALUE, one per line).</summary>
    public string? Environment { get; set; }

    /// <summary>X11 display to use (default: ":10.0" for VPS desktop).</summary>
    public string Display { get; set; } = ":10.0";

    /// <summary>Seconds to wait for the process/window to appear.</summary>
    public int StartupTimeoutSeconds { get; set; } = 30;

    /// <summary>Process name to match (e.g. "parasite_eve_port").</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Optional window title pattern to match (substring).</summary>
    public string? WindowTitlePattern { get; set; }

    /// <summary>Optional window class pattern to match (substring).</summary>
    public string? WindowClassPattern { get; set; }

    /// <summary>Expected exit codes; null means any exit is acceptable.</summary>
    public string? ExpectedExitCodes { get; set; }

    /// <summary>Log-line patterns that indicate success/failure.</summary>
    public string? SuccessLogPatterns { get; set; }
    public string? FailureLogPatterns { get; set; }

    /// <summary>Screenshot output subfolder under the workbench data directory.</summary>
    public string ScreenshotFolder { get; set; } = string.Empty;

    /// <summary>How many runs of captures to retain (oldest deleted first).</summary>
    public int RetainLastRuns { get; set; } = 5;

    /// <summary>Stop the launched process after captures complete.</summary>
    public bool StopAfterCapture { get; set; } = true;

    /// <summary>Leave the process alive after capture (for live inspection).</summary>
    public bool LeaveRunning { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Ordered capture checkpoints.</summary>
    public List<VisualCaptureCheckpoint> Checkpoints { get; set; } = [];
}

/// <summary>
/// One screenshot checkpoint inside a visual test profile.  Checkpoints
/// execute in order after the application window is detected.
/// </summary>
public sealed class VisualCaptureCheckpoint
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Seconds to wait after the previous checkpoint or launch.</summary>
    public int DelayAfterSeconds { get; set; } = 2;

    /// <summary>Optional window-title substring to wait for before capturing.</summary>
    public string? WaitForWindowTitle { get; set; }

    /// <summary>Optional log-line substring to wait for.</summary>
    public string? WaitForLogPattern { get; set; }

    /// <summary>Predefined input action to send before capture.</summary>
    public string? InputAction { get; set; }

    /// <summary>Screenshot filename (without path).</summary>
    public string ScreenshotFilename { get; set; } = string.Empty;

    /// <summary>Capture only the application window (true) or full desktop (false).</summary>
    public bool WindowOnly { get; set; } = true;

    /// <summary>If false, a failure here degrades the result but does not fail.</summary>
    public bool Required { get; set; } = true;

    /// <summary>Human-readable notes about what this checkpoint should show.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// One execution of a visual test profile.  Append-only history.
/// </summary>
public sealed class VisualTestRun
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public long ProjectId { get; set; }

    /// <summary>Overall result of the run.</summary>
    public VisualTestResult Result { get; set; } = VisualTestResult.Unknown;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>HEAD commit at the time of the run.</summary>
    public string? CommitSha { get; set; }

    /// <summary>Git branch at the time of the run.</summary>
    public string? Branch { get; set; }

    /// <summary>Whether the working tree was dirty.</summary>
    public bool Dirty { get; set; }

    /// <summary>Exit code of the launched process, if it exited.</summary>
    public int? ProcessExitCode { get; set; }

    /// <summary>Detected window id at time of capture.</summary>
    public string? DetectedWindowId { get; set; }

    /// <summary>Detected window title.</summary>
    public string? DetectedWindowTitle { get; set; }

    /// <summary>Detected window class.</summary>
    public string? DetectedWindowClass { get; set; }

    /// <summary>Resolution of captures.</summary>
    public string? Resolution { get; set; }

    /// <summary>Standard output/error from the launched process.</summary>
    public string OutputLog { get; set; } = string.Empty;

    /// <summary>Individual capture results.</summary>
    public List<VisualTestCapture> Captures { get; set; } = [];
}

/// <summary>
/// One screenshot capture within a visual test run.
/// </summary>
public sealed class VisualTestCapture
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public long CheckpointId { get; set; }
    public string CheckpointName { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>Path to the screenshot PNG file (relative to workbench data root).</summary>
    public string ScreenshotPath { get; set; } = string.Empty;

    /// <summary>Whether this checkpoint succeeded.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Error message if the checkpoint failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Window title at time of capture.</summary>
    public string? WindowTitle { get; set; }

    /// <summary>Resolution of this specific capture.</summary>
    public string? Resolution { get; set; }

    /// <summary>Pixel-difference percentage vs baseline (informational only).</summary>
    public double? PixelDiffPercent { get; set; }

    public DateTimeOffset CapturedAt { get; set; }
}

/// <summary>Result classification for a visual test run.</summary>
public enum VisualTestResult
{
    Unknown,
    Healthy,
    Degraded,
    Failed,
}

/// <summary>A manually-selected baseline run for comparison.</summary>
public sealed class VisualTestBaseline
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public long RunId { get; set; }
    public DateTimeOffset SelectedAt { get; set; }
    public string? Notes { get; set; }
}
