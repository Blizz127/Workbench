using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class VisualTestRepository(WorkbenchDatabase database) : IVisualTestRepository
{
    public VisualTestProfile? GetProfile(long id)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_profiles WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadProfile(r) : null;
    }

    public VisualTestProfile? GetProfileByStableId(long projectId, string stableId)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_profiles WHERE project_id = @pid AND stable_id = @sid;";
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@sid", stableId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadProfile(r) : null;
    }

    public List<VisualTestProfile> GetProfilesForProject(long projectId)
    {
        var list = new List<VisualTestProfile>();
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_profiles WHERE project_id = @pid ORDER BY name;";
        cmd.Parameters.AddWithValue("@pid", projectId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadProfile(r));
        return list;
    }

    public List<VisualCaptureCheckpoint> GetCheckpoints(long profileId)
    {
        var list = new List<VisualCaptureCheckpoint>();
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_checkpoints WHERE profile_id = @pid ORDER BY sort_order;";
        cmd.Parameters.AddWithValue("@pid", profileId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCheckpoint(r));
        return list;
    }

    public void AddProfile(VisualTestProfile profile)
    {
        using var c = database.OpenConnection();
        using var t = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = @"INSERT INTO visual_test_profiles
            (project_id, stable_id, name, description, launch_kind, artifact_kind, build_command, launch_command,
             working_directory, environment, display, startup_timeout_seconds, process_name,
             window_title_pattern, window_class_pattern, expected_exit_codes,
             success_log_patterns, failure_log_patterns, screenshot_folder,
             retain_last_runs, stop_after_capture, leave_running, created_at, updated_at)
            VALUES (@pid,@sid,@n,@d,@lk,@ak,@bc,@lc,@wd,@e,@disp,@to,@pn,@wt,@wc,@eec,@slp,@flp,@sf,@rlr,@sac,@lr,@ca,@ua);
            SELECT last_insert_rowid();";
        BindProfile(cmd, profile);
        profile.Id = (long)cmd.ExecuteScalar()!;
        t.Commit();
    }

    public void AddCaptureCheckpoint(VisualCaptureCheckpoint cp)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO visual_test_checkpoints
            (profile_id, sort_order, name, delay_after_seconds,
             wait_for_window_title, wait_for_log_pattern, input_action,
             screenshot_filename, window_only, required, notes)
            VALUES (@pid,@so,@n,@das,@wfwt,@wflp,@ia,@sf,@wo,@req,@notes);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@pid", cp.ProfileId);
        cmd.Parameters.AddWithValue("@so", cp.SortOrder);
        cmd.Parameters.AddWithValue("@n", cp.Name);
        cmd.Parameters.AddWithValue("@das", cp.DelayAfterSeconds);
        cmd.Parameters.AddWithValue("@wfwt", (object?)cp.WaitForWindowTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wflp", (object?)cp.WaitForLogPattern ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ia", (object?)cp.InputAction ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sf", cp.ScreenshotFilename);
        cmd.Parameters.AddWithValue("@wo", cp.WindowOnly ? 1L : 0L);
        cmd.Parameters.AddWithValue("@req", cp.Required ? 1L : 0L);
        cmd.Parameters.AddWithValue("@notes", (object?)cp.Notes ?? DBNull.Value);
        cp.Id = (long)cmd.ExecuteScalar()!;
    }

    public void UpdateProfile(VisualTestProfile profile)
    {
        using var c = database.OpenConnection();
        using var t = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = @"UPDATE visual_test_profiles SET
            name=@n, description=@d, launch_kind=@lk, artifact_kind=@ak, build_command=@bc,
            launch_command=@lc, working_directory=@wd, environment=@e, display=@disp,
            startup_timeout_seconds=@to, process_name=@pn,
            window_title_pattern=@wt, window_class_pattern=@wc,
            expected_exit_codes=@eec, success_log_patterns=@slp,
            failure_log_patterns=@flp, screenshot_folder=@sf,
            retain_last_runs=@rlr, stop_after_capture=@sac,
            leave_running=@lr, updated_at=@ua WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id", profile.Id);
        BindProfile(cmd, profile);
        cmd.ExecuteNonQuery();
        t.Commit();
    }

    public void DeleteProfile(long id)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM visual_test_profiles WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void AddRun(VisualTestRun run)
    {
        using var c = database.OpenConnection();
        using var t = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = @"INSERT INTO visual_test_runs
            (profile_id, project_id, result, started_at, completed_at, duration_ms,
             commit_sha, branch, dirty, process_exit_code, detected_window_id,
             detected_window_title, detected_window_class, resolution, output_log)
            VALUES (@pid,@prjid,@r,@sa,@ca,@dur,@cs,@br,@di,@pec,@dwi,@dwt,@dwc,@res,@ol);
            SELECT last_insert_rowid();";
        BindRun(cmd, run);
        run.Id = (long)cmd.ExecuteScalar()!;
        t.Commit();
        foreach (var capture in run.Captures)
        {
            capture.RunId = run.Id;
            AddCapture(capture);
        }
    }

    public void AddCapture(VisualTestCapture capture)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO visual_test_captures
            (run_id, checkpoint_id, checkpoint_name, sort_order, screenshot_path,
             succeeded, error_message, window_title, resolution, pixel_diff_percent, captured_at)
            VALUES (@rid,@cid,@cn,@so,@sp,@suc,@em,@wt,@res,@pdp,@ca);";
        cmd.Parameters.AddWithValue("@rid", capture.RunId);
        cmd.Parameters.AddWithValue("@cid", capture.CheckpointId);
        cmd.Parameters.AddWithValue("@cn", capture.CheckpointName);
        cmd.Parameters.AddWithValue("@so", capture.SortOrder);
        cmd.Parameters.AddWithValue("@sp", capture.ScreenshotPath);
        cmd.Parameters.AddWithValue("@suc", capture.Succeeded ? 1L : 0L);
        cmd.Parameters.AddWithValue("@em", (object?)capture.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wt", (object?)capture.WindowTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@res", (object?)capture.Resolution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pdp", (object?)capture.PixelDiffPercent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", capture.CapturedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public VisualTestRun? GetRun(long id)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_runs WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var run = ReadRun(r);
        run.Captures = GetCapturesForRun(run.Id);
        return run;
    }

    public List<VisualTestRun> GetRunsForProfile(long profileId, int limit = 20)
    {
        var list = new List<VisualTestRun>();
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_runs WHERE profile_id = @pid ORDER BY started_at DESC LIMIT @lim;";
        cmd.Parameters.AddWithValue("@pid", profileId);
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadRun(r));
        foreach (var run in list) run.Captures = GetCapturesForRun(run.Id);
        return list;
    }

    public List<VisualTestCapture> GetCapturesForRun(long runId)
    {
        var list = new List<VisualTestCapture>();
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_captures WHERE run_id = @rid ORDER BY sort_order;";
        cmd.Parameters.AddWithValue("@rid", runId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCapture(r));
        return list;
    }

    public VisualTestBaseline? GetBaseline(long profileId)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM visual_test_baselines WHERE profile_id = @pid;";
        cmd.Parameters.AddWithValue("@pid", profileId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadBaseline(r) : null;
    }

    public void SetBaseline(VisualTestBaseline baseline)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO visual_test_baselines
            (id, profile_id, run_id, selected_at, notes)
            VALUES ((SELECT id FROM visual_test_baselines WHERE profile_id=@pid), @pid, @rid, @sa, @n);";
        cmd.Parameters.AddWithValue("@pid", baseline.ProfileId);
        cmd.Parameters.AddWithValue("@rid", baseline.RunId);
        cmd.Parameters.AddWithValue("@sa", baseline.SelectedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@n", (object?)baseline.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void TrimRuns(long profileId, int retainCount)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"DELETE FROM visual_test_runs WHERE id IN (
            SELECT id FROM visual_test_runs WHERE profile_id = @pid
            ORDER BY started_at DESC LIMIT -1 OFFSET @retain);";
        cmd.Parameters.AddWithValue("@pid", profileId);
        cmd.Parameters.AddWithValue("@retain", retainCount);
        cmd.ExecuteNonQuery();
    }

    public bool IsRunning(long projectId)
    {
        using var c = database.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM visual_test_runs WHERE project_id = @pid AND completed_at IS NULL;";
        cmd.Parameters.AddWithValue("@pid", projectId);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    // --- reader / binder helpers ---

    private static VisualTestProfile ReadProfile(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), ProjectId = r.GetInt64(1), StableId = r.GetString(2), Name = r.GetString(3),
        Description = r.IsDBNull(4) ? null : r.GetString(4),
        LaunchKind = r.IsDBNull(5) ? VisualTestLaunchKind.Native : Enum.Parse<VisualTestLaunchKind>(r.GetString(5)),
        ArtifactKind = r.IsDBNull(6) ? VisualTestArtifactKind.NativePort : Enum.Parse<VisualTestArtifactKind>(r.GetString(6)),
        BuildCommand = r.IsDBNull(6) ? null : r.GetString(7),
        LaunchCommand = r.GetString(8),
        WorkingDirectory = r.IsDBNull(8) ? null : r.GetString(9),
        Environment = r.IsDBNull(9) ? null : r.GetString(10),
        Display = r.GetString(11), StartupTimeoutSeconds = r.GetInt32(12), ProcessName = r.GetString(13),
        WindowTitlePattern = r.IsDBNull(13) ? null : r.GetString(14),
        WindowClassPattern = r.IsDBNull(14) ? null : r.GetString(15),
        ExpectedExitCodes = r.IsDBNull(15) ? null : r.GetString(16),
        SuccessLogPatterns = r.IsDBNull(16) ? null : r.GetString(17),
        FailureLogPatterns = r.IsDBNull(17) ? null : r.GetString(18),
        ScreenshotFolder = r.GetString(19), RetainLastRuns = r.GetInt32(20),
        StopAfterCapture = r.GetInt32(21) != 0, LeaveRunning = r.GetInt32(22) != 0,
        CreatedAt = DateTimeOffset.Parse(r.GetString(23)), UpdatedAt = DateTimeOffset.Parse(r.GetString(24)),
    };

    private static void BindProfile(SqliteCommand cmd, VisualTestProfile p)
    {
        cmd.Parameters.AddWithValue("@pid", p.ProjectId); cmd.Parameters.AddWithValue("@sid", p.StableId);
        cmd.Parameters.AddWithValue("@n", p.Name); cmd.Parameters.AddWithValue("@d", (object?)p.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lk", p.LaunchKind.ToString());
        cmd.Parameters.AddWithValue("@ak", p.ArtifactKind.ToString());
        cmd.Parameters.AddWithValue("@bc", (object?)p.BuildCommand ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lc", p.LaunchCommand);
        cmd.Parameters.AddWithValue("@wd", (object?)p.WorkingDirectory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", (object?)p.Environment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@disp", p.Display); cmd.Parameters.AddWithValue("@to", p.StartupTimeoutSeconds);
        cmd.Parameters.AddWithValue("@pn", p.ProcessName);
        cmd.Parameters.AddWithValue("@wt", (object?)p.WindowTitlePattern ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wc", (object?)p.WindowClassPattern ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@eec", (object?)p.ExpectedExitCodes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@slp", (object?)p.SuccessLogPatterns ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@flp", (object?)p.FailureLogPatterns ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sf", p.ScreenshotFolder); cmd.Parameters.AddWithValue("@rlr", p.RetainLastRuns);
        cmd.Parameters.AddWithValue("@sac", p.StopAfterCapture ? 1L : 0L); cmd.Parameters.AddWithValue("@lr", p.LeaveRunning ? 1L : 0L);
        cmd.Parameters.AddWithValue("@ca", p.CreatedAt.ToString("O")); cmd.Parameters.AddWithValue("@ua", p.UpdatedAt.ToString("O"));
    }

    private static VisualCaptureCheckpoint ReadCheckpoint(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), ProfileId = r.GetInt64(1), SortOrder = r.GetInt32(2), Name = r.GetString(3),
        DelayAfterSeconds = r.GetInt32(4),
        WaitForWindowTitle = r.IsDBNull(5) ? null : r.GetString(5),
        WaitForLogPattern = r.IsDBNull(6) ? null : r.GetString(6),
        InputAction = r.IsDBNull(7) ? null : r.GetString(7),
        ScreenshotFilename = r.GetString(8), WindowOnly = r.GetInt32(9) != 0, Required = r.GetInt32(10) != 0,
        Notes = r.IsDBNull(11) ? null : r.GetString(11),
    };

    private static VisualTestRun ReadRun(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), ProfileId = r.GetInt64(1), ProjectId = r.GetInt64(2),
        Result = Enum.TryParse<VisualTestResult>(r.GetString(3), out var res) ? res : VisualTestResult.Unknown,
        StartedAt = DateTimeOffset.Parse(r.GetString(4)),
        CompletedAt = r.IsDBNull(5) ? null : DateTimeOffset.Parse(r.GetString(5)),
        Duration = TimeSpan.FromMilliseconds(r.GetInt64(6)),
        CommitSha = r.IsDBNull(7) ? null : r.GetString(7), Branch = r.IsDBNull(8) ? null : r.GetString(8),
        Dirty = r.GetInt32(9) != 0, ProcessExitCode = r.IsDBNull(10) ? null : r.GetInt32(10),
        DetectedWindowId = r.IsDBNull(11) ? null : r.GetString(11),
        DetectedWindowTitle = r.IsDBNull(12) ? null : r.GetString(12),
        DetectedWindowClass = r.IsDBNull(13) ? null : r.GetString(13),
        Resolution = r.IsDBNull(14) ? null : r.GetString(14), OutputLog = r.GetString(15),
    };

    private static void BindRun(SqliteCommand cmd, VisualTestRun r)
    {
        cmd.Parameters.AddWithValue("@pid", r.ProfileId); cmd.Parameters.AddWithValue("@prjid", r.ProjectId);
        cmd.Parameters.AddWithValue("@r", r.Result.ToString()); cmd.Parameters.AddWithValue("@sa", r.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ca", (object?)r.CompletedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dur", (long)r.Duration.TotalMilliseconds);
        cmd.Parameters.AddWithValue("@cs", (object?)r.CommitSha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@br", (object?)r.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@di", r.Dirty ? 1L : 0L);
        cmd.Parameters.AddWithValue("@pec", (object?)r.ProcessExitCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dwi", (object?)r.DetectedWindowId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dwt", (object?)r.DetectedWindowTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dwc", (object?)r.DetectedWindowClass ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@res", (object?)r.Resolution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ol", r.OutputLog);
    }

    private static VisualTestCapture ReadCapture(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), RunId = r.GetInt64(1), CheckpointId = r.GetInt64(2), CheckpointName = r.GetString(3),
        SortOrder = r.GetInt32(4), ScreenshotPath = r.GetString(5), Succeeded = r.GetInt32(6) != 0,
        ErrorMessage = r.IsDBNull(7) ? null : r.GetString(7), WindowTitle = r.IsDBNull(8) ? null : r.GetString(8),
        Resolution = r.IsDBNull(9) ? null : r.GetString(9),
        PixelDiffPercent = r.IsDBNull(10) ? null : r.GetDouble(10), CapturedAt = DateTimeOffset.Parse(r.GetString(11)),
    };

    private static VisualTestBaseline ReadBaseline(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), ProfileId = r.GetInt64(1), RunId = r.GetInt64(2),
        SelectedAt = DateTimeOffset.Parse(r.GetString(3)), Notes = r.IsDBNull(4) ? null : r.GetString(4),
    };
}
