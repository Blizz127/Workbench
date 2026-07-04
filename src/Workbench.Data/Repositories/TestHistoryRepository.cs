using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

/// <summary>Append-only test history; never overwritten.</summary>
public sealed class TestHistoryRepository(WorkbenchDatabase database) : ITestHistoryRepository
{
    // output_log is deliberately excluded: logs can be megabytes and list
    // views never show them. Fetch per record via GetOutputLog.
    private const string SelectColumns =
        "id, project_id, command, result, started_at, duration_ms, notes, commit_sha";

    public IReadOnlyList<TestRecord> GetForProject(long projectId, int limit = 100)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM test_records
            WHERE project_id = $pid ORDER BY started_at DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<TestRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public TestRecord? GetLatest(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM test_records
            WHERE project_id = $pid ORDER BY started_at DESC, id DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pid", projectId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public long Add(TestRecord record)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO test_records (project_id, command, result, started_at, duration_ms, notes, commit_sha, output_log)
            VALUES ($pid, $command, $result, $started_at, $duration_ms, $notes, $commit_sha, $output_log);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$pid", record.ProjectId);
        command.Parameters.AddWithValue("$command", record.Command);
        command.Parameters.AddWithValue("$result", record.Result.ToString());
        command.Parameters.AddWithValue("$started_at", SqliteMapping.ToDb(record.StartedAt));
        command.Parameters.AddWithValue("$duration_ms", (long)record.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$notes", SqliteMapping.ToDbNullable(record.Notes));
        command.Parameters.AddWithValue("$commit_sha", SqliteMapping.ToDbNullable(record.CommitSha));
        command.Parameters.AddWithValue("$output_log", record.OutputLog);

        record.Id = (long)command.ExecuteScalar()!;
        return record.Id;
    }

    public string? GetOutputLog(long recordId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT output_log FROM test_records WHERE id = $id;";
        command.Parameters.AddWithValue("$id", recordId);
        return command.ExecuteScalar() as string;
    }

    private static TestRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        Command = reader.GetString(2),
        Result = reader.GetEnum<TestResult>(3),
        StartedAt = reader.GetDateTimeOffset(4),
        Duration = TimeSpan.FromMilliseconds(reader.GetInt64(5)),
        Notes = reader.GetNullableString(6),
        CommitSha = reader.GetNullableString(7),
    };
}
