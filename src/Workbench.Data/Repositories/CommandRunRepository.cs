using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

/// <summary>Append-only history for matching-build and custom command executions.</summary>
public sealed class CommandRunRepository(WorkbenchDatabase database) : ICommandRunRepository
{
    // output_log is deliberately excluded from list/latest queries — see
    // BuildHistoryRepository for the same convention and its rationale.
    private const string SelectColumns =
        """
        id, project_id, command_name, command_text, working_directory, started_at,
        exit_code, duration_ms, output_truncated, git_head_sha, was_dirty,
        regression_detected, cancelled
        """;

    public IReadOnlyList<CommandRun> GetForProject(long projectId, int limit = 100, string? commandName = null)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM command_runs
            WHERE project_id = $pid AND ($name IS NULL OR command_name = $name)
            ORDER BY started_at DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$name", (object?)commandName ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<CommandRun>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public CommandRun? GetLatest(long projectId, string commandName)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM command_runs
            WHERE project_id = $pid AND command_name = $name
            ORDER BY started_at DESC, id DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$name", commandName);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public string? GetOutputLog(long recordId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT output_log FROM command_runs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", recordId);
        return command.ExecuteScalar() as string;
    }

    public long Add(CommandRun record)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO command_runs (
                project_id, command_name, command_text, working_directory, started_at,
                exit_code, duration_ms, output_log, output_truncated, git_head_sha,
                was_dirty, regression_detected, cancelled)
            VALUES (
                $pid, $command_name, $command_text, $working_directory, $started_at,
                $exit_code, $duration_ms, $output_log, $output_truncated, $git_head_sha,
                $was_dirty, $regression_detected, $cancelled);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$pid", record.ProjectId);
        command.Parameters.AddWithValue("$command_name", record.CommandName);
        command.Parameters.AddWithValue("$command_text", record.CommandText);
        command.Parameters.AddWithValue("$working_directory", (object?)record.WorkingDirectory ?? DBNull.Value);
        command.Parameters.AddWithValue("$started_at", SqliteMapping.ToDb(record.StartedAt));
        command.Parameters.AddWithValue("$exit_code", (object?)record.ExitCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration_ms", (long)record.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$output_log", record.OutputLog);
        command.Parameters.AddWithValue("$output_truncated", record.OutputTruncated ? 1 : 0);
        command.Parameters.AddWithValue("$git_head_sha", (object?)record.GitHeadSha ?? DBNull.Value);
        command.Parameters.AddWithValue("$was_dirty", record.WasDirty ? 1 : 0);
        command.Parameters.AddWithValue("$regression_detected", record.RegressionDetected ? 1 : 0);
        command.Parameters.AddWithValue("$cancelled", record.Cancelled ? 1 : 0);

        record.Id = (long)command.ExecuteScalar()!;
        return record.Id;
    }

    private static CommandRun Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        CommandName = reader.GetString(2),
        CommandText = reader.GetString(3),
        WorkingDirectory = reader.GetNullableString(4),
        StartedAt = reader.GetDateTimeOffset(5),
        ExitCode = reader.GetNullableInt32(6),
        Duration = TimeSpan.FromMilliseconds(reader.GetInt64(7)),
        OutputTruncated = reader.GetInt32(8) != 0,
        GitHeadSha = reader.GetNullableString(9),
        WasDirty = reader.GetInt32(10) != 0,
        RegressionDetected = reader.GetInt32(11) != 0,
        Cancelled = reader.GetInt32(12) != 0,
    };
}
