using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

/// <summary>
/// Append-only build/run history. There is intentionally no Update or Delete:
/// history is never overwritten.
/// </summary>
public sealed class BuildHistoryRepository(WorkbenchDatabase database) : IBuildHistoryRepository
{
    private const string SelectColumns =
        "id, project_id, kind, command, started_at, exit_code, duration_ms, output_log, commit_sha";

    public IReadOnlyList<BuildRecord> GetForProject(long projectId, int limit = 100, ExecutionKind? kind = null)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM build_records
            WHERE project_id = $pid AND ($kind IS NULL OR kind = $kind)
            ORDER BY started_at DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$kind", (object?)kind?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<BuildRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public BuildRecord? GetLatest(long projectId, ExecutionKind kind = ExecutionKind.Build)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM build_records
            WHERE project_id = $pid AND kind = $kind
            ORDER BY started_at DESC, id DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$kind", kind.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public long Add(BuildRecord record)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO build_records (project_id, kind, command, started_at, exit_code, duration_ms, output_log, commit_sha)
            VALUES ($pid, $kind, $command, $started_at, $exit_code, $duration_ms, $output_log, $commit_sha);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$pid", record.ProjectId);
        command.Parameters.AddWithValue("$kind", record.Kind.ToString());
        command.Parameters.AddWithValue("$command", record.Command);
        command.Parameters.AddWithValue("$started_at", SqliteMapping.ToDb(record.StartedAt));
        command.Parameters.AddWithValue("$exit_code", SqliteMapping.ToDbNullable(record.ExitCode));
        command.Parameters.AddWithValue("$duration_ms", (long)record.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$output_log", record.OutputLog);
        command.Parameters.AddWithValue("$commit_sha", SqliteMapping.ToDbNullable(record.CommitSha));

        record.Id = (long)command.ExecuteScalar()!;
        return record.Id;
    }

    private static BuildRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        Kind = reader.GetEnum<ExecutionKind>(2),
        Command = reader.GetString(3),
        StartedAt = reader.GetDateTimeOffset(4),
        ExitCode = reader.GetNullableInt32(5),
        Duration = TimeSpan.FromMilliseconds(reader.GetInt64(6)),
        OutputLog = reader.GetString(7),
        CommitSha = reader.GetNullableString(8),
    };
}
