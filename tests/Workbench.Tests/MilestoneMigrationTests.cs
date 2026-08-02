using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Data;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Migration 11 (milestone status + weight) upgrades a user_version 10
/// database in place: rows survive, status derives from the legacy
/// is_completed flag, and weight starts null. Afterwards the repository
/// keeps is_completed in sync with status on every write.
/// </summary>
public sealed class MilestoneMigrationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", "migration-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "v10.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Builds a minimal v10 database: the OLD milestones table with two rows, user_version = 10.</summary>
    private void CreateV10Database()
    {
        Directory.CreateDirectory(_directory);
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE milestones (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id   INTEGER NOT NULL,
                title        TEXT NOT NULL,
                is_completed INTEGER NOT NULL DEFAULT 0,
                completed_at TEXT,
                sort_order   INTEGER NOT NULL DEFAULT 0,
                notes        TEXT
            );
            INSERT INTO milestones (project_id, title, is_completed, completed_at, sort_order)
            VALUES (1, 'legacy done milestone', 1, '2026-07-01T10:00:00.0000000+00:00', 0);
            INSERT INTO milestones (project_id, title, is_completed, completed_at, sort_order)
            VALUES (1, 'legacy open milestone', 0, NULL, 1);
            PRAGMA user_version = 10;
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Migration_11_backfills_status_and_preserves_rows()
    {
        CreateV10Database();

        var database = new WorkbenchDatabase(DatabasePath); // runs migration 11

        var milestones = new MilestoneRepository(database).GetForProject(1);
        Assert.Equal(2, milestones.Count);

        var done = milestones.Single(m => m.Title == "legacy done milestone");
        Assert.Equal(MilestoneStatus.Done, done.Status);
        Assert.True(done.IsCompleted);
        Assert.NotNull(done.CompletedAt);
        Assert.Null(done.Weight);

        var open = milestones.Single(m => m.Title == "legacy open milestone");
        Assert.Equal(MilestoneStatus.NotStarted, open.Status);
        Assert.False(open.IsCompleted);
        Assert.Null(open.CompletedAt);
        Assert.Null(open.Weight);
    }

    [Fact]
    public void Migration_11_is_guarded_and_idempotent_on_reopen()
    {
        CreateV10Database();
        var first = new WorkbenchDatabase(DatabasePath);

        // Re-opening must not re-apply or fail; a fresh status survives.
        var repo = new MilestoneRepository(first);
        var open = repo.GetForProject(1).Single(m => m.Title == "legacy open milestone");
        open.Status = MilestoneStatus.Active;
        repo.Update(open);

        var second = new WorkbenchDatabase(DatabasePath);
        var milestones = new MilestoneRepository(second).GetForProject(1);
        Assert.Equal(2, milestones.Count);
        Assert.Equal(MilestoneStatus.Active, milestones.Single(m => m.Title == "legacy open milestone").Status);
        Assert.Equal(MilestoneStatus.Done, milestones.Single(m => m.Title == "legacy done milestone").Status);
    }

    [Fact]
    public void Status_done_syncs_the_legacy_flag_and_timestamp_on_write()
    {
        CreateV10Database();
        var repo = new MilestoneRepository(new WorkbenchDatabase(DatabasePath));

        var open = repo.GetForProject(1).Single(m => m.Title == "legacy open milestone");
        open.Status = MilestoneStatus.Done;
        repo.Update(open);

        var reloaded = repo.GetForProject(1).Single(m => m.Id == open.Id);
        Assert.True(reloaded.IsCompleted);
        Assert.NotNull(reloaded.CompletedAt);
    }

    [Fact]
    public void Leaving_done_clears_the_flag_and_timestamp_on_write()
    {
        CreateV10Database();
        var repo = new MilestoneRepository(new WorkbenchDatabase(DatabasePath));

        var done = repo.GetForProject(1).Single(m => m.Title == "legacy done milestone");
        done.Status = MilestoneStatus.Blocked;
        repo.Update(done);

        var reloaded = repo.GetForProject(1).Single(m => m.Id == done.Id);
        Assert.Equal(MilestoneStatus.Blocked, reloaded.Status);
        Assert.False(reloaded.IsCompleted);
        Assert.Null(reloaded.CompletedAt);
    }

    [Fact]
    public void Legacy_completed_only_writes_still_get_a_done_status()
    {
        CreateV10Database();
        var repo = new MilestoneRepository(new WorkbenchDatabase(DatabasePath));

        // The old checklist flow: only the flag is set, no status.
        repo.Add(new Milestone { ProjectId = 1, Title = "old-style add", IsCompleted = true });

        var added = repo.GetForProject(1).Single(m => m.Title == "old-style add");
        Assert.Equal(MilestoneStatus.Done, added.Status);
        Assert.NotNull(added.CompletedAt);
    }

    [Fact]
    public void Weight_roundtrips_and_defaults_to_null()
    {
        CreateV10Database();
        var repo = new MilestoneRepository(new WorkbenchDatabase(DatabasePath));

        repo.Add(new Milestone { ProjectId = 1, Title = "weighted", Weight = 5 });
        repo.Add(new Milestone { ProjectId = 1, Title = "unweighted" });

        var milestones = repo.GetForProject(1);
        Assert.Equal(5, milestones.Single(m => m.Title == "weighted").Weight);
        Assert.Null(milestones.Single(m => m.Title == "unweighted").Weight);
    }
}
