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
        foreach (var (targetVersion, sql) in Migrations)
        {
            if (version >= targetVersion)
                continue;

            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();

            command.CommandText = $"PRAGMA user_version = {targetVersion};";
            command.ExecuteNonQuery();
            transaction.Commit();
            version = targetVersion;
        }
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>Ordered schema migrations. Append new entries; never edit shipped ones.</summary>
    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, """
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
    ];
}
