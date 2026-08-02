using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Data;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

/// <summary>
/// Data-layer tests for the persistent mobile action audit trail against real
/// temp SQLite files: roundtrip, restart persistence, in-place migration,
/// filters, and retention.
/// </summary>
public sealed class MobileActionAuditRepositoryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "workbench-tests", Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "audit.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static MobileActionAuditRecord Record(
        long projectId = 7, string actionId = "logs-all",
        MobileActionAuditState state = MobileActionAuditState.Succeeded,
        DateTimeOffset? timestamp = null, string correlationId = "corr-1") =>
        new()
        {
            TimestampUtc = timestamp ?? new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero),
            ProjectId = projectId,
            ProjectName = "Banshee Realm 11",
            ActionId = actionId,
            ActionLabel = "View all logs",
            ActionType = "dockerLogsAll",
            Safety = "readOnly",
            State = state,
            SessionHash = "abc123",
            CommandPreview = "docker compose logs",
            OutputSummary = "log line",
            OutputTruncated = false,
            ExitCode = 0,
            DurationMs = 1200,
            FailureReason = null,
            Services = ["map", "world"],
            Source = "web",
            CorrelationId = correlationId,
        };

    [Fact]
    public void A_record_roundtrips_every_field()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);
        var record = Record(state: MobileActionAuditState.TimedOut);
        record.FailureReason = "hit the 30s timeout";
        record.OutputTruncated = true;

        var id = repo.Add(record);
        var loaded = repo.GetById(id);

        Assert.NotNull(loaded);
        Assert.Equal(record.TimestampUtc, loaded.TimestampUtc);
        Assert.Equal(7, loaded.ProjectId);
        Assert.Equal("Banshee Realm 11", loaded.ProjectName);
        Assert.Equal("logs-all", loaded.ActionId);
        Assert.Equal("View all logs", loaded.ActionLabel);
        Assert.Equal("dockerLogsAll", loaded.ActionType);
        Assert.Equal("readOnly", loaded.Safety);
        Assert.Equal(MobileActionAuditState.TimedOut, loaded.State);
        Assert.Equal("abc123", loaded.SessionHash);
        Assert.Equal("docker compose logs", loaded.CommandPreview);
        Assert.Equal("log line", loaded.OutputSummary);
        Assert.True(loaded.OutputTruncated);
        Assert.Equal(0, loaded.ExitCode);
        Assert.Equal(1200, loaded.DurationMs);
        Assert.Equal("hit the 30s timeout", loaded.FailureReason);
        Assert.Equal(["map", "world"], loaded.Services);
        Assert.Equal("web", loaded.Source);
        Assert.Equal("corr-1", loaded.CorrelationId);
    }

    [Fact]
    public void Nullable_fields_roundtrip_as_null()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);

        var id = repo.Add(new MobileActionAuditRecord
        {
            ProjectId = 7,
            ProjectName = "Banshee Realm 11",
            ActionId = "restart-map",
            ActionLabel = "Restart map",
            ActionType = "dockerRestartService",
            Safety = "requiresDoubleConfirmation",
            State = MobileActionAuditState.Attempted,
            CorrelationId = "corr-2",
        });

        var loaded = repo.GetById(id)!;
        Assert.Null(loaded.SessionHash);
        Assert.Null(loaded.CommandPreview);
        Assert.Null(loaded.OutputSummary);
        Assert.False(loaded.OutputTruncated);
        Assert.Null(loaded.ExitCode);
        Assert.Null(loaded.DurationMs);
        Assert.Null(loaded.FailureReason);
        Assert.Empty(loaded.Services);
        Assert.NotEqual(default, loaded.TimestampUtc); // defaulted at write time
    }

    [Fact]
    public void States_are_stored_in_the_lowercase_vocabulary()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);
        repo.Add(Record(state: MobileActionAuditState.TimedOut, correlationId: "c1"));
        repo.Add(Record(state: MobileActionAuditState.Attempted, correlationId: "c2"));

        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM mobile_action_audit ORDER BY id;";
        using var reader = command.ExecuteReader();
        var states = new List<string>();
        while (reader.Read())
            states.Add(reader.GetString(0));

        Assert.Equal(["timedOut", "attempted"], states);
    }

    [Fact]
    public void Records_persist_across_database_recreation()
    {
        long id;
        {
            var database = new WorkbenchDatabase(DatabasePath);
            var repo = new MobileActionAuditRepository(database);
            id = repo.Add(Record());
        }

        SqliteConnection.ClearAllPools();

        // A fresh WorkbenchDatabase + repository on the same file (a restart).
        var reopened = new WorkbenchDatabase(DatabasePath);
        var loaded = new MobileActionAuditRepository(reopened).GetById(id);

        Assert.NotNull(loaded);
        Assert.Equal("View all logs", loaded.ActionLabel);
        Assert.Equal(MobileActionAuditState.Succeeded, loaded.State);
        Assert.Equal("corr-1", loaded.CorrelationId);
    }

    [Fact]
    public void Migration_upgrades_an_existing_database_in_place()
    {
        // A pre-2E database: project data exists, no audit table, version 8.
        long projectId;
        {
            var database = new WorkbenchDatabase(DatabasePath);
            projectId = new ProjectRepository(database).Add(new Project { Name = "Survivor" });
        }

        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE mobile_action_audit; PRAGMA user_version = 8;";
            command.ExecuteNonQuery();
        }

        // Reopening applies migration 9 in place: the table appears, the
        // project row is untouched.
        var upgraded = new WorkbenchDatabase(DatabasePath);
        var project = new ProjectRepository(upgraded).GetById(projectId);
        Assert.NotNull(project);
        Assert.Equal("Survivor", project.Name);

        var repo = new MobileActionAuditRepository(upgraded);
        var id = repo.Add(Record());
        Assert.NotNull(repo.GetById(id));
    }

    [Fact]
    public void Filters_combine_project_action_state_and_date_range()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);
        var t = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        repo.Add(Record(projectId: 7, actionId: "logs-all", MobileActionAuditState.Attempted, t.AddHours(-3), "c1"));
        repo.Add(Record(projectId: 7, actionId: "logs-all", MobileActionAuditState.Succeeded, t.AddHours(-2), "c2"));
        repo.Add(Record(projectId: 7, actionId: "restart-map", MobileActionAuditState.Blocked, t.AddHours(-1), "c3"));
        repo.Add(Record(projectId: 9, actionId: "logs-all", MobileActionAuditState.Succeeded, t, "c4"));

        Assert.Equal(4, repo.Query(new MobileActionAuditFilter { Limit = 50 }).Count);
        Assert.Equal(3, repo.Query(new MobileActionAuditFilter { ProjectId = 7, Limit = 50 }).Count);
        Assert.Equal(3, repo.Query(new MobileActionAuditFilter { ActionId = "logs-all", Limit = 50 }).Count);
        Assert.Equal(2, repo.Query(
            new MobileActionAuditFilter { State = MobileActionAuditState.Succeeded, Limit = 50 }).Count);
        Assert.Single(repo.Query(new MobileActionAuditFilter
        {
            ProjectId = 7, ActionId = "logs-all", State = MobileActionAuditState.Succeeded, Limit = 50,
        }));

        var ranged = repo.Query(new MobileActionAuditFilter
        {
            FromUtc = t.AddHours(-2), ToUtc = t.AddHours(-1), Limit = 50,
        });
        Assert.Equal(2, ranged.Count); // bounds are inclusive
        Assert.All(ranged, r => Assert.InRange(r.TimestampUtc, t.AddHours(-2), t.AddHours(-1)));

        // Newest first.
        var all = repo.Query(new MobileActionAuditFilter { ProjectId = 7, Limit = 50 });
        Assert.Equal(["c3", "c2", "c1"], all.Select(r => r.CorrelationId).ToArray());
    }

    [Fact]
    public void Limit_caps_the_newest_records()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);
        var t = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 10; i++)
            repo.Add(Record(timestamp: t.AddMinutes(i), correlationId: $"c{i}"));

        var results = repo.Query(new MobileActionAuditFilter { Limit = 3 });

        Assert.Equal(3, results.Count);
        Assert.Equal("c9", results[0].CorrelationId);
    }

    [Fact]
    public void Retention_removes_records_beyond_the_count_cap()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);
        var t = DateTimeOffset.UtcNow;
        for (var i = 0; i < 10; i++)
            repo.Add(Record(timestamp: t.AddMinutes(i), correlationId: $"c{i}"));

        var deleted = repo.EnforceRetention(maxRecords: 4, maxAgeDays: 3650);

        Assert.Equal(6, deleted);
        var kept = repo.Query(new MobileActionAuditFilter { Limit = 50 });
        Assert.Equal(4, kept.Count);
        Assert.Equal(["c9", "c8", "c7", "c6"], kept.Select(r => r.CorrelationId).ToArray());
    }

    [Fact]
    public void Retention_removes_records_beyond_the_age_cap()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);
        var now = DateTimeOffset.UtcNow;
        repo.Add(Record(timestamp: now.AddDays(-100), correlationId: "ancient"));
        repo.Add(Record(timestamp: now.AddDays(-10), correlationId: "recent"));
        repo.Add(Record(timestamp: now, correlationId: "now"));

        var deleted = repo.EnforceRetention(maxRecords: 5000, maxAgeDays: 90);

        Assert.Equal(1, deleted);
        var kept = repo.Query(new MobileActionAuditFilter { Limit = 50 });
        Assert.Equal(["now", "recent"], kept.Select(r => r.CorrelationId).ToArray());
    }

    [Fact]
    public void Retention_applies_both_caps_whichever_removes_more()
    {
        var database = new WorkbenchDatabase(DatabasePath);
        var repo = new MobileActionAuditRepository(database);
        var now = DateTimeOffset.UtcNow;
        // Old enough to fail the age cap, new enough to sit inside the count cap:
        // union semantics must still delete it.
        repo.Add(Record(timestamp: now.AddDays(-100), correlationId: "old"));
        repo.Add(Record(timestamp: now, correlationId: "fresh"));

        var deleted = repo.EnforceRetention(maxRecords: 5000, maxAgeDays: 90);

        Assert.Equal(1, deleted);
        Assert.Single(repo.Query(new MobileActionAuditFilter { Limit = 50 }),
            r => r.CorrelationId == "fresh");
    }
}
