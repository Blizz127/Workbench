using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class BlockerRepository(WorkbenchDatabase database) : IBlockerRepository
{
    private const string SelectColumns =
        "id, project_id, stable_id, title, description, blocker_class, severity, status, " +
        "evidence, next_safest_step, owner, tags, source_reference, " +
        "is_resolved, resolution, created_at, updated_at, resolved_at";

    public IReadOnlyList<Blocker> GetForProject(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM blockers WHERE project_id = $pid ORDER BY is_resolved, id DESC;";
        command.Parameters.AddWithValue("$pid", projectId);
        return ReadAll(command);
    }

    public IReadOnlyList<Blocker> GetOpen(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM blockers WHERE project_id = $pid AND is_resolved = 0 ORDER BY id DESC;";
        command.Parameters.AddWithValue("$pid", projectId);
        return ReadAll(command);
    }

    public long Add(Blocker blocker)
    {
        if (blocker.CreatedAt == default)
            blocker.CreatedAt = DateTimeOffset.Now;
        if (blocker.UpdatedAt == default)
            blocker.UpdatedAt = blocker.CreatedAt;
        if (blocker.StableId.Length == 0)
            blocker.StableId = Guid.NewGuid().ToString("N");
        Normalize(blocker);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO blockers (project_id, stable_id, title, description, blocker_class, severity, status,
                evidence, next_safest_step, owner, tags, source_reference,
                is_resolved, resolution, created_at, updated_at, resolved_at)
            VALUES ($pid, $stable_id, $title, $description, $blocker_class, $severity, $status,
                $evidence, $next_safest_step, $owner, $tags, $source_reference,
                $resolved, $resolution, $created_at, $updated_at, $resolved_at);
            SELECT last_insert_rowid();
            """;
        Bind(command, blocker);
        blocker.Id = (long)command.ExecuteScalar()!;
        return blocker.Id;
    }

    public void Update(Blocker blocker)
    {
        blocker.UpdatedAt = DateTimeOffset.Now;
        Normalize(blocker);

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE blockers SET stable_id = $stable_id, title = $title, description = $description,
                blocker_class = $blocker_class, severity = $severity, status = $status,
                evidence = $evidence, next_safest_step = $next_safest_step, owner = $owner,
                tags = $tags, source_reference = $source_reference,
                is_resolved = $resolved, resolution = $resolution,
                created_at = $created_at, updated_at = $updated_at, resolved_at = $resolved_at
            WHERE id = $id;
            """;
        Bind(command, blocker);
        command.Parameters.AddWithValue("$id", blocker.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM blockers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Keeps the legacy flag and the lifecycle status consistent in both
    /// directions. Status is authoritative: Resolved/Dismissed forces the flag
    /// on, an open status forces it off. The one legacy convenience is the
    /// resolve flow — a caller that only sets <c>IsResolved</c> +
    /// <c>ResolvedAt</c> gets a Resolved status. Reopening (open status,
    /// cleared timestamp) therefore can't be flipped back by a stale flag.
    /// </summary>
    private static void Normalize(Blocker b)
    {
        if (b.IsResolved && b.Status is BlockerStatus.Active or BlockerStatus.Monitoring && b.ResolvedAt is not null)
            b.Status = BlockerStatus.Resolved;
        b.IsResolved = b.Status is BlockerStatus.Resolved or BlockerStatus.Dismissed;
        if (!b.IsResolved)
            b.ResolvedAt = null;
    }

    private static void Bind(SqliteCommand command, Blocker b)
    {
        command.Parameters.AddWithValue("$pid", b.ProjectId);
        command.Parameters.AddWithValue("$stable_id", b.StableId);
        command.Parameters.AddWithValue("$title", SqliteMapping.ToDbNullable(b.Title));
        command.Parameters.AddWithValue("$description", b.Description);
        command.Parameters.AddWithValue("$blocker_class", SqliteMapping.ToDbNullable(b.BlockerClass));
        command.Parameters.AddWithValue("$severity", SqliteMapping.ToDbNullable(b.Severity));
        command.Parameters.AddWithValue("$status", b.Status.ToString());
        command.Parameters.AddWithValue("$evidence", SqliteMapping.ToDbNullable(b.Evidence));
        command.Parameters.AddWithValue("$next_safest_step", SqliteMapping.ToDbNullable(b.NextSafestStep));
        command.Parameters.AddWithValue("$owner", SqliteMapping.ToDbNullable(b.Owner));
        command.Parameters.AddWithValue("$tags", SqliteMapping.ToJson(b.Tags));
        command.Parameters.AddWithValue("$source_reference", SqliteMapping.ToDbNullable(b.SourceReference));
        command.Parameters.AddWithValue("$resolved", b.IsResolved ? 1 : 0);
        command.Parameters.AddWithValue("$resolution", SqliteMapping.ToDbNullable(b.Resolution));
        command.Parameters.AddWithValue("$created_at", SqliteMapping.ToDb(b.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", SqliteMapping.ToDb(b.UpdatedAt));
        command.Parameters.AddWithValue("$resolved_at", SqliteMapping.ToDb(b.ResolvedAt));
    }

    private static List<Blocker> ReadAll(SqliteCommand command)
    {
        var results = new List<Blocker>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var createdAt = reader.GetDateTimeOffset(15);
            results.Add(new Blocker
            {
                Id = reader.GetInt64(0),
                ProjectId = reader.GetInt64(1),
                StableId = reader.GetNullableString(2) ?? string.Empty,
                Title = reader.GetNullableString(3),
                Description = reader.GetString(4),
                BlockerClass = reader.GetNullableString(5),
                Severity = reader.GetNullableString(6),
                Status = reader.GetEnum<BlockerStatus>(7),
                Evidence = reader.GetNullableString(8),
                NextSafestStep = reader.GetNullableString(9),
                Owner = reader.GetNullableString(10),
                Tags = SqliteMapping.FromJson(reader.GetString(11)),
                SourceReference = reader.GetNullableString(12),
                IsResolved = reader.GetInt32(13) != 0,
                Resolution = reader.GetNullableString(14),
                CreatedAt = createdAt,
                // Rows written by anything but this repository may lack the backfilled stamp.
                UpdatedAt = reader.GetNullableDateTimeOffset(16) ?? createdAt,
                ResolvedAt = reader.GetNullableDateTimeOffset(17),
            });
        }
        return results;
    }
}
