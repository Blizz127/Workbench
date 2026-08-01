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
    ];

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
