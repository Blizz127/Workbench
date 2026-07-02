using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class TimelineRepository(WorkbenchDatabase database) : ITimelineRepository
{
    public IReadOnlyList<TimelineEntry> GetForProject(long projectId, int limit = 200)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, project_id, timestamp, category, message, detail
            FROM timeline_entries WHERE project_id = $pid
            ORDER BY timestamp DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<TimelineEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new TimelineEntry
            {
                Id = reader.GetInt64(0),
                ProjectId = reader.GetInt64(1),
                Timestamp = reader.GetDateTimeOffset(2),
                Category = reader.GetEnum<TimelineCategory>(3),
                Message = reader.GetString(4),
                Detail = reader.GetNullableString(5),
            });
        }
        return results;
    }

    public long Add(TimelineEntry entry)
    {
        if (entry.Timestamp == default)
            entry.Timestamp = DateTimeOffset.Now;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO timeline_entries (project_id, timestamp, category, message, detail)
            VALUES ($pid, $timestamp, $category, $message, $detail);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$pid", entry.ProjectId);
        command.Parameters.AddWithValue("$timestamp", SqliteMapping.ToDb(entry.Timestamp));
        command.Parameters.AddWithValue("$category", entry.Category.ToString());
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$detail", SqliteMapping.ToDbNullable(entry.Detail));

        entry.Id = (long)command.ExecuteScalar()!;
        return entry.Id;
    }
}
