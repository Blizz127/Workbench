using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Data;
using Workbench.Data.Repositories;
using Xunit;

namespace Workbench.Tests;

public sealed class TimestampTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public TimestampTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void New_writes_store_timestamps_in_utc()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var id = projects.Add(new Project { Name = "utc-check" });

        using var connection = _fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var stored = (string)command.ExecuteScalar()!;

        Assert.EndsWith("+00:00", stored);
    }

    [Fact]
    public void Timeline_orders_chronologically_across_mixed_utc_offsets()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var timeline = new TimelineRepository(_fixture.Database);
        var id = projects.Add(new Project { Name = "ordering-check" });

        // Chronological order: earlier (08:00Z) has the lexicographically
        // LARGER local string — the case that broke v1 storage.
        var earlier = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(2)); // 08:00Z
        var later = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.FromHours(1));   // 08:30Z

        timeline.Add(new TimelineEntry { ProjectId = id, Message = "earlier", Timestamp = earlier });
        timeline.Add(new TimelineEntry { ProjectId = id, Message = "later", Timestamp = later });

        var entries = timeline.GetForProject(id);
        Assert.Equal("later", entries[0].Message);   // newest first
        Assert.Equal("earlier", entries[1].Message);
    }

    [Fact]
    public void Migration_rewrites_offset_timestamps_to_utc_preserving_the_instant()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var id = projects.Add(new Project { Name = "migration-check" });

        // Simulate a v1-era row: local-offset timestamp written directly.
        const string v1Value = "2026-03-15T14:30:00.1234567+05:00"; // 09:30:00.1234567Z
        using (var connection = _fixture.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO timeline_entries (project_id, timestamp, category, message) " +
                "VALUES ($id, $ts, 'General', 'v1 row');";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$ts", v1Value);
            command.ExecuteNonQuery();
        }

        int rewritten;
        using (var connection = _fixture.Database.OpenConnection())
        {
            rewritten = WorkbenchDatabase.NormalizeTimestampsToUtc(connection);
        }

        Assert.True(rewritten >= 1);

        var entry = new TimelineRepository(_fixture.Database)
            .GetForProject(id)
            .Single(e => e.Message == "v1 row");

        // Same instant, now expressed in UTC — full tick precision preserved.
        Assert.Equal(TimeSpan.Zero, entry.Timestamp.Offset);
        Assert.Equal(DateTimeOffset.Parse(v1Value), entry.Timestamp);
        Assert.Equal(1234567, entry.Timestamp.Ticks % TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void Migration_leaves_unparseable_values_untouched()
    {
        var projects = new ProjectRepository(_fixture.Database);
        var id = projects.Add(new Project { Name = "garbage-check" });

        using (var connection = _fixture.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO timeline_entries (project_id, timestamp, category, message) " +
                "VALUES ($id, 'not-a-date', 'General', 'garbage row');";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        using (var connection = _fixture.Database.OpenConnection())
        {
            WorkbenchDatabase.NormalizeTimestampsToUtc(connection); // must not throw
        }

        using (var connection = _fixture.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT timestamp FROM timeline_entries WHERE project_id = $id AND message = 'garbage row';";
            command.Parameters.AddWithValue("$id", id);
            Assert.Equal("not-a-date", (string)command.ExecuteScalar()!);
        }
    }
}
