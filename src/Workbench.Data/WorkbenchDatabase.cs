using System.Globalization;
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
            if (migration.Sql is { } sql)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            migration.Apply?.Invoke(connection, transaction);

            using (var bump = connection.CreateCommand())
            {
                bump.Transaction = transaction;
                bump.CommandText = $"PRAGMA user_version = {migration.Version};";
                bump.ExecuteNonQuery();
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

    /// <summary>
    /// One schema migration: DDL/DML in <see cref="Sql"/>, and/or a C# step in
    /// <see cref="Apply"/> for transformations SQL can't express precisely.
    /// </summary>
    private sealed record Migration(
        int Version,
        string? Sql = null,
        Action<SqliteConnection, SqliteTransaction>? Apply = null);

    /// <summary>Ordered schema migrations. Append new entries; never edit shipped ones.</summary>
    private static readonly Migration[] Migrations =
    [
        new(1, """
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

        // v2: normalize all stored timestamps to UTC. v1 wrote ISO-8601 strings
        // with the machine's local offset, which sort lexicographically wrong
        // across offset changes (DST). Done in C# to preserve full tick
        // precision — SQLite's date functions round to milliseconds.
        new(2, Apply: (connection, transaction) => NormalizeTimestampsToUtc(connection, transaction)),

        // v3: named command runners (matching-build + custom commands) with
        // their own append-only history table, richer frontier fields, and
        // the "last known git state" cache used by the Refresh Project action.
        new(3, """
            ALTER TABLE projects ADD COLUMN matching_build_command TEXT;
            ALTER TABLE projects ADD COLUMN prompt_rules TEXT;
            ALTER TABLE projects ADD COLUMN custom_log_patterns TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE projects ADD COLUMN last_known_branch TEXT;
            ALTER TABLE projects ADD COLUMN last_known_commit_sha TEXT;
            ALTER TABLE projects ADD COLUMN last_known_is_dirty INTEGER;
            ALTER TABLE projects ADD COLUMN last_known_ahead_by INTEGER;
            ALTER TABLE projects ADD COLUMN last_known_behind_by INTEGER;
            ALTER TABLE projects ADD COLUMN last_refreshed_at TEXT;

            ALTER TABLE frontiers ADD COLUMN symbol TEXT;
            ALTER TABLE frontiers ADD COLUMN evidence TEXT;
            ALTER TABLE frontiers ADD COLUMN blocker_class TEXT;
            ALTER TABLE frontiers ADD COLUMN next_safest_step TEXT;
            ALTER TABLE frontiers ADD COLUMN assigned_model TEXT;

            CREATE TABLE project_commands (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id   INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                name         TEXT NOT NULL,
                command_text TEXT NOT NULL,
                sort_order   INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX ix_project_commands_project ON project_commands(project_id, sort_order);

            CREATE TABLE command_runs (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id         INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                command_name       TEXT NOT NULL,
                command_text       TEXT NOT NULL,
                working_directory  TEXT,
                started_at         TEXT NOT NULL,
                exit_code          INTEGER,
                duration_ms        INTEGER NOT NULL DEFAULT 0,
                output_log         TEXT NOT NULL DEFAULT '',
                output_truncated   INTEGER NOT NULL DEFAULT 0,
                git_head_sha       TEXT,
                was_dirty          INTEGER NOT NULL DEFAULT 0,
                regression_detected INTEGER NOT NULL DEFAULT 0,
                cancelled          INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX ix_command_runs_project_time ON command_runs(project_id, started_at DESC);
            CREATE INDEX ix_command_runs_project_name ON command_runs(project_id, command_name, started_at DESC);
            """),
    ];

    /// <summary>Every timestamp column in the schema, for the v2 UTC rewrite.</summary>
    private static readonly (string Table, string[] Columns)[] TimestampColumns =
    [
        ("projects", ["created_at", "updated_at"]),
        ("milestones", ["completed_at"]),
        ("frontiers", ["created_at", "completed_at"]),
        ("blockers", ["created_at", "resolved_at"]),
        ("timeline_entries", ["timestamp"]),
        ("notes", ["created_at", "updated_at"]),
        ("build_records", ["started_at"]),
        ("test_records", ["started_at"]),
    ];

    /// <summary>
    /// Rewrites every non-UTC timestamp to its UTC equivalent (same instant,
    /// zero offset). Values that fail to parse are left untouched so the
    /// migration can never destroy data. Returns the number of rewritten
    /// values. Public for testability.
    /// </summary>
    public static int NormalizeTimestampsToUtc(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        var rewritten = 0;
        foreach (var (table, columns) in TimestampColumns)
        {
            foreach (var column in columns)
            {
                // Buffer the rows needing rewrite before updating: SQLite
                // dislikes writing while a reader is open on the connection.
                var updates = new List<(long Id, string Utc)>();

                using (var select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText = $"SELECT id, {column} FROM {table} WHERE {column} IS NOT NULL;";
                    using var reader = select.ExecuteReader();
                    while (reader.Read())
                    {
                        var raw = reader.GetString(1);
                        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out var parsed))
                            continue;

                        if (parsed.Offset == TimeSpan.Zero)
                            continue;

                        updates.Add((reader.GetInt64(0),
                            parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
                    }
                }

                foreach (var (id, utc) in updates)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = $"UPDATE {table} SET {column} = $value WHERE id = $id;";
                    update.Parameters.AddWithValue("$value", utc);
                    update.Parameters.AddWithValue("$id", id);
                    update.ExecuteNonQuery();
                    rewritten++;
                }
            }
        }

        return rewritten;
    }
}
