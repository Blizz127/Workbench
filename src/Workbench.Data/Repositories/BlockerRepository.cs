using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class BlockerRepository(WorkbenchDatabase database) : IBlockerRepository
{
    private const string SelectColumns = "id, project_id, description, is_resolved, resolution, created_at, resolved_at";

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

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO blockers (project_id, description, is_resolved, resolution, created_at, resolved_at)
            VALUES ($pid, $description, $resolved, $resolution, $created_at, $resolved_at);
            SELECT last_insert_rowid();
            """;
        Bind(command, blocker);
        blocker.Id = (long)command.ExecuteScalar()!;
        return blocker.Id;
    }

    public void Update(Blocker blocker)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE blockers SET description = $description, is_resolved = $resolved,
                resolution = $resolution, created_at = $created_at, resolved_at = $resolved_at
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

    private static void Bind(SqliteCommand command, Blocker b)
    {
        command.Parameters.AddWithValue("$pid", b.ProjectId);
        command.Parameters.AddWithValue("$description", b.Description);
        command.Parameters.AddWithValue("$resolved", b.IsResolved ? 1 : 0);
        command.Parameters.AddWithValue("$resolution", SqliteMapping.ToDbNullable(b.Resolution));
        command.Parameters.AddWithValue("$created_at", SqliteMapping.ToDb(b.CreatedAt));
        command.Parameters.AddWithValue("$resolved_at", SqliteMapping.ToDb(b.ResolvedAt));
    }

    private static List<Blocker> ReadAll(SqliteCommand command)
    {
        var results = new List<Blocker>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Blocker
            {
                Id = reader.GetInt64(0),
                ProjectId = reader.GetInt64(1),
                Description = reader.GetString(2),
                IsResolved = reader.GetInt32(3) != 0,
                Resolution = reader.GetNullableString(4),
                CreatedAt = reader.GetDateTimeOffset(5),
                ResolvedAt = reader.GetNullableDateTimeOffset(6),
            });
        }
        return results;
    }
}
