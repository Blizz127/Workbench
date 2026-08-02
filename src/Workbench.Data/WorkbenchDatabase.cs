using Microsoft.Data.Sqlite;

namespace Workbench.Data;

/// <summary>
/// Owns the SQLite connection string and the schema. Repositories create
/// short-lived connections through <see cref="OpenConnection"/>; pooling in
/// Microsoft.Data.Sqlite makes that cheap.
///
/// Schema changes are applied as ordered migrations keyed off
/// <c>PRAGMA user_version</c> so existing databases upgrade in place —
/// project history must survive application updates as well as restarts.
/// </summary>
public sealed class WorkbenchDatabase
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public WorkbenchDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();

        Migrate();
    }

    /// <summary>Default location: %APPDATA%/Workbench or ~/.config/Workbench (override with WORKBENCH_DATA_DIR).</summary>
    public static string GetDefaultDatabasePath()
    {
        var dir = Environment.GetEnvironmentVariable("WORKBENCH_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
                "Workbench");
        }

        return Path.Combine(dir, "workbench.db");
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Migrate()
    {
        using var connection = OpenConnection();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }

        var version = GetUserVersion(connection);
        foreach (var migration in Migrations)
        {
            if (version >= migration.Version)
                continue;

            using var transaction = connection.BeginTransaction();
            migration.Apply(connection, transaction);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"PRAGMA user_version = {migration.Version};";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            version = migration.Version;
        }
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>One ordered schema migration; most are plain SQL batches.</summary>
    private sealed record Migration(int Version, Action<SqliteConnection, SqliteTransaction> Apply)
    {
        public static Migration FromSql(int version, string sql) => new(version, (connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        });
    }

    /// <summary>Ordered schema migrations. Append new entries; never edit shipped ones.</summary>
    private static readonly Migration[] Migrations =
    [
        Migration.FromSql(1, """
            CREATE TABLE projects (
                id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                name                  TEXT NOT NULL,
                status                TEXT NOT NULL DEFAULT 'Active',
                description           TEXT,
                repository_path       TEXT,
                build_command         TEXT,
                run_command           TEXT,
                test_command          TEXT,
                documentation_paths   TEXT NOT NULL DEFAULT '[]',
                ai_context_paths      TEXT NOT NULL DEFAULT '[]',
                preferred_ai_provider TEXT,
                active_task           TEXT,
                current_objective     TEXT,
                created_at            TEXT NOT NULL,
                updated_at            TEXT NOT NULL
            );

            CREATE TABLE project_tags (
                project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                tag        TEXT NOT NULL,
                PRIMARY KEY (project_id, tag)
            );

            CREATE TABLE milestones (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id   INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                title        TEXT NOT NULL,
                is_completed INTEGER NOT NULL DEFAULT 0,
                completed_at TEXT,
                sort_order   INTEGER NOT NULL DEFAULT 0,
                notes        TEXT
            );
            CREATE INDEX ix_milestones_project ON milestones(project_id, sort_order);

            CREATE TABLE frontiers (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id   INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                title        TEXT NOT NULL,
                status       TEXT NOT NULL DEFAULT 'Next',
                notes        TEXT,
                created_at   TEXT NOT NULL,
                completed_at TEXT
            );
            CREATE INDEX ix_frontiers_project ON frontiers(project_id, status);

            CREATE TABLE blockers (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                description TEXT NOT NULL,
                is_resolved INTEGER NOT NULL DEFAULT 0,
                resolution  TEXT,
                created_at  TEXT NOT NULL,
                resolved_at TEXT
            );
            CREATE INDEX ix_blockers_project ON blockers(project_id, is_resolved);

            CREATE TABLE timeline_entries (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                timestamp  TEXT NOT NULL,
                category   TEXT NOT NULL DEFAULT 'General',
                message    TEXT NOT NULL,
                detail     TEXT
            );
            CREATE INDEX ix_timeline_project_time ON timeline_entries(project_id, timestamp DESC);

            CREATE TABLE notes (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                title      TEXT NOT NULL,
                content    TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_notes_project ON notes(project_id, updated_at DESC);

            CREATE TABLE build_records (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                kind        TEXT NOT NULL DEFAULT 'Build',
                command     TEXT NOT NULL,
                started_at  TEXT NOT NULL,
                exit_code   INTEGER,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                output_log  TEXT NOT NULL DEFAULT '',
                commit_sha  TEXT
            );
            CREATE INDEX ix_builds_project_time ON build_records(project_id, started_at DESC);

            CREATE TABLE test_records (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                command     TEXT NOT NULL,
                result      TEXT NOT NULL,
                started_at  TEXT NOT NULL,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                notes       TEXT,
                commit_sha  TEXT,
                output_log  TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX ix_tests_project_time ON test_records(project_id, started_at DESC);
            """),

        // Shared Workbench State: stable cross-machine identity, shareable
        // progress fields, and local-only sync bookkeeping. History tables are
        // deliberately untouched — they never leave this database.
        //
        // Numbered 4 (not 2) and column-guarded: real databases can descend
        // from the pre-rewrite Workbench lineage, which shipped migrations up
        // to user_version 3 and already carried prompt_rules and
        // custom_log_patterns on projects. A plain SQL batch would either be
        // skipped (version <= 3) or fail on the duplicate columns.
        new Migration(4, AddSharedStateColumns),

        // Launch profiles (post-launch runtime health validation) and the
        // append-only runtime_health table. Column-guarded like migration 4:
        // live databases must upgrade in place.
        new Migration(5, AddLaunchProfileAndRuntimeHealth),

        // Operations commands on the launch profile: real stop, all-service
        // logs, one-click update, and live metrics for the Run card.
        new Migration(6, AddOperationsCommands),

        // Explicit compose file selection for Docker operations (Phase 1B).
        new Migration(7, AddComposeFile),

        // Explicit tmux session name for project sessions (Phase 1D).
        new Migration(8, AddTmuxSession),

        // Persistent audit trail for the mobile action layer (Phase 2E).
        // Append-only; deliberately NO foreign key to projects — audit records
        // must survive project deletion.
        new Migration(9, AddMobileActionAudit),

        // Rich blockers (shared-state sync): stable cross-machine ids, status
        // lifecycle, severity/owner/tags, and per-blocker evidence/next step.
        // Non-destructive column adds with backfills; is_resolved stays and is
        // kept in sync with status by the repository.
        new Migration(10, AddRichBlockerColumns),

        // Milestone status lifecycle + optional progress weight. Non-destructive
        // column adds; status is backfilled from the legacy is_completed flag,
        // which stays and is kept in sync with status by the repository.
        new Migration(11, AddMilestoneStatusAndWeight),

        // Visual test profiles, checkpoints, runs, captures, and baselines
        // (Phase 3A: reusable visual validation for any project).
        new Migration(12, AddVisualTestTables),

        // Artifact identity for visual test profiles (RetailReference, MatchingBuild, NativePort).
        new Migration(13, AddVisualTestArtifactKind),
    ];

    private static void AddVisualTestTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var sql in new[]
        {
            @"CREATE TABLE IF NOT EXISTS visual_test_profiles (
                id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id              INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                stable_id               TEXT NOT NULL,
                name                    TEXT NOT NULL,
                description             TEXT,
                launch_kind             TEXT NOT NULL DEFAULT 'Native',
                build_command           TEXT,
                launch_command          TEXT NOT NULL,
                working_directory       TEXT,
                environment             TEXT,
                display                 TEXT NOT NULL DEFAULT ':10.0',
                startup_timeout_seconds INTEGER NOT NULL DEFAULT 30,
                process_name            TEXT NOT NULL DEFAULT '',
                window_title_pattern    TEXT,
                window_class_pattern    TEXT,
                expected_exit_codes     TEXT,
                success_log_patterns    TEXT,
                failure_log_patterns    TEXT,
                screenshot_folder       TEXT NOT NULL DEFAULT '',
                retain_last_runs        INTEGER NOT NULL DEFAULT 5,
                stop_after_capture      INTEGER NOT NULL DEFAULT 1,
                leave_running           INTEGER NOT NULL DEFAULT 0,
                created_at              TEXT NOT NULL,
                updated_at              TEXT NOT NULL)",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ix_vt_profiles_stable ON visual_test_profiles(project_id, stable_id)",
            @"CREATE TABLE IF NOT EXISTS visual_test_checkpoints (
                id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id            INTEGER NOT NULL REFERENCES visual_test_profiles(id) ON DELETE CASCADE,
                sort_order            INTEGER NOT NULL DEFAULT 0,
                name                  TEXT NOT NULL,
                delay_after_seconds   INTEGER NOT NULL DEFAULT 2,
                wait_for_window_title TEXT,
                wait_for_log_pattern  TEXT,
                input_action          TEXT,
                screenshot_filename   TEXT NOT NULL DEFAULT '',
                window_only           INTEGER NOT NULL DEFAULT 1,
                required              INTEGER NOT NULL DEFAULT 1,
                notes                 TEXT)",
            @"CREATE INDEX IF NOT EXISTS ix_vt_checkpoints_profile ON visual_test_checkpoints(profile_id, sort_order)",
            @"CREATE TABLE IF NOT EXISTS visual_test_runs (
                id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id           INTEGER NOT NULL REFERENCES visual_test_profiles(id) ON DELETE CASCADE,
                project_id           INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                result               TEXT NOT NULL DEFAULT 'Unknown',
                started_at           TEXT NOT NULL,
                completed_at         TEXT,
                duration_ms          INTEGER NOT NULL DEFAULT 0,
                commit_sha           TEXT,
                branch               TEXT,
                dirty                INTEGER NOT NULL DEFAULT 0,
                process_exit_code    INTEGER,
                detected_window_id   TEXT,
                detected_window_title TEXT,
                detected_window_class TEXT,
                resolution           TEXT,
                output_log           TEXT NOT NULL DEFAULT '')",
            @"CREATE INDEX IF NOT EXISTS ix_vt_runs_profile ON visual_test_runs(profile_id, started_at DESC)",
            @"CREATE TABLE IF NOT EXISTS visual_test_captures (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id            INTEGER NOT NULL REFERENCES visual_test_runs(id) ON DELETE CASCADE,
                checkpoint_id     INTEGER NOT NULL REFERENCES visual_test_checkpoints(id) ON DELETE CASCADE,
                checkpoint_name   TEXT NOT NULL DEFAULT '',
                sort_order        INTEGER NOT NULL DEFAULT 0,
                screenshot_path   TEXT NOT NULL DEFAULT '',
                succeeded         INTEGER NOT NULL DEFAULT 0,
                error_message     TEXT,
                window_title      TEXT,
                resolution        TEXT,
                pixel_diff_percent REAL,
                captured_at       TEXT NOT NULL)",
            @"CREATE INDEX IF NOT EXISTS ix_vt_captures_run ON visual_test_captures(run_id, sort_order)",
            @"CREATE TABLE IF NOT EXISTS visual_test_baselines (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id  INTEGER NOT NULL REFERENCES visual_test_profiles(id) ON DELETE CASCADE,
                run_id      INTEGER NOT NULL REFERENCES visual_test_runs(id) ON DELETE CASCADE,
                selected_at TEXT NOT NULL,
                notes       TEXT)",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ix_vt_baselines_profile ON visual_test_baselines(profile_id)",
        })
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static void AddVisualTestArtifactKind(SqliteConnection connection, SqliteTransaction transaction)
    {
        var existing = GetColumnNames(connection, transaction, "visual_test_profiles");
        if (existing.Count == 0) return;
        if (existing.Contains("artifact_kind")) return;

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE visual_test_profiles ADD COLUMN artifact_kind TEXT NOT NULL DEFAULT 'NativePort';";
        cmd.ExecuteNonQuery();
    }

    private static void AddMilestoneStatusAndWeight(SqliteConnection connection, SqliteTransaction transaction)
    {
        var existing = GetColumnNames(connection, transaction, "milestones");
        if (existing.Count == 0)
            return; // Table predates this lineage (partial test fixtures); nothing to upgrade.
        foreach (var (name, definition) in new (string Name, string Definition)[]
        {
            ("status", "TEXT NOT NULL DEFAULT 'NotStarted'"),
            ("weight", "INTEGER"),
        })
        {
            if (existing.Contains(name))
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE milestones ADD COLUMN {name} {definition};";
            command.ExecuteNonQuery();
        }

        // Backfill, idempotent: the legacy flag is the source of truth for
        // rows that predate the status column.
        using var backfill = connection.CreateCommand();
        backfill.Transaction = transaction;
        backfill.CommandText = "UPDATE milestones SET status = 'Done' WHERE is_completed != 0 AND status = 'NotStarted';";
        backfill.ExecuteNonQuery();
    }

    private static void AddRichBlockerColumns(SqliteConnection connection, SqliteTransaction transaction)
    {
        (string Name, string Definition)[] columns =
        [
            ("stable_id", "TEXT"),
            ("title", "TEXT"),
            ("blocker_class", "TEXT"),
            ("severity", "TEXT"),
            ("status", "TEXT NOT NULL DEFAULT 'Active'"),
            ("evidence", "TEXT"),
            ("next_safest_step", "TEXT"),
            ("owner", "TEXT"),
            ("updated_at", "TEXT"),
            ("tags", "TEXT NOT NULL DEFAULT '[]'"),
            ("source_reference", "TEXT"),
        ];

        var existing = GetColumnNames(connection, transaction, "blockers");
        foreach (var (name, definition) in columns)
        {
            if (existing.Contains(name))
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE blockers ADD COLUMN {name} {definition};";
            command.ExecuteNonQuery();
        }

        // Backfills, all idempotent: status derives from the legacy flag,
        // updated_at starts at created_at, every row gets its own stable id.
        using (var backfill = connection.CreateCommand())
        {
            backfill.Transaction = transaction;
            backfill.CommandText =
                """
                UPDATE blockers SET status = 'Resolved' WHERE is_resolved != 0 AND status = 'Active';
                UPDATE blockers SET updated_at = created_at WHERE updated_at IS NULL;
                """;
            backfill.ExecuteNonQuery();
        }

        // SQLite cannot add a UNIQUE column retroactively, so stable ids are
        // backfilled one GUID per row and uniqueness is enforced by index.
        var missingIds = new List<long>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT id FROM blockers WHERE stable_id IS NULL OR stable_id = '';";
            using var reader = select.ExecuteReader();
            while (reader.Read())
                missingIds.Add(reader.GetInt64(0));
        }

        foreach (var id in missingIds)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE blockers SET stable_id = $sid WHERE id = $id;";
            update.Parameters.AddWithValue("$sid", Guid.NewGuid().ToString("N"));
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        using (var index = connection.CreateCommand())
        {
            index.Transaction = transaction;
            index.CommandText =
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_blockers_stable_id ON blockers(stable_id) WHERE stable_id IS NOT NULL;";
            index.ExecuteNonQuery();
        }
    }

    private static void AddMobileActionAudit(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS mobile_action_audit (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc    TEXT NOT NULL,
                project_id       INTEGER NOT NULL,
                project_name     TEXT NOT NULL,
                action_id        TEXT NOT NULL,
                action_label     TEXT NOT NULL,
                action_type      TEXT NOT NULL,
                safety           TEXT NOT NULL,
                state            TEXT NOT NULL,
                session_hash     TEXT,
                command_preview  TEXT,
                output_summary   TEXT,
                output_truncated INTEGER NOT NULL DEFAULT 0,
                exit_code        INTEGER,
                duration_ms      INTEGER,
                failure_reason   TEXT,
                services         TEXT NOT NULL DEFAULT '[]',
                source           TEXT NOT NULL DEFAULT 'web',
                correlation_id   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_mobile_action_audit_project_time
                ON mobile_action_audit(project_id, timestamp_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_mobile_action_audit_correlation
                ON mobile_action_audit(correlation_id);
            """;
        command.ExecuteNonQuery();
    }

    private static void AddComposeFile(SqliteConnection connection, SqliteTransaction transaction)
    {
        var existing = GetColumnNames(connection, transaction, "projects");
        if (existing.Contains("compose_file"))
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE projects ADD COLUMN compose_file TEXT;";
        command.ExecuteNonQuery();
    }

    private static void AddTmuxSession(SqliteConnection connection, SqliteTransaction transaction)
    {
        var existing = GetColumnNames(connection, transaction, "projects");
        if (existing.Contains("tmux_session"))
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE projects ADD COLUMN tmux_session TEXT;";
        command.ExecuteNonQuery();
    }

    private static void AddOperationsCommands(SqliteConnection connection, SqliteTransaction transaction)
    {
        string[] columns =
        [
            "launch_stop_command",
            "launch_logs_command",
            "launch_update_command",
            "launch_metrics_command",
        ];

        var existing = GetColumnNames(connection, transaction, "projects");
        foreach (var name in columns)
        {
            if (existing.Contains(name))
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE projects ADD COLUMN {name} TEXT;";
            command.ExecuteNonQuery();
        }
    }

    private static void AddLaunchProfileAndRuntimeHealth(SqliteConnection connection, SqliteTransaction transaction)
    {
        (string Name, string Definition)[] columns =
        [
            ("launch_grace_seconds", "INTEGER"),
            ("launch_status_command", "TEXT"),
            ("launch_required_services", "TEXT NOT NULL DEFAULT '[]'"),
            ("launch_optional_services", "TEXT NOT NULL DEFAULT '[]'"),
            ("launch_healthy_patterns", "TEXT NOT NULL DEFAULT '[]'"),
            ("launch_failed_patterns", "TEXT NOT NULL DEFAULT '[]'"),
            ("launch_service_log_command", "TEXT"),
            ("launch_health_retries", "INTEGER"),
            ("launch_health_interval_seconds", "INTEGER"),
        ];

        var existing = GetColumnNames(connection, transaction, "projects");
        foreach (var (name, definition) in columns)
        {
            if (existing.Contains(name))
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE projects ADD COLUMN {name} {definition};";
            command.ExecuteNonQuery();
        }

        using var table = connection.CreateCommand();
        table.Transaction = transaction;
        table.CommandText =
            """
            CREATE TABLE IF NOT EXISTS runtime_health (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id       INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                checked_at       TEXT NOT NULL,
                health           TEXT NOT NULL DEFAULT 'Unknown',
                failed_component TEXT,
                output           TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS ix_runtime_health_project_time
                ON runtime_health(project_id, checked_at DESC);
            """;
        table.ExecuteNonQuery();
    }

    private static void AddSharedStateColumns(SqliteConnection connection, SqliteTransaction transaction)
    {
        (string Name, string Definition)[] columns =
        [
            ("shared_id", "TEXT"),
            ("blocker_class", "TEXT"),
            ("evidence", "TEXT"),
            ("next_safest_step", "TEXT"),
            ("prompt_rules", "TEXT NOT NULL DEFAULT '[]'"),
            ("custom_log_patterns", "TEXT NOT NULL DEFAULT '[]'"),
            ("github_repo_url", "TEXT"),
            ("default_branch", "TEXT"),
            ("shared_last_imported_at", "TEXT"),
            ("shared_last_exported_at", "TEXT"),
            ("shared_last_synced_stamp", "TEXT"),
        ];

        var existing = GetColumnNames(connection, transaction, "projects");
        foreach (var (name, definition) in columns)
        {
            if (existing.Contains(name))
                continue;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE projects ADD COLUMN {name} {definition};";
            command.ExecuteNonQuery();
        }

        using (var index = connection.CreateCommand())
        {
            index.Transaction = transaction;
            index.CommandText =
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_projects_shared_id ON projects(shared_id) WHERE shared_id IS NOT NULL;";
            index.ExecuteNonQuery();
        }
    }

    private static HashSet<string> GetColumnNames(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(1));
        return names;
    }
}
