using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Data;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Migration 10 (rich blockers) upgrades a user_version 9 database in place:
/// existing rows survive, stable ids are backfilled and unique, and the new
/// status column derives from the legacy is_resolved flag.
/// </summary>
public sealed class BlockerMigrationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "migration-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "v9.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Builds a minimal v9 database: the OLD blockers table with two rows, user_version = 9.</summary>
    private void CreateV9Database()
    {
        Directory.CreateDirectory(_directory);
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE blockers (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id  INTEGER NOT NULL,
                description TEXT NOT NULL,
                is_resolved INTEGER NOT NULL DEFAULT 0,
                resolution  TEXT,
                created_at  TEXT NOT NULL,
                resolved_at TEXT
            );
            INSERT INTO blockers (project_id, description, is_resolved, resolution, created_at, resolved_at)
            VALUES (1, 'legacy open blocker', 0, NULL, '2026-07-01T10:00:00.0000000+00:00', NULL);
            INSERT INTO blockers (project_id, description, is_resolved, resolution, created_at, resolved_at)
            VALUES (1, 'legacy resolved blocker', 1, 'fixed upstream', '2026-07-02T10:00:00.0000000+00:00',
                    '2026-07-05T10:00:00.0000000+00:00');
            PRAGMA user_version = 9;
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Migration_10_upgrades_a_v9_database_in_place()
    {
        CreateV9Database();

        var database = new WorkbenchDatabase(DatabasePath); // runs migration 10

        var blockers = new BlockerRepository(database).GetForProject(1);
        Assert.Equal(2, blockers.Count);

        // Rows preserved, stable ids backfilled non-empty and unique.
        Assert.All(blockers, b => Assert.False(string.IsNullOrEmpty(b.StableId)));
        Assert.Equal(2, blockers.Select(b => b.StableId).Distinct().Count());
        Assert.Contains(blockers, b => b.Description == "legacy open blocker");
        Assert.Contains(blockers, b => b.Description == "legacy resolved blocker");

        // Status derived from is_resolved; updated_at backfilled from created_at.
        var open = blockers.Single(b => b.Description == "legacy open blocker");
        Assert.Equal(BlockerStatus.Active, open.Status);
        Assert.False(open.IsResolved);
        Assert.Equal(open.CreatedAt, open.UpdatedAt);

        var resolved = blockers.Single(b => b.Description == "legacy resolved blocker");
        Assert.Equal(BlockerStatus.Resolved, resolved.Status);
        Assert.True(resolved.IsResolved);
        Assert.Equal("fixed upstream", resolved.Resolution);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal(resolved.CreatedAt, resolved.UpdatedAt);

        // The unique index exists and defaults landed for the JSON column.
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'ix_blockers_stable_id';
            """;
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
        Assert.All(blockers, b => Assert.Empty(b.Tags));
    }

    [Fact]
    public void Migration_10_enforces_stable_id_uniqueness()
    {
        CreateV9Database();
        var database = new WorkbenchDatabase(DatabasePath);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO blockers (project_id, stable_id, description, is_resolved, created_at)
            SELECT project_id, stable_id, 'duplicate stable id', 0, created_at FROM blockers LIMIT 1;
            """;
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void Migration_10_is_guarded_and_idempotent_on_reopen()
    {
        CreateV9Database();
        var first = new WorkbenchDatabase(DatabasePath);
        var ids = new BlockerRepository(first).GetForProject(1).Select(b => b.StableId).ToList();

        // Re-opening must not re-apply, re-backfill, or fail.
        var second = new WorkbenchDatabase(DatabasePath);
        var blockers = new BlockerRepository(second).GetForProject(1);
        Assert.Equal(2, blockers.Count);
        Assert.Equal(ids.Order(), blockers.Select(b => b.StableId).Order());
    }

    [Fact]
    public void New_rows_get_defaults_compatible_with_the_old_shape()
    {
        CreateV9Database();
        var database = new WorkbenchDatabase(DatabasePath);

        // An insert using only the pre-migration columns still works.
        using (var connection = database.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO blockers (project_id, description, is_resolved, created_at) VALUES (1, 'old-style insert', 0, '2026-08-01T00:00:00.0000000+00:00');";
            command.ExecuteNonQuery();
        }

        var blocker = new BlockerRepository(database).GetForProject(1)
            .Single(b => b.Description == "old-style insert");
        Assert.Equal(BlockerStatus.Active, blocker.Status); // column default
        Assert.Empty(blocker.Tags);                           // JSON default
    }
}
