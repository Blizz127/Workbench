using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

/// <summary>
/// Append-only persistent audit trail for the mobile action layer. All SQL
/// for the audit table lives here — the web layer only sees the interface.
/// </summary>
public sealed class MobileActionAuditRepository(WorkbenchDatabase database) : IMobileActionAuditRepository
{
    private const string SelectColumns =
        "id, timestamp_utc, project_id, project_name, action_id, action_label, action_type," +
        " safety, state, session_hash, command_preview, output_summary, output_truncated," +
        " exit_code, duration_ms, failure_reason, services, source, correlation_id";

    public long Add(MobileActionAuditRecord record)
    {
        if (record.TimestampUtc == default)
            record.TimestampUtc = DateTimeOffset.UtcNow;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO mobile_action_audit
                (timestamp_utc, project_id, project_name, action_id, action_label, action_type,
                 safety, state, session_hash, command_preview, output_summary, output_truncated,
                 exit_code, duration_ms, failure_reason, services, source, correlation_id)
            VALUES
                ($timestamp, $pid, $pname, $aid, $alabel, $atype,
                 $safety, $state, $session, $preview, $output, $truncated,
                 $exit, $duration, $failure, $services, $source, $correlation);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$timestamp", SqliteMapping.ToDb(record.TimestampUtc));
        command.Parameters.AddWithValue("$pid", record.ProjectId);
        command.Parameters.AddWithValue("$pname", record.ProjectName);
        command.Parameters.AddWithValue("$aid", record.ActionId);
        command.Parameters.AddWithValue("$alabel", record.ActionLabel);
        command.Parameters.AddWithValue("$atype", record.ActionType);
        command.Parameters.AddWithValue("$safety", record.Safety);
        command.Parameters.AddWithValue("$state", StateToDb(record.State));
        command.Parameters.AddWithValue("$session", SqliteMapping.ToDbNullable(record.SessionHash));
        command.Parameters.AddWithValue("$preview", SqliteMapping.ToDbNullable(record.CommandPreview));
        command.Parameters.AddWithValue("$output", SqliteMapping.ToDbNullable(record.OutputSummary));
        command.Parameters.AddWithValue("$truncated", record.OutputTruncated ? 1 : 0);
        command.Parameters.AddWithValue("$exit", SqliteMapping.ToDbNullable(record.ExitCode));
        command.Parameters.AddWithValue("$duration", (object?)record.DurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$failure", SqliteMapping.ToDbNullable(record.FailureReason));
        command.Parameters.AddWithValue("$services", SqliteMapping.ToJson(record.Services as List<string> ?? [.. record.Services]));
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$correlation", record.CorrelationId);

        record.Id = (long)command.ExecuteScalar()!;
        return record.Id;
    }

    public IReadOnlyList<MobileActionAuditRecord> Query(MobileActionAuditFilter filter)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM mobile_action_audit
            WHERE ($pid IS NULL OR project_id = $pid)
              AND ($aid IS NULL OR action_id = $aid)
              AND ($state IS NULL OR state = $state)
              AND ($from IS NULL OR timestamp_utc >= $from)
              AND ($to IS NULL OR timestamp_utc <= $to)
            ORDER BY timestamp_utc DESC, id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pid", (object?)filter.ProjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$aid", SqliteMapping.ToDbNullable(filter.ActionId));
        command.Parameters.AddWithValue("$state",
            filter.State is { } state ? StateToDb(state) : DBNull.Value);
        command.Parameters.AddWithValue("$from",
            filter.FromUtc is { } from ? SqliteMapping.ToDb(from) : DBNull.Value);
        command.Parameters.AddWithValue("$to",
            filter.ToUtc is { } to ? SqliteMapping.ToDb(to) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", filter.Limit);

        var results = new List<MobileActionAuditRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public MobileActionAuditRecord? GetById(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM mobile_action_audit WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int EnforceRetention(int maxRecords, int maxAgeDays)
    {
        var cutoff = SqliteMapping.ToDb(DateTimeOffset.UtcNow.AddDays(-Math.Max(0, maxAgeDays)));

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        // A record survives only while it is BOTH inside the age window AND
        // within the newest maxRecords rows — see the interface contract.
        command.CommandText =
            """
            DELETE FROM mobile_action_audit
            WHERE timestamp_utc < $cutoff
               OR id NOT IN (
                   SELECT id FROM mobile_action_audit
                   ORDER BY timestamp_utc DESC, id DESC LIMIT $keep);
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue("$keep", Math.Max(0, maxRecords));
        return command.ExecuteNonQuery();
    }

    /// <summary>The stored state vocabulary is lowercase; "timedOut" keeps its camel hump.</summary>
    private static string StateToDb(MobileActionAuditState state) => state switch
    {
        MobileActionAuditState.Attempted => "attempted",
        MobileActionAuditState.Blocked => "blocked",
        MobileActionAuditState.Rejected => "rejected",
        MobileActionAuditState.Running => "running",
        MobileActionAuditState.Succeeded => "succeeded",
        MobileActionAuditState.Failed => "failed",
        MobileActionAuditState.Cancelled => "cancelled",
        MobileActionAuditState.TimedOut => "timedOut",
        _ => state.ToString(),
    };

    private static MobileActionAuditRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        TimestampUtc = reader.GetDateTimeOffset(1),
        ProjectId = reader.GetInt64(2),
        ProjectName = reader.GetString(3),
        ActionId = reader.GetString(4),
        ActionLabel = reader.GetString(5),
        ActionType = reader.GetString(6),
        Safety = reader.GetString(7),
        State = reader.GetEnum<MobileActionAuditState>(8),
        SessionHash = reader.GetNullableString(9),
        CommandPreview = reader.GetNullableString(10),
        OutputSummary = reader.GetNullableString(11),
        OutputTruncated = reader.GetInt32(12) != 0,
        ExitCode = reader.GetNullableInt32(13),
        DurationMs = reader.IsDBNull(14) ? null : reader.GetInt64(14),
        FailureReason = reader.GetNullableString(15),
        Services = SqliteMapping.FromJson(reader.GetString(16)),
        Source = reader.GetString(17),
        CorrelationId = reader.GetString(18),
    };
}
