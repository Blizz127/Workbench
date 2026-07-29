using Microsoft.Data.Sqlite;
using Workbench.Core.Models;
using Workbench.Core.Services;

namespace Workbench.Data.Repositories;

public sealed class FrontierRepository(WorkbenchDatabase database) : IFrontierRepository
{
    private const string SelectColumns = "id, project_id, title, status, notes, created_at, completed_at";

    public IReadOnlyList<Frontier> GetForProject(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM frontiers WHERE project_id = $pid
            ORDER BY CASE status WHEN 'Current' THEN 0 WHEN 'Next' THEN 1 ELSE 2 END, id DESC;
            """;
        command.Parameters.AddWithValue("$pid", projectId);

        var results = new List<Frontier>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(Map(reader));
        return results;
    }

    public Frontier? GetCurrent(long projectId) => GetSingleByStatus(projectId, FrontierStatus.Current);

    public Frontier? GetNext(long projectId) => GetSingleByStatus(projectId, FrontierStatus.Next);

    private Frontier? GetSingleByStatus(long projectId, FrontierStatus status)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM frontiers WHERE project_id = $pid AND status = $status ORDER BY id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$pid", projectId);
        command.Parameters.AddWithValue("$status", status.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public long Add(Frontier frontier)
    {
        if (frontier.CreatedAt == default)
            frontier.CreatedAt = DateTimeOffset.Now;

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO frontiers (project_id, title, status, notes, created_at, completed_at)
            VALUES ($pid, $title, $status, $notes, $created_at, $completed_at);
            SELECT last_insert_rowid();
            """;
        Bind(command, frontier);
        frontier.Id = (long)command.ExecuteScalar()!;
        return frontier.Id;
    }

    public void Update(Frontier frontier)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE frontiers SET title = $title, status = $status, notes = $notes,
                created_at = $created_at, completed_at = $completed_at
            WHERE id = $id;
            """;
        Bind(command, frontier);
        command.Parameters.AddWithValue("$id", frontier.Id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM frontiers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, Frontier f)
    {
        command.Parameters.AddWithValue("$pid", f.ProjectId);
        command.Parameters.AddWithValue("$title", f.Title);
        command.Parameters.AddWithValue("$status", f.Status.ToString());
        command.Parameters.AddWithValue("$notes", SqliteMapping.ToDbNullable(f.Notes));
        command.Parameters.AddWithValue("$created_at", SqliteMapping.ToDb(f.CreatedAt));
        command.Parameters.AddWithValue("$completed_at", SqliteMapping.ToDb(f.CompletedAt));
    }

    private static Frontier Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        Title = reader.GetString(2),
        Status = reader.GetEnum<FrontierStatus>(3),
        Notes = reader.GetNullableString(4),
        CreatedAt = reader.GetDateTimeOffset(5),
        CompletedAt = reader.GetNullableDateTimeOffset(6),
    };
}
