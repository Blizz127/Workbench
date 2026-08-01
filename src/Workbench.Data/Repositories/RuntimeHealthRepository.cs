using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

/// <summary>
/// Append-only runtime health assessments. The newest row per project is the
/// current known state; rows are never updated or deleted.
/// </summary>
public sealed class RuntimeHealthRepository(WorkbenchDatabase database) : IRuntimeHealthRepository
{
    private const string SelectColumns =
        "id, project_id, checked_at, health, failed_component, output";

    public IReadOnlyList<RuntimeHealthRecord> GetForProject(long projectId, int limit = 20)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM runtime_health
            WHERE project_id = $pid
            ORDER BY id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<RuntimeHealthRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public RuntimeHealthRecord? GetLatest(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM runtime_health
            WHERE project_id = $pid
            ORDER BY id DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pid", projectId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public long Add(RuntimeHealthRecord record)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO runtime_health (project_id, checked_at, health, failed_component, output)
            VALUES ($pid, $checked_at, $health, $failed_component, $output);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$pid", record.ProjectId);
        command.Parameters.AddWithValue("$checked_at", SqliteMapping.ToDb(record.CheckedAt));
        command.Parameters.AddWithValue("$health", record.Health.ToString());
        command.Parameters.AddWithValue("$failed_component", SqliteMapping.ToDbNullable(record.FailedComponent));
        command.Parameters.AddWithValue("$output", record.Output);

        record.Id = (long)command.ExecuteScalar()!;
        return record.Id;
    }

    private static RuntimeHealthRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        CheckedAt = reader.GetDateTimeOffset(2),
        Health = reader.GetEnum<RuntimeHealth>(3),
        FailedComponent = reader.GetNullableString(4),
        Output = reader.GetString(5),
    };
}
