using Microsoft.Data.Sqlite;
using Workbench.Data;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Simulates a real user's pre-existing v1 database file (the exact DDL from
/// migration 1, hand-populated with rows and non-UTC timestamps like a real
/// v1 install would have) and verifies that opening it through the current
/// <see cref="WorkbenchDatabase"/> upgrades it in place — through migration 2
/// (UTC rewrite) and migration 3 (new columns/tables) — without losing or
/// corrupting any existing data.
/// </summary>
public sealed class MigrationPreservationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_directory, "legacy.db");

    public MigrationPreservationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private const string V1Schema =
        """
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
        """;

    /// <summary>
    /// Builds a v1 database file directly via raw ADO.NET (bypassing
    /// WorkbenchDatabase entirely) and seeds it with a project and a
    /// frontier carrying local-offset (non-UTC) timestamps, exactly as a
    /// real pre-migration-2 install would have on disk.
    /// </summary>
    private void CreateLegacyV1Database()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
        }.ToString());
        connection.Open();

        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = V1Schema;
            schema.ExecuteNonQuery();
        }

        // A local-offset timestamp, as v1 code (pre-migration-2) would have written.
        const string localOffsetTimestamp = "2025-11-03T09:15:00.0000000-05:00";

        using (var insertProject = connection.CreateCommand())
        {
            insertProject.CommandText =
                """
                INSERT INTO projects (
                    name, status, description, repository_path, build_command, run_command,
                    test_command, documentation_paths, ai_context_paths, active_task,
                    current_objective, created_at, updated_at)
                VALUES (
                    'Legacy Xenogears', 'Active', 'Pre-existing v1 project', '/repo/xeno',
                    'make -j8', './xeno', 'pytest', '["docs/"]', '["include/"]',
                    'Legacy active task', 'Legacy objective', $ts, $ts);
                """;
            insertProject.Parameters.AddWithValue("$ts", localOffsetTimestamp);
            insertProject.ExecuteNonQuery();
        }

        using (var insertFrontier = connection.CreateCommand())
        {
            insertFrontier.CommandText =
                """
                INSERT INTO frontiers (project_id, title, status, notes, created_at)
                VALUES (1, 'Legacy frontier title', 'Current', 'Legacy notes', $ts);
                """;
            insertFrontier.Parameters.AddWithValue("$ts", localOffsetTimestamp);
            insertFrontier.ExecuteNonQuery();
        }

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA user_version = 1;";
            pragma.ExecuteNonQuery();
        }
    }

    [Fact]
    public void Opening_a_legacy_v1_database_upgrades_to_v3_without_losing_existing_rows()
    {
        CreateLegacyV1Database();

        var database = new WorkbenchDatabase(DbPath);

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(3L, (long)command.ExecuteScalar()!);
        }

        var projects = new ProjectRepository(database);
        var project = Assert.Single(projects.GetAll());

        // Original v1 data must survive untouched.
        Assert.Equal("Legacy Xenogears", project.Name);
        Assert.Equal("Pre-existing v1 project", project.Description);
        Assert.Equal("/repo/xeno", project.RepositoryPath);
        Assert.Equal("make -j8", project.BuildCommand);
        Assert.Equal(["docs/"], project.DocumentationPaths);
        Assert.Equal(["include/"], project.AiContextPaths);
        Assert.Equal("Legacy active task", project.ActiveTask);

        // Migration 2's UTC rewrite must have applied to the pre-existing timestamp.
        Assert.Equal(TimeSpan.Zero, project.CreatedAt.Offset);
        Assert.Equal(
            DateTimeOffset.Parse("2025-11-03T09:15:00.0000000-05:00"),
            project.CreatedAt); // same instant, now expressed in UTC

        // New v3 columns exist, are queryable, and default to empty/null for old rows.
        Assert.Null(project.MatchingBuildCommand);
        Assert.Null(project.PromptRules);
        Assert.Empty(project.CustomLogPatterns);
        Assert.Null(project.LastKnownBranch);
        Assert.Null(project.LastRefreshedAt);

        var frontiers = new FrontierRepository(database);
        var frontier = frontiers.GetCurrent(project.Id);
        Assert.NotNull(frontier);
        Assert.Equal("Legacy frontier title", frontier.Title);
        Assert.Equal("Legacy notes", frontier.Notes);
        Assert.Equal(TimeSpan.Zero, frontier.CreatedAt.Offset);
        // New v3 frontier columns default to null for a pre-existing row.
        Assert.Null(frontier.Symbol);
        Assert.Null(frontier.Evidence);
        Assert.Null(frontier.BlockerClass);

        // New v3 tables must exist and be fully usable, not just present.
        var projectCommands = new ProjectCommandRepository(database);
        var commandRuns = new CommandRunRepository(database);
        Assert.Empty(projectCommands.GetForProject(project.Id));
        Assert.Empty(commandRuns.GetForProject(project.Id));

        var commandId = projectCommands.Add(new Core.Models.ProjectCommand
        {
            ProjectId = project.Id,
            Name = "post-migration-check",
            CommandText = "echo ok",
        });
        Assert.True(commandId > 0);
        Assert.Single(projectCommands.GetForProject(project.Id));
    }

    [Fact]
    public void Reopening_an_already_migrated_database_is_a_no_op()
    {
        CreateLegacyV1Database();
        _ = new WorkbenchDatabase(DbPath);

        // Constructing WorkbenchDatabase a second time against the same file
        // must not re-run migrations or throw (idempotent open of a current DB).
        var reopened = new WorkbenchDatabase(DbPath);

        using var connection = reopened.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM projects;";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }
}
