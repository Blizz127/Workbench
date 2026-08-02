using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Workbench.Core.Models;
using System.Text;

namespace Workbench.Core.Services;

/// <summary>
/// Executes visual test profiles on the VPS virtual desktop using
/// scrot (screenshots), xdotool (window management), and xwininfo
/// (window detection).  All executable paths are resolved from the
/// project repository — never from web input.
/// </summary>
public class VisualTestService : IVisualTestService
{
    private readonly IVisualTestRepository _repo;
    private readonly IProjectRepository _projects;
    private readonly IGitService _git;

    // Tool paths — resolved at construction so tests can override.
    private readonly string _xdotool = "xdotool";
    private readonly string _xwininfo = "xwininfo";
    private readonly string _xwd = "xwd";
    private readonly string _xwd2png;  // built-in pure-Python XWD→PNG converter

    private string _currentDisplay = Environment.GetEnvironmentVariable("DISPLAY") ?? ":10.0";

    public VisualTestService(IVisualTestRepository repo, IProjectRepository projects, IGitService git)
    {
        _repo = repo;
        _projects = projects;
        _git = git;
        _xwd2png = ResolveScriptPath("xwd2png");
    }

    public async Task<VisualTestRun> ExecuteAsync(
        VisualTestProfile profile, CancellationToken cancellationToken = default)
    {
        var project = _projects.GetById(profile.ProjectId)
            ?? throw new InvalidOperationException($"Project {profile.ProjectId} not found.");

        var run = new VisualTestRun
        {
            ProfileId = profile.Id,
            ProjectId = profile.ProjectId,
            StartedAt = DateTimeOffset.UtcNow,
            Result = VisualTestResult.Unknown,
        };

        _currentDisplay = profile.Display;

        // Capture git context
        try
        {
            if (!string.IsNullOrWhiteSpace(project.RepositoryPath))
            {
                var snapshot = _git.GetSnapshot(project.RepositoryPath);
                run.CommitSha = snapshot.LastCommit?.Sha;
                run.Branch = snapshot.BranchName;
                run.Dirty = snapshot.IsDirty;
            }
        }
        catch { /* non-fatal */ }

        var dataRoot = ResolveDataRoot();
        var screenshotDir = Path.Combine(dataRoot, "visual-tests", profile.ScreenshotFolder,
            run.StartedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(screenshotDir);

        var checkpoints = _repo.GetCheckpoints(profile.Id)
            .OrderBy(c => c.SortOrder).ToList();

        // Optional build step
        if (!string.IsNullOrWhiteSpace(profile.BuildCommand))
        {
            await RunBuildAsync(profile, project, run, cancellationToken);
            if (run.Result == VisualTestResult.Failed)
            {
                _repo.AddRun(run);
                return run;
            }
        }

        // Launch the application — async output capture feeds this buffer
        var processOutput = new StringBuilder();
        Process? process = null;
        string? windowId = null;
        try
        {
            process = LaunchApplication(profile, project, processOutput);
            run.OutputLog += $"[launch] PID={process.Id} CMD={profile.LaunchCommand}\n";

            // Diagnostic: what does xdotool see right now?
            try
            {
                var diag = RunTool(_xdotool, ["search", "--onlyvisible", "--name", "."]);
                var count = diag.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                run.OutputLog += $"[diag] xdotool sees {count} visible windows on {_currentDisplay}\n";
            }
            catch (Exception ex) { run.OutputLog += $"[diag] xdotool error: {ex.Message}\n"; }

            // Wait for the window to appear
            windowId = await WaitForWindowAsync(profile, process, run, cancellationToken);
            if (windowId == null)
            {
                run.Result = VisualTestResult.Failed;
                run.OutputLog += "[error] Window did not appear within timeout.\n";
                _repo.AddRun(run);
                return run;
            }

            run.DetectedWindowId = windowId;
            run.DetectedWindowTitle = GetWindowTitle(windowId);
            run.DetectedWindowClass = GetWindowClass(windowId);
            run.Resolution = GetWindowGeometry(windowId);
            run.OutputLog += $"[window] id={windowId} title='{run.DetectedWindowTitle}' class='{run.DetectedWindowClass}' geom={run.Resolution}\n";

            // Execute checkpoints in order
            bool anyRequiredFailed = false;
            bool anyOptionalFailed = false;

            foreach (var cp in checkpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var capture = new VisualTestCapture
                {
                    RunId = 0, // set after run is saved
                    CheckpointId = cp.Id,
                    CheckpointName = cp.Name,
                    SortOrder = cp.SortOrder,
                    CapturedAt = DateTimeOffset.UtcNow,
                };

                try
                {
                    // Wait
                    if (cp.DelayAfterSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(cp.DelayAfterSeconds), cancellationToken);

                    // Optional window-title wait
                    if (!string.IsNullOrWhiteSpace(cp.WaitForWindowTitle))
                    {
                        var titleOk = await WaitForWindowTitleAsync(windowId, cp.WaitForWindowTitle,
                            TimeSpan.FromSeconds(10), cancellationToken);
                        if (!titleOk)
                        {
                            capture.ErrorMessage = $"Window title did not match '{cp.WaitForWindowTitle}'";
                            capture.Succeeded = false;
                            if (cp.Required) anyRequiredFailed = true;
                            else anyOptionalFailed = true;
                            run.Captures.Add(capture);
                            _repo.AddCapture(capture);
                            continue;
                        }
                    }

                    // Optional input action
                    if (!string.IsNullOrWhiteSpace(cp.InputAction))
                        SendInput(windowId, cp.InputAction);

                    // Capture — don't double-append .png when the checkpoint filename
                    // already carries an extension (e.g. "01_boot.png").
                    var baseName = SanitizeFilename(cp.ScreenshotFilename);
                    var filename = Path.HasExtension(baseName)
                        ? $"{cp.SortOrder:D2}_{baseName}"
                        : $"{cp.SortOrder:D2}_{baseName}.png";
                    var path = Path.Combine(screenshotDir, filename);

                    if (cp.WindowOnly && windowId != null)
                        CaptureWindow(windowId, path);
                    else
                        CaptureDesktop(path);

                    capture.ScreenshotPath = Path.GetRelativePath(dataRoot, path);
                    capture.Succeeded = true;
                    capture.WindowTitle = GetWindowTitle(windowId);
                    capture.Resolution = GetWindowGeometry(windowId);
                    run.OutputLog += $"[capture:{cp.Name}] ok → {filename}\n";
                }
                catch (Exception ex)
                {
                    capture.ErrorMessage = ex.Message;
                    capture.Succeeded = false;
                    if (cp.Required) anyRequiredFailed = true;
                    else anyOptionalFailed = true;
                    run.OutputLog += $"[capture:{cp.Name}] FAIL: {ex.Message}\n";
                }

                run.Captures.Add(capture);
            }

            run.Result = anyRequiredFailed ? VisualTestResult.Failed
                : anyOptionalFailed ? VisualTestResult.Degraded
                : VisualTestResult.Healthy;
        }
        catch (OperationCanceledException)
        {
            run.Result = VisualTestResult.Failed;
            run.OutputLog += "[error] Visual test cancelled.\n";
        }
        catch (Exception ex)
        {
            run.Result = VisualTestResult.Failed;
            run.OutputLog += $"[error] {ex.Message}\n";
        }
        finally
        {
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Duration = run.CompletedAt.Value - run.StartedAt;

            if (profile.StopAfterCapture && process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await Task.Delay(500, CancellationToken.None);
                }
                catch { /* best effort */ }
                run.ProcessExitCode = process.HasExited ? process.ExitCode : null;
            }

            process?.Dispose();

            // Prepend captured process stdout/stderr to the service log
            var captured = processOutput.ToString();
            if (captured.Length > 0)
                run.OutputLog = captured + run.OutputLog;

            // Check success/failure log patterns against captured output
            DetectLogPatterns(run, profile, captured);

            _repo.AddRun(run);
            _repo.TrimRuns(profile.Id, profile.RetainLastRuns);
        }

        return run;
    }

    public async Task<VisualTestBaseline> CreateBaselineAsync(long profileId, long runId, string? notes = null)
    {
        var profile = _repo.GetProfile(profileId)
            ?? throw new InvalidOperationException($"Profile {profileId} not found.");

        var run = _repo.GetRun(runId)
            ?? throw new InvalidOperationException($"Run {runId} not found.");

        if (run.ProfileId != profileId)
            throw new InvalidOperationException($"Run {runId} does not belong to profile {profileId}.");

        if (run.Result is not VisualTestResult.Healthy and not VisualTestResult.Degraded)
            throw new InvalidOperationException(
                $"Run {runId} result is {run.Result}; only Healthy or Degraded runs can be baselines.");

        var baseline = new VisualTestBaseline
        {
            ProfileId = profileId,
            RunId = runId,
            SelectedAt = DateTimeOffset.UtcNow,
            Notes = notes,
        };
        _repo.SetBaseline(baseline);
        return baseline;
    }

    private async Task RunBuildAsync(VisualTestProfile profile, Project project,
        VisualTestRun run, CancellationToken ct)
    {
        run.OutputLog += $"[build] {profile.BuildCommand}\n";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{profile.BuildCommand}\"",
                WorkingDirectory = project.RepositoryPath ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                run.OutputLog += "[build] Failed to start.\n";
                run.Result = VisualTestResult.Failed;
                return;
            }

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            run.OutputLog += stdout;
            if (!string.IsNullOrWhiteSpace(stderr))
                run.OutputLog += stderr;

            if (proc.ExitCode != 0)
            {
                run.OutputLog += $"[build] Exit code {proc.ExitCode}.\n";
                run.Result = VisualTestResult.Failed;
            }
        }
        catch (Exception ex)
        {
            run.OutputLog += $"[build] Error: {ex.Message}\n";
            run.Result = VisualTestResult.Failed;
        }
    }

    internal Process LaunchApplication(VisualTestProfile profile, Project project, StringBuilder outputBuffer)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"DISPLAY={profile.Display} {profile.LaunchCommand}\"",
            WorkingDirectory = profile.WorkingDirectory ?? project.RepositoryPath ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Also set DISPLAY for the bash process itself (belt and suspenders)
        psi.Environment["DISPLAY"] = profile.Display;

        // Apply custom environment
        if (!string.IsNullOrWhiteSpace(profile.Environment))
        {
            foreach (var line in profile.Environment.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    psi.Environment[parts[0].Trim()] = parts[1].Trim();
            }
        }

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sync = new object();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) lock (sync) outputBuffer.AppendLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) lock (sync) outputBuffer.AppendLine(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return proc;
    }

    private async Task<string?> WaitForWindowAsync(VisualTestProfile profile, Process process,
        VisualTestRun run, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(profile.StartupTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                run.OutputLog += $"[process] Exited with code {process.ExitCode} before window appeared.\n";
                return null;
            }

            var windowId = FindWindow(profile.ProcessName, profile.WindowTitlePattern,
                profile.WindowClassPattern);
            if (windowId != null)
                return windowId;

            await Task.Delay(500, ct);
        }
        return null;
    }

    private async Task<bool> WaitForWindowTitleAsync(string windowId, string pattern,
        TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var title = GetWindowTitle(windowId);
            if (title != null && title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
            await Task.Delay(300, ct);
        }
        return false;
    }

    private string? FindWindow(string processName, string? titlePattern, string? classPattern)
    {
        try
        {
            // Use xdotool search to find window by name/class.
            // IMPORTANT: flags must come before the pattern argument.
            var args = new List<string> { "search", "--onlyvisible" };
            bool hasTitle = !string.IsNullOrWhiteSpace(titlePattern);
            bool hasClass = !string.IsNullOrWhiteSpace(classPattern);

            if (hasTitle) args.Add("--name");
            if (hasClass) args.Add("--class");

            // The pattern is the last positional argument
            if (hasTitle) args.Add(titlePattern!);
            else if (hasClass) args.Add(classPattern!);
            else args.Add("."); // match any

            var result = RunTool(_xdotool, [.. args]);
            var ids = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length > 0)
                return ids[0].Trim();

            // Fallback: search by process name via xdotool search --pid
            if (!string.IsNullOrWhiteSpace(processName))
            {
                // Find PIDs matching the process name
                var pgrep = RunTool("pgrep", ["-x", processName]);
                var pids = pgrep.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pid in pids)
                {
                    var widResult = RunTool(_xdotool, ["search", "--onlyvisible", "--pid", pid.Trim()]);
                    var wids = widResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (wids.Length > 0)
                        return wids[0].Trim();
                }
            }
        }
        catch { /* window detection failure is non-fatal */ }
        return null;
    }

    private string? GetWindowTitle(string windowId)
    {
        try { return RunTool(_xdotool, ["getwindowname", windowId]).Trim(); }
        catch { return null; }
    }

    private string? GetWindowClass(string windowId)
    {
        try
        {
            // xdotool getwindowclassname not available in older versions; use xprop
            var output = RunTool("xprop", ["-id", windowId, "WM_CLASS"]);
            // WM_CLASS(STRING) = "instance", "class"
            var match = Regex.Match(output, @"""([^""]+)""\s*,\s*""([^""]+)""");
            return match.Success ? match.Groups[2].Value : null;
        }
        catch { return null; }
    }

    private string? GetWindowGeometry(string windowId)
    {
        try
        {
            var info = RunTool(_xwininfo, ["-id", windowId]);
            var wMatch = Regex.Match(info, @"Width:\s*(\d+)");
            var hMatch = Regex.Match(info, @"Height:\s*(\d+)");
            if (wMatch.Success && hMatch.Success)
                return $"{wMatch.Groups[1].Value}x{hMatch.Groups[1].Value}";
        }
        catch { }
        return null;
    }

    internal void CaptureWindow(string windowId, string path)
    {
        try { RunTool(_xdotool, ["windowactivate", "--sync", windowId]); }
        catch { /* best effort */ }

        // xwd window capture → xwd2png converter (no external image lib deps)
        var tmp = Path.GetTempFileName();
        try
        {
            RunTool(_xwd, ["-id", windowId, "-out", tmp]);
            RunTool("python3", [_xwd2png, tmp, "-o", path]);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* temp dir may already be gone */ }
        }
    }

    internal void CaptureDesktop(string path)
    {
        // xwd root-window capture → xwd2png
        var tmp = Path.GetTempFileName();
        try
        {
            RunTool(_xwd, ["-root", "-out", tmp]);
            RunTool("python3", [_xwd2png, tmp, "-o", path]);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* temp dir may already be gone */ }
        }
    }

    private void SendInput(string windowId, string action)
    {
        // Action must be from a predefined allowlist
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Return", "Escape", "space", "Tab",
            "Up", "Down", "Left", "Right",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        };

        if (!allowed.Contains(action))
            throw new InvalidOperationException($"Input action '{action}' is not in the allowlist.");

        RunTool(_xdotool, ["windowactivate", "--sync", windowId, "key", action]);
    }

    internal virtual string RunTool(string tool, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Always inject the profile's display so every tool (xdotool, xprop,
        // xwininfo, xwd) targets the right virtual desktop, regardless of the
        // parent process's DISPLAY.  Preserve inherited XAUTHORITY.
        psi.Environment["DISPLAY"] = _currentDisplay;
        var xauth = Environment.GetEnvironmentVariable("XAUTHORITY");
        if (!string.IsNullOrWhiteSpace(xauth))
            psi.Environment["XAUTHORITY"] = xauth;
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {tool}.");
        if (!proc.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"{tool} timed out after 10s.");
        }
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"{tool} exited {proc.ExitCode}: {err}");
        }
        return proc.StandardOutput.ReadToEnd();
    }

    private static string ResolveDataRoot()
    {
        return Environment.GetEnvironmentVariable("WORKBENCH_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Workbench");
    }

    private static string ResolveScriptPath(string scriptName)
    {
        // Look for scripts/ next to the entry assembly (Workbench repo layout)
        var baseDir = AppContext.BaseDirectory;
        for (var d = baseDir; d is not null; d = Path.GetDirectoryName(d))
        {
            var candidate = Path.Combine(d, "scripts", scriptName);
            if (File.Exists(candidate))
                return candidate;
            // Don't walk past the filesystem root
            if (Path.GetDirectoryName(d) == d) break;
        }
        return scriptName; // fallback: hope it's on PATH
    }

    internal static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new StringBuilder();
        foreach (var c in name)
            result.Append(invalid.Contains(c) ? '_' : c);
        return result.ToString();
    }

    internal static void DetectLogPatterns(VisualTestRun run, VisualTestProfile profile, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        if (!string.IsNullOrWhiteSpace(profile.SuccessLogPatterns))
        {
            foreach (var pattern in profile.SuccessLogPatterns.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = pattern.Trim();
                if (p.Length > 0 && output.Contains(p, StringComparison.OrdinalIgnoreCase))
                    run.OutputLog += $"[pattern:success] matched '{p}'\n";
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.FailureLogPatterns))
        {
            foreach (var pattern in profile.FailureLogPatterns.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = pattern.Trim();
                if (p.Length > 0 && output.Contains(p, StringComparison.OrdinalIgnoreCase))
                    run.OutputLog += $"[pattern:failure] matched '{p}'\n";
            }
        }
    }
}

